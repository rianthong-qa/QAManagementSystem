using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record SuiteCaseDto(Guid AutomationCaseId, string AutomationCode, string TestCaseCode, string TestCaseTitle, string AutomationType, string Status, int SortOrder, bool IsRequired);
public sealed record AutomationSuiteListDto(Guid AutomationSuiteId, Guid ProjectId, string SuiteCode, string SuiteName, string? Description, bool IsActive, DateTime CreatedAt, DateTime? ClosedAt, int RevisionNo, int CaseCount, int ReadyCaseCount);
public sealed record AutomationSuiteDto(Guid AutomationSuiteId, Guid ProjectId, string SuiteCode, string SuiteName, string? Description, bool IsActive, Guid? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ClosedAt, int RevisionNo, IReadOnlyList<SuiteCaseDto> Cases);
public sealed record AutomationSuiteRevisionDto(Guid AutomationSuiteRevisionId, int RevisionNo, string ChangeType, string? Detail, string? ChangeReason, Guid? ChangedBy, string? ChangedByName, DateTime ChangedAt);
public sealed record CreateAutomationSuiteRequest(string? SuiteCode, string SuiteName, string? Description);
public sealed record UpdateAutomationSuiteRequest(string SuiteName, string? Description, string? ChangeReason = null);
public sealed record SuiteLifecycleRequest(string? ChangeReason = null);
public sealed record AddSuiteCasesRequest(IReadOnlyList<Guid> AutomationCaseIds, bool IsRequired, string? ChangeReason = null);
public sealed record UpdateSuiteCaseRequest(int SortOrder, bool IsRequired, string? ChangeReason = null);

public interface IAutomationSuiteRepository
{
    Task<IReadOnlyList<AutomationSuiteListDto>> ListSuitesAsync(Guid projectId, string? search, bool? isActive, CancellationToken ct);
    Task<AutomationSuiteDto?> GetSuiteAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationSuite?> FindSuiteAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<bool> SuiteCodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListSuiteCodesAsync(Guid projectId, string prefix, CancellationToken ct);
    Task AddSuiteAsync(AutomationSuite entity, CancellationToken ct);

    Task<bool> CaseExistsInProjectAsync(Guid projectId, Guid caseId, CancellationToken ct);
    Task<bool> SuiteCaseExistsAsync(Guid suiteId, Guid caseId, CancellationToken ct);
    Task<int> GetNextSuiteCaseSortOrderAsync(Guid suiteId, CancellationToken ct);
    Task AddSuiteCaseAsync(AutomationSuiteCase entity, CancellationToken ct);
    Task<AutomationSuiteCase?> FindSuiteCaseAsync(Guid suiteId, Guid caseId, CancellationToken ct);
    Task RemoveSuiteCaseAsync(AutomationSuiteCase entity, CancellationToken ct);
    Task<IReadOnlyList<string>> GetCaseCodesAsync(IReadOnlyList<Guid> caseIds, CancellationToken ct);

