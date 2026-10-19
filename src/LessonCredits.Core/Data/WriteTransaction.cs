using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LessonCredits.Core.Data;

public static class WriteTransaction
{
    /// <summary>
    /// 「先讀餘額或人數做判斷，再寫入」的操作，必須在第一次讀取前呼叫。
    /// Microsoft.Data.Sqlite 預設以 BEGIN IMMEDIATE 開始交易，一開始就取得寫入鎖，
    /// 同時進行的寫入操作會依序排隊，後到者讀到的是前者 commit 後的資料，
    /// 不會兩邊都讀到「剩 1 點」而扣成負數。
    /// </summary>
    public static Task<IDbContextTransaction> BeginWriteTransactionAsync(this AppDbContext db) =>
        db.Database.BeginTransactionAsync();
}
