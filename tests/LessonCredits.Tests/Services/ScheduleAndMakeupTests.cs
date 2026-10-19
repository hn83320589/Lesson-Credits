using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Tests.Services;

public class ScheduleAndMakeupTests : IDisposable
{
    private readonly TestEnv _env = new();
    private readonly int _teacher;

    public ScheduleAndMakeupTests()
    {
        _teacher = _env.AddTeacher();
    }

    [Fact]
    public async Task Generates_eight_weeks_of_sessions_and_is_idempotent()
    {
        _env.AddCourse(_teacher, CourseType.Group);

        var first = await _env.Schedule.GenerateSessionsAsync();
        var second = await _env.Schedule.GenerateSessionsAsync();

        Assert.Equal(8, first);
        Assert.Equal(0, second);
        using var db = _env.Db();
        var sessions = await db.Sessions.OrderBy(s => s.StartAt).ToListAsync();
        Assert.All(sessions, s => Assert.Equal(DayOfWeek.Tuesday, s.StartAt.DayOfWeek));
        Assert.Equal(new DateTime(2026, 10, 20, 16, 0, 0), sessions[0].StartAt);
    }

    [Fact]
    public async Task Closure_day_cancels_all_sessions_that_day_and_no_credits_are_deducted()
    {
        var student = _env.AddStudent();
        _env.AddBatch(student, new DateOnly(2026, 10, 1), 10, 8000);
        var courseA = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano, student);
        var courseB = _env.AddCourse(_env.AddTeacher("李老師"), CourseType.OneOnOne, Subject.Art);
        await _env.Schedule.GenerateSessionsAsync();
        var holiday = new DateOnly(2026, 10, 27);

        await _env.Schedule.AddClosureDayAsync(holiday, "颱風假", _env.Owner);

