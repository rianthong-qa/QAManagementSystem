namespace ProMaxx2.QA.Domain.Integrations;

// Maps a QA Hub Project to the CRM (BlueSea Helpdesk)'s own SysProductId/SysVersionId lookup values — one row
// per Project. Used when building the CRM Create Job payload for a Defect in that Project (SysProductId is
// required by CRM; SysVersionId is optional/project-level for Phase 1, no per-Release granularity).
public sealed class CrmProjectMapping
{
    private CrmProjectMapping() { }

    public CrmProjectMapping(Guid projectId, string crmProductId, string? crmVersionId)
    {
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(crmProductId)) throw new ArgumentException("Project and CRM Product Id are required.");
        CrmProjectMappingId = Guid.NewGuid();
        ProjectId = projectId;
        CrmProductId = crmProductId.Trim();
        CrmVersionId = string.IsNullOrWhiteSpace(crmVersionId) ? null : crmVersionId.Trim();
    }

    public Guid CrmProjectMappingId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string CrmProductId { get; private set; } = string.Empty;
    public string? CrmVersionId { get; private set; }

    public void Update(string crmProductId, string? crmVersionId)
    {
        if (string.IsNullOrWhiteSpace(crmProductId)) throw new ArgumentException("CRM Product Id is required.");
        CrmProductId = crmProductId.Trim();
        CrmVersionId = string.IsNullOrWhiteSpace(crmVersionId) ? null : crmVersionId.Trim();
    }
}
