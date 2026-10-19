using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LessonCredits.Tests.Services;

/// <summary>
/// 受測操作開始交易時發出訊號，讓併發測試精準控制交錯時機，不必猜時間差。
/// - 正確實作：讀取前就開始交易，訊號發出時尚未讀取，之後讀到的是另一方 commit 後的資料。
/// - 錯誤實作：先在交易外讀完餘額或人數，到 SaveChanges（或之後才明確開始的交易）才開始交易，
///   訊號發出時已讀到舊資料，最後依舊資料寫入，測試必定失敗。
/// - 若完全沒有開始交易，訊號永遠不會發出，測試會逾時失敗，不會誤判為通過。
/// 注意：不能在「第一次讀取」時發訊號。操作的第一次讀取通常是載入場次等資料，
/// 關鍵的餘額讀取在後面，那樣會讓另一方太早 commit，錯誤實作也會讀到新資料而通過。
/// </summary>
public sealed class TransactionStartingSignal : DbTransactionInterceptor
{
    private TaskCompletionSource? _armed;

    /// <summary>從現在起，下一次開始交易時會完成回傳的 Task。</summary>
    public Task Arm()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _armed, signal);
        return signal.Task;
    }

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        Signal();
        return result;
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        Signal();
        return ValueTask.FromResult(result);
    }

    private void Signal() => Interlocked.Exchange(ref _armed, null)?.TrySetResult();
}