        using var db = _env.Db();
        var thatDay = await db.Sessions
            .Where(s => s.StartAt >= holiday.ToDateTime(TimeOnly.MinValue) && s.StartAt < holiday.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .ToListAsync();
        Assert.Equal(2, thatDay.Count);
        Assert.All(thatDay, s => Assert.Equal(SessionStatus.Cancelled, s.Status));

        _env.SetNow(new DateTime(2026, 10, 27, 17, 0, 0));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.RecordAsync(
            thatDay.First(s => s.CourseId == courseA).Id, student, AttendanceStatus.Present, _env.Owner));
        Assert.False(await db.CreditEntries.AnyAsync(e => e.Type == CreditEntryType.Deduct));
        Assert.Equal(10, _env.Balance(student));
        Assert.NotEqual(courseA, courseB);
    }

    [Fact]
    public async Task Closure_day_is_skipped_when_generating_sessions()
    {
        _env.AddCourse(_teacher, CourseType.Group);
        await _env.Schedule.AddClosureDayAsync(new DateOnly(2026, 10, 27), "國定假日", _env.Owner);

        await _env.Schedule.GenerateSessionsAsync();

        using var db = _env.Db();
        Assert.Equal(7, await db.Sessions.CountAsync(s => s.Status == SessionStatus.Scheduled));
    }

    [Fact]
    public async Task Makeup_credit_older_than_60_days_cannot_be_used()
    {
        var (creditId, _) = await CreateMakeupCreditAsync(Subject.Piano);
        var groupCourse = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano);
        // 資格於 2026-10-13 產生，2026-12-12 為第 60 天
        var day61 = _env.AddSession(groupCourse, new DateTime(2026, 12, 13, 16, 0, 0));
        var day60 = _env.AddSession(groupCourse, new DateTime(2026, 12, 12, 16, 0, 0));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _env.Makeup.BookAsync(creditId, day61, _env.Owner));
        var booking = await _env.Makeup.BookAsync(creditId, day60, _env.Owner);

        Assert.Contains("有效期", ex.Message);
        Assert.NotEqual(0, booking.Id);
    }

    [Fact]
    public async Task Expired_makeup_credit_cannot_be_booked_even_for_an_earlier_session()
    {
        var (creditId, _) = await CreateMakeupCreditAsync(Subject.Piano);
        var groupCourse = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano);
        _env.SetNow(new DateTime(2026, 12, 13, 9, 0, 0));
        var session = _env.AddSession(groupCourse, new DateTime(2026, 12, 13, 16, 0, 0));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Makeup.BookAsync(creditId, session, _env.Owner));
    }

    [Fact]
    public async Task Cannot_book_makeup_into_a_full_group_class()
    {
        var (creditId, _) = await CreateMakeupCreditAsync(Subject.Piano);
        var others = Enumerable.Range(1, 4).Select(i => _env.AddStudent($"學生{i}")).ToArray();
        var full = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano, others);
        var session = _env.AddSession(full, new DateTime(2026, 10, 27, 16, 0, 0));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Makeup.BookAsync(creditId, session, _env.Owner));

        Assert.Contains("已滿", ex.Message);
    }

    [Fact]
    public async Task Makeup_bookings_count_towards_capacity()
    {
        var others = Enumerable.Range(1, 3).Select(i => _env.AddStudent($"學生{i}")).ToArray();
        var course = _env.AddCourse(_teacher, CourseType.Group, Subject.Piano, others);
        var session = _env.AddSession(course, new DateTime(2026, 10, 27, 16, 0, 0));
        var (first, _) = await CreateMakeupCreditAsync(Subject.Piano, "甲");
        var (second, _) = await CreateMakeupCreditAsync(Subject.Piano, "乙");

        await _env.Makeup.BookAsync(first, session, _env.Owner);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Makeup.BookAsync(second, session, _env.Owner));
    }

    [Fact]
    public async Task Cannot_book_makeup_into_other_subject_or_one_on_one()
    {
        var (creditId, _) = await CreateMakeupCreditAsync(Subject.Piano);
        var art = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Art), new DateTime(2026, 10, 27, 16, 0, 0));
        var oneOnOne = _env.AddSession(_env.AddCourse(_teacher, CourseType.OneOnOne, Subject.Piano), new DateTime(2026, 10, 27, 17, 0, 0));

        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Makeup.BookAsync(creditId, art, _env.Owner));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Makeup.BookAsync(creditId, oneOnOne, _env.Owner));
    }

    [Fact]
    public async Task Makeup_present_uses_credit_without_deduction()
    {
        var (creditId, student) = await CreateMakeupCreditAsync(Subject.Piano);
        var session = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano), new DateTime(2026, 10, 27, 16, 0, 0));
        await _env.Makeup.BookAsync(creditId, session, _env.Owner);
        var before = _env.Balance(student);
        _env.SetNow(new DateTime(2026, 10, 27, 16, 30, 0));

        await _env.Attendance.RecordAsync(session, student, AttendanceStatus.Present, TestEnv.TeacherActor(_teacher));

        Assert.Equal(before, _env.Balance(student));
        Assert.Equal(MakeupStatus.Used, await CreditStatusAsync(creditId));
    }

    [Fact]
    public async Task Makeup_absent_forfeits_credit_without_deduction()
    {
        var (creditId, student) = await CreateMakeupCreditAsync(Subject.Piano);
        var session = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano), new DateTime(2026, 10, 27, 16, 0, 0));
        await _env.Makeup.BookAsync(creditId, session, _env.Owner);
        var before = _env.Balance(student);
        _env.SetNow(new DateTime(2026, 10, 27, 16, 30, 0));

        await _env.Attendance.RecordAsync(session, student, AttendanceStatus.Absent, _env.Owner);

        Assert.Equal(before, _env.Balance(student));
        Assert.Equal(MakeupStatus.Forfeited, await CreditStatusAsync(creditId));
    }

    [Fact]
    public async Task Leave_from_one_on_one_grants_credit_bookable_only_in_group_class()
    {
        var student = _env.AddStudent("一對一學生");
        _env.AddBatch(student, new DateOnly(2026, 10, 1), 10, 8000);
        var oneOnOne = _env.AddCourse(_teacher, CourseType.OneOnOne, Subject.Piano, student);
        var session = _env.AddSession(oneOnOne, new DateTime(2026, 10, 20, 16, 0, 0));
        await _env.Attendance.RequestLeaveAsync(session, student, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 20, 17, 0, 0));
        await _env.Attendance.RecordAsync(session, student, AttendanceStatus.Leave, _env.Owner);
        using var db = _env.Db();
        var credit = await db.MakeupCredits.SingleAsync();
        var group = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano), new DateTime(2026, 10, 27, 16, 0, 0));

        var bookable = await _env.Makeup.FindBookableSessionsAsync(credit.Id);

        Assert.Equal([group], bookable.Select(s => s.SessionId));
    }

    [Fact]
    public async Task Source_attendance_cannot_be_corrected_back_once_makeup_credit_is_used()
    {
        var (creditId, _) = await CreateMakeupCreditAsync(Subject.Piano);
        var session = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano), new DateTime(2026, 10, 27, 16, 0, 0));
        await _env.Makeup.BookAsync(creditId, session, _env.Owner);
        using var db = _env.Db();
        var sourceAttendanceId = (await db.MakeupCredits.SingleAsync(c => c.Id == creditId)).SourceAttendanceId;

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.CorrectAsync(
            sourceAttendanceId, AttendanceStatus.Present, "改回出席", _env.Owner));

        Assert.Contains("補課", ex.Message);
    }

    /// <summary>於 2026-10-13（週二）的一對一課準時請假，產生一筆補課資格。現在時間設回 2026-10-19。</summary>
    private async Task<(int CreditId, int StudentId)> CreateMakeupCreditAsync(Subject subject, string name = "請假學生")
    {
        var student = _env.AddStudent(name);
        _env.AddBatch(student, new DateOnly(2026, 10, 1), 10, 8000);
        var course = _env.AddCourse(_teacher, CourseType.OneOnOne, subject, student);
        var start = new DateTime(2026, 10, 13, 16, 0, 0);
        var session = _env.AddSession(course, start);
        _env.SetNow(start.AddDays(-2));
        await _env.Attendance.RequestLeaveAsync(session, student, _env.Owner);
        _env.SetNow(start.AddHours(1));
        await _env.Attendance.RecordAsync(session, student, AttendanceStatus.Leave, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 19, 10, 0, 0));

        using var db = _env.Db();
        var credit = await db.MakeupCredits.SingleAsync(c => c.StudentId == student);
        return (credit.Id, student);
    }

    private async Task<MakeupStatus> CreditStatusAsync(int creditId)
    {
        using var db = _env.Db();
        return (await db.MakeupCredits.SingleAsync(c => c.Id == creditId)).Status;
    }

    public void Dispose() => _env.Dispose();
}
