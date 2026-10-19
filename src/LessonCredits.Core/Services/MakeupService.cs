using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public record BookableSession(int SessionId, string CourseName, string TeacherName, DateTime StartAt, int SeatsTaken);

public record MakeupCreditView(
    int Id,
    int StudentId,
    string StudentName,
    Subject Subject,
    DateOnly IssuedOn,
    DateOnly ExpiresOn,
    MakeupStatus Status,
    bool IsExpired,
    int? BookingId,
    DateTime? BookedSessionStartAt,
    string? BookedCourseName);

public class MakeupService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public async Task<List<MakeupCreditView>> ListCreditsAsync(int? studentId = null, bool openOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var today = clock.Today;
        var credits = await db.MakeupCredits
            .Where(m => studentId == null || m.StudentId == studentId)
            .Where(m => !openOnly || m.Status == MakeupStatus.Available || m.Status == MakeupStatus.Booked)
            .OrderBy(m => m.ExpiresOn)
            .Select(m => new
            {
                m.Id,
                m.StudentId,
                StudentName = m.Student!.Name,
                m.Subject,
                m.IssuedOn,
                m.ExpiresOn,
                m.Status,
                Booking = m.Bookings
                    .Where(b => !b.IsCancelled)
                    .Select(b => new { b.Id, b.Session!.StartAt, CourseName = b.Session.Course!.Name })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return credits
            .Select(m => new MakeupCreditView(
                m.Id, m.StudentId, m.StudentName, m.Subject, m.IssuedOn, m.ExpiresOn, m.Status,
                IsExpired: m.Status == MakeupStatus.Available && !MakeupPolicy.IsValidOn(m.ExpiresOn, today),
                m.Booking?.Id, m.Booking?.StartAt, m.Booking?.CourseName))
            .ToList();
    }

    /// <summary>同科目、小班、尚未開始、在資格有效期內且仍有空位的場次。</summary>
    public async Task<List<BookableSession>> FindBookableSessionsAsync(int creditId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var credit = await db.MakeupCredits.FindAsync(creditId) ?? throw new NotFoundException("找不到補課資格。");
        var now = clock.Now;
        var lastStart = credit.ExpiresOn.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var candidates = await db.Sessions
            .Where(s => s.Status == SessionStatus.Scheduled
                && s.StartAt > now
                && s.StartAt < lastStart
                && s.Course!.Type == CourseType.Group
                && s.Course.Subject == credit.Subject
                && !s.Course.Enrollments.Any(e => e.StudentId == credit.StudentId)
                && !s.MakeupBookings.Any(b => !b.IsCancelled && b.MakeupCredit!.StudentId == credit.StudentId))
            .OrderBy(s => s.StartAt)
            .Select(s => new BookableSession(
                s.Id,
                s.Course!.Name,
                s.Course.Teacher!.Name,
                s.StartAt,
                s.Course.Enrollments.Count + s.MakeupBookings.Count(b => !b.IsCancelled)))
            .ToListAsync();

        return candidates.Where(s => s.SeatsTaken < Course.GroupCapacity).ToList();
    }

    public async Task<MakeupBooking> BookAsync(int creditId, int sessionId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.BeginWriteTransactionAsync();
        var credit = await db.MakeupCredits.FindAsync(creditId) ?? throw new NotFoundException("找不到補課資格。");
        var session = await db.Sessions
            .Include(s => s.Course!).ThenInclude(c => c.Enrollments)
            .Include(s => s.MakeupBookings.Where(b => !b.IsCancelled)).ThenInclude(b => b.MakeupCredit)
            .SingleOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new NotFoundException("找不到場次。");
        var course = session.Course!;

        if (credit.Status != MakeupStatus.Available)
        {
            throw new BusinessRuleException($"此補課資格目前為「{credit.Status.Label()}」，無法預約。");
        }

        if (!MakeupPolicy.IsValidOn(credit.ExpiresOn, clock.Today) || !MakeupPolicy.IsValidOn(credit.ExpiresOn, session.Date))
        {
            throw new BusinessRuleException($"補課資格有效期至 {credit.ExpiresOn:yyyy-MM-dd}，超過 60 天無法使用。");
        }

        if (course.Type != CourseType.Group)
        {
            throw new BusinessRuleException("補課只能預約小班場次。");
        }

        if (course.Subject != credit.Subject)
        {
            throw new BusinessRuleException($"補課需為同科目（{credit.Subject.Label()}）。");
        }

        if (session.Status != SessionStatus.Scheduled || session.StartAt <= clock.Now)
        {
            throw new BusinessRuleException("此場次已停課或已開始，無法預約。");
        }

        if (course.Enrollments.Any(e => e.StudentId == credit.StudentId)
            || session.MakeupBookings.Any(b => b.MakeupCredit!.StudentId == credit.StudentId))
        {
            throw new BusinessRuleException("學生已在此場次名單中。");
        }

        if (course.Enrollments.Count + session.MakeupBookings.Count >= Course.GroupCapacity)
        {
            throw new BusinessRuleException($"此小班已滿 {Course.GroupCapacity} 人，無法預約補課。");
        }

        var booking = new MakeupBooking { MakeupCreditId = creditId, SessionId = sessionId };
        db.MakeupBookings.Add(booking);
        credit.Status = MakeupStatus.Booked;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return booking;
    }

    public async Task CancelBookingAsync(int bookingId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.BeginWriteTransactionAsync();
        var booking = await db.MakeupBookings
            .Include(b => b.MakeupCredit)
            .Include(b => b.Session)
            .SingleOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException("找不到補課預約。");
        if (booking.IsCancelled || booking.MakeupCredit!.Status != MakeupStatus.Booked)
        {
            throw new BusinessRuleException("此預約已取消或已點名。");
        }

        if (booking.Session!.StartAt <= clock.Now)
        {
            throw new BusinessRuleException("補課已開始，無法取消。");
        }

        booking.IsCancelled = true;
        booking.MakeupCredit.Status = MakeupStatus.Available;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
