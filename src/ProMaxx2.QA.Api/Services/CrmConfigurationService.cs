using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record CrmConfigurationView(string MerchantId, string Username, bool HasPassword, string? PasswordHint, bool IsEnabled, DateTimeOffset? UpdatedAt);
public sealed record CrmRuntimeConfig(string MerchantId, string Username);
public sealed class CrmNotConfiguredException(string message) : InvalidOperationException(message);

// Encrypted login credential for the CRM integration — one row PER QA HUB USER (self-service; see AuthController's
// /auth/me/crm). Mirrors SharedAiConfigurationService's encrypt/decrypt pattern (Data Protection API).
public sealed class CrmConfigurationService(QaDbContext db, IDataProtectionProvider protectionProvider, CrmTokenService tokenService)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ProMaxx2.QA.CrmConfiguration.Password.v1");

    public async Task<CrmConfigurationView> GetViewAsync(Guid userId, CancellationToken ct)
    {
        var s = await db.CrmConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (s is null) return new("", "", false, null, false, null);
        return new(s.MerchantId, s.Username, !string.IsNullOrWhiteSpace(s.EncryptedPassword), s.PasswordHint, s.IsEnabled, s.UpdatedAt);
    }

    public async Task<(CrmRuntimeConfig cfg, string password)> GetRuntimeAsync(Guid userId, CancellationToken ct)
    {
        var s = await db.CrmConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct)
            ?? throw new CrmNotConfiguredException("คุณยังไม่ได้ตั้งค่าบัญชี CRM ของตัวเอง กรุณาตั้งค่าที่ปุ่ม \"บัญชี CRM ของฉัน\" ก่อน");
        if (!s.IsEnabled) throw new CrmNotConfiguredException("บัญชี CRM ของคุณถูกปิดใช้งานไว้ กรุณาเปิดใช้งานก่อน");
        if (string.IsNullOrWhiteSpace(s.EncryptedPassword)) throw new CrmNotConfiguredException("ยังไม่ได้ตั้งค่า Password ของบัญชี CRM");
        string password;
        try { password = protector.Unprotect(s.EncryptedPassword); }
        catch (CryptographicException) { throw new CrmNotConfiguredException("ไม่สามารถอ่าน Password ที่เข้ารหัสไว้ได้ กรุณาบันทึกใหม่"); }
        return (new(s.MerchantId, s.Username), password);
    }

    public async Task<CrmConfigurationView> SaveAsync(Guid userId, string merchantId, string username, string? password, bool isEnabled, bool clearPassword, CancellationToken ct)
    {
        var s = await db.CrmConfigurations.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var encrypted = clearPassword ? "" : s?.EncryptedPassword ?? "";
        var hint = clearPassword ? null : s?.PasswordHint;
        if (!string.IsNullOrWhiteSpace(password))
        {
            var v = password.Trim();
            encrypted = protector.Protect(v);
            hint = "••••";
        }
        if (isEnabled && string.IsNullOrWhiteSpace(encrypted)) throw new ArgumentException("กรุณาระบุ Password");
        if (s is null) { s = new(userId, merchantId, username, encrypted, hint, isEnabled); db.CrmConfigurations.Add(s); }
        else s.Update(merchantId, username, encrypted, hint, isEnabled);
        await db.SaveChangesAsync(ct);
        tokenService.Invalidate(userId); // credentials just changed — force re-auth on the next call instead of waiting up to ~24h for the cached token to expire
        return await GetViewAsync(userId, ct);
    }
}
