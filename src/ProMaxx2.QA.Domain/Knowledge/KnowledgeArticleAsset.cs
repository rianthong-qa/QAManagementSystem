namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>
/// รูป/ฟอนต์ที่แยกออกจากบทความ HTML (เดิมฝังเป็น data: base64 ทำให้ไฟล์ใหญ่เกินเพดาน) — ไฟล์อยู่บนดิสก์ App_Data/KnowledgeAssets/,
/// HTML ของบทความอ้างถึงด้วย <c>qa-asset:{id:N}</c> และหน้าเว็บแปลงเป็น URL ตอนแสดงผล (htmlArticle.ts)
/// อัปโหลดก่อนบันทึกบทความ (KnowledgeArticleId ว่าง) แล้วผูกกับบทความเมื่อบันทึก — ที่ไม่ถูกผูกเกิน 2 วันถูกลบ (KnowledgeAssetService)
/// </summary>
public sealed class KnowledgeArticleAsset
{
    private KnowledgeArticleAsset() { }

    public KnowledgeArticleAsset(Guid id, string contentType, long sizeBytes, string contentHash, Guid? uploadedBy)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(contentType) || sizeBytes <= 0) throw new ArgumentException("Asset id, type and size are required.");
        KnowledgeArticleAssetId = id;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ContentHash = contentHash;
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
    }

    public Guid KnowledgeArticleAssetId { get; private set; }
    public Guid? KnowledgeArticleId { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public Guid? UploadedBy { get; private set; }
    public DateTime UploadedAt { get; private set; }

    public void AttachTo(Guid knowledgeArticleId) => KnowledgeArticleId ??= knowledgeArticleId;
}
