namespace LessonCredits.Core.Time;

/// <summary>把 TimeProvider 的 UTC 時間轉成台灣本地時間。系統內所有「現在」都從這裡取得。</summary>
public class TaipeiClock(TimeProvider timeProvider)
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public DateTime Now => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(Now);
}
