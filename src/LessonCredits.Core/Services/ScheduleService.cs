using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public class ScheduleService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public const int HorizonWeeks = 8;

    /// <summary>為所有課程補齊今天起 8 週內的場次。已存在或停課日會跳過，可重複執行。</summary>
    public async Task<int> GenerateSessionsAsync(int? courseId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var today = clock.Today;
        var end = today.AddDays(HorizonWeeks * 7);
        var from = today.ToDateTime(TimeOnly.MinValue);
        var to = end.ToDateTime(TimeOnly.MinValue);

        var courses = await db.Courses.Where(c => courseId == null || c.Id == courseId).ToListAsync();
        var closures = (await db.ClosureDays.Select(c => c.Date).ToListAsync()).ToHashSet();
        var existing = (await db.Sessions
            .Where(s => s.StartAt >= from && s.StartAt < to)
            .Select(s => new { s.CourseId, s.StartAt })
            .ToListAsync())
            .Select(s => (s.CourseId, s.StartAt))
            .ToHashSet();

        var added = 0;
        foreach (var course in courses)
        {
            for (var date = today; date < end; date = date.AddDays(1))
            {
                if (date.DayOfWeek != course.DayOfWeek || closures.Contains(date))
                {
                    continue;
                }

                var startAt = date.ToDateTime(course.StartTime);
                if (existing.Contains((course.Id, startAt)))
                {
                    continue;
                }

                db.Sessions.Add(new Session { CourseId = course.Id, StartAt = startAt, Status = SessionStatus.Scheduled });
                added++;
            }
        }

        await db.SaveChangesAsync();
        return added;
    }

    /// <summary>標記停課日：當天所有場次取消、補課預約退回。已有點名的日期不可停課，避免默默改帳。</summary>
    public async Task AddClosureDayAsync(DateOnly date, string reason, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.ClosureDays.AnyAsync(c => c.Date == date))
        {
            throw new BusinessRuleException($"{date:yyyy-MM-dd} 已經是停課日。");
        }

        var from = date.ToDateTime(TimeOnly.MinValue);
        var to = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var sessions = await db.Sessions
            .Include(s => s.Attendances)
            .Include(s => s.MakeupBookings).ThenInclude(b => b.MakeupCredit)
            .Where(s => s.StartAt >= from && s.StartAt < to)
            .ToListAsync();

        if (sessions.Any(s => s.Attendances.Count > 0))
        {
            throw new BusinessRuleException("當天已有點名紀錄，請先修正點名後再設定停課。");
        }

        foreach (var session in sessions)
        {
            session.Status = SessionStatus.Cancelled;
            foreach (var booking in session.MakeupBookings.Where(b => !b.IsCancelled))
            {
                booking.IsCancelled = true;
                booking.MakeupCredit!.Status = MakeupStatus.Available;
            }
        }

        db.ClosureDays.Add(new ClosureDay { Date = date, Reason = reason });
        await db.SaveChangesAsync();
    }
}
