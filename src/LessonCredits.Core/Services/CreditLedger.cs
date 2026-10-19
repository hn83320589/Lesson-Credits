using LessonCredits.Core.Data;
using LessonCredits.Core.Domain;
using LessonCredits.Core.Rules;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Core.Services;

/// <summary>從流水帳計算各批剩餘點數，並寫入扣點／還原分錄。</summary>
internal static class CreditLedger
{
    public static async Task<List<BatchBalance>> LoadBalancesAsync(AppDbContext db, int studentId)
    {
        var map = await LoadBalancesAsync(db, [studentId]);
        return map.GetValueOrDefault(studentId) ?? [];
    }

    public static async Task<Dictionary<int, List<BatchBalance>>> LoadBalancesAsync(
        AppDbContext db, IReadOnlyCollection<int> studentIds)
    {
        var rows = await db.CreditBatches
            .Where(b => studentIds.Contains(b.StudentId))
            .Select(b => new
            {
                b.Id,
                b.StudentId,
                b.PurchasedOn,
                b.ExpiresOn,
                b.Amount,
                b.Lessons,
                Remaining = b.Entries.Sum(e => e.Quantity),
            })
            .ToListAsync();

        // 單價在記憶體計算：SQLite 沒有原生 decimal
        return rows
            .GroupBy(r => r.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new BatchBalance(
                    r.Id, r.PurchasedOn, r.ExpiresOn, r.Remaining, (decimal)r.Amount / r.Lessons)).ToList());
    }

    /// <summary>依 FEFO 扣 1 點；沒有可用點數時拋出含原因的例外。</summary>
    public static async Task DeductAsync(AppDbContext db, Attendance attendance, DateOnly sessionDate, DateTime now)
    {
        var balances = await LoadBalancesAsync(db, attendance.StudentId);
        var batch = CreditRules.PickBatchForDeduction(balances, sessionDate)
            ?? throw new BusinessRuleException(InsufficientReason(balances, sessionDate));

        db.CreditEntries.Add(new CreditEntry
        {
            BatchId = batch.BatchId,
            Type = CreditEntryType.Deduct,
            Quantity = -1,
            Attendance = attendance,
            OccurredAt = now,
        });
    }

    /// <summary>把這筆點名扣掉的點數還原回原本的批次。</summary>
    public static async Task RestoreAsync(AppDbContext db, Attendance attendance, DateTime now)
    {
        var netByBatch = await db.CreditEntries
            .Where(e => e.AttendanceId == attendance.Id)
            .GroupBy(e => e.BatchId)
            .Select(g => new { BatchId = g.Key, Net = g.Sum(e => e.Quantity) })
            .ToListAsync();

        foreach (var row in netByBatch.Where(r => r.Net < 0))
        {
            db.CreditEntries.Add(new CreditEntry
            {
                BatchId = row.BatchId,
                Type = CreditEntryType.Restore,
                Quantity = -row.Net,
                AttendanceId = attendance.Id,
                OccurredAt = now,
            });
        }
    }

    public static string InsufficientReason(IReadOnlyCollection<BatchBalance> balances, DateOnly onDate)
    {
        var expired = balances
            .Where(b => b.Remaining > 0 && !CreditRules.IsUsable(b.ExpiresOn, onDate))
            .OrderByDescending(b => b.ExpiresOn)
            .FirstOrDefault();

        return expired is null
            ? "點數不足（剩餘 0 點），請先購買點數。"
            : $"點數不足：剩餘 {expired.Remaining} 點已於 {expired.ExpiresOn:yyyy-MM-dd} 過期，請先購買點數。";
    }
}