    Task AddRevisionAsync(AutomationSuiteRevision entity, CancellationToken ct);
    Task<IReadOnlyList<AutomationSuiteRevisionDto>> ListRevisionsAsync(Guid suiteId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class AutomationSuiteService(IAutomationSuiteRepository repository, IProjectRepository projects)
{
    public Task<IReadOnlyList<AutomationSuiteListDto>> ListAsync(Guid projectId, string? search, bool? isActive, CancellationToken ct)
        => repository.ListSuitesAsync(projectId, search, isActive, ct);

    public async Task<AutomationSuiteDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");

    public async Task<IReadOnlyList<AutomationSuiteRevisionDto>> ListRevisionsAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        _ = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        return await repository.ListRevisionsAsync(id, ct);
    }

    private async Task RecordRevisionAsync(AutomationSuite suite, string changeType, string? detail, string? changeReason, Guid? userId, CancellationToken ct)
        => await repository.AddRevisionAsync(new AutomationSuiteRevision(suite.AutomationSuiteId, suite.BumpRevision(), changeType, detail, changeReason, userId), ct);

    public async Task<AutomationSuiteDto> CreateAsync(Guid projectId, CreateAutomationSuiteRequest r, Guid? userId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        var code = string.IsNullOrWhiteSpace(r.SuiteCode)
            ? BusinessCodeGenerator.NextAvailable($"{project.ProjectCode}-AS", await repository.ListSuiteCodesAsync(projectId, $"{project.ProjectCode}-AS", ct))
            : r.SuiteCode.Trim().ToUpperInvariant();
        if (await repository.SuiteCodeExistsAsync(projectId, code, null, ct)) throw new DuplicateCodeException("Suite code already exists.");
        var entity = new AutomationSuite(projectId, code, r.SuiteName, r.Description, userId);
        await repository.AddSuiteAsync(entity, ct);
        await repository.AddRevisionAsync(new AutomationSuiteRevision(entity.AutomationSuiteId, entity.RevisionNo, "Created", $"Suite \"{entity.SuiteName}\" created", null, userId), ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(entity.AutomationSuiteId, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> UpdateAsync(Guid id, Guid projectId, UpdateAutomationSuiteRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        var previousName = entity.SuiteName;
        entity.Update(r.SuiteName, r.Description, userId);
        var detail = previousName != entity.SuiteName ? $"Renamed \"{previousName}\" → \"{entity.SuiteName}\"" : "Details updated";
        await RecordRevisionAsync(entity, "Updated", detail, r.ChangeReason, userId, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> CloseAsync(Guid id, Guid projectId, SuiteLifecycleRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        entity.Close(userId);
        await RecordRevisionAsync(entity, "Closed", "Suite closed", r.ChangeReason, userId, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> ReopenAsync(Guid id, Guid projectId, SuiteLifecycleRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        entity.Reopen(userId);
        await RecordRevisionAsync(entity, "Reopened", "Suite reopened", r.ChangeReason, userId, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> AddCasesAsync(Guid id, Guid projectId, AddSuiteCasesRequest r, CancellationToken ct)
    {
        var suite = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        if (!suite.IsActive) throw new InvalidOperationException("Cannot modify cases on a closed suite. Reopen it first.");
        if (r.AutomationCaseIds is null || r.AutomationCaseIds.Count == 0) throw new ArgumentException("กรุณาเลือก Automation Case อย่างน้อย 1 รายการ");
        var distinctIds = r.AutomationCaseIds.Distinct().ToList();
        foreach (var caseId in distinctIds)
            if (!await repository.CaseExistsInProjectAsync(projectId, caseId, ct))
                throw new ArgumentException("พบ Automation Case ที่ไม่มีอยู่จริงหรือไม่ได้อยู่ใน Project นี้");
        var nextSort = await repository.GetNextSuiteCaseSortOrderAsync(id, ct);
        var addedIds = new List<Guid>();
        foreach (var caseId in distinctIds)
        {
            if (await repository.SuiteCaseExistsAsync(id, caseId, ct)) continue; // already in the suite — idempotent add
            await repository.AddSuiteCaseAsync(new AutomationSuiteCase(id, caseId, nextSort++, r.IsRequired), ct);
            addedIds.Add(caseId);
        }
        if (addedIds.Count > 0)
        {
            var codes = await repository.GetCaseCodesAsync(addedIds, ct);
            await RecordRevisionAsync(suite, "CasesAdded", $"Added {addedIds.Count} case(s): {string.Join(", ", codes)}", r.ChangeReason, null, ct);
        }
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> UpdateCaseAsync(Guid id, Guid projectId, Guid caseId, UpdateSuiteCaseRequest r, Guid? userId, CancellationToken ct)
    {
        var suite = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        if (!suite.IsActive) throw new InvalidOperationException("Cannot modify cases on a closed suite. Reopen it first.");
        var link = await repository.FindSuiteCaseAsync(id, caseId, ct) ?? throw new EntityNotFoundException("Case is not part of this suite.");
        link.Update(r.SortOrder, r.IsRequired);
        await RecordRevisionAsync(suite, "CaseUpdated", $"{link.AutomationCase.AutomationCode}: sort order {r.SortOrder}, {(r.IsRequired ? "Required" : "Optional")}", r.ChangeReason, userId, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }

    public async Task<AutomationSuiteDto> RemoveCaseAsync(Guid id, Guid projectId, Guid caseId, string? changeReason, Guid? userId, CancellationToken ct)
    {
        var suite = await repository.FindSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        if (!suite.IsActive) throw new InvalidOperationException("Cannot modify cases on a closed suite. Reopen it first.");
        var link = await repository.FindSuiteCaseAsync(id, caseId, ct) ?? throw new EntityNotFoundException("Case is not part of this suite.");
        var code = link.AutomationCase.AutomationCode;
        await repository.RemoveSuiteCaseAsync(link, ct);
        await RecordRevisionAsync(suite, "CaseRemoved", $"Removed {code}", changeReason, userId, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSuiteAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
    }
}
