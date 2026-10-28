using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// ลิงก์อ่านอย่างเดียวของบทความความรู้ (`/?ka=&lt;code&gt;`) — เปิดได้โดยไม่ต้อง Login, ไม่หมดอายุ, ยกเลิกได้
/// code สุ่ม 8 ตัวชุดอักษรเดียวกับลิงก์แชร์ Defect (31^8 รูปแบบ + rate limit "share") จึงเดาไม่ได้ในทางปฏิบัติ
/// แชร์ได้เฉพาะบทความที่เผยแพร่แล้ว — endpoint สาธารณะตรวจซ้ำทุกครั้ง (ลบ/เปลี่ยนเป็นฉบับร่าง = เปิดไม่ได้ทันที)
/// </summary>
public sealed class KnowledgeArticleShareService(QaDbContext db, IConfiguration configuration)
{
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";
    private const int CodeLength = 8;

    public string UrlFor(string code) => $"{(configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/')}/?ka={code}";

    public Task<string?> GetActiveCodeAsync(Guid articleId, CancellationToken ct) =>
        db.KnowledgeArticleShareLinks.AsNoTracking().Where(x => x.KnowledgeArticleId == articleId && x.RevokedAt == null).Select(x => x.Code).FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, string>> GetActiveCodesAsync(IReadOnlyCollection<Guid> articleIds, CancellationToken ct) =>
        await db.KnowledgeArticleShareLinks.AsNoTracking().Where(x => articleIds.Contains(x.KnowledgeArticleId) && x.RevokedAt == null)
            .ToDictionaryAsync(x => x.KnowledgeArticleId, x => x.Code, ct);

    /// <summary>คืนลิงก์ที่ใช้งานอยู่ หรือสร้างใหม่ — created = true เมื่อสร้างใหม่ (ผู้เรียกบันทึก Audit)</summary>
    public async Task<(string Code, bool Created)> GetOrCreateAsync(Guid articleId, Guid? userId, CancellationToken ct)
    {
        if (await GetActiveCodeAsync(articleId, ct) is { } existing) return (existing, false);
        string code;
        do code = string.Concat(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
        while (await db.KnowledgeArticleShareLinks.AnyAsync(x => x.Code == code, ct));
        var link = new KnowledgeArticleShareLink(code, articleId, userId);
        db.KnowledgeArticleShareLinks.Add(link);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // กดแชร์พร้อมกันสองคำขอ — อีกคำขอสร้างลิงก์ไปก่อน (unique filtered index) ใช้ของที่มีอยู่แทน
            db.Entry(link).State = EntityState.Detached;
            return ((await GetActiveCodeAsync(articleId, ct))!, false);
        }
        return (code, true);
    }

    public async Task<bool> RevokeAsync(Guid articleId, Guid? userId, CancellationToken ct)
    {
        var links = await db.KnowledgeArticleShareLinks.Where(x => x.KnowledgeArticleId == articleId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var link in links) link.Revoke(userId);
        return links.Count > 0;
    }

    /// <summary>บทความที่เปิดได้จากลิงก์สาธารณะ — null เมื่อไม่พบ/ยกเลิกแล้ว/บทความถูกลบหรือยังไม่เผยแพร่</summary>
    public async Task<KnowledgeArticle?> ResolveAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 12) return null;
        var articleId = await db.KnowledgeArticleShareLinks.AsNoTracking().Where(x => x.Code == code && x.RevokedAt == null).Select(x => (Guid?)x.KnowledgeArticleId).FirstOrDefaultAsync(ct);
        if (articleId is null) return null;
        return await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(x => x.KnowledgeArticleId == articleId && !x.IsDeleted && x.Status == "Published", ct);
    }
}
