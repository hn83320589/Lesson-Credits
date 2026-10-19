namespace LessonCredits.Core.Domain;

public class Course
{
    public const int GroupCapacity = 4;

    public int Id { get; set; }
    public required string Name { get; set; }
    public Subject Subject { get; set; }
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public CourseType Type { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public List<Enrollment> Enrollments { get; set; } = [];
    public List<Session> Sessions { get; set; } = [];

    public int Capacity => Type == CourseType.Group ? GroupCapacity : 1;
}

public class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
}

public class Session
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course? Course { get; set; }

    /// <summary>台灣本地時間。</summary>
    public DateTime StartAt { get; set; }

    public SessionStatus Status { get; set; }
    public List<Attendance> Attendances { get; set; } = [];
    public List<MakeupBooking> MakeupBookings { get; set; } = [];

    public DateOnly Date => DateOnly.FromDateTime(StartAt);
}

public class ClosureDay
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = "";
}
