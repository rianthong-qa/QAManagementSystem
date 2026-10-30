namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>บทความความรู้ที่เขียนในระบบ (FAQ, วิธีแก้ปัญหา, ขั้นตอนงาน) — ProjectId = null คือความรู้กลางทุก Project; เฉพาะ Published ที่เข้าคลังความรู้</summary>
public sealed class KnowledgeArticle
{
    public static readonly string[] Statuses = ["Draft", "Published"];
    /// <summary>Markdown = เขียนในระบบ; Html = นำเข้าจาก HTML — BodyHtml คือ HTML ที่ทำความสะอาดแล้วไว้แสดงผล ส่วน BodyMarkdown เก็บข้อความที่สกัดได้สำหรับค้นหา/AI</summary>
    public static readonly string[] ContentFormats = ["Markdown", "Html"];

    private KnowledgeArticle() { }

    public KnowledgeArticle(string title, string bodyMarkdown, Guid? projectId, string? tags, string status, Guid? createdBy, string? bodyHtml = null)
    {
        KnowledgeArticleId = Guid.NewGuid();
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        Apply(title, bodyMarkdown, projectId, tags, status, bodyHtml);
    }

    public Guid KnowledgeArticleId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string BodyMarkdown { get; private set; } = string.Empty;
    public string ContentFormat { get; private set; } = "Markdown";
    public string? BodyHtml { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string? Tags { get; private set; }
    public string Status { get; private set; } = "Draft";
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(string title, string bodyMarkdown, Guid? projectId, string? tags, string status, Guid? updatedBy, string? bodyHtml = null)
    {
        Apply(title, bodyMarkdown, projectId, tags, status, bodyHtml);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Delete(Guid? deletedBy) { IsDeleted = true; DeletedAt = UpdatedAt = DateTime.UtcNow; DeletedBy = UpdatedBy = deletedBy; }
    /// <summary>กู้คืน — UpdatedAt ใหม่ทำให้ KnowledgeIndexService index กลับเข้าคลังความรู้</summary>
    public void Restore(Guid? userId) { IsDeleted = false; DeletedAt = null; DeletedBy = null; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }

    // bodyHtml มีค่า = บทความ HTML (bodyMarkdown คือข้อความที่สกัดจาก HTML แล้ว); null = บทความ Markdown และล้าง HTML เดิม
    private void Apply(string title, string bodyMarkdown, Guid? projectId, string? tags, string status, string? bodyHtml)
    {
        Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("กรุณาระบุหัวข้อบทความ") : title.Trim();
        if (Title.Length > 300) throw new ArgumentException("หัวข้อบทความยาวได้ไม่เกิน 300 ตัวอักษร");
        BodyMarkdown = string.IsNullOrWhiteSpace(bodyMarkdown)
            ? throw new ArgumentException(bodyHtml is null ? "กรุณาระบุเนื้อหาบทความ" : "ไม่พบข้อความในไฟล์ HTML")
            : bodyMarkdown.Trim();
        BodyHtml = string.IsNullOrWhiteSpace(bodyHtml) ? null : bodyHtml.Trim();
        ContentFormat = BodyHtml is null ? "Markdown" : "Html";
        ProjectId = projectId;
        Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        if (Tags?.Length > 500) throw new ArgumentException("Tag ยาวได้ไม่เกิน 500 ตัวอักษร");
        Status = Statuses.FirstOrDefault(x => string.Equals(x, status?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("สถานะบทความไม่ถูกต้อง");
    }
}

/// <summary>
/// ไฟล์เอกสารในคลังความรู้ (PDF / DOCX / MD / TXT) — ไฟล์จริงอยู่บนดิสก์ App_Data/KnowledgeFiles, เก็บข้อความที่สกัดได้ไว้สร้าง chunk
/// Origin: Upload = ผู้ใช้อัปโหลด, Import = นำเข้าจากโฟลเดอร์เอกสารของระบบ (ImportPath = path สัมพัทธ์ ใช้นำเข้าซ้ำแบบ idempotent)
/// </summary>
public sealed class KnowledgeFile
{
    public static readonly string[] AllowedExtensions = [".pdf", ".docx", ".md", ".txt"];
    public const int MaxExtractedChars = 1_000_000;

    private KnowledgeFile() { }

    public KnowledgeFile(string fileName, string contentType, long sizeBytes, string storagePath, string contentHash, Guid? projectId, string? description, Guid? uploadedBy, string? importPath = null)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) throw new ArgumentException("รองรับเฉพาะไฟล์ PDF, DOCX, MD และ TXT");
        var name = fileName!.Trim();
        KnowledgeFileId = Guid.NewGuid();
        FileName = name.Length > 260 ? name[^260..] : name;
        ProjectId = projectId;
        Description = Cut(description, 1000);
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
        ImportPath = Cut(importPath, 300);
        Origin = importPath is null ? "Upload" : "Import";
        ContentType = contentType; SizeBytes = sizeBytes; StoragePath = storagePath; ContentHash = contentHash;
    }

    public Guid KnowledgeFileId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    /// <summary>SHA-256 ของไฟล์ — กันอัปโหลดซ้ำ และรู้ว่าไฟล์นำเข้าเปลี่ยนหรือไม่</summary>
    public string ContentHash { get; private set; } = string.Empty;
    public string Origin { get; private set; } = "Upload";
    public string? ImportPath { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string? Description { get; private set; }
    public string? ExtractedText { get; private set; }
    public string? ExtractError { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public Guid? UploadedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();

    /// <summary>ข้อความที่สกัดได้ (ตัดที่ MaxExtractedChars) หรือเหตุผลที่สกัดไม่ได้</summary>
    public void SetExtractedText(string? text, string? error)
    {
        ExtractedText = string.IsNullOrWhiteSpace(text) ? null : text.Length > MaxExtractedChars ? text[..MaxExtractedChars] : text;
        ExtractError = Cut(error, 1000);
    }

    /// <summary>ไฟล์นำเข้าที่เนื้อหาเปลี่ยน — เปลี่ยนไฟล์บนดิสก์และข้อความ (index ใหม่ผ่าน UpdatedAt)</summary>
    public void ReplaceContent(string contentType, long sizeBytes, string storagePath, string contentHash)
    {
        ContentType = contentType; SizeBytes = sizeBytes; StoragePath = storagePath; ContentHash = contentHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Delete(Guid? deletedBy = null) { IsDeleted = true; DeletedAt = UpdatedAt = DateTime.UtcNow; DeletedBy = deletedBy; }
    public void Restore() { IsDeleted = false; DeletedAt = null; DeletedBy = null; UpdatedAt = DateTime.UtcNow; }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>
/// รายละเอียดปัญหาของ CRM Ticket (mask ข้อมูลอ่อนไหวแล้ว) — ต้นฉบับไม่เก็บ ดูสดจาก CRM ได้
/// FetchedAt = ดึงล่าสุด (ใช้ตัดสินว่าต้องดึงใหม่), ChangedAt = เนื้อหาเปลี่ยนล่าสุด (ใช้ mark Stale ในคลังความรู้), FetchError = ดึงไม่สำเร็จ (ลองใหม่ภายหลัง)
/// </summary>
public sealed class CrmTicketContent
{
    public const int MaxDescriptionChars = 20_000;

    private CrmTicketContent() { }

    public CrmTicketContent(string jobNo)
    {
        JobNo = string.IsNullOrWhiteSpace(jobNo) ? throw new ArgumentException("JobNo is required.") : jobNo.Trim();
    }

    public string JobNo { get; private set; } = string.Empty;
    public string? SubjectMasked { get; private set; }
    public string? DescriptionMasked { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public DateTime FetchedAt { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string? FetchError { get; private set; }

    /// <summary>บันทึกผลดึงที่สำเร็จ — คืน true เมื่อเนื้อหาเปลี่ยน (ต้องแทนคำตอบและ index ใหม่)</summary>
    public bool Apply(string? subjectMasked, string? descriptionMasked, string contentHash, DateTime fetchedAt)
    {
        var changed = ContentHash != contentHash;
        SubjectMasked = Cut(subjectMasked, 300);
        DescriptionMasked = Cut(descriptionMasked, MaxDescriptionChars);
        ContentHash = contentHash; FetchedAt = fetchedAt; FetchError = null;
        if (changed) ChangedAt = fetchedAt;
        return changed;
    }

    /// <summary>ดึงไม่สำเร็จ — คงเนื้อหาเดิม (ถ้ามี) และจดเหตุผล</summary>
    public void Fail(string error, DateTime fetchedAt)
    {
        FetchError = Cut(error, 500); FetchedAt = fetchedAt;
    }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>คำตอบใน CRM Ticket (mask แล้ว) — 1 แถวต่อ AnswerNo</summary>
public sealed class CrmTicketAnswer
{
    private CrmTicketAnswer() { }

    public CrmTicketAnswer(string jobNo, string answerNo, string? answerType, string? textMasked, DateTime? answeredAt)
    {
        JobNo = jobNo; AnswerNo = answerNo; AnswerType = answerType; TextMasked = textMasked; AnsweredAt = answeredAt; FetchedAt = DateTime.UtcNow;
    }

    public long CrmTicketAnswerId { get; private set; }
    public string JobNo { get; private set; } = string.Empty;
    public string AnswerNo { get; private set; } = string.Empty;
    public string? AnswerType { get; private set; }
    public string? TextMasked { get; private set; }
    public DateTime? AnsweredAt { get; private set; }
    public DateTime FetchedAt { get; private set; }
}
