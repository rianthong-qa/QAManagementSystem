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

/// <summary>ไฟล์เอกสารที่อัปโหลดเข้าคลังความรู้ (PDF / DOCX / MD / TXT) — ไฟล์อยู่บนดิสก์ App_Data/KnowledgeFiles, เก็บข้อความที่แยกได้ไว้ใช้สร้าง chunk</summary>
public sealed class KnowledgeFile
{
    public static readonly string[] AllowedExtensions = [".pdf", ".docx", ".md", ".txt"];

    private KnowledgeFile() { }

    public KnowledgeFile(string fileName, string contentType, long sizeBytes, string storagePath, Guid? projectId, string? description, Guid? uploadedBy)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) throw new ArgumentException("รองรับเฉพาะไฟล์ PDF, DOCX, MD และ TXT");
        KnowledgeFileId = Guid.NewGuid();
        FileName = fileName!.Trim();
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        ProjectId = projectId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
    }

    public Guid KnowledgeFileId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public Guid? ProjectId { get; private set; }
    public string? Description { get; private set; }
    public string? ExtractedText { get; private set; }
    public string? ExtractError { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public Guid? UploadedBy { get; private set; }

    public void SetExtractedText(string? text, string? error) { ExtractedText = text; ExtractError = error; }
    public void Delete() => IsDeleted = true;
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
