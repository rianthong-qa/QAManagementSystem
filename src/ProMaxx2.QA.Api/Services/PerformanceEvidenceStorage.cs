using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Api.Services;

// ที่เก็บไฟล์หลักฐานของบันทึกงาน (หน้า ผลงานและการประเมิน) — ไฟล์อยู่บนดิสก์ที่ App_Data/PerformanceEvidence/{workLogEntryId}/
// ส่วน metadata อยู่ในตาราง WorkLogAttachments; รูปแบบเดียวกับ DefectImageStorage แต่รับ PDF ด้วย (ใบประกาศ/เอกสารอบรม)
// กฎ: PNG/JPG/WebP/PDF, สูงสุด 5 ไฟล์ต่อบันทึก, ไฟล์ละ ≤ 5 MB, รวม ≤ 20 MB
public sealed class PerformanceEvidenceStorage(IWebHostEnvironment environment)
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 5_000_000;
    public const long MaxTotalBytes = 20_000_000;

    public sealed record PendingFile(Guid Id, string DisplayName, string StoredName, string ContentType, byte[] Data);

    public string Directory(Guid workLogEntryId) => Path.Combine(environment.ContentRootPath, "App_Data", "PerformanceEvidence", workLogEntryId.ToString("N"));
    public string PathOf(Guid workLogEntryId, string storedFileName) => Path.Combine(Directory(workLogEntryId), storedFileName);

    /// <summary>ตรวจและอ่านไฟล์ที่อัปโหลด — คืน error (ข้อความภาษาไทยสำหรับผู้ใช้) ถ้าไม่ผ่าน; <paramref name="existing"/> คือขนาดของไฟล์ที่แนบอยู่แล้ว</summary>
    public async Task<(List<PendingFile> Files, string? Error)> ReadAsync(IReadOnlyList<IFormFile> files, IReadOnlyCollection<long> existing, CancellationToken ct)
    {
        if (files.Count == 0) return ([], "ไม่พบไฟล์ที่อัปโหลด");
        if (existing.Count + files.Count > MaxFiles) return ([], $"แนบไฟล์ได้สูงสุด {MaxFiles} ไฟล์ต่อบันทึก");
        if (files.Any(x => x.Length == 0 || x.Length > MaxFileBytes) || existing.Sum() + files.Sum(x => x.Length) > MaxTotalBytes) return ([], "ไฟล์แต่ละไฟล์ต้องไม่เกิน 5 MB และรวมไม่เกิน 20 MB");
        var pending = new List<PendingFile>();
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".pdf")) return ([], "รองรับเฉพาะ PNG, JPG, WebP และ PDF");
            await using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct); var bytes = memory.ToArray();
            if (!ValidFile(bytes, extension)) return ([], $"ไฟล์ {Path.GetFileName(file.FileName)} ไม่ตรงกับนามสกุลไฟล์");
            var safeName = new string(Path.GetFileNameWithoutExtension(file.FileName).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').Take(70).ToArray()).Trim();
            var display = $"{(safeName.Length > 0 ? safeName : "evidence")}{extension}";
            var id = Guid.NewGuid();
            pending.Add(new PendingFile(id, display, $"{id:N}{extension}", ContentTypeFor(extension), bytes));
        }
        return (pending, null);
    }

    /// <summary>เขียนไฟล์ลงดิสก์และคืน entity ที่ต้อง Add — ถ้าเขียนไม่สำเร็จจะลบไฟล์ที่เขียนไปแล้วก่อน throw</summary>
    public async Task<List<WorkLogAttachment>> WriteAsync(Guid workLogEntryId, IReadOnlyList<PendingFile> files, Guid uploadedBy, CancellationToken ct)
    {
        var directory = Directory(workLogEntryId); System.IO.Directory.CreateDirectory(directory);
        var written = new List<string>();
        try
        {
            foreach (var item in files) { var path = Path.Combine(directory, item.StoredName); await File.WriteAllBytesAsync(path, item.Data, ct); written.Add(path); }
        }
        catch { foreach (var path in written) DefectImageStorage.TryDelete(path); throw; }
        var now = DateTime.UtcNow;
        return files.Select(x => new WorkLogAttachment(x.Id, workLogEntryId, x.DisplayName, x.StoredName, x.Data.LongLength, x.ContentType, uploadedBy, now)).ToList();
    }

    public void DeleteFiles(Guid workLogEntryId, IEnumerable<string> storedFileNames) { foreach (var name in storedFileNames) DefectImageStorage.TryDelete(PathOf(workLogEntryId, name)); }

    /// <summary>ลบทั้งโฟลเดอร์ของบันทึกงาน (หลังลบบันทึกงานแล้ว) — คืน false ถ้าลบไม่ได้ (ไฟล์ถูกล็อก)</summary>
    public bool DeleteDirectory(Guid workLogEntryId)
    {
        try { var dir = Directory(workLogEntryId); if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string ContentTypeFor(string extension) => extension == ".pdf" ? "application/pdf" : DefectImageStorage.ContentTypeFor(extension);

    private static bool ValidFile(byte[] bytes, string extension) => extension switch
    {
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        ".pdf" => bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
        _ => false,
    };
}
