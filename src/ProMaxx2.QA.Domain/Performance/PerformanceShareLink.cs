namespace ProMaxx2.QA.Domain.Performance;

/// <summary>
/// ลิงก์อ่านอย่างเดียวของ "สรุปผลงานพนักงาน" สำหรับส่งให้ HR (`/?p=&lt;code&gt;`) — 1 ลิงก์ = พนักงาน 1 คน + ช่วงวันที่ที่ตรึงไว้
/// ข้อมูลคำนวณสดทุกครั้งที่เปิดตามสิทธิ์ของผู้สร้าง; มีวันหมดอายุเสมอและยกเลิกได้ (ไม่ลบแถว เพื่อเก็บประวัติการแชร์)
/// </summary>
public sealed class PerformanceShareLink
{
    public const int MaxLifetimeDays = 90;

    private PerformanceShareLink() { }

    public PerformanceShareLink(string code, Guid userId, DateOnly fromDate, DateOnly toDate, bool includeEvaluations, bool includeWorkLogs, string? note, DateTime expiresAt, Guid createdBy, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(code) || userId == Guid.Empty || createdBy == Guid.Empty) throw new ArgumentException("Share code, employee and creator are required.");
        if (fromDate > toDate) throw new ArgumentException("วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด");
        if (note?.Trim().Length > 200) throw new ArgumentException("หมายเหตุยาวเกิน 200 ตัวอักษร");
        if (expiresAt <= createdAt || expiresAt > createdAt.AddDays(MaxLifetimeDays)) throw new ArgumentException($"วันหมดอายุต้องอยู่ภายใน {MaxLifetimeDays} วัน");
        PerformanceShareLinkId = Guid.NewGuid();
        Code = code;
        UserId = userId;
        FromDate = fromDate;
        ToDate = toDate;
        IncludeEvaluations = includeEvaluations;
        IncludeWorkLogs = includeWorkLogs;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid PerformanceShareLinkId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    /// <summary>พนักงานที่ถูกสรุปผลงาน</summary>
    public Guid UserId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public bool IncludeEvaluations { get; private set; }
    public bool IncludeWorkLogs { get; private set; }
    /// <summary>ส่งให้ใคร/เพื่ออะไร เช่น "HR — ผ่านทดลองงาน"</summary>
    public string? Note { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public int ViewCount { get; private set; }
    public DateTime? LastViewedAt { get; private set; }

    public bool IsUsable(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    public void Revoke(Guid revokedBy, DateTime utcNow)
    {
        if (RevokedAt is not null) return;
        RevokedAt = utcNow;
        RevokedBy = revokedBy;
    }

    public void RecordView(DateTime utcNow)
    {
        ViewCount++;
        LastViewedAt = utcNow;
    }
}
