namespace LessonCredits.Core.Domain;

/// <summary>請假登記。<see cref="RequestedAt"/> 由系統以 TimeProvider 蓋章，不可手動輸入。</summary>
public class LeaveRequest
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int StudentId { get; set; }
    public DateTime RequestedAt { get; set; }
    public bool IsOnTime { get; set; }
}

public class Attendance
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public AttendanceStatus Status { get; set; }
    public int? MakeupBookingId { get; set; }
    public MakeupBooking? MakeupBooking { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime? CorrectedAt { get; set; }
    public string? CorrectionNote { get; set; }

    public bool IsMakeup => MakeupBookingId is not null;
}

public class MakeupCredit
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public Subject Subject { get; set; }
    public int SourceAttendanceId { get; set; }
    public Attendance? SourceAttendance { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public MakeupStatus Status { get; set; }
    public List<MakeupBooking> Bookings { get; set; } = [];
}

public class MakeupBooking
{
    public int Id { get; set; }
    public int MakeupCreditId { get; set; }
    public MakeupCredit? MakeupCredit { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public bool IsCancelled { get; set; }
}
