namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationObjectVerification
{
    private AutomationObjectVerification() { }
    public AutomationObjectVerification(Guid automationObjectId, Guid? requestedAgentId, Guid? requestedBy)
    {
        if (automationObjectId == Guid.Empty) throw new ArgumentException("Object is required.");
        AutomationObjectVerificationId = Guid.NewGuid();
        AutomationObjectId = automationObjectId;
        RequestedAgentId = requestedAgentId;
        RequestedBy = requestedBy;
        Status = "Pending";
        RequestedAt = DateTime.UtcNow;
    }
    public Guid AutomationObjectVerificationId { get; private set; }
    public Guid AutomationObjectId { get; private set; }
    public Guid? RequestedAgentId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? ActualControlType { get; private set; }
    public string? ActualAutomationId { get; private set; }
    public string? Message { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public AutomationObject Object { get; private set; } = null!;
    public AutomationAgent? AssignedAgent { get; private set; }

    public void Assign(Guid agentId)
    {
        if (Status != "Pending") throw new InvalidOperationException("Only pending verifications can be assigned.");
        AssignedAgentId = agentId;
        Status = "Assigned";
    }
    public void Complete(string status, string? actualControlType, string? actualAutomationId, string? message)
    {
        if (status is not ("Found" or "NotFound" or "Duplicate" or "ControlTypeMismatch" or "Error")) throw new ArgumentException("Invalid verification status.");
        if (Status is not ("Pending" or "Assigned")) throw new InvalidOperationException("Verification is already completed.");
        Status = status;
        ActualControlType = actualControlType?.Trim();
        ActualAutomationId = actualAutomationId?.Trim();
        Message = message?.Trim();
        CompletedAt = DateTime.UtcNow;
    }
}
