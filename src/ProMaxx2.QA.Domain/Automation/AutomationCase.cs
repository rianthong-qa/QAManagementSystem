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
    public string? MaintenanceReason { get; private set; }
    public Guid? MaintenanceOwnerUserId { get; private set; }
    public DateTime? MaintenanceOpenedAt { get; private set; }
    public bool IsQuarantined { get; private set; }
    public string? QuarantineReason { get; private set; }
    public Guid? QuarantineOwnerUserId { get; private set; }
    public DateTime? QuarantineExpiresAt { get; private set; }
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
    public void RequireMaintenance(string? reason, Guid? userId)
    {
        Status = "MaintenanceRequired";
        MaintenanceReason = string.IsNullOrWhiteSpace(reason) ? MaintenanceReason : reason.Trim();
        MaintenanceOpenedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }
    public void AssignMaintenanceOwner(Guid ownerUserId)
    {
        if (Status != "MaintenanceRequired") throw new InvalidOperationException("Only cases in MaintenanceRequired can have a maintenance owner assigned.");
        MaintenanceOwnerUserId = ownerUserId;
        UpdatedAt = DateTime.UtcNow;
    }
    public void ResolveMaintenance(Guid? userId)
    {
        if (Status != "MaintenanceRequired") throw new InvalidOperationException("Only cases in MaintenanceRequired can be resolved.");
        Status = "NeedsReview";
        MaintenanceReason = null;
        MaintenanceOwnerUserId = null;
        MaintenanceOpenedAt = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }
    public void Quarantine(string reason, Guid? ownerUserId, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Quarantine reason is required.");
        IsQuarantined = true;
        QuarantineReason = reason.Trim();
        QuarantineOwnerUserId = ownerUserId;
        QuarantineExpiresAt = expiresAt;
        UpdatedAt = DateTime.UtcNow;
    }
    public void Unquarantine()
    {
        IsQuarantined = false;
        QuarantineReason = null;
        QuarantineOwnerUserId = null;
        QuarantineExpiresAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
    public void SetTargetApp(string targetApp)
    {
        if (targetApp is not ("Pos" or "App" or "WindowsUI")) throw new ArgumentException("Target app must be Pos, App or WindowsUI.");
        AutomationType = targetApp;
        UpdatedAt = DateTime.UtcNow;
    }
}