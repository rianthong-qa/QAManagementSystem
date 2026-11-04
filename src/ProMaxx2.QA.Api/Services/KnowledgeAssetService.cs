using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record KnowledgeAssetUploadResult(string? AssetId, string? Error);

/// <summary>
/// รูป/ฟอนต์ที่หน้าเว็บแยกออกจากบทความ HTML ก่อนบันทึก (ดู KnowledgeArticleAsset) — ไฟล์อยู่ที่ App_Data/KnowledgeAssets/{id:N}
/// ชนิดไฟล์ตัดสินจากเนื้อไฟล์ (magic bytes) ไม่เชื่อชนิดที่ผู้อัปโหลดบอก; ต่อคำขอ ≤ 60 ไฟล์, ไฟล์ละ ≤ 15 MB, รวม ≤ 50 MB (Cloudflare รับคำขอ ≤ 100 MB)
/// </summary>
public sealed class KnowledgeAssetService(QaDbContext db, IWebHostEnvironment environment, ILogger<KnowledgeAssetService> logger)
{
    public const int MaxFiles = 60;
    public const long MaxFileBytes = 15_000_000;
    public const long MaxTotalBytes = 50_000_000;
    /// <summary>ไฟล์ที่อัปโหลดแล้วแต่ไม่ถูกผูกกับบทความ (ผู้ใช้ปิดฟอร์ม/บันทึกไม่สำเร็จ) เก็บไว้เท่านี้ก่อนลบ</summary>
    private static readonly TimeSpan OrphanRetention = TimeSpan.FromDays(2);

    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "KnowledgeAssets");
    private string PathOf(Guid id) => Path.Combine(Root, id.ToString("N"));

    public async Task<IReadOnlyList<KnowledgeAssetUploadResult>> UploadAsync(IReadOnlyList<byte[]> files, Guid userId, CancellationToken ct)
    {
        if (files.Count == 0 || files.Count > MaxFiles) throw new ArgumentException($"อัปโหลดได้ครั้งละ 1–{MaxFiles} ไฟล์");
        if (files.Sum(x => x.LongLength) > MaxTotalBytes) throw new ArgumentException("ขนาดไฟล์รวมต่อครั้งต้องไม่เกิน 50 MB");
        await DeleteOrphansAsync(ct);
        Directory.CreateDirectory(Root);
        var results = new List<KnowledgeAssetUploadResult>();
        var written = new List<string>();
        try
        {
            foreach (var data in files)
            {
                if (data.LongLength == 0 || data.LongLength > MaxFileBytes) { results.Add(new(null, "ไฟล์ว่างหรือใหญ่เกิน 15 MB")); continue; }
                if (DetectContentType(data) is not { } contentType) { results.Add(new(null, "ไม่ใช่รูปภาพหรือฟอนต์ที่รองรับ")); continue; }
                var asset = new KnowledgeArticleAsset(Guid.NewGuid(), contentType, data.LongLength, Convert.ToHexStringLower(SHA256.HashData(data)), userId);
                var path = PathOf(asset.KnowledgeArticleAssetId);
                await File.WriteAllBytesAsync(path, data, ct);
                written.Add(path);
                db.KnowledgeArticleAssets.Add(asset);
                results.Add(new(asset.KnowledgeArticleAssetId.ToString("N"), null));
            }
            await db.SaveChangesAsync(ct);
        }
        catch { foreach (var path in written) DefectImageStorage.TryDelete(path); throw; }
        return results;
    }

    public async Task<(string Path, string ContentType)?> OpenAsync(Guid id, CancellationToken ct)
    {
        var asset = await db.KnowledgeArticleAssets.AsNoTracking().FirstOrDefaultAsync(x => x.KnowledgeArticleAssetId == id, ct);
        if (asset is null) return null;
        var path = PathOf(id);
        return File.Exists(path) ? (path, asset.ContentType) : null;
    }

    /// <summary>ผูกไฟล์ที่ HTML ของบทความอ้างถึงและยังไม่มีเจ้าของเข้ากับบทความ (ไม่ SaveChanges — ให้บันทึกพร้อมบทความ)</summary>
    public async Task AttachAsync(Guid articleId, string? html, CancellationToken ct)
    {
        var ids = HtmlArticleContent.AssetIds(html);
        if (ids.Count == 0) return;
        var assets = await db.KnowledgeArticleAssets.Where(x => ids.Contains(x.KnowledgeArticleAssetId) && x.KnowledgeArticleId == null).ToListAsync(ct);
        foreach (var asset in assets) asset.AttachTo(articleId);
    }

    private async Task DeleteOrphansAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - OrphanRetention;
        var orphans = await db.KnowledgeArticleAssets.Where(x => x.KnowledgeArticleId == null && x.UploadedAt < cutoff).Take(200).ToListAsync(ct);
        if (orphans.Count == 0) return;
        db.KnowledgeArticleAssets.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
        foreach (var orphan in orphans) DefectImageStorage.TryDelete(PathOf(orphan.KnowledgeArticleAssetId));
        logger.LogInformation("Deleted {Count} unattached knowledge article assets", orphans.Count);
    }

    /// <summary>ชนิดไฟล์จากเนื้อไฟล์ — null = ไม่รองรับ (SVG เสิร์ฟพร้อม CSP sandbox จึงรันสคริปต์ไม่ได้แม้เปิดตรง ๆ)</summary>
    public static string? DetectContentType(byte[] d)
    {
        bool At(int offset, params byte[] sig) => d.Length >= offset + sig.Length && d.AsSpan(offset, sig.Length).SequenceEqual(sig);
        bool Ascii(int offset, string sig) => At(offset, Encoding.ASCII.GetBytes(sig));
        if (At(0, 0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (At(0, 0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (Ascii(0, "GIF8")) return "image/gif";
        if (Ascii(0, "RIFF") && Ascii(8, "WEBP")) return "image/webp";
        if (Ascii(4, "ftypavif") || Ascii(4, "ftypavis")) return "image/avif";
        if (Ascii(0, "BM")) return "image/bmp";
        if (At(0, 0x00, 0x00, 0x01, 0x00)) return "image/x-icon";
        if (Ascii(0, "wOFF")) return "font/woff";
        if (Ascii(0, "wOF2")) return "font/woff2";
        if (At(0, 0x00, 0x01, 0x00, 0x00) || Ascii(0, "true")) return "font/ttf";
        if (Ascii(0, "OTTO")) return "font/otf";
        var head = Encoding.UTF8.GetString(d, 0, Math.Min(d.Length, 2048)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if ((head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<!--", StringComparison.Ordinal))
            && head.Contains("<svg", StringComparison.OrdinalIgnoreCase)) return "image/svg+xml";
        return null;
    }
}
