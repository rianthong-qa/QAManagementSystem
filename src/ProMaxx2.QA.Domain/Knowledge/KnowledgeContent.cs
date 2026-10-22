namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>บทความความรู้ที่เขียนในระบบ (FAQ, วิธีแก้ปัญหา, ขั้นตอนงาน) — ProjectId = null คือความรู้กลางทุก Project; เฉพาะ Published ที่เข้าคลังความรู้</summary>
public sealed class KnowledgeArticle
{
    public static readonly string[] Statuses = ["Draft", "Published"];

    private KnowledgeArticle() { }

    public KnowledgeArticle(string title, string bodyMarkdown, Guid? projectId, string? tags, string status, Guid? createdBy)
    {
        KnowledgeArticleId = Guid.NewGuid();
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        Apply(title, bodyMarkdown, projectId, tags, status);
    }

    public Guid KnowledgeArticleId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string BodyMarkdown { get; private set; } = string.Empty;
    public Guid? ProjectId { get; private set; }
    public string? Tags { get; private set; }
    public string Status { get; private set; } = "Draft";
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(string title, string bodyMarkdown, Guid? projectId, string? tags, string status, Guid? updatedBy)
    {
        Apply(title, bodyMarkdown, projectId, tags, status);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Delete(Guid? deletedBy) { IsDeleted = true; UpdatedAt = DateTime.UtcNow; UpdatedBy = deletedBy; }

    private void Apply(string title, string bodyMarkdown, Guid? projectId, string? tags, string status)
    {
        Title = string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("กรุณาระบุหัวข้อบทความ") : title.Trim();
        if (Title.Length > 300) throw new ArgumentException("หัวข้อบทความยาวได้ไม่เกิน 300 ตัวอักษร");
        BodyMarkdown = string.IsNullOrWhiteSpace(bodyMarkdown) ? throw new ArgumentException("กรุณาระบุเนื้อหาบทความ") : bodyMarkdown.Trim();
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

    public void Delete() { IsDeleted = true; UpdatedAt = DateTime.UtcNow; }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>รายละเอียดปัญหาของ CRM Ticket (mask ข้อมูลอ่อนไหวแล้ว) — ต้นฉบับไม่เก็บ ดูสดจาก CRM ได้</summary>
public sealed class CrmTicketContent
{
    private CrmTicketContent() { }

    public CrmTicketContent(string jobNo, string? descriptionMasked, string contentHash)
    {
        JobNo = string.IsNullOrWhiteSpace(jobNo) ? throw new ArgumentException("JobNo is required.") : jobNo.Trim();
        Apply(descriptionMasked, contentHash);
    }

    public string JobNo { get; private set; } = string.Empty;
    public string? DescriptionMasked { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public DateTime FetchedAt { get; private set; }

    /// <summary>คืน true เมื่อเนื้อหาเปลี่ยน (ต้อง index ใหม่)</summary>
    public bool Apply(string? descriptionMasked, string contentHash)
    {
        var changed = ContentHash != contentHash;
        DescriptionMasked = descriptionMasked; ContentHash = contentHash; FetchedAt = DateTime.UtcNow;
        return changed;
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
