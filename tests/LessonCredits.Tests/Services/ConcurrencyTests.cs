using LessonCredits.Core.Domain;
using LessonCredits.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LessonCredits.Tests.Services;

/// <summary>
/// 模擬兩個操作同時讀到相同的餘額或人數。
/// 做法：另一條連線先取得寫入鎖（BEGIN IMMEDIATE），代表「另一位老師正在點名」；
/// 等受測操作開始交易（<see cref="TransactionStartingSignal"/>），另一條連線才寫入並 commit。
/// 若受測操作在交易外先讀取，此時已讀到過期的資料，最後扣成負數或超收；
/// 若讀取前就開始交易，會等到鎖釋放才讀，讀到的是最新資料。
/// 交錯時機由訊號決定，不依賴時間差，所以機器快慢不影響結果。
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private static readonly DateTime ClassStart = new(2026, 10, 20, 16, 0, 0);

    /// <summary>只用來避免測試在出錯時卡住，不影響交錯時機。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TransactionStartingSignal _signal = new();
    private readonly TestEnv _env;
    private readonly int _teacher;

    public ConcurrencyTests()
    {
        _env = new TestEnv(useFileDatabase: true, _signal);
        _teacher = _env.AddTeacher();
    }

    [Fact]
    public async Task Concurrent_deductions_cannot_overdraw_the_last_credit()
    {
        // 學生只剩 1 點，鋼琴老師與美術老師同時點名
        var student = _env.AddStudent();
        var batch = _env.AddBatch(student, new DateOnly(2026, 10, 1), 1, 900);
        var session = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano, student), ClassStart);
        _env.SetNow(ClassStart.AddMinutes(30));

        using var other = HoldWriteLock();
        var started = _signal.Arm();
        var record = Task.Run(() => _env.Attendance.RecordAsync(session, student, AttendanceStatus.Present, _env.Owner));
        await started.WaitAsync(Timeout);
        Execute(other, $"INSERT INTO CreditEntries (BatchId, Type, Quantity, OccurredAt) VALUES ({batch}, 'Deduct', -1, '2026-10-20 16:10:00'); COMMIT;");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => record);

        Assert.Contains("點數不足", ex.Message);
        Assert.Equal(0, await RemainingAsync(student));
    }

    [Fact]
    public async Task Refund_racing_with_a_deduction_settles_the_latest_balance()
    {
        // 店長確認退費的同時，老師正在幫同一位學生點名
        var student = _env.AddStudent();
        var batch = _env.AddBatch(student, new DateOnly(2026, 10, 1), 10, 8000);

        using var other = HoldWriteLock();
        var started = _signal.Arm();
        var confirm = Task.Run(() => _env.Refunds.ConfirmAsync(student, _env.Owner));
        await started.WaitAsync(Timeout);
        Execute(other, $"INSERT INTO CreditEntries (BatchId, Type, Quantity, OccurredAt) VALUES ({batch}, 'Deduct', -1, '2026-10-19 09:59:00'); COMMIT;");

        var refund = await confirm;

        Assert.Equal(9 * 800, refund.TotalAmount);
        Assert.Equal(0, await RemainingAsync(student));
    }

    [Fact]
    public async Task Concurrent_makeup_bookings_cannot_exceed_group_capacity()
    {
        // 小班已有 3 人，剩 1 個名額；兩筆補課預約同時送出
        var regulars = Enumerable.Range(1, 3).Select(i => _env.AddStudent($"固定{i}")).ToArray();
        var group = _env.AddSession(_env.AddCourse(_teacher, CourseType.Group, Subject.Piano, regulars), new DateTime(2026, 10, 27, 16, 0, 0));
        var first = await CreateMakeupCreditAsync("甲");
        var second = await CreateMakeupCreditAsync("乙");

        using var other = HoldWriteLock();
        var started = _signal.Arm();
        var book = Task.Run(() => _env.Makeup.BookAsync(first, group, _env.Owner));
        await started.WaitAsync(Timeout);
        Execute(other, $"INSERT INTO MakeupBookings (MakeupCreditId, SessionId, IsCancelled) VALUES ({second}, {group}, 0); " +
                       $"UPDATE MakeupCredits SET Status = 'Booked' WHERE Id = {second}; COMMIT;");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => book);

        Assert.Contains("已滿", ex.Message);
        using var db = _env.Db();
        Assert.Equal(1, await db.MakeupBookings.CountAsync(b => b.SessionId == group && !b.IsCancelled));
    }

    private SqliteConnection HoldWriteLock()
    {
        var connection = new SqliteConnection(_env.ConnectionString);
        connection.Open();
        Execute(connection, "BEGIN IMMEDIATE;");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>含過期批次在內的實際剩餘點數，負數代表超扣。</summary>
    private async Task<int> RemainingAsync(int studentId)
    {
        using var db = _env.Db();
        return await db.CreditEntries.Where(e => e.Batch!.StudentId == studentId).SumAsync(e => e.Quantity);
    }

    /// <summary>於 2026-10-13 的一對一課準時請假，取得一筆鋼琴補課資格。</summary>
    private async Task<int> CreateMakeupCreditAsync(string name)
    {
        var student = _env.AddStudent(name);
        _env.AddBatch(student, new DateOnly(2026, 10, 1), 10, 8000);
        var start = new DateTime(2026, 10, 13, 16, 0, 0);
        var session = _env.AddSession(_env.AddCourse(_teacher, CourseType.OneOnOne, Subject.Piano, student), start);
        _env.SetNow(start.AddDays(-2));
        await _env.Attendance.RequestLeaveAsync(session, student, _env.Owner);
        _env.SetNow(start.AddHours(1));
        await _env.Attendance.RecordAsync(session, student, AttendanceStatus.Leave, _env.Owner);
        _env.SetNow(new DateTime(2026, 10, 19, 10, 0, 0));

        using var db = _env.Db();
        return (await db.MakeupCredits.SingleAsync(m => m.StudentId == student)).Id;
    }

    public void Dispose() => _env.Dispose();
}
