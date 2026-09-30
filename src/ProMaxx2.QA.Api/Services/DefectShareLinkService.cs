using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

// ลิงก์อ่านอย่างเดียวของ Defect สำหรับคนที่ไม่มีบัญชี QA Hub (เช่นทีม CRM/Dev ที่เปิดจาก ticket) — Description ใน CRM
// จำกัด 1000 ตัวอักษรและไม่มีรูปแนบ จึงแนบลิงก์นี้ไว้ท้ายข้อความเสมอ
//
// ลิงก์ใช้ short code สุ่ม 8 ตัว (`?d=<code>`) เก็บในตาราง DefectShareLinks — Defect ละ 1 code ใช้ซ้ำทุกครั้ง; ไม่มีวันหมดอายุ
// เพราะ ticket ใน CRM อยู่ได้นานเท่าที่เคสยังเปิด ปิดการเข้าถึงได้ด้วยการลบ Defect (endpoint คืน 404)
// token แบบยาว (Data Protection ของ DefectId, `?defectShare=`) ยังเปิดได้เพื่อไม่ให้ลิงก์ที่ส่งไปแล้วเสีย
public sealed class DefectShareLinkService(QaDbContext db, IDataProtectionProvider protectionProvider, IConfiguration configuration)
{
    // ตัวพิมพ์เล็กล้วน (collation ของ SQL Server ไม่แยกตัวพิมพ์อยู่แล้ว) และตัด 0/o/1/l/i ที่อ่านสับสนออก —
    // 31^8 ≈ 8.5×10^11 รูปแบบ ร่วมกับ rate limit "share" (120 ครั้ง/นาที/IP) จึงเดาไม่ได้ในทางปฏิบัติ
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";
    private const int CodeLength = 8;
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ProMaxx2.QA.DefectShare.v1");

    public async Task<string> GetOrCreateUrlAsync(Guid defectId, CancellationToken ct)
    {
        var baseUrl = (configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/');
        return $"{baseUrl}/?d={await GetOrCreateCodeAsync(defectId, ct)}";
    }

    public async Task<string> GetOrCreateCodeAsync(Guid defectId, CancellationToken ct)
    {
        var existing = await db.DefectShareLinks.AsNoTracking().Where(x => x.DefectId == defectId).Select(x => x.Code).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;
        string code;
        do code = string.Concat(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
        while (await db.DefectShareLinks.AnyAsync(x => x.Code == code, ct));
        var link = new DefectShareLink(code, defectId);
        db.DefectShareLinks.Add(link);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // ส่งซ้ำพร้อมกันสองคำขอ — อีกคำขอสร้าง code ของ Defect นี้ไปก่อน (unique index DefectId) ใช้ของที่มีอยู่แทน
            db.Entry(link).State = EntityState.Detached;
            return await db.DefectShareLinks.AsNoTracking().Where(x => x.DefectId == defectId).Select(x => x.Code).FirstAsync(ct);
        }
        return code;
    }

    // รับได้ทั้ง short code และ token แบบยาวรุ่นแรก
    public async Task<Guid?> ResolveAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Length <= 12)
            return await db.DefectShareLinks.AsNoTracking().Where(x => x.Code == key).Select(x => (Guid?)x.DefectId).FirstOrDefaultAsync(ct);
        return TryReadLegacyToken(key, out var id) ? id : null;
    }

    public string CreateLegacyToken(Guid defectId) => WebEncoders.Base64UrlEncode(protector.Protect(defectId.ToByteArray()));

    private bool TryReadLegacyToken(string token, out Guid defectId)
    {
        defectId = Guid.Empty;
        try
        {
            var bytes = protector.Unprotect(WebEncoders.Base64UrlDecode(token));
            if (bytes.Length != 16) return false;
            defectId = new Guid(bytes);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return false; }
    }
}
