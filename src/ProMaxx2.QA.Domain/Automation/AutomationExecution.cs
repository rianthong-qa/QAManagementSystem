namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationExecution
{
    private AutomationExecution() { }
    public AutomationExecution(Guid automationCaseId, Guid automationVersionId, Guid? agentId, Guid buildId, Guid environmentId, string? requestedBy, string targetApp = "WindowsUI")
    {
        if (automationCaseId == Guid.Empty || automationVersionId == Guid.Empty || buildId == Guid.Empty || environmentId == Guid.Empty) throw new ArgumentException("Automation case, version, build and environment are required.");
        AutomationExecutionId = Guid.NewGuid();
        AutomationCaseId = automationCaseId;
        AutomationVersionId = automationVersionId;
        AgentId = agentId;
        BuildId = buildId;
        EnvironmentId = environmentId;
        TargetApp = string.IsNullOrWhiteSpace(targetApp) ? "WindowsUI" : targetApp.Trim();
        Status = "Queued";
        CreatedAt = DateTime.UtcNow;
        RequestedBy = requestedBy;
    }
    public Guid AutomationExecutionId { get; private set; }
    public Guid AutomationCaseId { get; private set; }
    public Guid AutomationVersionId { get; private set; }
    public Guid? TestExecutionId { get; private set; }
    public Guid? DefectId { get; private set; }
    public string TargetApp { get; private set; } = "WindowsUI";
    public Guid? AgentId { get; private set; }
    public Guid BuildId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public Guid? JobId { get; private set; }
    public string Status { get; private set; } = "Queued";
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public long? DurationMs { get; private set; }
    public string? FailureType { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? RequestedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public ICollection<AutomationStepResult> StepResults { get; private set; } = [];
    public AutomationCase AutomationCase { get; private set; } = null!;
    public AutomationVersion AutomationVersion { get; private set; } = null!;
    public AutomationAgent? Agent { get; private set; }
    public Releases.Build Build { get; private set; } = null!;
    public Execution.TestEnvironment Environment { get; private set; } = null!;

    public void AssignAgent(Guid agentId) => AgentId = agentId;
    public void LinkJob(Guid jobId) => JobId = jobId;
    public void Start(Guid agentId, DateTime at)
    {
        if (Status != "Queued") throw new InvalidOperationException("Only queued executions can be started.");
        AgentId = agentId;
        Status = "Running";
        StartedAt = at;
    }
    public void Complete(string status, string? failureType, string? errorCode, string? errorMessage, DateTime at)
    {
        if (status is not ("Passed" or "Failed" or "Blocked" or "Cancelled" or "Timeout" or "AgentLost")) throw new ArgumentException("Invalid execution status.");
        Status = status;
        CompletedAt = at;
        DurationMs = StartedAt.HasValue ? (long)(at - StartedAt.Value).TotalMilliseconds : 0;
        FailureType = failureType?.Trim();
        ErrorCode = errorCode?.Trim();
        ErrorMessage = errorMessage?.Trim();
    }
    public void LinkTestExecution(Guid testExecutionId) => TestExecutionId = testExecutionId;
    public void LinkDefect(Guid defectId) => DefectId = defectId;
    public void AddStepResult(AutomationStepResult result) => StepResults.Add(result);
}

public sealed class AutomationStepResult
{
    private AutomationStepResult() { }
    public AutomationStepResult(Guid executionId, int stepNo, string actionCode, string status, string? actualResult, string? errorCode, string? errorMessage, string? evidencePath, DateTime startedAt, DateTime completedAt)
    {
        if (status is not ("Pass" or "Fail" or "Blocked" or "Skipped")) throw new ArgumentException("Invalid step status.");
        AutomationStepResultId = Guid.NewGuid();
        AutomationExecutionId = executionId;
        StepNo = stepNo;
        ActionCode = actionCode;
        Status = status;
        ActualResult = actualResult?.Trim();
        ErrorCode = errorCode?.Trim();
        ErrorMessage = errorMessage?.Trim();
        EvidencePath = evidencePath?.Trim();
        StartedAt = startedAt;
        CompletedAt = completedAt;
        DurationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);
    }
    public Guid AutomationStepResultId { get; private set; }
    public Guid AutomationExecutionId { get; private set; }
    public int StepNo { get; private set; }
    public string ActionCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Skipped";
    public DateTime StartedAt { get; private set; }
    public DateTime CompletedAt { get; private set; }
    public long DurationMs { get; private set; }
    public string? ActualResult { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? EvidencePath { get; private set; }
    public AutomationExecution Execution { get; private set; } = null!;
    public void AttachEvidence(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Evidence path is required.");
        EvidencePath = path.Trim();
    }
}