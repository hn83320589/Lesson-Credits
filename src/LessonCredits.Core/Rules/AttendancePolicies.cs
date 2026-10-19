using LessonCredits.Core.Domain;

namespace LessonCredits.Core.Rules;

public static class LeavePolicy
{
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(24);

    /// <summary>上課前 24 小時（含）以前提出才算準時請假。</summary>
    public static bool IsOnTime(DateTime sessionStart, DateTime requestedAt) =>
        requestedAt <= sessionStart - MinimumNotice;
}

public static class MakeupPolicy
{
    public const int ValidDays = 60;

    public static DateOnly ExpiryDate(DateOnly issuedOn) => issuedOn.AddDays(ValidDays);

    public static bool IsValidOn(DateOnly expiresOn, DateOnly date) => date <= expiresOn;
}

/// <summary>點名狀態對點數與補課資格的效果。補課場次一律不扣點、不產生新資格。</summary>
public static class AttendanceEffects
{
    public static bool Deducts(AttendanceStatus status, bool isMakeup) =>
        !isMakeup && status is AttendanceStatus.Present or AttendanceStatus.Absent;

    public static bool GrantsMakeup(AttendanceStatus status, bool isMakeup) =>
        !isMakeup && status == AttendanceStatus.Leave;
}
