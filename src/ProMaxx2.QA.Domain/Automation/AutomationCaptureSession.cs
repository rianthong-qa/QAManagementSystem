namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationCaptureSession
{
    private AutomationCaptureSession() { }
    public AutomationCaptureSession(Guid projectId, Guid moduleId, Guid testCaseId, Guid? userId, string applicationCode, string sourceMachine, string? applicationVersion, string itemsJson)
    {
        CaptureSessionId = Guid.NewGuid(); ProjectId = projectId; ModuleId = moduleId; TestCaseId = testCaseId;
        UserId = userId; ApplicationCode = applicationCode.Trim().ToLowerInvariant(); SourceMachine = sourceMachine.Trim();
        ApplicationVersion = applicationVersion?.Trim(); ItemsJson = itemsJson; Status = "Draft"; CreatedAt = DateTime.UtcNow; ExpiresAt = DateTime.UtcNow.AddHours(8);
    }
    public Guid CaptureSessionId { get; private set; } public Guid ProjectId { get; private set; } public Guid ModuleId { get; private set; } public Guid TestCaseId { get; private set; }
    public Guid? UserId { get; private set; } public string ApplicationCode { get; private set; } = ""; public string SourceMachine { get; private set; } = "";
    public string? ApplicationVersion { get; private set; } public string ItemsJson { get; private set; } = "[]"; public string Status { get; private set; } = "Draft";
    public DateTime CreatedAt { get; private set; } public DateTime ExpiresAt { get; private set; } public DateTime? CompletedAt { get; private set; }
    public void Complete(string status)
    {
        if (Status is "Committed" or "Discarded" or "Expired") throw new InvalidOperationException("Capture session is already complete.");
        if (status is not ("Committed" or "Discarded" or "Expired")) throw new ArgumentException("Invalid capture session status.");
        Status = status; CompletedAt = DateTime.UtcNow;
    }
}
