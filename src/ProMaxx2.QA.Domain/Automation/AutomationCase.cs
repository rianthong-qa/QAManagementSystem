namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationCase
{
    private AutomationCase() { }
    public AutomationCase(Guid testCaseId, string automationCode, string automationType, Guid? ownerUserId, Guid? createdBy)
    {
        if (testCaseId == Guid.Empty || string.IsNullOrWhiteSpace(automationCode)) throw new ArgumentException("TestCase and automation code are required.");
        AutomationCaseId = Guid.NewGuid();
        TestCaseId = testCaseId;
        AutomationCode = automationCode.Trim().ToUpperInvariant();
        AutomationType = string.IsNullOrWhiteSpace(automationType) ? "WindowsUI" : automationType.Trim();
        Status = "Draft";
        CurrentVersionNo = 0;
        OwnerUserId = ownerUserId;
        IsAiGenerated = false;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }
    public Guid AutomationCaseId { get; private set; }
    public Guid TestCaseId { get; private set; }
    public string AutomationCode { get; private set; } = string.Empty;
    public string AutomationType { get; private set; } = "WindowsUI";
    public string Status { get; private set; } = "Draft";
    public int CurrentVersionNo { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public bool IsAiGenerated { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public TestManagement.TestCase TestCase { get; private set; } = null!;
    public ICollection<AutomationVersion> Versions { get; private set; } = [];

    public void MarkGeneratedByAi(bool value) { IsAiGenerated = value; }
    public void ChangeStatus(string status)
    {
        if (status is not ("Draft" or "NeedsReview" or "Validated" or "Approved" or "Ready" or "Running" or "MaintenanceRequired")) throw new ArgumentException("Invalid automation case status.");
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }
    public void SetVersion(int versionNo, Guid? userId)
    {
        CurrentVersionNo = versionNo;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }
    public void SoftDelete(Guid? userId) { IsDeleted = true; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void RequireMaintenance(Guid? userId) { Status = "MaintenanceRequired"; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void SetTargetApp(string targetApp)
    {
        if (targetApp is not ("Pos" or "App" or "WindowsUI")) throw new ArgumentException("Target app must be Pos, App or WindowsUI.");
        AutomationType = targetApp;
        UpdatedAt = DateTime.UtcNow;
    }
}