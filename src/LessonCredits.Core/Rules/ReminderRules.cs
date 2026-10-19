namespace LessonCredits.Core.Rules;

public record ReminderFlags(int UsableBalance, bool LowBalance, bool ExpiringSoon, DateOnly? NextExpiry)
{
    public bool NeedsReminder => LowBalance || ExpiringSoon;
}

public static class ReminderRules
{
    public const int LowBalanceThreshold = 2;
    public const int ExpiryWindowDays = 30;

    public static ReminderFlags Evaluate(IEnumerable<BatchBalance> batches, DateOnly today)
    {
        var usable = batches.Where(b => b.Remaining > 0 && CreditRules.IsUsable(b.ExpiresOn, today)).ToList();
        var nextExpiry = usable.Count == 0 ? (DateOnly?)null : usable.Min(b => b.ExpiresOn);
        var balance = usable.Sum(b => b.Remaining);

        return new ReminderFlags(
            balance,
            LowBalance: balance <= LowBalanceThreshold,
            ExpiringSoon: nextExpiry <= today.AddDays(ExpiryWindowDays),
            nextExpiry);
    }
}
