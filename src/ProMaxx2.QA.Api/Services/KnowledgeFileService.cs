using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record KnowledgeFileDto(Guid KnowledgeFileId, string FileName, string Extension, long SizeBytes, Guid? ProjectId, string? ProjectName, string? Description,
    string Origin, string? ImportPath, int ExtractedChars, string? ExtractError, DateTime UploadedAt, string? UploadedByName, DateTime? UpdatedAt, string? IndexState, DateTime? IndexedAt);
public sealed record KnowledgeFileUploadResult(string FileName, bool Success, string? Message, KnowledgeFileDto? Item);
public sealed record KnowledgeImportResult(int Added, int Updated, int Unchanged, int Failed, IReadOnlyList<string> Messages);
public sealed record IncomingKnowledgeFile(string FileName, byte[] Data);

/// <summary>
/// ไฟล์เอกสารในคลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md Phase 3) — ไฟล์บนดิสก์ App_Data/KnowledgeFiles/{id}/, ข้อความที่สกัดได้อยู่ในตาราง
/// อัปโหลดครั้งละ ≤ 10 ไฟล์, ไฟล์ละ ≤ 20 MB, รวม ≤ 50 MB (Cloudflare แผนฟรีรับคำขอได้ ≤ 100 MB); กันไฟล์ซ้ำด้วย SHA-256
/// นำเข้าเอกสารระบบ (SYS_ADMIN) อ่านเฉพาะโฟลเดอร์ใน config Knowledge:SystemDocumentFolders — ผู้ใช้ส่ง path เองไม่ได้
/// </summary>
public sealed class KnowledgeFileService(QaDbContext db, IWebHostEnvironment environment, IConfiguration configuration, KnowledgeIndexTrigger trigger)
{
    public const int MaxFiles = 10;
    public const long MaxFileBytes = 20_000_000;
    public const long MaxTotalBytes = 50_000_000;
    public const long MaxImportFileBytes = 5_000_000;
    public const string AuditEntityType = "KnowledgeFile";

    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "KnowledgeFiles");

    public async Task<IReadOnlyList<KnowledgeFileUploadResult>> UploadAsync(IReadOnlyList<IncomingKnowledgeFile> files, Guid? projectId, string? description, Guid userId, string? clientIp, CancellationToken ct)
    {
        if (files.Count == 0 || files.Count > MaxFiles) throw new ArgumentException($"อัปโหลดได้ครั้งละ 1–{MaxFiles} ไฟล์");
        if (files.Any(x => x.Data.LongLength == 0 || x.Data.LongLength > MaxFileBytes) || files.Sum(x => x.Data.LongLength) > MaxTotalBytes)
            throw new ArgumentException("ไฟล์ละไม่เกิน 20 MB และรวมไม่เกิน 50 MB ต่อครั้ง");
        var results = new List<(string Name, bool Success, string Message, KnowledgeFile? File)>();
        var added = new List<KnowledgeFile>();
        foreach (var incoming in files)
        {
            var name = DisplayName(incoming.FileName);
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (!KnowledgeFile.AllowedExtensions.Contains(extension)) { results.Add((name, false, "รองรับเฉพาะ PDF, DOCX, MD และ TXT", null)); continue; }
            if (!DocumentTextExtractor.LooksValid(incoming.Data, extension)) { results.Add((name, false, $"เนื้อหาไฟล์ไม่ใช่ {extension.TrimStart('.').ToUpperInvariant()} ที่ถูกต้อง", null)); continue; }
            var hash = FileHash(incoming.Data);
            var duplicate = await db.KnowledgeFiles.AsNoTracking().Where(x => !x.IsDeleted && x.ContentHash == hash && x.ProjectId == projectId).Select(x => x.FileName).FirstOrDefaultAsync(ct)
                ?? added.Where(x => x.ContentHash == hash).Select(x => x.FileName).FirstOrDefault();
            if (duplicate is not null) { results.Add((name, false, $"ซ้ำกับไฟล์ \"{duplicate}\" ที่มีอยู่แล้ว", null)); continue; }
            var extracted = DocumentTextExtractor.Extract(incoming.Data, extension);
            var file = await SaveNewAsync(name, incoming.Data, hash, projectId, description, userId, null, ct);
            file.SetExtractedText(extracted.Text, extracted.Error);
            db.KnowledgeFiles.Add(file);
            db.AuditLogs.Add(new AuditLog(userId, "Created", AuditEntityType, file.KnowledgeFileId.ToString(),
                $"อัปโหลดเอกสาร \"{file.FileName}\" ({FormatSize(file.SizeBytes)}) {(extracted.Error is null ? $"สกัดข้อความได้ {file.ExtractedText?.Length ?? 0:N0} ตัวอักษร" : $"สกัดข้อความไม่ได้: {extracted.Error}")}", null, null, clientIp));
            added.Add(file);
            results.Add((name, extracted.Error is null, extracted.Error ?? $"สกัดข้อความได้ {file.ExtractedText?.Length ?? 0:N0} ตัวอักษร", file));
        }
        await db.SaveChangesAsync(ct);
        if (added.Count > 0) trigger.Signal();
        var dtos = (await MapAsync(added, ct)).ToDictionary(x => x.KnowledgeFileId);
        return results.Select(r => new KnowledgeFileUploadResult(r.Name, r.Success, r.Message, r.File is null ? null : dtos[r.File.KnowledgeFileId])).ToList();
    }

    /// <summary>นำเข้าเอกสาร .md / .txt จากโฟลเดอร์ระบบเป็นความรู้กลาง — ซ้ำได้: ไม่เปลี่ยน = ข้าม, เนื้อหาเปลี่ยน = อัปเดต</summary>
    public async Task<KnowledgeImportResult> ImportSystemDocumentsAsync(Guid userId, string? clientIp, CancellationToken ct)
    {
        var folders = configuration.GetSection("Knowledge:SystemDocumentFolders").Get<string[]>() is { Length: > 0 } configured
            ? configured : [Path.Combine(environment.ContentRootPath, "..", "..", "Document", "ProMaxx2_MD")];
        int added = 0, updated = 0, unchanged = 0, failed = 0;
        var messages = new List<string>();
        foreach (var configuredFolder in folders)
        {
            var folder = Path.GetFullPath(Path.IsPathRooted(configuredFolder) ? configuredFolder : Path.Combine(environment.ContentRootPath, configuredFolder));
            if (!Directory.Exists(folder)) { messages.Add($"ไม่พบโฟลเดอร์ {Path.GetFileName(folder)}"); continue; }
            var folderName = new DirectoryInfo(folder).Name;
            var paths = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(x => Path.GetExtension(x).ToLowerInvariant() is ".md" or ".txt").Order(StringComparer.Ordinal).Take(500).ToList();
            foreach (var path in paths)
            {
                var importPath = $"{folderName}/{Path.GetRelativePath(folder, path).Replace('\\', '/')}";
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length == 0 || info.Length > MaxImportFileBytes) { failed++; messages.Add($"{importPath}: ขนาดไฟล์ไม่อยู่ในช่วง 1 B – 5 MB"); continue; }
                    var data = await File.ReadAllBytesAsync(path, ct);
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (!DocumentTextExtractor.LooksValid(data, extension)) { failed++; messages.Add($"{importPath}: ไม่ใช่ไฟล์ข้อความ"); continue; }
                    var hash = FileHash(data);
                    var existing = await db.KnowledgeFiles.FirstOrDefaultAsync(x => !x.IsDeleted && x.ImportPath == importPath, ct);
                    if (existing is not null && existing.ContentHash == hash) { unchanged++; continue; }
                    var extracted = DocumentTextExtractor.Extract(data, extension);
                    if (existing is null)
                    {
                        var file = await SaveNewAsync(Path.GetFileName(path), data, hash, null, $"เอกสารระบบ: {importPath}", userId, importPath, ct);
                        file.SetExtractedText(extracted.Text, extracted.Error);
                        db.KnowledgeFiles.Add(file);
                        added++;
                    }
                    else
                    {
                        var stored = await WriteAsync(existing.KnowledgeFileId, Path.GetFileName(path), data, ct);
                        existing.ReplaceContent(ContentTypeFor(extension), data.LongLength, stored, hash);
                        existing.SetExtractedText(extracted.Text, extracted.Error);
                        updated++;
                    }
                    if (extracted.Error is not null) messages.Add($"{importPath}: {extracted.Error}");
                }
                catch (IOException ex) { failed++; messages.Add($"{importPath}: อ่านไฟล์ไม่สำเร็จ ({ex.Message})"); }
            }
        }
        db.AuditLogs.Add(new AuditLog(userId, "Imported", AuditEntityType, "system-documents",
            $"นำเข้าเอกสารระบบ: ใหม่ {added}, อัปเดต {updated}, ไม่เปลี่ยน {unchanged}, ไม่สำเร็จ {failed}", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        if (added + updated > 0) trigger.Signal();
        return new(added, updated, unchanged, failed, messages);
    }

    public async Task<bool> DeleteAsync(Guid fileId, IReadOnlyCollection<Guid> allowedProjects, Guid userId, string? clientIp, CancellationToken ct)
    {
        var file = await db.KnowledgeFiles.FirstOrDefaultAsync(x => x.KnowledgeFileId == fileId && !x.IsDeleted && (x.ProjectId == null || allowedProjects.Contains(x.ProjectId.Value)), ct);
        if (file is null) return false;
        file.Delete();
        db.AuditLogs.Add(new AuditLog(userId, "Deleted", AuditEntityType, fileId.ToString(), $"ลบเอกสาร \"{file.FileName}\" ออกจากคลังความรู้", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        try { var folder = Path.Combine(Root, fileId.ToString("N")); if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { /* ลบไฟล์ไม่ได้ไม่กระทบการลบในระบบ */ }
        trigger.Signal();
        return true;
    }

    public async Task<IReadOnlyList<KnowledgeFileDto>> ListAsync(IReadOnlyCollection<Guid> allowedProjects, Guid? projectId, string? search, CancellationToken ct)
    {
        var q = db.KnowledgeFiles.AsNoTracking().Where(x => !x.IsDeleted && (x.ProjectId == null || allowedProjects.Contains(x.ProjectId.Value)));
        if (projectId.HasValue) q = q.Where(x => x.ProjectId == projectId || x.ProjectId == null);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(x => x.FileName.Contains(s) || (x.Description != null && x.Description.Contains(s))); }
        return await MapAsync(await q.OrderByDescending(x => x.UpdatedAt ?? x.UploadedAt).Take(500).ToListAsync(ct), ct);
    }

    /// <summary>ไฟล์สำหรับดาวน์โหลด — null เมื่อไม่พบ/ไม่มีสิทธิ์/ไฟล์บนดิสก์หาย</summary>
    public async Task<(string Path, string ContentType, string FileName)?> OpenAsync(Guid fileId, IReadOnlyCollection<Guid> allowedProjects, CancellationToken ct)
    {
        var file = await db.KnowledgeFiles.AsNoTracking().FirstOrDefaultAsync(x => x.KnowledgeFileId == fileId && !x.IsDeleted && (x.ProjectId == null || allowedProjects.Contains(x.ProjectId.Value)), ct);
        if (file is null) return null;
        var full = Path.GetFullPath(Path.Combine(Root, file.StoragePath));
        if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return (full, file.ContentType, file.FileName);
    }

    private async Task<KnowledgeFile> SaveNewAsync(string fileName, byte[] data, string hash, Guid? projectId, string? description, Guid userId, string? importPath, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var file = new KnowledgeFile(fileName, ContentTypeFor(extension), data.LongLength, "pending", hash, projectId, description, userId, importPath);
        var stored = await WriteAsync(file.KnowledgeFileId, fileName, data, ct);
        file.ReplaceContent(ContentTypeFor(extension), data.LongLength, stored, hash);
        return file;
    }

    /// <summary>เขียนไฟล์ลง App_Data/KnowledgeFiles/{id}/ — ชื่อบนดิสก์สุ่ม + นามสกุล (กัน path traversal / ชื่อแปลก); คืน path สัมพัทธ์กับ Root</summary>
    private async Task<string> WriteAsync(Guid fileId, string fileName, byte[] data, CancellationToken ct)
    {
        var folder = Path.Combine(Root, fileId.ToString("N"));
        Directory.CreateDirectory(folder);
        var stored = $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        await File.WriteAllBytesAsync(Path.Combine(folder, stored), data, ct);
        return Path.Combine(fileId.ToString("N"), stored);
    }

    private async Task<IReadOnlyList<KnowledgeFileDto>> MapAsync(IReadOnlyList<KnowledgeFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return [];
        var projectIds = files.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => x.ProjectName, ct);
        var userIds = files.Where(x => x.UploadedBy.HasValue).Select(x => x.UploadedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var keys = files.Select(x => x.KnowledgeFileId.ToString()).ToList();
        var docs = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.SourceType == "File" && keys.Contains(x.SourceKey)).ToDictionaryAsync(x => x.SourceKey, x => new { x.State, x.IndexedAt }, ct);
        return files.Select(f =>
        {
            var doc = docs.GetValueOrDefault(f.KnowledgeFileId.ToString());
            return new KnowledgeFileDto(f.KnowledgeFileId, f.FileName, f.Extension, f.SizeBytes, f.ProjectId, f.ProjectId.HasValue ? projects.GetValueOrDefault(f.ProjectId.Value) : null,
                f.Description, f.Origin, f.ImportPath, f.ExtractedText?.Length ?? 0, f.ExtractError, f.UploadedAt, f.UploadedBy.HasValue ? users.GetValueOrDefault(f.UploadedBy.Value) : null,
                f.UpdatedAt, doc?.State, doc?.IndexedAt);
        }).ToList();
    }

    /// <summary>ชื่อไฟล์ที่แสดง — ตัด path และอักขระที่ใช้ไม่ได้ แต่คงตัวอักษรไทยรวมสระ/วรรณยุกต์</summary>
    public static string DisplayName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/').Split('/').Last());
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.' or '(' or ')'
            || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark).ToArray()).Trim(' ', '.');
        if (cleaned.Length == 0) cleaned = "document";
        return cleaned.Length > 200 ? cleaned[..(200 - Path.GetExtension(cleaned).Length)] + Path.GetExtension(cleaned) : cleaned;
    }

    public static string ContentTypeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".md" => "text/markdown; charset=utf-8",
        _ => "text/plain; charset=utf-8",
    };

    private static string FileHash(byte[] data) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));
    private static string FormatSize(long bytes) => bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:0.#} MB" : $"{Math.Max(1, bytes / 1000)} KB";
}
