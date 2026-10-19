using LessonCredits.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<CreditBatch> CreditBatches => Set<CreditBatch>();
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundLine> RefundLines => Set<RefundLine>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ClosureDay> ClosureDays => Set<ClosureDay>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<MakeupCredit> MakeupCredits => Set<MakeupCredit>();
    public DbSet<MakeupBooking> MakeupBookings => Set<MakeupBooking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // 列舉以字串儲存，直接查資料庫時也看得懂
        builder.Properties<Role>().HaveConversion<string>();
        builder.Properties<Subject>().HaveConversion<string>();
        builder.Properties<CourseType>().HaveConversion<string>();
        builder.Properties<SessionStatus>().HaveConversion<string>();
        builder.Properties<AttendanceStatus>().HaveConversion<string>();
        builder.Properties<CreditEntryType>().HaveConversion<string>();
        builder.Properties<MakeupStatus>().HaveConversion<string>();
        builder.Properties<DayOfWeek>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(u => u.UserName).IsUnique();
        model.Entity<Session>().HasIndex(s => new { s.CourseId, s.StartAt }).IsUnique();
        model.Entity<Session>().HasIndex(s => s.StartAt);
        model.Entity<Enrollment>().HasIndex(e => new { e.CourseId, e.StudentId }).IsUnique();
        model.Entity<Attendance>().HasIndex(a => new { a.SessionId, a.StudentId }).IsUnique();
        model.Entity<LeaveRequest>().HasIndex(l => new { l.SessionId, l.StudentId }).IsUnique();
        model.Entity<ClosureDay>().HasIndex(c => c.Date).IsUnique();

        model.Entity<Refund>().HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RefundId);
        model.Entity<CreditEntry>().HasOne<Refund>().WithMany().HasForeignKey(e => e.RefundId);
        model.Entity<MakeupCredit>()
            .HasOne(m => m.SourceAttendance).WithMany().HasForeignKey(m => m.SourceAttendanceId);
        model.Entity<Attendance>()
            .HasOne(a => a.MakeupBooking).WithMany().HasForeignKey(a => a.MakeupBookingId);
        model.Entity<Session>()
            .HasMany(s => s.MakeupBookings).WithOne(b => b.Session).HasForeignKey(b => b.SessionId);

        // 帳務資料不允許連鎖刪除
        foreach (var fk in model.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
