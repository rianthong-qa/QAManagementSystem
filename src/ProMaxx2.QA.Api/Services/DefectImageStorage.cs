using Microsoft.AspNetCore.Http;
using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Api.Services;

// ที่เก็บไฟล์รูปของ Defect (รูปประกอบ Defect และรูปในคอมเมนต์) — ไฟล์อยู่บนดิสก์ที่ App_Data/DefectAttachments/{defectId}/
// ส่วน metadata อยู่ในตาราง DefectAttachments; ใช้กฎเดียวกันทั้งสองแบบ: PNG/JPG/WebP, สูงสุด 5 รูป, รูปละ ≤ 5 MB, รวม ≤ 20 MB
public sealed class DefectImageStorage(IWebHostEnvironment environment)
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 5_000_000;
    public const long MaxTotalBytes = 20_000_000;

    public sealed record PendingImage(Guid Id, string DisplayName, string StoredName, string ContentType, byte[] Data);

    public string Directory(Guid defectId) => Path.Combine(environment.ContentRootPath, "App_Data", "DefectAttachments", defectId.ToString("N"));
    public string PathOf(Guid defectId, string storedFileName) => Path.Combine(Directory(defectId), storedFileName);

    /// <summary>ตรวจและอ่านไฟล์ที่อัปโหลด — คืน error (ข้อความภาษาไทยสำหรับผู้ใช้) ถ้าไม่ผ่าน; <paramref name="existing"/> คือขนาดของรูปที่มีอยู่แล้วในกลุ่มเดียวกัน</summary>
    public async Task<(List<PendingImage> Images, string? Error)> ReadAsync(IReadOnlyList<IFormFile> files, IReadOnlyCollection<long> existing, CancellationToken ct)
    {
        if (files.Count == 0 || files.Count > MaxFiles || existing.Count + files.Count > MaxFiles) return ([], $"แนบรูปได้สูงสุด {MaxFiles} รูป");
        if (files.Any(x => x.Length == 0 || x.Length > MaxFileBytes) || existing.Sum() + files.Sum(x => x.Length) > MaxTotalBytes) return ([], "รูปแต่ละไฟล์ต้องไม่เกิน 5 MB และรวมไม่เกิน 20 MB");
        var pending = new List<PendingImage>();
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) return ([], "รองรับเฉพาะ PNG, JPG และ WebP");
            await using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct); var bytes = memory.ToArray();
            if (!ValidImage(bytes, extension)) return ([], $"ไฟล์ {Path.GetFileName(file.FileName)} ไม่ใช่รูปภาพที่ถูกต้อง");
            var safeName = new string(Path.GetFileNameWithoutExtension(file.FileName).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').Take(70).ToArray()).Trim();
            var display = $"{(safeName.Length > 0 ? safeName : "image")}{extension}";
            var id = Guid.NewGuid();
            pending.Add(new PendingImage(id, display, $"{id:N}_{display}", ContentTypeFor(extension), bytes));
        }
        return (pending, null);
    }

    /// <summary>เขียนไฟล์ลงดิสก์และคืน entity ที่ต้อง Add — ถ้าเขียนไม่สำเร็จจะลบไฟล์ที่เขียนไปแล้วก่อน throw</summary>
    public async Task<List<DefectAttachment>> WriteAsync(Guid defectId, IReadOnlyList<PendingImage> images, Guid? uploadedBy, Guid? commentId, CancellationToken ct)
    {
        var directory = Directory(defectId); System.IO.Directory.CreateDirectory(directory);
        var written = new List<string>();
        try
        {
            foreach (var item in images) { var path = Path.Combine(directory, item.StoredName); await File.WriteAllBytesAsync(path, item.Data, ct); written.Add(path); }
        }
        catch { foreach (var path in written) TryDelete(path); throw; }
        var now = DateTime.UtcNow;
        return images.Select(x => new DefectAttachment(x.Id, defectId, x.DisplayName, x.StoredName, x.Data.LongLength, x.ContentType, uploadedBy, now, commentId)).ToList();
    }

    public void DeleteFiles(Guid defectId, IEnumerable<string> storedFileNames) { foreach (var name in storedFileNames) TryDelete(PathOf(defectId, name)); }

    public static bool TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }

    public static string ContentTypeFor(string extension) => extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream" };

    private static bool ValidImage(byte[] bytes, string extension) => extension switch
    {
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };
}
