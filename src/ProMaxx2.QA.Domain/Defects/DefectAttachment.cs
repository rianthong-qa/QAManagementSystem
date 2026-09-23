namespace ProMaxx2.QA.Domain.Defects;

/// <summary>Metadata ของรูปที่แนบกับ Defect — ตัวไฟล์ยังเก็บบนดิสก์ที่ <c>App_Data/DefectAttachments/{defectId}/{StoredFileName}</c>
/// แต่รายการ/ผู้อัปโหลด/เวลาอยู่ในฐานข้อมูล เพื่อให้ backup/restore และ audit ตามข้อมูล Defect ได้</summary>
public sealed class DefectAttachment
{
    private DefectAttachment() { }

    public DefectAttachment(Guid attachmentId, Guid defectId, string fileName, string storedFileName, long sizeBytes, string contentType, Guid? uploadedBy, DateTime uploadedAt)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.");
        if (string.IsNullOrWhiteSpace(storedFileName)) throw new ArgumentException("Stored file name is required.");
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        DefectAttachmentId = attachmentId; DefectId = defectId; FileName = fileName.Trim(); StoredFileName = storedFileName;
        SizeBytes = sizeBytes; ContentType = contentType; UploadedBy = uploadedBy; UploadedAt = uploadedAt;
    }

    public Guid DefectAttachmentId { get; private set; }
    public Guid DefectId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public Guid? UploadedBy { get; private set; }
    public DateTime UploadedAt { get; private set; }
}
