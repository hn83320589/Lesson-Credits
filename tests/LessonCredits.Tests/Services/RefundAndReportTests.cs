using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Tests.Services;

public class RefundAndReportTests : IDisposable
{
    private readonly TestEnv _env = new();

    [Fact]
    public async Task Refund_quote_matches_hand_calculation()
    {
        var student = SeedRefundScenario();

        var quote = await _env.Refunds.QuoteAsync(student);

        // 5 × 750 + 10 × 800 + 已過期 1 × 900 不退 = 11,750
        Assert.Equal(11_750, quote.Total);
    }

    [Fact]
    public async Task Confirmed_refund_zeroes_credits_and_blocks_further_attendance()
    {
        var student = SeedRefundScenario();
        var teacher = _env.AddTeacher();
        var course = _env.AddCourse(teacher, CourseType.Group, Subject.Piano, student);
        var session = _env.AddSession(course, new DateTime(2026, 10, 20, 16, 0, 0));

        var refund = await _env.Refunds.ConfirmAsync(student, _env.Owner);

        Assert.Equal(11_750, refund.TotalAmount);
        Assert.Equal(0, _env.Balance(student));
        using var db = _env.Db();
        var remainingIncludingExpired = await db.CreditEntries.Where(e => e.Batch!.StudentId == student).SumAsync(e => e.Quantity);
        Assert.Equal(0, remainingIncludingExpired);
        Assert.Equal(3, await db.RefundLines.CountAsync());

        _env.SetNow(new DateTime(2026, 10, 20, 16, 30, 0));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _env.Attendance.RecordAsync(
            session, student, AttendanceStatus.Present, _env.Owner));
        Assert.Contains("點數不足", ex.Message);
    }

    [Fact]
    public async Task Teacher_cannot_confirm_refund()
    {
        var student = SeedRefundScenario();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _env.Refunds.ConfirmAsync(student, TestEnv.TeacherActor(_env.AddTeacher())));
    }

    [Fact]
    public async Task Reminder_list_covers_low_balance_and_expiring_soon()
    {
        var low = _env.AddStudent("剩一點");
        _env.AddBatch(low, new DateOnly(2026, 10, 1), 10, 8000, used: 9);
        var expiring = _env.AddStudent("快到期");
        _env.AddBatch(expiring, new DateOnly(2026, 5, 10), 10, 8000); // 2026-11-10 到期
        var fine = _env.AddStudent("沒問題");
        _env.AddBatch(fine, new DateOnly(2026, 10, 1), 10, 8000);

        var rows = await _env.Reports.GetRemindersAsync();

        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(r => r.StudentId == low).Flags.LowBalance);
        Assert.True(rows.Single(r => r.StudentId == expiring).Flags.ExpiringSoon);
        Assert.DoesNotContain(rows, r => r.StudentId == fine);
    }

    [Fact]
    public async Task Monthly_report_matches_hand_calculation()
    {
        var teacherA = _env.AddTeacher("A 老師");
        var teacherB = _env.AddTeacher("B 老師");
        var s750 = _env.AddStudent("單價750");
        _env.AddBatch(s750, new DateOnly(2026, 9, 1), 20, 15000);
        var s800 = _env.AddStudent("單價800");
        _env.AddBatch(s800, new DateOnly(2026, 9, 1), 10, 8000);
        var courseA = _env.AddCourse(teacherA, CourseType.Group, Subject.Piano, s750, s800);
        var courseB = _env.AddCourse(teacherB, CourseType.OneOnOne, Subject.Art, s800);

        // A 老師 10 月三堂：750 學生 3 次出席，800 學生 2 次出席、1 次之後被改成請假
        var dates = new[] { new DateTime(2026, 10, 6, 16, 0, 0), new DateTime(2026, 10, 13, 16, 0, 0), new DateTime(2026, 10, 20, 16, 0, 0) };
        int lastAttendanceOf800 = 0;
        foreach (var date in dates)
        {
            var session = _env.AddSession(courseA, date);
            _env.SetNow(date.AddMinutes(30));
            await _env.Attendance.RecordAsync(session, s750, AttendanceStatus.Present, _env.Owner);
            await _env.Attendance.RecordAsync(session, s800, AttendanceStatus.Present, _env.Owner);
            using var db = _env.Db();
            lastAttendanceOf800 = (await db.Attendances.SingleAsync(a => a.SessionId == session && a.StudentId == s800)).Id;
        }

        await _env.Attendance.CorrectAsync(lastAttendanceOf800, AttendanceStatus.Leave, "補登請假", _env.Owner);

        // B 老師：10 月一堂缺席（800），9 月一堂（不列入 10 月）
        var octB = _env.AddSession(courseB, new DateTime(2026, 10, 8, 10, 0, 0));
        var sepB = _env.AddSession(courseB, new DateTime(2026, 9, 24, 10, 0, 0));
        _env.SetNow(new DateTime(2026, 10, 30, 12, 0, 0));
        await _env.Attendance.RecordAsync(octB, s800, AttendanceStatus.Absent, _env.Owner);
        await _env.Attendance.RecordAsync(sepB, s800, AttendanceStatus.Present, _env.Owner);

        var report = await _env.Reports.GetMonthlyReportAsync(2026, 10);

        var a = report.Rows.Single(r => r.TeacherId == teacherA);
        var b = report.Rows.Single(r => r.TeacherId == teacherB);
        Assert.Equal(3, a.LessonCount);
        Assert.Equal(3 * 750 + 2 * 800, a.Revenue);       // 3,850
        Assert.Equal(0, b.LessonCount);                   // 只有缺席，沒有實際授課
        Assert.Equal(800, b.Revenue);                     // 缺席仍扣點，認列收入
        Assert.Equal(4_650, report.TotalRevenue);
    }

    /// <summary>手算範例：A 20 堂 15,000 剩 5、B 10 堂 8,000 剩 10、C 單堂 900 已過期剩 1。</summary>
    private int SeedRefundScenario()
    {
        var student = _env.AddStudent("退費學生");
        _env.AddBatch(student, new DateOnly(2026, 6, 1), 20, 15000, used: 15);
        _env.AddBatch(student, new DateOnly(2026, 9, 1), 10, 8000);
        _env.AddBatch(student, new DateOnly(2026, 3, 1), 1, 900);
        return student;
    }

    public void Dispose() => _env.Dispose();
}
