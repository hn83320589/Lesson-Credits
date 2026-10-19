using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public class CreditService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public async Task<CreditBatch> PurchaseAsync(int studentId, int planId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        var student = await db.Students.FindAsync(studentId) ?? throw new NotFoundException("找不到學生。");
        var plan = await db.Plans.FindAsync(planId) ?? throw new NotFoundException("找不到方案。");
        if (!plan.IsActive)
        {
            throw new BusinessRuleException("此方案已停售。");
        }

        var today = clock.Today;
        var batch = new CreditBatch
        {
            StudentId = studentId,
            PlanName = plan.Name,
            PurchasedOn = today,
            Lessons = plan.Lessons,
            Amount = plan.Price,
            ExpiresOn = CreditRules.ExpiryDate(today),
        };
        batch.Entries.Add(new CreditEntry { Type = CreditEntryType.Purchase, Quantity = plan.Lessons, OccurredAt = clock.Now });
        db.CreditBatches.Add(batch);

        // 退費後再購買視為重新入學
        student.IsActive = true;
        await db.SaveChangesAsync();
        return batch;
    }

    public async Task<int> GetUsableBalanceAsync(int studentId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var balances = await CreditLedger.LoadBalancesAsync(db, studentId);
        return CreditRules.UsableBalance(balances, clock.Today);
    }

    public async Task<Dictionary<int, int>> GetUsableBalancesAsync(IReadOnlyCollection<int> studentIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var balances = await CreditLedger.LoadBalancesAsync(db, studentIds);
        return studentIds.ToDictionary(
            id => id,
            id => CreditRules.UsableBalance(balances.GetValueOrDefault(id) ?? [], clock.Today));
    }
}
