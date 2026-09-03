using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Requirements;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Requirements;
using ProMaxx2.QA.Domain.TestManagement;

namespace ProMaxx2.QA.UnitTests;

public sealed class RequirementAndTestCaseServiceTests
{
    [Fact]
    public async Task Requirement_service_generates_code_when_blank()
    {
        var projectRepository = new FakeProjectRepository();
        var requirementRepository = new FakeRequirementRepository(
            ["PMX2-SALES-REQ-004", "PMX2-SALES-REQ-003"]);
        var service = new RequirementService(requirementRepository, projectRepository);

        var result = await service.CreateAsync(
            new CreateRequirementRequest(
                projectRepository.ProjectId,
                null,
                projectRepository.ModuleId,
                " ",
                "Title",
                null,
                null,
                "P1",
                null,
                null,
                null,
                true),
            null,
            CancellationToken.None);

        Assert.Equal("PMX2-SALES-REQ-005", result.RequirementCode);
    }

    [Fact]
    public async Task Test_case_service_generates_code_when_blank()
    {
        var projectRepository = new FakeProjectRepository();
        var testCaseRepository = new FakeTestCaseRepository(
            ["PMX2-SALES-TC-004", "PMX2-SALES-TC-003"]);
        var service = new TestCaseService(testCaseRepository, projectRepository);

        var result = await service.CreateAsync(
            new CreateTestCaseRequest(
                projectRepository.ProjectId,
                projectRepository.ModuleId,
                " ",
                "Title",
                null,
                null,
                "P1",
                "Functional",
                false,
                null,
                []),
            null,
            CancellationToken.None);

        Assert.Equal("PMX2-SALES-TC-005", result.TestCaseCode);
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        private readonly Guid _projectId = Guid.NewGuid();
        private readonly Guid _moduleId = Guid.NewGuid();

        public Guid ProjectId => _projectId;
        public Guid ModuleId => _moduleId;

        public Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProjectDto>>([new(_projectId, "PMX2", "Project", null, "Active", null, true, DateTime.UtcNow)]);

        public Task<IReadOnlyList<ProjectDto>> ListForUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProjectDto>>([new(_projectId, "PMX2", "Project", null, "Active", null, true, DateTime.UtcNow)]);

        public Task<ProjectDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<ProjectDto?>(new ProjectDto(id, "PMX2", "Project", null, "Active", null, true, DateTime.UtcNow));

        public Task<bool> ProjectCodeExistsAsync(string code, CancellationToken ct) => Task.FromResult(false);

        public Task<IReadOnlyList<string>> ListProjectCodesAsync(string prefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task AddAsync(Project project, CancellationToken ct) => Task.CompletedTask;

        public Task<Project?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Project?>(new Project("PMX2", "Project", null, null, null));

        public Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ModuleDto>>([new(_moduleId, projectId, null, "SALES", "Sales", null, null, true, 1)]);

        public Task<IReadOnlyList<ProductModule>> ListModuleEntitiesAsync(Guid projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProductModule>>([]);

        public Task<bool> ModuleCodeExistsAsync(Guid projectId, string code, CancellationToken ct) => Task.FromResult(false);

        public Task<IReadOnlyList<string>> ListModuleCodesAsync(Guid projectId, string prefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<ProductModule?> FindModuleAsync(Guid id, CancellationToken ct) => Task.FromResult<ProductModule?>(id == _moduleId ? new ProductModule(_projectId, "SALES", "Sales", null, null, null, null) : null);

        public Task AddModuleAsync(ProductModule module, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> ModuleHasReferencesAsync(Guid moduleId, CancellationToken ct) => Task.FromResult(false);

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task AddProjectUserAsync(Guid userId, Guid projectId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeRequirementRepository(IReadOnlyList<string> existingCodes) : IRequirementRepository
    {
        private readonly List<RequirementDto> _items = existingCodes.Select(code => new RequirementDto(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), code, "Title", null, null, "P1", null, null, null, "Draft", 1, true, DateTime.UtcNow, 0)).ToList();

        public Task<PagedResult<RequirementDto>> ListAsync(RequirementFilter filter, CancellationToken ct) => Task.FromResult(new PagedResult<RequirementDto>(_items.Count, _items));

        public Task<RequirementDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<RequirementDto?>(new RequirementDto(id, Guid.NewGuid(), null, Guid.NewGuid(), "PMX2-SALES-REQ-005", "Title", null, null, "P1", null, null, null, "Draft", 1, true, DateTime.UtcNow, 0));

        public Task<Requirement?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Requirement?>(null);

        public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct) => Task.FromResult(_items.Any(x => x.RequirementCode.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(_items.Select(x => x.RequirementCode).ToList());

        public Task AddAsync(Requirement entity, CancellationToken ct) => Task.CompletedTask;

        public void AddRevision(RequirementRevision revision) { }

        public Task<IReadOnlyList<RequirementRevisionDto>> RevisionsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<RequirementRevisionDto>>([]);

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeTestCaseRepository(IReadOnlyList<string> existingCodes) : ITestCaseRepository
    {
        private readonly List<TestCaseListDto> _items = existingCodes.Select(code => new TestCaseListDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, "Title", "P1", "Functional", false, "Draft", 1, null, 0)).ToList();

        public Task<PagedResult<TestCaseListDto>> ListAsync(Guid? projectId, Guid? moduleId, string? priority, string? testType, string? status, bool? automation, string? search, string? automationTarget, Guid? createdBy, string? sortBy, int page, int size, CancellationToken ct) => Task.FromResult(new PagedResult<TestCaseListDto>(_items.Count, _items));

        public Task<AutomationCandidateSummaryDto> AutomationSummaryAsync(Guid projectId, CancellationToken ct) => Task.FromResult(new AutomationCandidateSummaryDto(_items.Count, _items.Count(x => x.Status == "Ready"), _items.Count(x => x.AutomationTarget == "pos"), _items.Count(x => x.AutomationTarget == "app"), _items.Count(x => x.AutomationTarget is null)));

        public Task<TestCaseDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<TestCaseDto?>(new TestCaseDto(id, Guid.NewGuid(), Guid.NewGuid(), "PMX2-SALES-TC-005", "Title", null, null, "P1", "Functional", false, "Draft", 1, null, []));

        public Task<TestCase?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<TestCase?>(null);

        public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct) => Task.FromResult(_items.Any(x => x.TestCaseCode.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(TestCase entity, CancellationToken ct) => Task.CompletedTask;

        public void AddSteps(IEnumerable<TestStep> steps) { }

        public Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(_items.Select(x => x.TestCaseCode).ToList());

        public Task LinkAsync(Guid requirementId, Guid testCaseId, string? coverageType, CancellationToken ct) => Task.CompletedTask;

        public Task UnlinkAsync(Guid requirementId, Guid testCaseId, CancellationToken ct) => Task.CompletedTask;

        public Task<RtmListResultDto> RtmAsync(Guid releaseId, string? search, string? moduleId, string? coverage, string? status, int page, int size, CancellationToken ct) => Task.FromResult(new RtmListResultDto(new PagedResult<RtmRow>(0, []), new RtmSummaryDto(0, 0, 0, [])));

        public Task<CoverageSummary> CoverageAsync(Guid releaseId, CancellationToken ct) => Task.FromResult(new CoverageSummary(0, 0, 0, 0));

        public Task<IReadOnlyList<TestCaseRevisionDto>> RevisionsAsync(Guid testCaseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<TestCaseRevisionDto>>([]);

        public Task<IReadOnlyList<TestCaseRequirementDto>> RequirementsAsync(Guid testCaseId, CancellationToken ct) => Task.FromResult<IReadOnlyList<TestCaseRequirementDto>>([]);

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
