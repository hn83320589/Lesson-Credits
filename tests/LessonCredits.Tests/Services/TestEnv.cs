using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using LessonCredits.Core.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace LessonCredits.Tests.Services;

/// <summary>
/// 每個測試一個獨立的 SQLite 資料庫。
/// 不用 EF InMemory provider，因為它不檢查外鍵與 unique 約束。
/// 預設為 in-memory（所有 DbContext 共用一條連線）；併發測試需要多條連線互相競爭鎖，改用暫存檔。
/// </summary>
public sealed class TestEnv : IDisposable
{
    private readonly SqliteConnection? _sharedConnection;

    public TestEnv(bool useFileDatabase = false, params IInterceptor[] interceptors)
    {
        DbContextOptions<AppDbContext> options;
        if (useFileDatabase)
        {
            // 關閉連線池，結束時才能刪除暫存檔
            ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"lessoncredits-test-{Guid.NewGuid():N}.db")};Pooling=False";
            options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).AddInterceptors(interceptors).Options;
        }
        else
        {
            _sharedConnection = new SqliteConnection("DataSource=:memory:");
            _sharedConnection.Open();
            options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_sharedConnection).AddInterceptors(interceptors).Options;
        }

        Factory = new TestDbContextFactory(options);
        using var db = Factory.CreateDbContext();
        db.Database.EnsureCreated();

        Time = new FakeTimeProvider();
        SetNow(new DateTime(2026, 10, 19, 10, 0, 0));
        Clock = new TaipeiClock(Time);

        Credits = new CreditService(Factory, Clock);
        Schedule = new ScheduleService(Factory, Clock);
        Attendance = new AttendanceService(Factory, Clock);
        Makeup = new MakeupService(Factory, Clock);
        Refunds = new RefundService(Factory, Clock);
        Reports = new ReportService(Factory, Clock);
        Admin = new AdminService(Factory, Clock);
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    /// <summary>暫存檔資料庫的連線字串；in-memory 模式為 null。</summary>
    public string? ConnectionString { get; }
    public FakeTimeProvider Time { get; }
    public TaipeiClock Clock { get; }
    public CreditService Credits { get; }
    public ScheduleService Schedule { get; }
    public AttendanceService Attendance { get; }
    public MakeupService Makeup { get; }
    public RefundService Refunds { get; }
    public ReportService Reports { get; }
    public AdminService Admin { get; }

    public Actor Owner { get; } = new(1, Role.Owner, null);

    public static Actor TeacherActor(int teacherId) => new(100 + teacherId, Role.Teacher, teacherId);

    /// <summary>以台灣時間設定「現在」；AdjustTime 允許時間倒退。</summary>
    public void SetNow(DateTime taipeiLocal) =>
        Time.AdjustTime(new DateTimeOffset(taipeiLocal, TimeSpan.FromHours(8)));

    public AppDbContext Db() => Factory.CreateDbContext();

    public int AddTeacher(string name = "王老師")
    {
        using var db = Db();
        var teacher = new Teacher { Name = name };
        db.Teachers.Add(teacher);
        db.SaveChanges();
        return teacher.Id;
    }

    public int AddStudent(string name = "小明")
    {
        using var db = Db();
        var student = new Student { Name = name, Parent = new Parent { Name = name + "家長" } };
        db.Students.Add(student);
        db.SaveChanges();
        return student.Id;
    }

    public int AddPlan(int lessons, int price)
    {
        using var db = Db();
        var plan = new Plan { Name = $"{lessons} 堂方案", Lessons = lessons, Price = price };
        db.Plans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    /// <summary>直接建立一批點數（可指定過去的購買日與已用點數），用於準備測試資料。</summary>
    public int AddBatch(int studentId, DateOnly purchasedOn, int lessons, int amount, int used = 0)
    {
        using var db = Db();
        var batch = new CreditBatch
        {
            StudentId = studentId,
            PlanName = $"{lessons} 堂",
            PurchasedOn = purchasedOn,
            Lessons = lessons,
            Amount = amount,
            ExpiresOn = purchasedOn.AddMonths(6),
        };
        batch.Entries.Add(new CreditEntry { Type = CreditEntryType.Purchase, Quantity = lessons, OccurredAt = purchasedOn.ToDateTime(TimeOnly.MinValue) });
        if (used > 0)
        {
            batch.Entries.Add(new CreditEntry { Type = CreditEntryType.Deduct, Quantity = -used, OccurredAt = purchasedOn.ToDateTime(TimeOnly.MinValue) });
        }

        db.CreditBatches.Add(batch);
        db.SaveChanges();
        return batch.Id;
    }

    public int AddCourse(int teacherId, CourseType type, Subject subject = Subject.Piano, params int[] studentIds)
    {
        using var db = Db();
        var course = new Course
        {
            Name = $"{subject.Label()}{type.Label()}",
            TeacherId = teacherId,
            Type = type,
            Subject = subject,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(16, 0),
        };
        foreach (var id in studentIds)
        {
            course.Enrollments.Add(new Enrollment { StudentId = id });
        }

        db.Courses.Add(course);
        db.SaveChanges();
        return course.Id;
    }

    public int AddSession(int courseId, DateTime startAt)
    {
        using var db = Db();
        var session = new Session { CourseId = courseId, StartAt = startAt, Status = SessionStatus.Scheduled };
        db.Sessions.Add(session);
        db.SaveChanges();
        return session.Id;
    }

    public void Enroll(int courseId, int studentId)
    {
        using var db = Db();
        db.Enrollments.Add(new Enrollment { CourseId = courseId, StudentId = studentId });
        db.SaveChanges();
    }

    public int Balance(int studentId) => Credits.GetUsableBalanceAsync(studentId).GetAwaiter().GetResult();

    public void Dispose()
    {
        _sharedConnection?.Dispose();
        if (ConnectionString is not null)
        {
            File.Delete(new SqliteConnectionStringBuilder(ConnectionString).DataSource);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
