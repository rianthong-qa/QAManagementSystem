namespace ProMaxx2.QA.Domain.Integrations;

// Login credential for the CRM (BlueSea Helpdesk / BlueID) integration — one row PER QA HUB USER, not a single
// shared row. CRM tracks work by whoever is actually logged in (OwnerSubjectId/Assignto/RecipientId all resolve
// to a real CRM staff identity), so each QA Hub user logs in with their own BlueID Employee account — the 3
// fields BlueID's own Employee login form asks for (MerchantID, Username, Password). Self-service: every user
// manages their own row (see AuthController's /auth/me/crm), never admin-managed on someone else's behalf. Base
// URL / Token URL / client_id are fixed constants for this one integration (see CrmApiClient/CrmTokenService),
// not per-user configurable, since this connector only ever talks to one CRM system. Username doubles as the
// seniorSoftID used for the CRM ticket's RecipientId/OwnerSubjectId/Posted fields — no separate "Service Account
// id" needed since the user logs in as themselves directly.
public sealed class CrmConfiguration
{
    private CrmConfiguration() { }

    public CrmConfiguration(Guid userId, string merchantId, string username, string encryptedPassword, string? passwordHint, bool isEnabled)
    {
        CrmConfigurationId = Guid.NewGuid();
        UserId = userId;
        Update(merchantId, username, encryptedPassword, passwordHint, isEnabled);
    }

    public Guid CrmConfigurationId { get; private set; }
    public Guid UserId { get; private set; }
    public string MerchantId { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string EncryptedPassword { get; private set; } = string.Empty;
    public string? PasswordHint { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string merchantId, string username, string encryptedPassword, string? passwordHint, bool isEnabled)
    {
        MerchantId = Required(merchantId, nameof(merchantId));
        Username = Required(username, nameof(username));
        EncryptedPassword = encryptedPassword;
        PasswordHint = passwordHint;
        IsEnabled = isEnabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
}
