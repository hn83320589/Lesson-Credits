namespace LessonCredits.Core.Rules;

/// <summary>某批點數在某一刻的快照，供規則計算使用。</summary>
public record BatchBalance(int BatchId, DateOnly PurchasedOn, DateOnly ExpiresOn, int Remaining, decimal UnitPrice);

public static class CreditRules
{
    public const int ValidMonths = 6;

    public static DateOnly ExpiryDate(DateOnly purchasedOn) => purchasedOn.AddMonths(ValidMonths);

    /// <summary>到期日當天仍可使用，隔天起失效。</summary>
    public static bool IsUsable(DateOnly expiresOn, DateOnly onDate) => onDate <= expiresOn;

    public static int UsableBalance(IEnumerable<BatchBalance> batches, DateOnly onDate) =>
        batches.Where(b => IsUsable(b.ExpiresOn, onDate)).Sum(b => b.Remaining);

    /// <summary>先到期的先扣（FEFO）；同到期日則先購買的先扣。</summary>
    public static BatchBalance? PickBatchForDeduction(IEnumerable<BatchBalance> batches, DateOnly onDate) =>
        batches
            .Where(b => b.Remaining > 0 && IsUsable(b.ExpiresOn, onDate))
            .OrderBy(b => b.ExpiresOn)
            .ThenBy(b => b.PurchasedOn)
            .ThenBy(b => b.BatchId)
            .FirstOrDefault();
}
