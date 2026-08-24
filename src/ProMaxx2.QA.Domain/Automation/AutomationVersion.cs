using System.Text.Json;

namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationVersion
{
    private AutomationVersion() { }
    public AutomationVersion(Guid automationCaseId, int versionNo, int testCaseRevisionNo, string dslJson, bool generatedByAi, string? aiProvider, string? aiModel, double? aiConfidence, Guid? createdBy)
    {
        if (automationCaseId == Guid.Empty || versionNo < 1 || string.IsNullOrWhiteSpace(dslJson)) throw new ArgumentException("Automation case, version number and DSL are required.");
        AutomationVersionId = Guid.NewGuid();
        AutomationCaseId = automationCaseId;
        VersionNo = versionNo;
        TestCaseRevisionNo = testCaseRevisionNo;
        DslVersion = "1.0";
        DslJson = dslJson;
        GeneratedByAi = generatedByAi;
        AiProvider = aiProvider?.Trim();
        AiModel = aiModel?.Trim();
        AiConfidence = aiConfidence;
        ValidationStatus = "Pending";
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }
    public Guid AutomationVersionId { get; private set; }
    public Guid AutomationCaseId { get; private set; }
    public int VersionNo { get; private set; }
    public int TestCaseRevisionNo { get; private set; }
    public string DslVersion { get; private set; } = "1.0";
    public string DslJson { get; private set; } = "{}";
    public bool GeneratedByAi { get; private set; }
    public string? AiProvider { get; private set; }
    public string? AiModel { get; private set; }
    public double? AiConfidence { get; private set; }
    public string ValidationStatus { get; private set; } = "Pending";
    public string? ValidationErrors { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public string? ChangeReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    public void SetValidation(string status, string? errors)
    {
        ValidationStatus = status;
        ValidationErrors = errors?.Trim();
    }
    public void Approve(Guid? userId)
    {
        if (ValidationStatus != "Valid") throw new InvalidOperationException("Only versions that pass validation can be approved.");
        ApprovedBy = userId;
        ApprovedAt = DateTime.UtcNow;
    }
    public void RecordChangeReason(string? reason) => ChangeReason = reason?.Trim();
    public DslDocument? ToDsl() => JsonSerializer.Deserialize<DslDocument>(DslJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    public void UpdateDsl(string dslJson)
    {
        if (string.IsNullOrWhiteSpace(dslJson)) throw new ArgumentException("DSL is required.");
        DslJson = dslJson;
        ValidationStatus = "Pending";
        ValidationErrors = null;
        ApprovedBy = null;
        ApprovedAt = null;
    }
}