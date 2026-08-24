namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationEvidence
{
    private AutomationEvidence() { }
    public AutomationEvidence(Guid automationExecutionId, int? stepNo, string evidenceType, string filePath, string? capturedBy)
    {
        if (automationExecutionId == Guid.Empty) throw new ArgumentException("Execution is required.");
        AutomationEvidenceId = Guid.NewGuid();
        AutomationExecutionId = automationExecutionId;
        StepNo = stepNo;
        EvidenceType = evidenceType.Trim();
        FilePath = filePath.Trim();
        CapturedBy = capturedBy?.Trim();
        CapturedAt = DateTime.UtcNow;
    }
    public Guid AutomationEvidenceId { get; private set; }
    public Guid AutomationExecutionId { get; private set; }
    public int? StepNo { get; private set; }
    public string EvidenceType { get; private set; } = "Screenshot";
    public string FilePath { get; private set; } = string.Empty;
    public string? CapturedBy { get; private set; }
    public DateTime CapturedAt { get; private set; }
    public AutomationExecution Execution { get; private set; } = null!;
}