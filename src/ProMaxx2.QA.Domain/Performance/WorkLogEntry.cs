namespace ProMaxx2.QA.Domain.Performance;

/// <summary>งานที่บันทึกเองเพิ่มจากข้อมูลในระบบ — Highlight = ผลงานเด่น, Concern = ข้อสังเกตที่หัวหน้าเห็นเท่านั้น</summary>
public sealed class WorkLogEntry
{
    public static readonly string[] Categories = ["Testing", "Automation", "Document", "Support", "Training", "Meeting", "Other"];
    public static readonly string[] Kinds = ["WorkLog", "Highlight", "Concern"];

    private WorkLogEntry() { }

    public WorkLogEntry(Guid userId, DateOnly workDate, string category, string kind, string title, string? description, decimal? effortHours, Guid? projectId, string? referenceUrl, Guid? createdBy)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        WorkLogEntryId = Guid.NewGuid();
        UserId = userId;
        Kind = Normalize(Kinds, kind, "ประเภทบันทึกไม่ถูกต้อง");
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        Apply(workDate, category, title, description, effortHours, projectId, referenceUrl);
    }

    public Guid WorkLogEntryId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public string Category { get; private set; } = "Other";
    public string Kind { get; private set; } = "WorkLog";
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal? EffortHours { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string? ReferenceUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    /// <summary>ลบแบบเก็บไว้ — บันทึกงานใช้ในการประเมินผลและรายงานประจำสัปดาห์ จึงกู้คืนได้ (SYS_ADMIN)</summary>
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    public void SoftDelete(Guid? userId) { IsDeleted = true; DeletedAt = DateTime.UtcNow; DeletedBy = userId; }
    public void Restore() { IsDeleted = false; DeletedAt = null; DeletedBy = null; }

    public void Update(DateOnly workDate, string category, string title, string? description, decimal? effortHours, Guid? projectId, string? referenceUrl, Guid? updatedBy)
    {
        Apply(workDate, category, title, description, effortHours, projectId, referenceUrl);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    private void Apply(DateOnly workDate, string category, string title, string? description, decimal? effortHours, Guid? projectId, string? referenceUrl)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("ต้องระบุหัวข้อ");
        if (title.Trim().Length > 200) throw new ArgumentException("หัวข้อยาวเกิน 200 ตัวอักษร");
        if (description?.Trim().Length > 4000) throw new ArgumentException("รายละเอียดยาวเกิน 4000 ตัวอักษร");
        if (effortHours is < 0 or > 24) throw new ArgumentException("ชั่วโมงทำงานต้องอยู่ระหว่าง 0–24");
        WorkDate = workDate;
        Category = Normalize(Categories, category, "หมวดงานไม่ถูกต้อง");
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        EffortHours = effortHours;
        ProjectId = projectId == Guid.Empty ? null : projectId;
        ReferenceUrl = NormalizeUrl(referenceUrl);
    }

    // ลิงก์ถูกแสดงเป็น <a href> — รับเฉพาะ http/https เพื่อไม่ให้ใส่ javascript: ได้
    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 500 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("ลิงก์อ้างอิงต้องขึ้นต้นด้วย http:// หรือ https://");
        return trimmed;
    }

    private static string Normalize(string[] allowed, string? value, string error)
        => allowed.SingleOrDefault(x => x.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException(error);
}

/// <summary>ไฟล์หลักฐานแนบกับบันทึกงาน (รูป/PDF) — ไฟล์อยู่บนดิสก์ App_Data/PerformanceEvidence/{workLogEntryId}/ ส่วน metadata อยู่ที่นี่ และถูกลบพร้อมบันทึกงาน</summary>
public sealed class WorkLogAttachment
{
    private WorkLogAttachment() { }

    public WorkLogAttachment(Guid id, Guid workLogEntryId, string fileName, string storedFileName, long sizeBytes, string contentType, Guid? uploadedBy, DateTime uploadedAt)
    {
        WorkLogAttachmentId = id;
        WorkLogEntryId = workLogEntryId;
        FileName = fileName;
        StoredFileName = storedFileName;
        SizeBytes = sizeBytes;
        ContentType = contentType;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
    }

    public Guid WorkLogAttachmentId { get; private set; }
    public Guid WorkLogEntryId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public Guid? UploadedBy { get; private set; }
    public DateTime UploadedAt { get; private set; }
}
