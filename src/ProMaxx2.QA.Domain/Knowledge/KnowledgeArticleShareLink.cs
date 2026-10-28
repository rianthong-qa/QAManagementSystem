namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>
/// ลิงก์อ่านอย่างเดียวของบทความความรู้ (`/?ka=&lt;code&gt;`) สำหรับคนที่ไม่มีบัญชี QA Hub — ไม่มีวันหมดอายุ
/// บทความละ 1 ลิงก์ที่ใช้งานอยู่ (สร้างซ้ำได้หลังยกเลิก); ยกเลิกแล้ว (RevokedAt) หรือบทความถูกลบ/เปลี่ยนเป็นฉบับร่าง = เปิดไม่ได้
/// </summary>
public sealed class KnowledgeArticleShareLink
{
    private KnowledgeArticleShareLink() { }

    public KnowledgeArticleShareLink(string code, Guid knowledgeArticleId, Guid? createdBy)
    {
        if (string.IsNullOrWhiteSpace(code) || knowledgeArticleId == Guid.Empty) throw new ArgumentException("Share code and article are required.");
        KnowledgeArticleShareLinkId = Guid.NewGuid();
        Code = code;
        KnowledgeArticleId = knowledgeArticleId;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid KnowledgeArticleShareLinkId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid KnowledgeArticleId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }

    public void Revoke(Guid? revokedBy)
    {
        if (RevokedAt.HasValue) return;
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
    }
}
