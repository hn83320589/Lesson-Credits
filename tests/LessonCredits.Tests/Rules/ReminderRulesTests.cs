using LessonCredits.Core.Rules;

namespace LessonCredits.Tests.Rules;

public class ReminderRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void Flags_low_balance_at_two_credits_or_fewer()
    {
        var two = ReminderRules.Evaluate([Batch(expiresOn: Today.AddDays(100), remaining: 2)], Today);
        var three = ReminderRules.Evaluate([Batch(expiresOn: Today.AddDays(100), remaining: 3)], Today);

        Assert.True(two.LowBalance);
        Assert.False(three.LowBalance);
    }

    [Fact]
    public void Flags_credits_expiring_within_30_days_inclusive()
    {
        var day30 = ReminderRules.Evaluate([Batch(expiresOn: Today.AddDays(30), remaining: 8)], Today);
        var day31 = ReminderRules.Evaluate([Batch(expiresOn: Today.AddDays(31), remaining: 8)], Today);

        Assert.True(day30.ExpiringSoon);
        Assert.Equal(Today.AddDays(30), day30.NextExpiry);
        Assert.False(day31.ExpiringSoon);
    }

    [Fact]
    public void Used_up_batches_do_not_count_as_expiring()
    {
        var flags = ReminderRules.Evaluate(
            [Batch(expiresOn: Today.AddDays(5), remaining: 0), Batch(expiresOn: Today.AddDays(200), remaining: 9)],
            Today);

        Assert.False(flags.ExpiringSoon);
        Assert.False(flags.LowBalance);
    }

    [Fact]
    public void Can_match_both_conditions_at_once()
    {
        var flags = ReminderRules.Evaluate([Batch(expiresOn: Today.AddDays(10), remaining: 1)], Today);

        Assert.True(flags.LowBalance);
        Assert.True(flags.ExpiringSoon);
        Assert.True(flags.NeedsReminder);
    }

    private static BatchBalance Batch(DateOnly expiresOn, int remaining) =>
        new(1, expiresOn.AddMonths(-6), expiresOn, remaining, 800m);
}
