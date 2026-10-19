using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using LessonCredits.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

public class RefundService(IDbContextFactory<AppDbContext> dbFactory, TaipeiClock clock)
{
    public async Task<RefundQuote> QuoteAsync(int studentId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var balances = await CreditLedger.LoadBalancesAsync(db, studentId);
        return RefundCalculator.Quote(balances, clock.Today);
    }

    /// <summary>確認退費：所有批次（含已過期）剩餘點數歸零、留下退費紀錄、作廢未使用的補課資格。</summary>
    public async Task<Refund> ConfirmAsync(int studentId, Actor actor)
    {
        actor.EnsureOwner();
        await using var db = await dbFactory.CreateDbContextAsync();
        // 讀取剩餘點數前就開始交易：避免試算後、寫入前有人扣點，造成多退款或扣成負數。
        // 需要 Refund.Id 才能寫流水帳，分兩次 SaveChanges，同一個交易保證全有或全無。
        await using var tx = await db.BeginWriteTransactionAsync();
        var student = await db.Students.FindAsync(studentId) ?? throw new NotFoundException("找不到學生。");
        var balances = await CreditLedger.LoadBalancesAsync(db, studentId);
        var quote = RefundCalculator.Quote(balances, clock.Today);
        if (quote.Lines.Count == 0)
        {
            throw new BusinessRuleException("此學生沒有剩餘點數。");
        }

        var now = clock.Now;
        var refund = new Refund
        {
            StudentId = studentId,
            ConfirmedAt = now,
            ConfirmedByUserId = actor.UserId,
            TotalAmount = quote.Total,
        };
        foreach (var line in quote.Lines)
        {
            refund.Lines.Add(new RefundLine { BatchId = line.Batch.BatchId, Quantity = line.Batch.Remaining, IsExpired = line.IsExpired });
        }

        db.Refunds.Add(refund);
        await db.SaveChangesAsync();

        foreach (var line in quote.Lines)
        {
            db.CreditEntries.Add(new CreditEntry
            {
                BatchId = line.Batch.BatchId,
                Type = CreditEntryType.Refund,
                Quantity = -line.Batch.Remaining,
                RefundId = refund.Id,
                OccurredAt = now,
            });
        }

        var openCredits = await db.MakeupCredits
            .Include(m => m.Bookings.Where(b => !b.IsCancelled))
            .Where(m => m.StudentId == studentId && (m.Status == MakeupStatus.Available || m.Status == MakeupStatus.Booked))
            .ToListAsync();
        foreach (var credit in openCredits)
        {
            credit.Status = MakeupStatus.Voided;
            credit.Bookings.ForEach(b => b.IsCancelled = true);
        }

        student.IsActive = false;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return refund;
    }
}
