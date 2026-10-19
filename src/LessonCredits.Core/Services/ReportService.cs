using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public record ReminderRow(int StudentId, string StudentName, string ParentName, string ParentPhone, ReminderFlags Flags);

public record MonthlyTeacherRow(int TeacherId, string TeacherName, int LessonCount, int DeductedCredits, int Revenue);

public record MonthlyReport(int Year, int Month, IReadOnlyList<MonthlyTeacherRow> Rows)
{
    public int TotalLessons => Rows.Sum(r => r.LessonCount);
    public int TotalCredits => Rows.Sum(r => r.DeductedCredits);
    public int TotalRevenue => Rows.Sum(r => r.Revenue);
}

public class ReportService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    /// <summary>在學學生中，剩餘點數 2 點以下或有點數將在 30 天內到期者。</summary>
    public async Task<List<ReminderRow>> GetRemindersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var students = await db.Students
            .Where(s => s.IsActive)
            .Select(s => new { s.Id, s.Name, ParentName = s.Parent!.Name, s.Parent.Phone })
            .ToListAsync();
        var balances = await CreditLedger.LoadBalancesAsync(db, students.Select(s => s.Id).ToList());
        var today = clock.Today;

        return students
            .Select(s => new ReminderRow(
                s.Id, s.Name, s.ParentName, s.Phone,
                ReminderRules.Evaluate(balances.GetValueOrDefault(s.Id) ?? [], today)))
            .Where(r => r.Flags.NeedsReminder)
            .OrderBy(r => r.Flags.UsableBalance)
            .ThenBy(r => r.Flags.NextExpiry)
            .ToList();
    }

    /// <summary>
    /// 月份一律依「場次日期」歸屬。授課堂數 = 至少一位學生出席的場次；
    /// 點數收入 = 該月場次的扣點分錄（扣除還原）× 該批單價。
    /// </summary>
    public async Task<MonthlyReport> GetMonthlyReportAsync(int year, int month)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);

        var teachers = await db.Teachers.OrderBy(t => t.Id).Select(t => new { t.Id, t.Name }).ToListAsync();

        var lessonCounts = await db.Sessions
            .Where(s => s.StartAt >= from && s.StartAt < to && s.Status == SessionStatus.Scheduled)
            .Where(s => s.Attendances.Any(a => a.Status == AttendanceStatus.Present))
            .GroupBy(s => s.Course!.TeacherId)
            .Select(g => new { TeacherId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeacherId, x => x.Count);

        var entries = await db.CreditEntries
            .Where(e => e.AttendanceId != null
                && (e.Type == CreditEntryType.Deduct || e.Type == CreditEntryType.Restore)
                && e.Attendance!.Session!.StartAt >= from
                && e.Attendance.Session.StartAt < to)
            .Select(e => new
            {
                e.Attendance!.Session!.Course!.TeacherId,
                e.Quantity,
                e.Batch!.Amount,
                e.Batch.Lessons,
            })
            .ToListAsync();

        var revenue = RevenueCalculator.ByTeacher(
            entries.Select(e => new RevenueEntry(e.TeacherId, e.Quantity, (decimal)e.Amount / e.Lessons)));
        var credits = entries.GroupBy(e => e.TeacherId).ToDictionary(g => g.Key, g => -g.Sum(e => e.Quantity));

        var rows = teachers
            .Select(t => new MonthlyTeacherRow(
                t.Id,
                t.Name,
                lessonCounts.GetValueOrDefault(t.Id),
                credits.GetValueOrDefault(t.Id),
                revenue.GetValueOrDefault(t.Id)))
            .ToList();
        return new MonthlyReport(year, month, rows);
    }
}
