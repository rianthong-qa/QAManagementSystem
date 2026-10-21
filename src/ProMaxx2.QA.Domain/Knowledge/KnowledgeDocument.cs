namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>
/// เอกสารในคลังความรู้ — 1 แถวต่อ 1 แหล่งข้อมูล (Defect / CRM Ticket / บทความ / ไฟล์) ข้อความที่ค้นได้อยู่ใน <see cref="KnowledgeChunk"/>
/// Scope กำหนดสิทธิ์ตอนค้น: Project = เฉพาะสมาชิก Project, Crm = ผู้มีสิทธิ์ CRM.VIEW, Global = ทุกคนที่ใช้คลังความรู้ได้
/// State = Stale เมื่อข้อมูลต้นทางเปลี่ยน แล้ว KnowledgeIndexWorker สร้าง chunk ใหม่ (ContentHash เดิม = ไม่สร้างซ้ำ)
/// </summary>
public sealed class KnowledgeDocument
{
    public static readonly string[] SourceTypes = ["Defect", "CrmTicket", "Article", "File"];
    public static readonly string[] Scopes = ["Project", "Crm", "Global"];
    public static readonly string[] States = ["Stale", "Indexed", "Failed", "Excluded"];

    private KnowledgeDocument() { }

    public KnowledgeDocument(string sourceType, string sourceKey, string scope, Guid? projectId, string title, string? sourceUrl)
    {
        KnowledgeDocumentId = Guid.NewGuid();
        SourceType = Normalize(SourceTypes, sourceType, "ประเภทแหล่งความรู้ไม่ถูกต้อง");
        SourceKey = string.IsNullOrWhiteSpace(sourceKey) ? throw new ArgumentException("SourceKey is required.") : sourceKey.Trim();
        CreatedAt = DateTime.UtcNow;
        UpdateSource(scope, projectId, title, sourceUrl);
    }

    public Guid KnowledgeDocumentId { get; private set; }
    public string SourceType { get; private set; } = "Article";
    public string SourceKey { get; private set; } = string.Empty;
    public string Scope { get; private set; } = "Global";
    public Guid? ProjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? SourceUrl { get; private set; }
    public string? ContentHash { get; private set; }
    public string State { get; private set; } = "Stale";
    public bool ExcludeFromAi { get; private set; }
    public bool ContainsCustomerData { get; private set; }
    public int ChunkCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? IndexedAt { get; private set; }
    public string? Error { get; private set; }

    /// <summary>ข้อมูลประจำแหล่ง (ชื่อ/สิทธิ์/ลิงก์) เปลี่ยน — ต้องสร้าง chunk ใหม่</summary>
    public void UpdateSource(string scope, Guid? projectId, string title, string? sourceUrl)
    {
        Scope = Normalize(Scopes, scope, "ขอบเขตความรู้ไม่ถูกต้อง");
        if (Scope == "Project" && projectId is null) throw new ArgumentException("ความรู้ระดับ Project ต้องระบุ Project");
        ProjectId = projectId;
        Title = Cut(string.IsNullOrWhiteSpace(title) ? SourceKey : title.Trim(), 300)!;
        SourceUrl = Cut(sourceUrl, 500);
        ContainsCustomerData = SourceType == "CrmTicket";
        if (State != "Excluded") State = "Stale";
    }

    public void MarkStale() { if (State != "Excluded") State = "Stale"; }

    public void MarkIndexed(string contentHash, int chunkCount)
    {
        ContentHash = contentHash; ChunkCount = chunkCount; State = "Indexed"; IndexedAt = DateTime.UtcNow; Error = null;
    }

    public void MarkFailed(string error) { State = "Failed"; Error = Cut(error, 2000); }

    /// <summary>แหล่งข้อมูลถูกลบ — คง record ไว้แต่ไม่ค้น (worker ลบ chunk ทิ้ง)</summary>
    public void Exclude() { State = "Excluded"; ChunkCount = 0; }

    /// <summary>กลับมาใช้ได้อีก (เช่น ยกเลิกการลบ) — ต้องสร้าง chunk ใหม่</summary>
    public void Restore() { if (State == "Excluded") { State = "Stale"; ContentHash = null; } }

    public void SetExcludeFromAi(bool exclude) => ExcludeFromAi = exclude;

    private static string Normalize(string[] allowed, string value, string error) =>
        allowed.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException(error);

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>ข้อความช่วงหนึ่งของเอกสาร (~1,000 ตัวอักษร) — มี Full-Text Index ภาษาไทย (LCID 1054) และ embedding (float32 little-endian) เมื่อเปิดใช้</summary>
public sealed class KnowledgeChunk
{
    private KnowledgeChunk() { }

    public KnowledgeChunk(Guid knowledgeDocumentId, int chunkNo, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Chunk text is required.");
        KnowledgeDocumentId = knowledgeDocumentId;
        ChunkNo = chunkNo;
        Text = text;
        CreatedAt = DateTime.UtcNow;
    }

    public int KnowledgeChunkId { get; private set; }
    public Guid KnowledgeDocumentId { get; private set; }
    public int ChunkNo { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public byte[]? Embedding { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public int? EmbeddingDim { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void SetEmbedding(float[] vector, string model)
    {
        if (vector.Length == 0) throw new ArgumentException("Embedding is empty.");
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        Embedding = bytes; EmbeddingModel = model; EmbeddingDim = vector.Length;
    }

    public float[]? GetEmbedding()
    {
        if (Embedding is null) return null;
        var vector = new float[Embedding.Length / sizeof(float)];
        Buffer.BlockCopy(Embedding, 0, vector, 0, Embedding.Length);
        return vector;
    }
}
