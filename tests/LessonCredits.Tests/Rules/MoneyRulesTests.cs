using LessonCredits.Core.Rules;

namespace LessonCredits.Tests.Rules;

public class MoneyRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Fact]
    public void Refund_matches_hand_calculation_with_two_prices_and_an_expired_batch()
    {
        // A: 20 堂 15,000（單價 750）剩 5 → 3,750
        // B: 10 堂  8,000（單價 800）剩 10 → 8,000
        // C: 單堂 900，2026-09-01 已過期剩 1 → 0
        var batches = new[]
        {
            new BatchBalance(1, new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 1), 5, 15000m / 20),
            new BatchBalance(2, new DateOnly(2026, 9, 1), new DateOnly(2027, 3, 1), 10, 8000m / 10),
            new BatchBalance(3, new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 1), 1, 900m),
        };

        var quote = RefundCalculator.Quote(batches, Today);

        Assert.Equal(11_750, quote.Total);
        Assert.True(quote.Lines.Single(l => l.Batch.BatchId == 3).IsExpired);
        Assert.Equal(0m, quote.Lines.Single(l => l.Batch.BatchId == 3).Amount);
    }

    [Fact]
    public void Refund_rounds_half_away_from_zero_once_on_the_total()
    {
        // 自訂方案 3 堂 1,000 元，剩 2 點 → 666.666… → 667
        var batches = new[] { new BatchBalance(1, Today, Today.AddMonths(6), 2, 1000m / 3) };

        Assert.Equal(667, RefundCalculator.Quote(batches, Today).Total);
    }

    [Fact]
    public void Money_round_uses_half_away_from_zero_not_bankers_rounding()
    {
        Assert.Equal(3, Money.Round(2.5m));
        Assert.Equal(4, Money.Round(3.5m));
    }

    [Fact]
    public void Revenue_matches_hand_calculation_including_a_restored_deduction()
    {
        // 老師 1：扣 3 點（750）+ 扣 2 點（800）− 還原 1 點（800）= 2,250 + 1,600 − 800 = 3,050
        // 老師 2：扣 1 點（900）= 900
        var entries = new[]
        {
            new RevenueEntry(1, -1, 750m), new RevenueEntry(1, -1, 750m), new RevenueEntry(1, -1, 750m),
            new RevenueEntry(1, -1, 800m), new RevenueEntry(1, -1, 800m),
            new RevenueEntry(1, +1, 800m),
            new RevenueEntry(2, -1, 900m),
        };

        var revenue = RevenueCalculator.ByTeacher(entries);

        Assert.Equal(3_050, revenue[1]);
        Assert.Equal(900, revenue[2]);
    }
}
