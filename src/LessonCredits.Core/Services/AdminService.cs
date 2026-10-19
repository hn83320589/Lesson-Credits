using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public record StudentListRow(int Id, string Name, int ParentId, string ParentName, bool IsActive, int UsableBalance, DateOnly? NextExpiry);

public record BatchView(
    int Id, string PlanName, DateOnly PurchasedOn, DateOnly ExpiresOn, int Lessons, int Amount,
    decimal UnitPrice, int Remaining, bool IsExpired);

public record EntryView(
    DateTime OccurredAt, CreditEntryType Type, int Quantity, int BatchId, string PlanName,
    string? CourseName, DateTime? SessionStartAt, AttendanceStatus? AttendanceStatus);

public record AttendanceView(
    int AttendanceId, DateTime SessionStartAt, string CourseName, AttendanceStatus Status, bool IsMakeup, string? CorrectionNote);

public record RefundView(int Id, DateTime ConfirmedAt, int TotalAmount);

public record StudentDetail(
    int Id, string Name, string ParentName, string ParentPhone, bool IsActive, int UsableBalance,
    IReadOnlyList<BatchView> Batches, IReadOnlyList<EntryView> Entries,
    IReadOnlyList<AttendanceView> Attendances, IReadOnlyList<RefundView> Refunds);

public record CourseRow(
    int Id, string Name, Subject Subject, CourseType Type, int TeacherId, string TeacherName,
    DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes, int Capacity, IReadOnlyList<(int Id, string Name)> Students);

public record NewCourse(string Name, Subject Subject, CourseType Type, int TeacherId, DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes);

