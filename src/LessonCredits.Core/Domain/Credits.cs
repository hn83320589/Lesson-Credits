namespace LessonCredits.Core.Domain;

public class Plan
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Lessons { get; set; }
    public int Price { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 一次購買形成的一批點數。方案名稱、堂數與金額在購買當下快照，日後方案調價不影響。
/// 剩餘點數不存欄位，由 <see cref="Entries"/> 加總而得。
/// </summary>
public class CreditBatch
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public required string PlanName { get; set; }
    public DateOnly PurchasedOn { get; set; }
    public int Lessons { get; set; }
    public int Amount { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public List<CreditEntry> Entries { get; set; } = [];

    public decimal UnitPrice => (decimal)Amount / Lessons;
}

/// <summary>點數流水帳。只新增、不修改；修正以反向分錄表達。</summary>
public class CreditEntry
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public CreditBatch? Batch { get; set; }
    public CreditEntryType Type { get; set; }
    public int Quantity { get; set; }
    public int? AttendanceId { get; set; }
    public Attendance? Attendance { get; set; }
    public int? RefundId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class Refund
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public int ConfirmedByUserId { get; set; }
    public int TotalAmount { get; set; }
    public List<RefundLine> Lines { get; set; } = [];
}

public class RefundLine
{
    public int Id { get; set; }
    public int RefundId { get; set; }
    public int BatchId { get; set; }
    public CreditBatch? Batch { get; set; }
    public int Quantity { get; set; }
    public bool IsExpired { get; set; }
}
