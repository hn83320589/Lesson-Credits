using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Tests.Services;

public class CreditAndAttendanceTests : IDisposable
{
    private static readonly DateTime ClassStart = new(2026, 10, 20, 16, 0, 0);

    private readonly TestEnv _env = new();
    private readonly int _teacher;
    private readonly int _student;
    private readonly int _session;

    public CreditAndAttendanceTests()
    {
        _teacher = _env.AddTeacher();
        _student = _env.AddStudent();
        var course = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano, _student);
        _session = _env.AddSession(course, ClassStart);
    }

    [Fact]
    public async Task Buying_ten_lesson_plan_adds_ten_credits_expiring_in_six_months()
    {
        var plan = _env.AddPlan(10, 8000);

        var batch = await _env.Credits.PurchaseAsync(_student, plan, _env.Owner);

        Assert.Equal(10, _env.Balance(_student));
        Assert.Equal(new DateOnly(2026, 10, 19), batch.PurchasedOn);
        Assert.Equal(new DateOnly(2027, 4, 19), batch.ExpiresOn);
        Assert.Equal(8000, batch.Amount);
    }

    [Fact]
    public async Task Teacher_cannot_buy_credits()
    {
        var plan = _env.AddPlan(10, 8000);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _env.Credits.PurchaseAsync(_student, plan, TestEnv.TeacherActor(_teacher)));
    }

    [Theory]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.Absent)]
    public async Task Present_or_absent_deducts_one_credit(AttendanceStatus status)
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(30));

        await _env.Attendance.RecordAsync(_session, _student, status, TestEnv.TeacherActor(_teacher));

        Assert.Equal(9, _env.Balance(_student));
    }

    [Fact]
    public async Task On_time_leave_does_not_deduct_and_grants_makeup_credit()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddHours(-24).AddMinutes(-1));
        var leave = await _env.Attendance.RequestLeaveAsync(_session, _student, _env.Owner);
        _env.SetNow(ClassStart.AddMinutes(30));

        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Leave, TestEnv.TeacherActor(_teacher));

        Assert.True(leave.IsOnTime);
        Assert.Equal(10, _env.Balance(_student));
        using var db = _env.Db();
        var credit = await db.MakeupCredits.SingleAsync();
        Assert.Equal(MakeupStatus.Available, credit.Status);
        Assert.Equal(new DateOnly(2026, 10, 20).AddDays(60), credit.ExpiresOn);
    }

    [Fact]
    public async Task Late_leave_cannot_be_recorded_as_leave_and_absence_deducts()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddHours(-23).AddMinutes(-59));
        var leave = await _env.Attendance.RequestLeaveAsync(_session, _student, _env.Owner);
        _env.SetNow(ClassStart.AddMinutes(30));
        var teacher = TestEnv.TeacherActor(_teacher);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Leave, teacher));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Absent, teacher);

        Assert.False(leave.IsOnTime);
        Assert.Contains("24 小時", ex.Message);
        Assert.Equal(9, _env.Balance(_student));
        using var db = _env.Db();
        Assert.Empty(db.MakeupCredits);
    }

    [Fact]
    public async Task Cannot_mark_present_without_credits_and_reason_is_given()
    {
        _env.SetNow(ClassStart.AddMinutes(30));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner));

        Assert.Contains("點數不足", ex.Message);
        using var db = _env.Db();
        Assert.Empty(db.Attendances);
    }

    [Fact]
    public async Task Expired_credits_cannot_be_used_and_reason_mentions_expiry()
    {
        _env.AddBatch(_student, new DateOnly(2026, 3, 1), 10, 8000, used: 5);
        _env.SetNow(ClassStart.AddMinutes(30));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner));

        Assert.Contains("過期", ex.Message);
    }

    [Fact]
    public async Task Deducts_from_earliest_expiring_batch_first()
    {
        var later = _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        var earlier = _env.AddBatch(_student, new DateOnly(2026, 6, 1), 20, 15000);
        _env.SetNow(ClassStart.AddMinutes(30));

        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);

        using var db = _env.Db();
        var deduct = await db.CreditEntries.SingleAsync(e => e.Type == CreditEntryType.Deduct);
        Assert.Equal(earlier, deduct.BatchId);
        Assert.NotEqual(later, deduct.BatchId);
    }

    [Fact]
    public async Task Teacher_cannot_take_attendance_for_another_teachers_session()
    {
        var otherTeacher = _env.AddTeacher("李老師");
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(30));

        await Assert.ThrowsAsync<ForbiddenException>(() => _env.Attendance.RecordAsync(
            _session, _student, AttendanceStatus.Present, TestEnv.TeacherActor(otherTeacher)));
    }

    [Fact]
    public async Task Cannot_take_attendance_before_class_starts()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(-1));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.RecordAsync(
            _session, _student, AttendanceStatus.Present, _env.Owner));
    }

    [Fact]
    public async Task Correcting_present_to_leave_restores_credit_and_grants_makeup_then_back_to_present_reverses_both()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(30));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, TestEnv.TeacherActor(_teacher));
        var attendanceId = await AttendanceIdAsync();
        Assert.Equal(9, _env.Balance(_student));

        await _env.Attendance.CorrectAsync(attendanceId, AttendanceStatus.Leave, "家長前一週已請假", _env.Owner);

        Assert.Equal(10, _env.Balance(_student));
        Assert.Equal(MakeupStatus.Available, await MakeupStatusAsync());

        await _env.Attendance.CorrectAsync(attendanceId, AttendanceStatus.Present, "誤改", _env.Owner);

        Assert.Equal(9, _env.Balance(_student));
        Assert.Equal(MakeupStatus.Voided, await MakeupStatusAsync());
    }

    [Fact]
    public async Task Correcting_present_to_absent_does_not_touch_credits()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(30));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);

        await _env.Attendance.CorrectAsync(await AttendanceIdAsync(), AttendanceStatus.Absent, "記錯", _env.Owner);

        Assert.Equal(9, _env.Balance(_student));
        using var db = _env.Db();
        Assert.Equal(1, await db.CreditEntries.CountAsync(e => e.AttendanceId != null));
    }

    [Fact]
    public async Task Teacher_cannot_correct_attendance()
    {
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        _env.SetNow(ClassStart.AddMinutes(30));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);

        await Assert.ThrowsAsync<ForbiddenException>(() => _env.Attendance.CorrectAsync(
            AttendanceIdAsync().Result, AttendanceStatus.Leave, "x", TestEnv.TeacherActor(_teacher)));
    }

    private async Task<int> AttendanceIdAsync()
    {
        using var db = _env.Db();
        return (await db.Attendances.SingleAsync()).Id;
    }

    private async Task<MakeupStatus> MakeupStatusAsync()
    {
        using var db = _env.Db();
        return (await db.MakeupCredits.SingleAsync()).Status;
    }

    public void Dispose() => _env.Dispose();
}
