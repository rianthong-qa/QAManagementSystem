namespace ProMaxx2.QA.Domain.Settings;

// Single-row Gmail SMTP config for outbound notification email (Phase 3 of the CRM integration — see
// Document/03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md §9). Same encrypted-secret shape/pattern as
// CrmConfiguration.cs (Data Protection API via EmailConfigurationService), just for an SMTP App Password instead
// of a CRM login password. Not CRM-specific itself — lives alongside AiConfiguration as a general app setting so
// any future feature can reuse it, not just CRM notifications.
public sealed class EmailConfiguration
{
    private EmailConfiguration() { }

    public EmailConfiguration(string smtpHost, int smtpPort, string senderEmail, string? senderDisplayName, string encryptedPassword, string? passwordHint, bool isEnabled)
    {
        EmailConfigurationId = Guid.NewGuid();
        Update(smtpHost, smtpPort, senderEmail, senderDisplayName, encryptedPassword, passwordHint, isEnabled);
    }

    public Guid EmailConfigurationId { get; private set; }
    public string SmtpHost { get; private set; } = "smtp.gmail.com";
    public int SmtpPort { get; private set; } = 587;
    public string SenderEmail { get; private set; } = string.Empty;
    public string? SenderDisplayName { get; private set; }
    public string EncryptedPassword { get; private set; } = string.Empty;
    public string? PasswordHint { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string smtpHost, int smtpPort, string senderEmail, string? senderDisplayName, string encryptedPassword, string? passwordHint, bool isEnabled)
    {
        SmtpHost = Required(smtpHost, nameof(smtpHost));
        if (smtpPort is <= 0 or > 65535) throw new ArgumentException("SMTP port must be between 1 and 65535.");
        SmtpPort = smtpPort;
        SenderEmail = Required(senderEmail, nameof(senderEmail));
        SenderDisplayName = string.IsNullOrWhiteSpace(senderDisplayName) ? null : senderDisplayName.Trim();
        EncryptedPassword = encryptedPassword;
        PasswordHint = passwordHint;
        IsEnabled = isEnabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
}
