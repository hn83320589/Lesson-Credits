using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;

namespace LessonCredits.Tests.Rules;

public class LeaveAndMakeupPolicyTests
{
    private static readonly DateTime SessionStart = new(2026, 10, 20, 16, 0, 0);

    [Fact]
    public void Leave_requested_24h01m_before_class_is_on_time()
    {
        var requestedAt = SessionStart - TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1);

        Assert.True(LeavePolicy.IsOnTime(SessionStart, requestedAt));
    }

    [Fact]
    public void Leave_requested_23h59m_before_class_is_late()
    {
        var requestedAt = SessionStart - TimeSpan.FromHours(23) - TimeSpan.FromMinutes(59);

        Assert.False(LeavePolicy.IsOnTime(SessionStart, requestedAt));
    }

    [Fact]
    public void Leave_requested_exactly_24h_before_class_is_on_time()
    {
        Assert.True(LeavePolicy.IsOnTime(SessionStart, SessionStart.AddHours(-24)));
    }

    [Fact]
    public void Makeup_credit_is_valid_on_day_60_but_not_day_61()
    {
        var issuedOn = new DateOnly(2026, 10, 1);
        var expiresOn = MakeupPolicy.ExpiryDate(issuedOn);

        Assert.True(MakeupPolicy.IsValidOn(expiresOn, issuedOn.AddDays(60)));
        Assert.False(MakeupPolicy.IsValidOn(expiresOn, issuedOn.AddDays(61)));
    }

    [Theory]
    [InlineData(AttendanceStatus.Present, false, true)]
    [InlineData(AttendanceStatus.Absent, false, true)]
    [InlineData(AttendanceStatus.Leave, false, false)]
    [InlineData(AttendanceStatus.Present, true, false)]
    [InlineData(AttendanceStatus.Absent, true, false)]
    public void Deducts_credit_only_for_regular_present_or_absent(
        AttendanceStatus status, bool isMakeup, bool expected)
    {
        Assert.Equal(expected, AttendanceEffects.Deducts(status, isMakeup));
    }

    [Fact]
    public void Only_regular_leave_grants_makeup_credit()
    {
        Assert.True(AttendanceEffects.GrantsMakeup(AttendanceStatus.Leave, isMakeup: false));
        Assert.False(AttendanceEffects.GrantsMakeup(AttendanceStatus.Present, isMakeup: false));
        Assert.False(AttendanceEffects.GrantsMakeup(AttendanceStatus.Leave, isMakeup: true));
    }
}
