using System.Security.Cryptography;
using System.Text;

namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-P1-008: an authentication credential for one project's CI/CD → QA Hub webhook. Only the SHA-256
/// hash of the secret is ever persisted (the plaintext secret is generated once, returned to the caller at
/// creation, and never retrievable again — same handling as a password, minus the deliberately-slow hashing a
/// human-chosen password needs, since this secret is a 256-bit random value with no brute-force risk to defend
/// against). <see cref="TokenPrefix"/> is a short, non-secret slice kept only so the token list UI can tell tokens
/// apart without ever showing (or being able to show) the secret again.</summary>
public sealed class AutomationWebhookToken
{
    private AutomationWebhookToken() { }

    private AutomationWebhookToken(Guid projectId, string name, string tokenHash, string tokenPrefix, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        AutomationWebhookTokenId = Guid.NewGuid();
        ProjectId = projectId;
        Name = name.Trim();
        TokenHash = tokenHash;
        TokenPrefix = tokenPrefix;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid AutomationWebhookTokenId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string TokenPrefix { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }

    /// <summary>Generates a new random secret and the token entity that stores only its hash. Returns both — the
    /// caller (the API layer) must hand <paramref name="plainTextToken"/> back to the requester exactly once and
    /// never persist or log it.</summary>
    public static (AutomationWebhookToken Token, string PlainTextToken) Generate(Guid projectId, string name, Guid? createdBy)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var plainTextToken = "whk_" + Convert.ToHexString(secret).ToLowerInvariant();
        var token = new AutomationWebhookToken(projectId, name, Hash(plainTextToken), plainTextToken[..12], createdBy);
        return (token, plainTextToken);
    }

    public static string Hash(string plainTextToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainTextToken))).ToLowerInvariant();

    public void RecordUse(DateTime utcNow) => LastUsedAtUtc = utcNow;

    public void Revoke(Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Token is already revoked.");
        IsActive = false;
        RevokedAt = DateTime.UtcNow;
        RevokedBy = userId;
    }
}

/// <summary>AUT-P1-008: one received webhook call, keyed by the caller-supplied <see cref="RequestId"/> so a
/// redelivered/replayed webhook is recognized and answered idempotently instead of creating a second Build — but
/// only a prior <b>successful</b> delivery blocks a repeat; a "Failed" delivery does not consume the RequestId, so
/// a legitimate retry after a transient failure (e.g. the release didn't exist yet) still goes through. Also the
/// audit trail satisfying "trace กลับ Build ได้" — <see cref="BuildId"/> links every delivery back to the Build it
/// did (or didn't) create.</summary>
public sealed class AutomationWebhookDelivery
{
    private AutomationWebhookDelivery() { }
    public AutomationWebhookDelivery(Guid projectId, Guid automationWebhookTokenId, string requestId, DateTime receivedAtUtc, Guid? buildId, string status, string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("RequestId is required (used for replay protection).");
        AutomationWebhookDeliveryId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationWebhookTokenId = automationWebhookTokenId;
        RequestId = requestId.Trim();
        ReceivedAtUtc = receivedAtUtc;
        BuildId = buildId;
        Status = status;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
    }
    public Guid AutomationWebhookDeliveryId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationWebhookTokenId { get; private set; }
    public string RequestId { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public Guid? BuildId { get; private set; }
    /// <summary>"Created" (made a new Build) / "Duplicate" (RequestId already seen — replay, returned the original result) / "Failed" (release not found, duplicate build number, etc.).</summary>
    public string Status { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public AutomationWebhookToken Token { get; private set; } = null!;
}
