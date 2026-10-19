namespace LessonCredits.Core.Rules;

public static class Money
{
    /// <summary>四捨五入到元。.NET 預設為銀行家捨入，必須明確指定 AwayFromZero。</summary>
    public static int Round(decimal amount) => (int)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
}

public record RefundQuoteLine(BatchBalance Batch, bool IsExpired, decimal Amount);

public record RefundQuote(IReadOnlyList<RefundQuoteLine> Lines, decimal ExactTotal, int Total);

public static class RefundCalculator
{
    /// <summary>未過期的剩餘點數依各自單價計算；已過期不退。小數只在總額四捨五入一次。</summary>
    public static RefundQuote Quote(IEnumerable<BatchBalance> batches, DateOnly today)
    {
        var lines = batches
            .Where(b => b.Remaining > 0)
            .Select(b =>
            {
                var expired = !CreditRules.IsUsable(b.ExpiresOn, today);
                return new RefundQuoteLine(b, expired, expired ? 0m : b.Remaining * b.UnitPrice);
            })
            .ToList();

        var exact = lines.Sum(l => l.Amount);
        return new RefundQuote(lines, exact, Money.Round(exact));
    }
}

/// <param name="Quantity">流水帳數量：扣點為 -1，還原為 +1。</param>
public record RevenueEntry(int TeacherId, int Quantity, decimal UnitPrice);

public static class RevenueCalculator
{
    /// <summary>認列收入 = 實際扣掉的點數 × 單價（還原分錄沖減），依老師分別四捨五入。</summary>
    public static IReadOnlyDictionary<int, int> ByTeacher(IEnumerable<RevenueEntry> entries) =>
        entries
            .GroupBy(e => e.TeacherId)
            .ToDictionary(g => g.Key, g => Money.Round(g.Sum(e => -e.Quantity * e.UnitPrice)));
}
