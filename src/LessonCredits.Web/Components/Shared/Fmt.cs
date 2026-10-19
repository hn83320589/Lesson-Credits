using LessonCredits.Core.Domain;

namespace LessonCredits.Web.Components.Shared;

public static class Fmt
{
    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];

    public static string Money(int amount) => $"NT$ {amount:N0}";

    public static string Money(decimal amount) => $"NT$ {amount:N2}";

    public static string Date(DateOnly date) => $"{date:yyyy-MM-dd}（{Weekdays[(int)date.DayOfWeek]}）";

    public static string DateTime(DateTime value) => $"{value:yyyy-MM-dd}（{Weekdays[(int)value.DayOfWeek]}）{value:HH:mm}";

    public static string Weekday(DayOfWeek day) => "週" + Weekdays[(int)day];

    public static string StatusBadge(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "bg-success",
        AttendanceStatus.Leave => "bg-info text-dark",
        AttendanceStatus.Absent => "bg-danger",
        _ => "bg-secondary",
    };

    public static string EntryBadge(CreditEntryType type) => type switch
    {
        CreditEntryType.Purchase => "bg-primary",
        CreditEntryType.Deduct => "bg-warning text-dark",
        CreditEntryType.Restore => "bg-info text-dark",
        CreditEntryType.Refund => "bg-dark",
        _ => "bg-secondary",
    };
}
