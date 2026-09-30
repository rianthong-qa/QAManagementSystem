namespace ProMaxx2.QA.Domain.Defects;

// short code สุ่มของลิงก์อ่านอย่างเดียว (`?d=<code>`) ที่แนบท้าย Description ของ CRM ticket — Defect ละ 1 code
// ใช้ซ้ำทุกครั้งที่ส่ง; ปิดการเข้าถึงได้ด้วยการลบ Defect (endpoint ตรวจ IsDeleted)
public sealed class DefectShareLink
{
    private DefectShareLink() { }
    public DefectShareLink(string code, Guid defectId)
    {
        if (string.IsNullOrWhiteSpace(code) || defectId == Guid.Empty) throw new ArgumentException("Share code and defect are required.");
        DefectShareLinkId = Guid.NewGuid(); Code = code; DefectId = defectId; CreatedAt = DateTime.UtcNow;
    }
    public Guid DefectShareLinkId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid DefectId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
