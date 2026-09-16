namespace ProMaxx2.QA.Domain.Governance;

public sealed class AuditLog
{
    private AuditLog() { }

    public AuditLog(Guid? userId, string action, string entityType, string entityId, string? changeSummary, string? beforeJson, string? afterJson, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Audit action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Audit entity type is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("Audit entity id is required.", nameof(entityId));
        AuditLogId = Guid.NewGuid();
        UserId = userId;
        Action = action.Trim();
        EntityType = entityType.Trim();
        EntityId = entityId.Trim();
        ChangeSummary = changeSummary?.Trim();
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        ClientIp = clientIp?.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public Guid AuditLogId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? ChangeSummary { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? ClientIp { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
