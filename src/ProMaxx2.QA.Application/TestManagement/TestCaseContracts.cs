using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Application.TestManagement;

public sealed record StepDto(int StepNo, string Action, string? TestData, string ExpectedResult);
public sealed record TestCaseDto(Guid TestCaseId, Guid ProjectId, Guid ModuleId, string TestCaseCode, string Title, string? Objective, string? Preconditions, string Priority, string? TestType, bool AutomationCandidate, string Status, int RevisionNo, Guid? OwnerUserId, IReadOnlyList<StepDto> Steps);
public sealed record CreateTestCaseRequest(Guid ProjectId, Guid ModuleId, string TestCaseCode, string Title, string? Objective, string? Preconditions, string Priority, string? TestType, bool AutomationCandidate, Guid? OwnerUserId, IReadOnlyList<StepDto> Steps);
public sealed record ChangeTestCaseStatusRequest(string Status);
public sealed record RtmRow(Guid RequirementId, string RequirementCode, string Title, string Priority, int TestCaseCount, string CoverageStatus, string Status);
public sealed record CoverageSummary(int TotalRequirements, int Covered, int NotCovered, decimal CoveragePercent);
public sealed record CreateTestCaseRevisionRequest(string Title, string? Objective, string? Preconditions, string ChangeReason, IReadOnlyList<StepDto> Steps);
public sealed record UpdateTestCaseRequest(Guid ModuleId, string Title, string? Objective, string? Preconditions, string Priority, string? TestType, bool AutomationCandidate, Guid? OwnerUserId, string ChangeReason, IReadOnlyList<StepDto> Steps);

public interface ITestCaseRepository
{
    Task<IReadOnlyList<TestCaseDto>> ListAsync(Guid? projectId, string? search, CancellationToken ct);
    Task<TestCaseDto?> GetAsync(Guid id, CancellationToken ct);
    Task<TestCase?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct);
    Task AddAsync(TestCase entity, CancellationToken ct);
    void AddSteps(IEnumerable<TestStep> steps);
    Task LinkAsync(Guid requirementId, Guid testCaseId, string? coverageType, CancellationToken ct);
    Task UnlinkAsync(Guid requirementId, Guid testCaseId, CancellationToken ct);
    Task<IReadOnlyList<RtmRow>> RtmAsync(Guid releaseId, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public sealed class TestCaseService(ITestCaseRepository repository)
{
    private readonly IProjectRepository? projectRepository;

    public TestCaseService(ITestCaseRepository repository, IProjectRepository? projectRepository) : this(repository)
    {
        this.projectRepository = projectRepository;
    }

    public Task<IReadOnlyList<TestCaseDto>> ListAsync(Guid? projectId, string? search, CancellationToken ct) => repository.ListAsync(projectId, search, ct);

    public async Task<TestCaseDto> GetAsync(Guid id, CancellationToken ct) => await repository.GetAsync(id, ct) ?? throw new EntityNotFoundException("Test case not found.");

    public async Task<TestCaseDto> CreateAsync(CreateTestCaseRequest r, Guid? userId, CancellationToken ct)
    {
        var code = await ResolveCodeAsync(r, ct);
        if (await repository.CodeExistsAsync(r.ProjectId, code, ct))
            throw new DuplicateCodeException("Test case code already exists.");

        var e = new TestCase(r.ProjectId, r.ModuleId, code, r.Title, r.Objective, r.Preconditions, r.Priority, r.TestType, r.AutomationCandidate, r.OwnerUserId, r.Steps.Select(x => new TestStepInput(x.StepNo, x.Action, x.TestData, x.ExpectedResult)), userId);
        await repository.AddAsync(e, ct);
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(e.TestCaseId, ct))!;
    }

    public async Task<TestCaseDto> UpdateAsync(Guid id, UpdateTestCaseRequest r, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test case not found.");
        e.Update(r.ModuleId, r.Title, r.Objective, r.Preconditions, r.Priority, r.TestType, r.AutomationCandidate, r.OwnerUserId, r.Steps.Select(x => new TestStepInput(x.StepNo, x.Action, x.TestData, x.ExpectedResult)), r.ChangeReason, userId);
        repository.AddSteps(e.Steps.Where(x => x.RevisionNo == e.RevisionNo));
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    public async Task<TestCaseDto> ReviseAsync(Guid id, CreateTestCaseRevisionRequest r, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test case not found.");
        e.CreateRevision(r.Title, r.Objective, r.Preconditions, r.Steps.Select(x => new TestStepInput(x.StepNo, x.Action, x.TestData, x.ExpectedResult)), r.ChangeReason, userId);
        repository.AddSteps(e.Steps.Where(x => x.RevisionNo == e.RevisionNo));
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    public async Task<TestCaseDto> StatusAsync(Guid id, string status, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test case not found.");
        e.ChangeStatus(status, userId);
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    public async Task DeleteAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test case not found.");
        e.SoftDelete(userId);
        await repository.SaveAsync(ct);
    }

    public async Task LinkAsync(Guid requirementId, Guid testCaseId, string? coverageType, CancellationToken ct)
    {
        _ = await repository.FindAsync(testCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        await repository.LinkAsync(requirementId, testCaseId, coverageType, ct);
        await repository.SaveAsync(ct);
    }

    public async Task UnlinkAsync(Guid requirementId, Guid testCaseId, CancellationToken ct)
    {
        await repository.UnlinkAsync(requirementId, testCaseId, ct);
        await repository.SaveAsync(ct);
    }

    public Task<IReadOnlyList<RtmRow>> RtmAsync(Guid releaseId, CancellationToken ct) => repository.RtmAsync(releaseId, ct);

    public async Task<CoverageSummary> CoverageAsync(Guid releaseId, CancellationToken ct)
    {
        var rows = await repository.RtmAsync(releaseId, ct);
        return new CoverageSummary(rows.Count, rows.Count(x => x.CoverageStatus.Equals("Covered", StringComparison.OrdinalIgnoreCase)), rows.Count(x => x.CoverageStatus.Equals("Not Covered", StringComparison.OrdinalIgnoreCase)), rows.Count == 0 ? 0 : Math.Round((decimal)rows.Count(x => x.CoverageStatus.Equals("Covered", StringComparison.OrdinalIgnoreCase)) / rows.Count * 100, 1));
    }

    private async Task<string> ResolveCodeAsync(CreateTestCaseRequest r, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(r.TestCaseCode))
            return r.TestCaseCode.Trim().ToUpperInvariant();

        if (projectRepository is null)
            throw new ArgumentException("Test case code is required.");

        var project = await projectRepository.GetAsync(r.ProjectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        var module = await projectRepository.FindModuleAsync(r.ModuleId, ct) ?? throw new EntityNotFoundException("Module not found.");
        if (module.ProjectId != r.ProjectId)
            throw new EntityNotFoundException("Module not found.");

        var prefix = BusinessCodeGenerator.ContextualPrefix(project.ProjectCode, module.ModuleCode, "TC");
        return await BusinessCodeGenerator.NextAvailableAsync(prefix, candidate => repository.CodeExistsAsync(r.ProjectId, candidate, ct));
    }
}
