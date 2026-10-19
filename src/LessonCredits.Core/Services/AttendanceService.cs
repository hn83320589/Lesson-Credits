using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public record LeaveInfo(DateTime RequestedAt, bool IsOnTime);

public record RosterRow(
    int StudentId,
    string StudentName,
    bool IsMakeup,
    int? AttendanceId,
    AttendanceStatus? Status,
    string? CorrectionNote,
    LeaveInfo? Leave,
    int UsableBalance);

public record SessionRoster(
    int SessionId,
    string CourseName,
    Subject Subject,
    CourseType CourseType,
    string TeacherName,
    DateTime StartAt,
    SessionStatus Status,
    bool CanRecord,
    bool CanRequestLeave,
    IReadOnlyList<RosterRow> Rows);

public record SessionSummary(
    int SessionId,
    string CourseName,
    string TeacherName,
    CourseType CourseType,
    DateTime StartAt,
    SessionStatus Status,
    int StudentCount,
    int RecordedCount);

public class AttendanceService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public async Task<List<SessionSummary>> ListSessionsAsync(DateOnly from, DateOnly to, Actor actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return await db.Sessions
            .Where(s => s.StartAt >= start && s.StartAt < end)
            .Where(s => actor.Role == Role.Owner || s.Course!.TeacherId == actor.TeacherId)
            .OrderBy(s => s.StartAt)
            .Select(s => new SessionSummary(
                s.Id,
                s.Course!.Name,
                s.Course.Teacher!.Name,
                s.Course.Type,
                s.StartAt,
                s.Status,
                s.Course.Enrollments.Count + s.MakeupBookings.Count(b => !b.IsCancelled),
                s.Attendances.Count))
            .ToListAsync();
    }

    public async Task<SessionRoster> GetRosterAsync(int sessionId, Actor actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var session = await db.Sessions
            .Include(s => s.Course!).ThenInclude(c => c.Teacher)
            .Include(s => s.Course!).ThenInclude(c => c.Enrollments).ThenInclude(e => e.Student)
            .Include(s => s.MakeupBookings.Where(b => !b.IsCancelled)).ThenInclude(b => b.MakeupCredit!).ThenInclude(m => m.Student)
            .Include(s => s.Attendances)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new NotFoundException("找不到場次。");
        actor.EnsureCanAccessCourse(session.Course!.TeacherId);

        var leaves = await db.LeaveRequests.Where(l => l.SessionId == sessionId).ToListAsync();
        var students = session.Course.Enrollments.Select(e => (e.Student!, IsMakeup: false))
            .Concat(session.MakeupBookings.Select(b => (b.MakeupCredit!.Student!, IsMakeup: true)))
            .ToList();
        var balances = await CreditLedger.LoadBalancesAsync(db, students.Select(s => s.Item1.Id).ToList());

        var rows = students
            .Select(s =>
            {
                var attendance = session.Attendances.SingleOrDefault(a => a.StudentId == s.Item1.Id);
                var leave = leaves.SingleOrDefault(l => l.StudentId == s.Item1.Id);
                return new RosterRow(
                    s.Item1.Id,
                    s.Item1.Name,
                    s.IsMakeup,
                    attendance?.Id,
                    attendance?.Status,
                    attendance?.CorrectionNote,
                    leave is null ? null : new LeaveInfo(leave.RequestedAt, leave.IsOnTime),
                    CreditRules.UsableBalance(balances.GetValueOrDefault(s.Item1.Id) ?? [], session.Date));
            })
            .OrderBy(r => r.IsMakeup)
            .ThenBy(r => r.StudentName)
            .ToList();

        var now = clock.Now;
        var scheduled = session.Status == SessionStatus.Scheduled;
        return new SessionRoster(
            session.Id,
            session.Course.Name,
            session.Course.Subject,
            session.Course.Type,
            session.Course.Teacher!.Name,
            session.StartAt,
            session.Status,
            CanRecord: scheduled && now >= session.StartAt,
            CanRequestLeave: scheduled && now < session.StartAt,
            rows);
    }

    /// <summary>登記請假。請假時間由系統蓋章，並在此刻判定是否符合 24 小時前提出。</summary>
    public async Task<LeaveRequest> RequestLeaveAsync(int sessionId, int studentId, Actor actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var session = await LoadSessionAsync(db, sessionId, actor);
        var now = clock.Now;
        if (now >= session.StartAt)
        {
            throw new BusinessRuleException("已經開始上課，無法請假。");
        }

        if (!session.Course!.Enrollments.Any(e => e.StudentId == studentId))
        {
            throw new BusinessRuleException("只有本課程的固定學生可以請假；補課預約請改用取消預約。");
        }

        if (await db.LeaveRequests.AnyAsync(l => l.SessionId == sessionId && l.StudentId == studentId))
        {
            throw new BusinessRuleException("此學生已登記請假。");
        }

        var leave = new LeaveRequest
        {
            SessionId = sessionId,
            StudentId = studentId,
            RequestedAt = now,
            IsOnTime = LeavePolicy.IsOnTime(session.StartAt, now),
        };
        db.LeaveRequests.Add(leave);
        await db.SaveChangesAsync();
        return leave;
    }

    public async Task RecordAsync(int sessionId, int studentId, AttendanceStatus status, Actor actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var session = await LoadSessionAsync(db, sessionId, actor);
        var now = clock.Now;
        if (now < session.StartAt)
        {
            throw new BusinessRuleException("尚未開始上課，無法點名。");
        }

        if (await db.Attendances.AnyAsync(a => a.SessionId == sessionId && a.StudentId == studentId))
        {
            throw new BusinessRuleException("此學生已點名；如需修改請由店長修正。");
        }

        var attendance = new Attendance
        {
            SessionId = sessionId,
            StudentId = studentId,
            Status = status,
            RecordedByUserId = actor.UserId,
            RecordedAt = now,
        };

        var booking = await db.MakeupBookings
            .Include(b => b.MakeupCredit)
            .SingleOrDefaultAsync(b => b.SessionId == sessionId && !b.IsCancelled && b.MakeupCredit!.StudentId == studentId);
        if (booking is not null)
        {
            RecordMakeup(attendance, booking, status);
        }
        else if (session.Course!.Enrollments.Any(e => e.StudentId == studentId))
        {
            await RecordRegularAsync(db, attendance, session, status, now);
        }
        else
        {
            throw new BusinessRuleException("此學生不在本場次名單中。");
        }

        db.Attendances.Add(attendance);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 店長修正點名：先撤銷舊狀態的效果，再套用新狀態的效果。
    /// 修正代表承認原紀錄有誤，因此改為「請假」時不受 24 小時限制。
    /// </summary>
    public async Task CorrectAsync(int attendanceId, AttendanceStatus newStatus, string note, Actor actor)
    {
        actor.EnsureOwner();
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new BusinessRuleException("請填寫修正原因。");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var attendance = await db.Attendances
            .Include(a => a.Session!).ThenInclude(s => s.Course)
            .Include(a => a.MakeupBooking!).ThenInclude(b => b.MakeupCredit)
            .SingleOrDefaultAsync(a => a.Id == attendanceId)
            ?? throw new NotFoundException("找不到點名紀錄。");
        var oldStatus = attendance.Status;
        if (oldStatus == newStatus)
        {
            throw new BusinessRuleException("狀態沒有變更。");
        }

        var now = clock.Now;
        if (attendance.IsMakeup)
        {
            RecordMakeup(attendance, attendance.MakeupBooking!, newStatus);
        }
        else
        {
            await ApplyCorrectionEffectsAsync(db, attendance, oldStatus, newStatus, now);
        }

        attendance.Status = newStatus;
        attendance.CorrectedAt = now;
        attendance.CorrectionNote = $"{oldStatus.Label()} → {newStatus.Label()}：{note.Trim()}";
        await db.SaveChangesAsync();
    }

    private async Task ApplyCorrectionEffectsAsync(
        AppDbContext db, Attendance attendance, AttendanceStatus oldStatus, AttendanceStatus newStatus, DateTime now)
    {
        var oldDeducts = AttendanceEffects.Deducts(oldStatus, isMakeup: false);
        var newDeducts = AttendanceEffects.Deducts(newStatus, isMakeup: false);
        var oldGrants = AttendanceEffects.GrantsMakeup(oldStatus, isMakeup: false);
        var newGrants = AttendanceEffects.GrantsMakeup(newStatus, isMakeup: false);

        if (oldGrants && !newGrants)
        {
            var credit = await db.MakeupCredits.SingleAsync(
                m => m.SourceAttendanceId == attendance.Id && m.Status != MakeupStatus.Voided);
            if (credit.Status != MakeupStatus.Available)
            {
                throw new BusinessRuleException(
                    $"此請假產生的補課資格目前為「{credit.Status.Label()}」，無法修正；請先取消補課預約。");
            }

            credit.Status = MakeupStatus.Voided;
        }

        if (oldDeducts && !newDeducts)
        {
            await CreditLedger.RestoreAsync(db, attendance, now);
        }
        else if (!oldDeducts && newDeducts)
        {
            await CreditLedger.DeductAsync(db, attendance, attendance.Session!.Date, now);
        }

        if (newGrants && !oldGrants)
        {
            // 修正產生的資格自修正日起算，避免店家延誤修正讓家長吃虧
            GrantMakeup(db, attendance, attendance.Session!.Course!.Subject, DateOnly.FromDateTime(now));
        }
    }

    private static async Task RecordRegularAsync(
        AppDbContext db, Attendance attendance, Session session, AttendanceStatus status, DateTime now)
    {
        if (status == AttendanceStatus.Leave)
        {
            var leave = await db.LeaveRequests.SingleOrDefaultAsync(
                l => l.SessionId == session.Id && l.StudentId == attendance.StudentId);
            if (leave is null || !leave.IsOnTime)
            {
                var detail = leave is null ? "沒有請假紀錄" : $"請假時間 {leave.RequestedAt:MM/dd HH:mm}";
                throw new BusinessRuleException($"未於上課 24 小時前請假（{detail}），請登記為缺席。");
            }

            GrantMakeup(db, attendance, session.Course!.Subject, session.Date);
            return;
        }

        await CreditLedger.DeductAsync(db, attendance, session.Date, now);
    }

    private static void RecordMakeup(Attendance attendance, MakeupBooking booking, AttendanceStatus status)
    {
        booking.MakeupCredit!.Status = status switch
        {
            AttendanceStatus.Present => MakeupStatus.Used,
            AttendanceStatus.Absent => MakeupStatus.Forfeited,
            _ => throw new BusinessRuleException("補課場次只能登記出席或缺席。"),
        };
        attendance.MakeupBookingId = booking.Id;
    }

    private static void GrantMakeup(AppDbContext db, Attendance attendance, Subject subject, DateOnly issuedOn)
    {
        db.MakeupCredits.Add(new MakeupCredit
        {
            StudentId = attendance.StudentId,
            Subject = subject,
            SourceAttendance = attendance,
            IssuedOn = issuedOn,
            ExpiresOn = MakeupPolicy.ExpiryDate(issuedOn),
            Status = MakeupStatus.Available,
        });
    }

    private static async Task<Session> LoadSessionAsync(AppDbContext db, int sessionId, Actor actor)
    {
        var session = await db.Sessions
            .Include(s => s.Course!).ThenInclude(c => c.Enrollments)
            .SingleOrDefaultAsync(s => s.Id == sessionId)
            ?? throw new NotFoundException("找不到場次。");
        actor.EnsureCanAccessCourse(session.Course!.TeacherId);
        if (session.Status == SessionStatus.Cancelled)
        {
            throw new BusinessRuleException("此場次已停課。");
        }

        return session;
    }
}
