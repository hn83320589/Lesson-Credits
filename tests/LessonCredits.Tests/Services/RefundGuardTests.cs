using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Tests.Services;

/// <summary>退費後，帳與錢必須對得起來：已退費的點數不能因為修正點名或請假而重新出現。</summary>
public class RefundGuardTests : IDisposable
{
    private static readonly DateTime ClassStart = new(2026, 10, 20, 16, 0, 0);

    private readonly TestEnv _env = new();
    private readonly int _student;
    private readonly int _session;

    public RefundGuardTests()
    {
        var teacher = _env.AddTeacher();
        _student = _env.AddStudent();
        _env.AddBatch(_student, new DateOnly(2026, 10, 1), 10, 8000);
        var course = _env.AddCourse(teacher, CourseType.Group, Subject.Piano, _student);
        _session = _env.AddSession(course, ClassStart);
    }

    [Fact]
    public async Task Correction_that_changes_credits_is_rejected_after_refund()
    {
        // 10/20 出席扣點 → 10/25 退費 → 10/26 店長想把 10/20 改成請假
        _env.SetNow(ClassStart.AddMinutes(30));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 25, 10, 0, 0));
        var refund = await _env.Refunds.ConfirmAsync(_student, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 26, 10, 0, 0));
        var attendanceId = await AttendanceIdAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.CorrectAsync(
            attendanceId, AttendanceStatus.Leave, "家長說其實有請假", _env.Owner));

        Assert.Contains("退費", ex.Message);
        Assert.Equal(9 * 800, refund.TotalAmount);
        using var db = _env.Db();
        Assert.Equal(0, await db.CreditEntries.Where(e => e.Batch!.StudentId == _student).SumAsync(e => e.Quantity));
        Assert.Empty(db.MakeupCredits);
    }

    [Fact]
    public async Task Correction_that_does_not_change_credits_is_still_allowed_after_refund()
    {
        _env.SetNow(ClassStart.AddMinutes(30));
        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 25, 10, 0, 0));
        await _env.Refunds.ConfirmAsync(_student, _env.Owner);

        await _env.Attendance.CorrectAsync(await AttendanceIdAsync(), AttendanceStatus.Absent, "記錯", _env.Owner);

        using var db = _env.Db();
        Assert.Equal(AttendanceStatus.Absent, (await db.Attendances.SingleAsync()).Status);
        Assert.Equal(0, await db.CreditEntries.Where(e => e.Batch!.StudentId == _student).SumAsync(e => e.Quantity));
    }

    [Fact]
    public async Task Refunded_student_cannot_be_recorded_even_as_leave()
    {
        // 準時請假後才退費；「請假」不需要點數，只看餘額擋不住，必須檢查在學狀態
        _env.SetNow(ClassStart.AddDays(-2));
        await _env.Attendance.RequestLeaveAsync(_session, _student, _env.Owner);
        await _env.Refunds.ConfirmAsync(_student, _env.Owner);
        _env.SetNow(ClassStart.AddMinutes(30));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.RecordAsync(
            _session, _student, AttendanceStatus.Leave, _env.Owner));

        Assert.Contains("退費", ex.Message);
        using var db = _env.Db();
        Assert.Empty(db.MakeupCredits);
    }

    [Fact]
    public async Task Refunded_student_cannot_request_leave()
    {
        await _env.Refunds.ConfirmAsync(_student, _env.Owner);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Attendance.RequestLeaveAsync(_session, _student, _env.Owner));

        Assert.Contains("退費", ex.Message);
    }

    [Theory]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.Absent)]
    [InlineData(AttendanceStatus.Leave)]
    public async Task Session_before_refund_cannot_be_recorded_late_even_after_repurchase(AttendanceStatus status)
    {
        // 10/20 當天沒點名 → 10/25 退費 → 10/26 重新購買（恢復在學）→ 10/27 補點 10/20
        _env.SetNow(ClassStart.AddDays(-2));
        await _env.Attendance.RequestLeaveAsync(_session, _student, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 25, 10, 0, 0));
        await _env.Refunds.ConfirmAsync(_student, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 26, 10, 0, 0));
        await _env.Credits.PurchaseAsync(_student, _env.AddPlan(10, 8000), _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 27, 10, 0, 0));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Attendance.RecordAsync(_session, _student, status, _env.Owner));

        Assert.Contains("退費", ex.Message);
        Assert.Equal(10, _env.Balance(_student));
        using var db = _env.Db();
        Assert.Empty(db.Attendances);
        Assert.Empty(db.MakeupCredits);
    }

    [Fact]
    public async Task Session_after_repurchase_can_be_recorded_normally()
    {
        _env.SetNow(new DateTime(2026, 10, 15, 10, 0, 0));
        await _env.Refunds.ConfirmAsync(_student, _env.Owner);
        await _env.Credits.PurchaseAsync(_student, _env.AddPlan(10, 8000), _env.Owner);
        _env.SetNow(ClassStart.AddMinutes(30));

        await _env.Attendance.RecordAsync(_session, _student, AttendanceStatus.Present, _env.Owner);

        Assert.Equal(9, _env.Balance(_student));
    }

    private async Task<int> AttendanceIdAsync()
    {
        using var db = _env.Db();
        return (await db.Attendances.SingleAsync()).Id;
    }

    public void Dispose() => _env.Dispose();
}
