using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record EmailConfigurationView(string SmtpHost, int SmtpPort, string SenderEmail, string? SenderDisplayName, bool HasPassword, string? PasswordHint, bool IsEnabled, DateTimeOffset? UpdatedAt);
public sealed record EmailRuntimeConfig(string SmtpHost, int SmtpPort, string SenderEmail, string? SenderDisplayName);
public sealed class EmailNotConfiguredException(string message) : InvalidOperationException(message);

// Encrypted Gmail SMTP credential — mirrors CrmConfigurationService.cs exactly (single row, Data Protection API,
// distinct protector purpose string). Used by EmailSenderService for every notification email (CRM Phase 3).
public sealed class EmailConfigurationService(QaDbContext db, IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ProMaxx2.QA.EmailConfiguration.Password.v1");

    public async Task<EmailConfigurationView> GetViewAsync(CancellationToken ct)
    {
        var s = await db.EmailConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (s is null) return new("smtp.gmail.com", 587, "", null, false, null, false, null);
        return new(s.SmtpHost, s.SmtpPort, s.SenderEmail, s.SenderDisplayName, !string.IsNullOrWhiteSpace(s.EncryptedPassword), s.PasswordHint, s.IsEnabled, s.UpdatedAt);
    }

    public async Task<(EmailRuntimeConfig cfg, string password)> GetRuntimeAsync(CancellationToken ct)
    {
        var s = await db.EmailConfigurations.AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new EmailNotConfiguredException("ยังไม่ได้ตั้งค่า Email/SMTP กรุณาตั้งค่าใน Setting Center");
        if (!s.IsEnabled) throw new EmailNotConfiguredException("การส่งอีเมลถูกปิดใช้งานใน Setting Center");
        if (string.IsNullOrWhiteSpace(s.EncryptedPassword)) throw new EmailNotConfiguredException("ยังไม่ได้ตั้งค่า App Password ของ Email");
        string password;
        try { password = protector.Unprotect(s.EncryptedPassword); }
        catch (CryptographicException) { throw new EmailNotConfiguredException("ไม่สามารถอ่าน App Password ที่เข้ารหัสไว้ได้ กรุณาบันทึกใหม่"); }
        return (new(s.SmtpHost, s.SmtpPort, s.SenderEmail, s.SenderDisplayName), password);
    }

    public async Task<EmailConfigurationView> SaveAsync(string smtpHost, int smtpPort, string senderEmail, string? senderDisplayName, string? password, bool isEnabled, bool clearPassword, CancellationToken ct)
    {
        var s = await db.EmailConfigurations.SingleOrDefaultAsync(ct);
        var encrypted = clearPassword ? "" : s?.EncryptedPassword ?? "";
        var hint = clearPassword ? null : s?.PasswordHint;
        if (!string.IsNullOrWhiteSpace(password))
        {
            var v = password.Trim();
            encrypted = protector.Protect(v);
            hint = "••••";
        }
        if (isEnabled && string.IsNullOrWhiteSpace(encrypted)) throw new ArgumentException("กรุณาระบุ App Password");
        if (s is null) { s = new(smtpHost, smtpPort, senderEmail, senderDisplayName, encrypted, hint, isEnabled); db.EmailConfigurations.Add(s); }
        else s.Update(smtpHost, smtpPort, senderEmail, senderDisplayName, encrypted, hint, isEnabled);
        await db.SaveChangesAsync(ct);
        return await GetViewAsync(ct);
    }
}
