using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class TestCycleRepository(QaDbContext db, ProjectAccessContext projectCtx) : ITestCycleRepository
{
    public async Task<IReadOnlyList<EnvironmentDto>> EnvironmentsAsync(Guid? projectId, CancellationToken ct)
    {
        var query = db.TestEnvironments.AsNoTracking().AsQueryable();
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        else if (projectCtx.AllowedProjectIds.Length > 0) query = query.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        else query = query.Where(_ => false);
        return await query.OrderBy(x => x.EnvironmentName).Select(x => new EnvironmentDto(x.TestEnvironmentId, x.ProjectId, x.EnvironmentName, x.BaseUrl, x.IsActive)).ToListAsync(ct);
    }

    public Task AddEnvironmentAsync(TestEnvironment entity, CancellationToken ct) => db.TestEnvironments.AddAsync(entity, ct).AsTask();

    private IQueryable<TestCycleDto> Project(IQueryable<TestCycle> query) => query.Select(x => new TestCycleDto(
        x.TestCycleId, x.ProjectId, x.ReleaseId, db.Releases.Where(r => r.ReleaseId == x.ReleaseId).Select(r => r.ReleaseCode).First(),
        x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).First(),
        x.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).First(),
        x.TestSuiteId, db.TestSuites.Where(s => s.TestSuiteId == x.TestSuiteId).Select(s => s.SuiteName).FirstOrDefault(),
        x.CycleCode, x.CycleName, x.CycleType, x.StartDate, x.EndDate, x.OwnerUserId, x.Status, x.Notes,
        x.Cases.Count, x.Cases.Count(c => c.CurrentStatus != "NotRun"), x.Cases.Count == 0 ? 0 : Math.Round(x.Cases.Count(c => c.CurrentStatus != "NotRun") * 100m / x.Cases.Count, 2),
        new List<SuiteModuleDto>(), x.CreatedBy, db.Users.Where(u => u.UserId == x.CreatedBy).Select(u => u.DisplayName).FirstOrDefault(), x.CreatedAt,
        x.CopiedFromTestCycleId, db.TestCycles.Where(c => c.TestCycleId == x.CopiedFromTestCycleId).Select(c => c.CycleCode).FirstOrDefault()));

    public async Task<TestCycleListResultDto> ListAsync(Guid? projectId, Guid? releaseId, Guid? buildId, Guid? moduleId, string? search, string? status, string? cycleType, Guid? createdBy, int page, int size, CancellationToken ct)
    {
        var query = db.TestCycles.AsNoTracking().Where(x => !x.IsDeleted);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        else if (projectCtx.AllowedProjectIds.Length > 0) query = query.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        else query = query.Where(_ => false);
        if (releaseId.HasValue) query = query.Where(x => x.ReleaseId == releaseId);
        if (buildId.HasValue) query = query.Where(x => x.BuildId == buildId);
        if (moduleId.HasValue) query = query.Where(x => x.Cases.Any(c => c.TestCase.ModuleId == moduleId));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.CycleName.Contains(search) || x.CycleCode.Contains(search));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(cycleType)) query = query.Where(x => x.CycleType == cycleType);
        if (createdBy.HasValue) query = query.Where(x => x.CreatedBy == createdBy);
        var total = await query.CountAsync(ct);
        var inProgress = await query.CountAsync(x => x.Status == "InProgress", ct);
        var completed = await query.CountAsync(x => x.Status == "Completed", ct);
        var cancelled = await query.CountAsync(x => x.Status == "Cancelled", ct);
        var executedCases = await query.SelectMany(x => x.Cases).CountAsync(x => x.CurrentStatus != "NotRun", ct);
        var totalCases = await query.SelectMany(x => x.Cases).CountAsync(ct);
        var pageNumber = Math.Max(1, page);
        var pageSize = Math.Clamp(size, 10, 150);
        var items = await Project(query.OrderByDescending(x => x.CreatedAt).Skip((pageNumber - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        var cycleIds = items.Select(x => x.TestCycleId).ToList();
        var moduleLinks = await db.TestCycleCases.AsNoTracking().Where(x => cycleIds.Contains(x.TestCycleId)).Select(x => new { x.TestCycleId, x.TestCase.ModuleId }).Distinct().ToListAsync(ct);
        var moduleIds = moduleLinks.Select(x => x.ModuleId).Distinct().ToList();
        var moduleInfo = await db.Modules.AsNoTracking().Where(m => moduleIds.Contains(m.ModuleId)).ToDictionaryAsync(m => m.ModuleId, m => new SuiteModuleDto(m.ModuleId, m.ModuleCode, m.ModuleName), ct);
        var byCycle = moduleLinks.GroupBy(x => x.TestCycleId).ToDictionary(g => g.Key, g => (IReadOnlyList<SuiteModuleDto>)g.Select(x => moduleInfo.GetValueOrDefault(x.ModuleId)).Where(x => x is not null).Select(x => x!).OrderBy(x => x.ModuleCode).ToList());
        var result = items.Select(x => x with { Modules = byCycle.GetValueOrDefault(x.TestCycleId, Array.Empty<SuiteModuleDto>()) }).ToList();
        return new(new(total, result), new(total, inProgress, completed, cancelled, executedCases, totalCases));
    }

    public async Task<IReadOnlyList<TestCycleOptionDto>> ListOptionsAsync(Guid? projectId, CancellationToken ct)
    {
        var query = db.TestCycles.AsNoTracking().Where(x => !x.IsDeleted);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        else if (projectCtx.AllowedProjectIds.Length > 0) query = query.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        else query = query.Where(_ => false);
        return await query.OrderByDescending(x => x.CreatedAt).Select(x => new TestCycleOptionDto(x.TestCycleId, x.ProjectId, x.ReleaseId, x.BuildId, x.CycleCode, x.CycleName, x.Status)).ToListAsync(ct);
    }

    public async Task<TestCycleDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var query = db.TestCycles.AsNoTracking().Where(x => x.TestCycleId == id && !x.IsDeleted);
        if (projectCtx.AllowedProjectIds.Length > 0) query = query.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        return await Project(query).SingleOrDefaultAsync(ct);
    }

    public async Task<TestCycle?> FindAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.TestCycles.SingleOrDefaultAsync(x => x.TestCycleId == id && !x.IsDeleted, ct);
        return entity is not null && projectCtx.AllowedProjectIds.Length > 0 && !projectCtx.AllowedProjectIds.Contains(entity.ProjectId) ? null : entity;
    }

    public Task<bool> CodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct) => db.TestCycles.AnyAsync(x => x.ProjectId == projectId && x.CycleCode == code && (!excludeId.HasValue || x.TestCycleId != excludeId), ct);
    public async Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct) => await db.TestCycles.AsNoTracking().Where(x => x.ProjectId == projectId && x.CycleCode.StartsWith(prefix)).Select(x => x.CycleCode).ToListAsync(ct);

    public async Task ValidateReferencesAsync(SaveTestCycleRequest request, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.ProjectId == request.ProjectId && x.IsActive, ct)) throw new ArgumentException("Project is not active.");
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == request.ReleaseId && x.ProjectId == request.ProjectId, ct) ?? throw new EntityNotFoundException("Release not found in project.");
        if (release.Status is "Released" or "Cancelled") throw new ArgumentException("Cannot create a cycle for a closed release.");
        if (!await db.Builds.AnyAsync(x => x.BuildId == request.BuildId && x.ReleaseId == request.ReleaseId && x.IsActive, ct)) throw new ArgumentException("Build is not active or does not belong to selected release.");
        if (!await db.TestEnvironments.AnyAsync(x => x.TestEnvironmentId == request.EnvironmentId && x.ProjectId == request.ProjectId && x.IsActive, ct)) throw new ArgumentException("Environment is not active in selected project.");
        if (request.TestSuiteId.HasValue && !await db.TestSuites.AnyAsync(x => x.TestSuiteId == request.TestSuiteId && x.ProjectId == request.ProjectId && x.IsActive, ct)) throw new ArgumentException("Test suite is not active in selected project.");
    }

    public async Task<TestCycle> CloneAsync(Guid sourceCycleId, CloneTestCycleRequest request, Guid? userId, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var source = await db.TestCycles.Include(x => x.Cases).SingleOrDefaultAsync(x => x.TestCycleId == sourceCycleId && !x.IsDeleted, ct) ?? throw new EntityNotFoundException("Source test cycle not found.");
        if (allowed.Length > 0 && !allowed.Contains(source.ProjectId)) throw new EntityNotFoundException("Source test cycle not found.");
        if (!await db.Projects.AnyAsync(x => x.ProjectId == source.ProjectId && x.IsActive, ct)) throw new ArgumentException("Project is not active.");
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == request.TargetReleaseId && x.ProjectId == source.ProjectId, ct) ?? throw new EntityNotFoundException("Target release not found in project.");
        if (release.Status is "Released" or "Cancelled") throw new ArgumentException("Cannot create a cycle for a closed release.");
        if (!await db.Builds.AnyAsync(x => x.BuildId == request.TargetBuildId && x.ReleaseId == request.TargetReleaseId && x.IsActive, ct)) throw new ArgumentException("Target build is not active or does not belong to selected release.");
        if (!await db.TestEnvironments.AnyAsync(x => x.TestEnvironmentId == request.TargetEnvironmentId && x.ProjectId == source.ProjectId && x.IsActive, ct)) throw new ArgumentException("Target environment is not active in selected project.");
        if (request.CloneMode == TestCycleCloneModes.SuiteLatest && !source.TestSuiteId.HasValue) throw new ArgumentException("SuiteLatest requires the source cycle to have a test suite.");
        if (request.CloneMode == TestCycleCloneModes.SuiteLatest && !await db.TestSuites.AnyAsync(x => x.TestSuiteId == source.TestSuiteId && x.ProjectId == source.ProjectId && x.IsActive, ct)) throw new ArgumentException("Source test suite is not active in the project.");
        var target = new TestCycle(source.ProjectId, request.TargetReleaseId, request.TargetBuildId, request.TargetEnvironmentId, source.TestSuiteId, request.CycleCode!, request.CycleName, request.CycleType, request.StartDate, request.EndDate, request.OwnerUserId, request.Notes, userId, source.TestCycleId);
        IEnumerable<TestCycleCase> cases;
        if (request.CloneMode == TestCycleCloneModes.SuiteLatest)
        {
            var latest = await db.TestSuiteCases.Where(x => x.TestSuiteId == source.TestSuiteId).OrderBy(x => x.SortOrder).Select(x => new { x.TestCaseId, x.TestCase.RevisionNo, x.TestCase.Priority }).ToListAsync(ct);
            cases = latest.Select((x, index) => new TestCycleCase(target.TestCycleId, x.TestCaseId, x.RevisionNo, x.Priority, index + 1));
        }
        else
        {
            cases = source.Cases.OrderBy(x => x.ExecutionOrder).Select((x, index) => new TestCycleCase(target.TestCycleId, x.TestCaseId, x.TestCaseRevisionNo, x.Priority ?? "P2", index + 1));
        }
        foreach (var testCycleCase in cases) target.Cases.Add(testCycleCase);
        db.TestCycles.Add(target);
        db.AuditLogs.Add(new ProMaxx2.QA.Domain.Governance.AuditLog(userId, "Clone", "TestCycle", target.TestCycleId.ToString(), $"Cloned from {source.CycleCode} ({source.TestCycleId}) using {request.CloneMode}; {target.Cases.Count} cases copied.", null, $"{{\"sourceTestCycleId\":\"{source.TestCycleId}\",\"sourceCycleCode\":\"{source.CycleCode}\",\"cloneMode\":\"{request.CloneMode}\",\"caseCount\":{target.Cases.Count}}}", null));
        await db.SaveChangesAsync(ct);
        return target;
    }

    public Task AddAsync(TestCycle entity, CancellationToken ct) => db.TestCycles.AddAsync(entity, ct).AsTask();

    public async Task PopulateAsync(Guid cycleId, Guid suiteId, bool requiredOnly, CancellationToken ct)
    {
        var source = await db.TestSuiteCases.Where(x => x.TestSuiteId == suiteId && (!requiredOnly || x.IsRequired)).OrderBy(x => x.SortOrder).Select(x => new { x.TestCaseId, x.TestCase.RevisionNo, x.TestCase.Priority }).ToListAsync(ct);
        db.TestCycleCases.AddRange(source.Select((x, index) => new TestCycleCase(cycleId, x.TestCaseId, x.RevisionNo, x.Priority, index + 1)));
    }

    public Task DeleteAsync(TestCycle entity, CancellationToken ct) { db.TestCycles.Update(entity); return Task.CompletedTask; }
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
