namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationJob
{
    private AutomationJob() { }
    public AutomationJob(Guid automationExecutionId, Guid? requestedAgentId, int priority, DateTime queuedAt)
    {
        if (automationExecutionId == Guid.Empty) throw new ArgumentException("Execution is required.");
        JobId = Guid.NewGuid();
        AutomationExecutionId = automationExecutionId;
        Priority = Math.Clamp(priority, 1, 10);
        RequestedAgentId = requestedAgentId;
        AssignedAgentId = null;
        Status = "Queued";
        QueuedAt = queuedAt;
        RetryCount = 0;
    }
    public Guid JobId { get; private set; }
    public Guid AutomationExecutionId { get; private set; }
    public int Priority { get; private set; }
    public Guid? RequestedAgentId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public string Status { get; private set; } = "Queued";
    public DateTime QueuedAt { get; private set; }
    public DateTime? AssignedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public AutomationExecution AutomationExecution { get; private set; } = null!;
    public AutomationAgent? AssignedAgent { get; private set; }

    public void Assign(Guid agentId)
    {
        if (Status != "Queued") throw new InvalidOperationException("Only queued jobs can be assigned.");
        AssignedAgentId = agentId;
        AssignedAt = DateTime.UtcNow;
        Status = "Assigned";
    }
    public void MarkStarted(DateTime at)
    {
        if (Status != "Assigned") throw new InvalidOperationException("Only assigned jobs can be started.");
        StartedAt = at;
        Status = "Running";
    }
    public void Complete(string status, string? error)
    {
        if (status is not ("Passed" or "Failed" or "Blocked" or "Cancelled" or "Timeout" or "AgentLost")) throw new ArgumentException("Invalid job status.");
        if (Status is "Passed" or "Failed" or "Blocked" or "Cancelled" or "Timeout" or "AgentLost") throw new InvalidOperationException("Job is already completed.");
        Status = status;
        CompletedAt = DateTime.UtcNow;
        LastError = error?.Trim();
    }
    public void Retry(string? error)
    {
        RetryCount += 1;
        Status = "Queued";
        AssignedAgentId = null;
        AssignedAt = null;
        StartedAt = null;
        CompletedAt = null;
        LastError = error?.Trim();
    }
}