/// <summary>基本資料維護與查詢：家長、學生、方案、課程、停課日。皆限店長。</summary>
public class AdminService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public async Task<List<Parent>> ListParentsAsync(Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Parents.Include(p => p.Students).OrderBy(p => p.Name).AsNoTracking().ToListAsync();
    }

    public async Task<Parent> AddParentAsync(string name, string phone, Actor actor)
    {
        actor.EnsureOwner();
        RequireText(name, "家長姓名");
        await using var db = await dbFactory.CreateDbContextAsync();
        var parent = new Parent { Name = name.Trim(), Phone = phone.Trim() };
        db.Parents.Add(parent);
        await db.SaveChangesAsync();
        return parent;
    }

    public async Task<Student> AddStudentAsync(int parentId, string name, Actor actor)
    {
        actor.EnsureOwner();
        RequireText(name, "學生姓名");
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Parents.AnyAsync(p => p.Id == parentId))
        {
            throw new NotFoundException("找不到家長。");
        }

        var student = new Student { ParentId = parentId, Name = name.Trim() };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    public async Task<List<StudentListRow>> ListStudentsAsync(Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        var students = await db.Students
            .OrderBy(s => s.Parent!.Name).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.ParentId, ParentName = s.Parent!.Name, s.IsActive })
            .ToListAsync();
        var balances = await CreditLedger.LoadBalancesAsync(db, students.Select(s => s.Id).ToList());
        var today = clock.Today;

        return students
            .Select(s =>
            {
                var flags = ReminderRules.Evaluate(balances.GetValueOrDefault(s.Id) ?? [], today);
                return new StudentListRow(s.Id, s.Name, s.ParentId, s.ParentName, s.IsActive, flags.UsableBalance, flags.NextExpiry);
            })
            .ToList();
    }

    public async Task<StudentDetail> GetStudentDetailAsync(int studentId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        var student = await db.Students.Include(s => s.Parent).AsNoTracking().SingleOrDefaultAsync(s => s.Id == studentId)
            ?? throw new NotFoundException("找不到學生。");
        var today = clock.Today;

        var batchRows = await db.CreditBatches
            .Where(b => b.StudentId == studentId)
            .OrderBy(b => b.PurchasedOn)
            .Select(b => new { b.Id, b.PlanName, b.PurchasedOn, b.ExpiresOn, b.Lessons, b.Amount, Remaining = b.Entries.Sum(e => e.Quantity) })
            .ToListAsync();
        var batches = batchRows
            .Select(b => new BatchView(
                b.Id, b.PlanName, b.PurchasedOn, b.ExpiresOn, b.Lessons, b.Amount, (decimal)b.Amount / b.Lessons,
                b.Remaining, !CreditRules.IsUsable(b.ExpiresOn, today)))
            .ToList();

        var entries = await db.CreditEntries
            .Where(e => e.Batch!.StudentId == studentId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Select(e => new EntryView(
                e.OccurredAt, e.Type, e.Quantity, e.BatchId, e.Batch!.PlanName,
                e.Attendance == null ? null : e.Attendance.Session!.Course!.Name,
                e.Attendance == null ? null : e.Attendance.Session!.StartAt,
                e.Attendance == null ? null : e.Attendance.Status))
            .ToListAsync();

        var attendances = await db.Attendances
            .Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.Session!.StartAt)
            .Select(a => new AttendanceView(
                a.Id, a.Session!.StartAt, a.Session.Course!.Name, a.Status, a.MakeupBookingId != null, a.CorrectionNote))
            .ToListAsync();

        var refunds = await db.Refunds
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.ConfirmedAt)
            .Select(r => new RefundView(r.Id, r.ConfirmedAt, r.TotalAmount))
            .ToListAsync();

        var usable = batches.Where(b => !b.IsExpired).Sum(b => b.Remaining);
        return new StudentDetail(
            student.Id, student.Name, student.Parent!.Name, student.Parent.Phone, student.IsActive, usable,
            batches, entries, attendances, refunds);
    }

    public async Task<List<Plan>> ListPlansAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Plans.OrderBy(p => p.Lessons).AsNoTracking().ToListAsync();
    }

    /// <summary>新增或修改方案。調整價格不影響已購買的批次，因為批次在購買時已快照金額。</summary>
    public async Task SavePlanAsync(int? id, string name, int lessons, int price, bool isActive, Actor actor)
    {
        actor.EnsureOwner();
        RequireText(name, "方案名稱");
        if (lessons <= 0 || price < 0)
        {
            throw new BusinessRuleException("堂數必須大於 0，金額不可為負數。");
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var plan = id is null ? new Plan { Name = "" } : await db.Plans.FindAsync(id) ?? throw new NotFoundException("找不到方案。");
        plan.Name = name.Trim();
        plan.Lessons = lessons;
        plan.Price = price;
        plan.IsActive = isActive;
        if (id is null)
        {
            db.Plans.Add(plan);
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<Teacher>> ListTeachersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Teachers.OrderBy(t => t.Id).AsNoTracking().ToListAsync();
    }

    public async Task<List<CourseRow>> ListCoursesAsync(Actor actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var courses = await db.Courses
            .Where(c => actor.Role == Role.Owner || c.TeacherId == actor.TeacherId)
            .Include(c => c.Teacher)
            .Include(c => c.Enrollments).ThenInclude(e => e.Student)
            .OrderBy(c => c.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)c.DayOfWeek).ThenBy(c => c.StartTime)
            .AsNoTracking()
            .ToListAsync();

        return courses
            .Select(c => new CourseRow(
                c.Id, c.Name, c.Subject, c.Type, c.TeacherId, c.Teacher!.Name, c.DayOfWeek, c.StartTime, c.DurationMinutes, c.Capacity,
                c.Enrollments.Select(e => (e.StudentId, e.Student!.Name)).OrderBy(s => s.Name).ToList()))
            .ToList();
    }

    public async Task<Course> AddCourseAsync(NewCourse input, Actor actor)
    {
        actor.EnsureOwner();
        RequireText(input.Name, "課程名稱");
        await using var db = await dbFactory.CreateDbContextAsync();
        var course = new Course
        {
            Name = input.Name.Trim(),
            Subject = input.Subject,
            Type = input.Type,
            TeacherId = input.TeacherId,
            DayOfWeek = input.DayOfWeek,
            StartTime = input.StartTime,
            DurationMinutes = input.DurationMinutes,
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    public async Task EnrollAsync(int courseId, int studentId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        var course = await db.Courses.Include(c => c.Enrollments).SingleOrDefaultAsync(c => c.Id == courseId)
            ?? throw new NotFoundException("找不到課程。");
        if (course.Enrollments.Any(e => e.StudentId == studentId))
        {
            throw new BusinessRuleException("學生已在課程名單中。");
        }

        // 未來場次的補課預約也佔名額
        var now = clock.Now;
        var maxBookings = await db.Sessions
            .Where(s => s.CourseId == courseId && s.StartAt > now)
            .Select(s => s.MakeupBookings.Count(b => !b.IsCancelled))
            .OrderByDescending(n => n)
            .FirstOrDefaultAsync();
        if (course.Enrollments.Count + maxBookings >= course.Capacity)
        {
            throw new BusinessRuleException($"課程已滿（上限 {course.Capacity} 人，含已預約的補課）。");
        }

        db.Enrollments.Add(new Enrollment { CourseId = courseId, StudentId = studentId });
        await db.SaveChangesAsync();
    }

    public async Task UnenrollAsync(int courseId, int studentId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.CourseId == courseId && e.StudentId == studentId)
            ?? throw new NotFoundException("學生不在課程名單中。");
        db.Enrollments.Remove(enrollment);
        await db.SaveChangesAsync();
    }

    public async Task<List<ClosureDay>> ListClosureDaysAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ClosureDays.OrderByDescending(c => c.Date).AsNoTracking().ToListAsync();
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleException($"請輸入{field}。");
        }
    }
}
