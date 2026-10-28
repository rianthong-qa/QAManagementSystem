using System.Net;
using System.Net.Mail;

namespace ProMaxx2.QA.Api.Services;

// Thin wrapper around the BCL's SmtpClient (STARTTLS on 587 — exactly what a Gmail App Password needs) —
// deliberately no MailKit/other NuGet dependency, this is the only place SMTP is touched. Throws on failure
// (does not swallow) — every caller is responsible for its own try/catch per the "email is best-effort, never
// blocks the real action" policy agreed for CRM Phase 3, except the Setting Center "ส่งอีเมลทดสอบ" endpoint which
// deliberately wants the real error to reach the admin.
public sealed class EmailSenderService(EmailConfigurationService emailConfig)
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct, bool isHtml = false)
    {
        var (cfg, password) = await emailConfig.GetRuntimeAsync(ct);
        using var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort) { EnableSsl = true, Credentials = new NetworkCredential(cfg.SenderEmail, password) };
        using var message = new MailMessage(new MailAddress(cfg.SenderEmail, cfg.SenderDisplayName ?? "QA Hub"), new MailAddress(toEmail)) { Subject = subject, Body = body, IsBodyHtml = isHtml };
        await client.SendMailAsync(message, ct);
    }
}
