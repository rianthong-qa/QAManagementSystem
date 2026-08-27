namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationAction
{
    private AutomationAction() { }
    public AutomationAction(string actionCode, string actionName, string category, string? description, string parameterSchemaJson, string handlerKey, string? minimumAgentVersion)
    {
        if (string.IsNullOrWhiteSpace(actionCode)) throw new ArgumentException("Action code is required.");
        AutomationActionId = Guid.NewGuid();
        ActionCode = actionCode.Trim().ToUpperInvariant();
        ActionName = actionName.Trim();
        Category = category.Trim();
        Description = description?.Trim();
        ParameterSchemaJson = string.IsNullOrWhiteSpace(parameterSchemaJson) ? "{}" : parameterSchemaJson;
        HandlerKey = string.IsNullOrWhiteSpace(handlerKey) ? ActionCode : handlerKey.Trim();
        MinimumAgentVersion = minimumAgentVersion?.Trim();
        IsActive = true;
        RetrySafety = "Unsafe";
        CreatedAt = DateTime.UtcNow;
    }
    public Guid AutomationActionId { get; private set; }
    public string ActionCode { get; private set; } = string.Empty;
    public string ActionName { get; private set; } = string.Empty;
    public string Category { get; private set; } = "Generic";
    public string? Description { get; private set; }
    public string ParameterSchemaJson { get; private set; } = "{}";
    public string HandlerKey { get; private set; } = string.Empty;
    public string? MinimumAgentVersion { get; private set; }
    public bool IsActive { get; private set; }
    public string RetrySafety { get; private set; } = "Unsafe";
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public void Update(string actionName, string category, string? description, string parameterSchemaJson, string handlerKey, string? minimumAgentVersion, bool isActive, string? retrySafety = null)
    {
        if (string.IsNullOrWhiteSpace(actionName) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(handlerKey))
            throw new ArgumentException("Action name, category and handler key are required.");
        ActionName = actionName.Trim();
        Category = category.Trim();
        Description = description?.Trim();
        ParameterSchemaJson = string.IsNullOrWhiteSpace(parameterSchemaJson) ? "{}" : parameterSchemaJson;
        HandlerKey = handlerKey.Trim();
        MinimumAgentVersion = minimumAgentVersion?.Trim();
        IsActive = isActive;
        if (retrySafety is not null)
        {
            if (retrySafety is not ("Safe" or "Unsafe" or "Conditional")) throw new ArgumentException("Retry safety must be Safe, Unsafe or Conditional.");
            RetrySafety = retrySafety;
        }
        UpdatedAt = DateTime.UtcNow;
    }
}
