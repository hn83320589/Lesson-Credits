using LessonCredits.Core.Rules;

namespace LessonCredits.Tests.Rules;

public class CreditRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void ExpiryDate_is_six_months_after_purchase()
    {
        Assert.Equal(new DateOnly(2027, 4, 19), CreditRules.ExpiryDate(new DateOnly(2026, 10, 19)));
    }

    [Fact]
    public void ExpiryDate_clamps_to_end_of_shorter_month()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), CreditRules.ExpiryDate(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void Batch_is_usable_on_expiry_date_but_not_the_day_after()
    {
        var expiresOn = new DateOnly(2026, 12, 1);

        Assert.True(CreditRules.IsUsable(expiresOn, expiresOn));
        Assert.False(CreditRules.IsUsable(expiresOn, expiresOn.AddDays(1)));
    }

    [Fact]
    public void UsableBalance_excludes_expired_batches()
    {
        var batches = new[]
        {
            Batch(1, expiresOn: Today.AddDays(10), remaining: 3),
            Batch(2, expiresOn: Today.AddDays(-1), remaining: 5),
        };

        Assert.Equal(3, CreditRules.UsableBalance(batches, Today));
    }

    [Fact]
    public void PickBatchForDeduction_takes_the_earliest_expiring_batch_first()
    {
        var batches = new[]
        {
            Batch(1, expiresOn: Today.AddDays(90), remaining: 10),
            Batch(2, expiresOn: Today.AddDays(30), remaining: 2),
            Batch(3, expiresOn: Today.AddDays(60), remaining: 4),
        };

        Assert.Equal(2, CreditRules.PickBatchForDeduction(batches, Today)?.BatchId);
    }

    [Fact]
    public void PickBatchForDeduction_skips_expired_and_empty_batches()
    {
        var batches = new[]
        {
            Batch(1, expiresOn: Today.AddDays(-1), remaining: 5),
            Batch(2, expiresOn: Today.AddDays(10), remaining: 0),
            Batch(3, expiresOn: Today.AddDays(20), remaining: 1),
        };

        Assert.Equal(3, CreditRules.PickBatchForDeduction(batches, Today)?.BatchId);
    }

    [Fact]
    public void PickBatchForDeduction_returns_null_when_no_usable_credit()
    {
        var batches = new[] { Batch(1, expiresOn: Today.AddDays(-1), remaining: 5) };

        Assert.Null(CreditRules.PickBatchForDeduction(batches, Today));
    }

    private static BatchBalance Batch(int id, DateOnly expiresOn, int remaining) =>
        new(id, expiresOn.AddMonths(-6), expiresOn, remaining, 800m);
}
