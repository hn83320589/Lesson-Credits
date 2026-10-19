namespace LessonCredits.Core.Domain;

public enum Role
{
    Owner,
    Teacher,
}

public enum Subject
{
    Piano,
    Art,
}

public enum CourseType
{
    OneOnOne,
    Group,
}

public enum SessionStatus
{
    Scheduled,
    Cancelled,
}

public enum AttendanceStatus
{
    Present,
    Leave,
    Absent,
}

public enum CreditEntryType
{
    Purchase,
    Deduct,
    Restore,
    Refund,
}

public enum MakeupStatus
{
    Available,
    Booked,
    Used,
    Forfeited,
    Voided,
}

public static class EnumLabels
{
    public static string Label(this Subject subject) => subject switch
    {
        Subject.Piano => "鋼琴",
        Subject.Art => "美術",
        _ => subject.ToString(),
    };

    public static string Label(this CourseType type) => type switch
    {
        CourseType.OneOnOne => "一對一",
        CourseType.Group => "小班",
        _ => type.ToString(),
    };

    public static string Label(this AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "出席",
        AttendanceStatus.Leave => "請假",
        AttendanceStatus.Absent => "缺席",
        _ => status.ToString(),
    };

    public static string Label(this CreditEntryType type) => type switch
    {
        CreditEntryType.Purchase => "購買",
        CreditEntryType.Deduct => "扣點",
        CreditEntryType.Restore => "還原",
        CreditEntryType.Refund => "退費歸零",
        _ => type.ToString(),
    };

    public static string Label(this MakeupStatus status) => status switch
    {
        MakeupStatus.Available => "可使用",
        MakeupStatus.Booked => "已預約",
        MakeupStatus.Used => "已補課",
        MakeupStatus.Forfeited => "補課缺席作廢",
        MakeupStatus.Voided => "已作廢",
        _ => status.ToString(),
    };

    public static string Label(this Role role) => role switch
    {
        Role.Owner => "店長",
        Role.Teacher => "老師",
        _ => role.ToString(),
    };
}
