using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed partial class AutomationRepository(QaDbContext db) : IAutomationRepository, IAutomationSuiteRepository, IAutomationScheduleRepository, IAutomationBuildTriggerRepository, IAutomationWebhookRepository, IAutomationDataSnapshotRepository, IAutomationDataRestoreRepository, IAutomationDataSeedRepository, IAutomationEnvironmentDataProfileRepository
{
    public async Task<IReadOnlyList<AutomationCaseDto>> ListCasesAsync(Guid projectId, string? search, int take, CancellationToken ct)
    {
        var q = db.AutomationCases.AsNoTracking().Where(x => !x.IsDeleted && x.TestCase.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.AutomationCode.Contains(search) || x.TestCase.TestCaseCode.Contains(search) || x.TestCase.Title.Contains(search));
        var rows = await q.OrderByDescending(x => x.CreatedAt).Take(take)
            .Select(x => new { x.AutomationCaseId, x.TestCaseId, TestCaseCode = x.TestCase.TestCaseCode, TestCaseTitle = x.TestCase.Title, x.AutomationCode, x.AutomationType, x.Status, x.CurrentVersionNo, VersionCount = x.Versions.Count, x.OwnerUserId, OwnerName = x.OwnerUserId != null ? db.Users.Where(u => u.UserId == x.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null, x.IsAiGenerated, x.CreatedAt, x.MaintenanceReason, x.MaintenanceOwnerUserId, x.MaintenanceOpenedAt, x.IsQuarantined, x.QuarantineReason, x.QuarantineOwnerUserId, x.QuarantineExpiresAt })
            .ToListAsync(ct);
        return rows.Select(r => new AutomationCaseDto(r.AutomationCaseId, r.TestCaseId, r.TestCaseCode, r.TestCaseTitle, r.AutomationCode, r.AutomationType, r.Status, r.CurrentVersionNo, r.VersionCount, r.OwnerUserId, r.OwnerName, r.IsAiGenerated, r.CreatedAt, r.MaintenanceReason, r.MaintenanceOwnerUserId, r.MaintenanceOpenedAt, r.IsQuarantined, r.QuarantineReason, r.QuarantineOwnerUserId, r.QuarantineExpiresAt)).ToList();
    }

    public async Task<PagedResult<AutomationCaseDto>> ListCasesPagedAsync(Guid projectId, string? search, string? status, string? automationTarget, string? sortBy, int page, int size, CancellationToken ct)
    {
        var q = db.AutomationCases.AsNoTracking().Where(x => !x.IsDeleted && x.TestCase.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.AutomationCode.Contains(search) || x.TestCase.TestCaseCode.Contains(search) || x.TestCase.Title.Contains(search));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(automationTarget)) q = q.Where(x => x.AutomationType == automationTarget);
        var total = await q.CountAsync(ct);
        var p = Math.Max(1, page);
        var s = Math.Clamp(size, 1, 200);
        var ordered = sortBy switch
        {
            "code" => q.OrderBy(x => x.AutomationCode),
            "status" => q.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt),
            _ => q.OrderByDescending(x => x.CreatedAt),
        };
        var rows = await ordered.Skip((p - 1) * s).Take(s)
            .Select(x => new { x.AutomationCaseId, x.TestCaseId, TestCaseCode = x.TestCase.TestCaseCode, TestCaseTitle = x.TestCase.Title, x.AutomationCode, x.AutomationType, x.Status, x.CurrentVersionNo, VersionCount = x.Versions.Count, x.OwnerUserId, OwnerName = x.OwnerUserId != null ? db.Users.Where(u => u.UserId == x.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null, x.IsAiGenerated, x.CreatedAt, x.MaintenanceReason, x.MaintenanceOwnerUserId, x.MaintenanceOpenedAt, x.IsQuarantined, x.QuarantineReason, x.QuarantineOwnerUserId, x.QuarantineExpiresAt })
            .ToListAsync(ct);
        var items = rows.Select(r => new AutomationCaseDto(r.AutomationCaseId, r.TestCaseId, r.TestCaseCode, r.TestCaseTitle, r.AutomationCode, r.AutomationType, r.Status, r.CurrentVersionNo, r.VersionCount, r.OwnerUserId, r.OwnerName, r.IsAiGenerated, r.CreatedAt, r.MaintenanceReason, r.MaintenanceOwnerUserId, r.MaintenanceOpenedAt, r.IsQuarantined, r.QuarantineReason, r.QuarantineOwnerUserId, r.QuarantineExpiresAt)).ToList();
        return new PagedResult<AutomationCaseDto>(total, items);
    }

    public async Task<AutomationCaseDto?> GetCaseAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        var r = await db.AutomationCases.AsNoTracking().Where(x => x.AutomationCaseId == id && !x.IsDeleted && x.TestCase.ProjectId == projectId)
            .Select(x => new { x.AutomationCaseId, x.TestCaseId, TestCaseCode = x.TestCase.TestCaseCode, TestCaseTitle = x.TestCase.Title, x.AutomationCode, x.AutomationType, x.Status, x.CurrentVersionNo, VersionCount = x.Versions.Count, x.OwnerUserId, x.IsAiGenerated, x.CreatedAt, x.MaintenanceReason, x.MaintenanceOwnerUserId, x.MaintenanceOpenedAt, x.IsQuarantined, x.QuarantineReason, x.QuarantineOwnerUserId, x.QuarantineExpiresAt })
            .SingleOrDefaultAsync(ct);
        if (r is null) return null;
        return new AutomationCaseDto(r.AutomationCaseId, r.TestCaseId, r.TestCaseCode, r.TestCaseTitle, r.AutomationCode, r.AutomationType, r.Status, r.CurrentVersionNo, r.VersionCount, r.OwnerUserId, null, r.IsAiGenerated, r.CreatedAt, r.MaintenanceReason, r.MaintenanceOwnerUserId, r.MaintenanceOpenedAt, r.IsQuarantined, r.QuarantineReason, r.QuarantineOwnerUserId, r.QuarantineExpiresAt);
    }

    public Task<AutomationCase?> FindCaseAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationCases.Include(x => x.Versions).SingleOrDefaultAsync(x => x.AutomationCaseId == id && !x.IsDeleted && x.TestCase.ProjectId == projectId, ct);

    public Task<AutomationCase?> FindCaseByIdAsync(Guid id, CancellationToken ct)
        => db.AutomationCases.SingleOrDefaultAsync(x => x.AutomationCaseId == id && !x.IsDeleted, ct);

    public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct)
        => db.AutomationCases.AnyAsync(x => !x.IsDeleted && x.AutomationCode == code && x.TestCase.ProjectId == projectId, ct);

    public Task AddCaseAsync(AutomationCase entity, CancellationToken ct) => db.AutomationCases.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationVersionDto>> ListVersionsAsync(Guid caseId, CancellationToken ct)
        => await db.AutomationVersions.AsNoTracking().Where(x => x.AutomationCaseId == caseId).OrderByDescending(x => x.VersionNo)
            .Select(x => new AutomationVersionDto(x.AutomationVersionId, x.AutomationCaseId, x.VersionNo, x.TestCaseRevisionNo, x.DslVersion, x.DslJson, x.GeneratedByAi, x.AiProvider, x.AiModel, x.AiConfidence, x.ValidationStatus, x.ValidationErrors, x.ApprovedBy, x.ApprovedAt, x.ChangeReason, x.CreatedAt)).ToListAsync(ct);

    public Task<AutomationVersion?> FindVersionAsync(Guid versionId, CancellationToken ct)
        => db.AutomationVersions.SingleOrDefaultAsync(x => x.AutomationVersionId == versionId, ct);

    public Task AddVersionAsync(AutomationVersion entity, CancellationToken ct) => db.AutomationVersions.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationActionDto>> ListActionsAsync(CancellationToken ct)
        => await db.AutomationActions.AsNoTracking().OrderBy(x => x.Category).ThenBy(x => x.ActionCode)
            .Select(x => new AutomationActionDto(x.AutomationActionId, x.ActionCode, x.ActionName, x.Category, x.Description, x.ParameterSchemaJson, x.HandlerKey, x.MinimumAgentVersion, x.IsActive, x.RetrySafety)).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListActionCodesAsync(CancellationToken ct)
        => await db.AutomationActions.AsNoTracking().Where(x => x.IsActive).Select(x => x.ActionCode).ToListAsync(ct);

    public Task<AutomationAction?> FindActionAsync(Guid id, CancellationToken ct)
        => db.AutomationActions.SingleOrDefaultAsync(x => x.AutomationActionId == id, ct);

    public Task<AutomationAction?> FindActionByCodeAsync(string code, CancellationToken ct)
        => db.AutomationActions.SingleOrDefaultAsync(x => x.ActionCode == code.Trim().ToUpperInvariant(), ct);

    public Task AddActionAsync(AutomationAction entity, CancellationToken ct) => db.AutomationActions.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationObjectDto>> ListObjectsAsync(Guid projectId, string? search, CancellationToken ct)
    {
        var q = db.AutomationObjects.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.ObjectCode.Contains(search) || x.ObjectName.Contains(search) || x.ScreenCode.Contains(search));
        return await q.OrderBy(x => x.ApplicationCode).ThenBy(x => x.ScreenCode).ThenBy(x => x.ObjectCode)
            .Select(x => new AutomationObjectDto(x.AutomationObjectId, x.ProjectId, x.ModuleId, x.Module != null ? x.Module.ModuleCode : null, x.Module != null ? x.Module.ModuleName : null, x.ApplicationCode, x.ScreenCode, x.ObjectCode, x.ObjectName, x.ControlType, x.AutomationId, x.SelectorJson, x.ObjectVersion, x.IsActive)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListObjectKeysAsync(Guid projectId, CancellationToken ct)
        => await db.AutomationObjects.AsNoTracking().Where(x => x.ProjectId == projectId && x.IsActive).Select(x => x.ScreenCode + "." + x.ObjectCode).Distinct().ToListAsync(ct);

    public Task<AutomationObject?> FindObjectAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationObjects.SingleOrDefaultAsync(x => x.AutomationObjectId == id && x.ProjectId == projectId, ct);

    public Task<bool> ObjectKeyExistsAsync(Guid projectId, string applicationCode, string screenCode, string objectCode, Guid? excludeId, CancellationToken ct)
    {
        var app = string.IsNullOrWhiteSpace(applicationCode) ? "Promaxx2" : applicationCode.Trim();
        var screen = string.IsNullOrWhiteSpace(screenCode) ? "Default" : screenCode.Trim();
        var code = objectCode.Trim().ToUpperInvariant();
        return db.AutomationObjects.AnyAsync(x => x.ProjectId == projectId && x.ApplicationCode == app && x.ScreenCode == screen && x.ObjectCode == code && (!excludeId.HasValue || x.AutomationObjectId != excludeId.Value), ct);
    }

    public Task<bool> ObjectAutomationIdExistsAsync(Guid projectId, string applicationCode, string automationId, Guid? excludeId, CancellationToken ct)
    {
        var app = string.IsNullOrWhiteSpace(applicationCode) ? "Promaxx2" : applicationCode.Trim();
        var id = automationId.Trim();
        return db.AutomationObjects.AnyAsync(x => x.ProjectId == projectId && x.ApplicationCode == app && x.AutomationId == id && (!excludeId.HasValue || x.AutomationObjectId != excludeId.Value), ct);
    }

    public Task AddObjectAsync(AutomationObject entity, CancellationToken ct) => db.AutomationObjects.AddAsync(entity, ct).AsTask();

    public Task<AutomationAgent?> FindAgentByCodeAsync(string agentCode, CancellationToken ct)
        => db.AutomationAgents.Include(x => x.Capabilities).SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);

    public async Task<IReadOnlyList<AutomationAgentDto>> ListAgentsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var agents = await db.AutomationAgents.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync(ct);
        return agents.Select(x => new AutomationAgentDto(x.AgentId, x.AgentCode, x.MachineName, x.AgentVersion, x.OperatingSystem, x.Architecture, x.Status, x.LastHeartbeatAt, x.CurrentExecutionId, x.RegisteredAt, x.IsEnabled, !x.IsEnabled ? "Disabled" : now - x.LastHeartbeatAt <= TimeSpan.FromSeconds(60) ? "Online" : "Offline", x.Capabilities.Select(c => c.CapabilityCode).ToList())).ToList();
    }

    public Task AddAgentAsync(AutomationAgent entity, CancellationToken ct) => db.AutomationAgents.AddAsync(entity, ct).AsTask();

    public async Task<AutomationExecutionDto?> GetExecutionAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        var r = await db.AutomationExecutions.AsNoTracking().Where(x => x.AutomationExecutionId == id && x.AutomationCase.TestCase.ProjectId == projectId)
            .Select(x => new { x.AutomationExecutionId, x.AutomationCaseId, AutomationCode = x.AutomationCase.AutomationCode, TestCaseCode = x.AutomationCase.TestCase.TestCaseCode, TestCaseTitle = x.AutomationCase.TestCase.Title, x.AutomationVersionId, VersionNo = x.AutomationVersion.VersionNo, x.TestExecutionId, x.DefectId, x.TargetApp, x.AgentId, AgentCode = x.Agent != null ? x.Agent.AgentCode : null, x.BuildId, BuildNumber = x.Build.BuildNumber, x.EnvironmentId, EnvironmentName = x.Environment.EnvironmentName, x.JobId, x.Status, x.StartedAt, x.CompletedAt, x.DurationMs, x.FailureType, x.ErrorCode, x.ErrorMessage, x.ClassifiedFailureType, x.ClassifiedRecommendation, x.RetryOfExecutionId, x.RetryCount })
            .SingleOrDefaultAsync(ct);
        if (r is null) return null;
        var steps = await db.AutomationStepResults.AsNoTracking().Where(s => s.AutomationExecutionId == id).OrderBy(s => s.StepNo)
            .Select(s => new AutomationStepResultDto(s.AutomationStepResultId, s.StepNo, s.ActionCode, s.Status, s.StartedAt, s.CompletedAt, s.DurationMs, s.ActualResult, s.ErrorCode, s.ErrorMessage)).ToListAsync(ct);
        var evidence = await ListEvidenceAsync(id, ct);
        return new AutomationExecutionDto(r.AutomationExecutionId, r.AutomationCaseId, r.AutomationCode, r.TestCaseCode, r.TestCaseTitle, r.AutomationVersionId, r.VersionNo, r.TestExecutionId, r.DefectId, r.TargetApp, r.AgentId, r.AgentCode, r.BuildId, r.BuildNumber, r.EnvironmentId, r.EnvironmentName, r.JobId, r.Status, r.StartedAt, r.CompletedAt, r.DurationMs, r.FailureType, r.ErrorCode, r.ErrorMessage, steps, evidence, r.ClassifiedFailureType, r.ClassifiedRecommendation, r.RetryOfExecutionId, r.RetryCount);
    }

    public Task<AutomationExecution?> FindExecutionAsync(Guid id, CancellationToken ct)
        => db.AutomationExecutions.Include(x => x.StepResults).Include(x => x.AutomationCase).ThenInclude(x => x.TestCase).SingleOrDefaultAsync(x => x.AutomationExecutionId == id, ct);

    public Task AddExecutionAsync(AutomationExecution entity, CancellationToken ct) => db.AutomationExecutions.AddAsync(entity, ct).AsTask();

    public Task<AutomationJob?> FindJobByExecutionAsync(Guid executionId, CancellationToken ct)
        => db.AutomationJobs.SingleOrDefaultAsync(x => x.AutomationExecutionId == executionId, ct);

    public Task AddJobAsync(AutomationJob entity, CancellationToken ct) => db.AutomationJobs.AddAsync(entity, ct).AsTask();

    public Task<AutomationJob?> FindJobAsync(Guid jobId, CancellationToken ct)
        => db.AutomationJobs.SingleOrDefaultAsync(x => x.JobId == jobId, ct);

    public async Task<AutomationJobPackageDto?> ClaimNextJobAsync(string agentCode, string agentVersion, IReadOnlyList<string> capabilities, string targetApp, CancellationToken ct)
    {
        var agent = await db.AutomationAgents.Include(x => x.Capabilities).SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);
        if (agent is null || !agent.IsEnabled || agent.IsDeleted) return null;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var now = DateTime.UtcNow;
        var q = db.AutomationJobs.Where(x => x.Status == "Queued" && x.QueuedAt <= now);
        var target = targetApp?.Trim() ?? "WindowsUI";
        if (target != "WindowsUI")
        {
            q = q.Where(j => j.AutomationExecution.AutomationCase.AutomationType == target || j.AutomationExecution.AutomationCase.AutomationType == "WindowsUI");
        }
        var job = await q.OrderBy(x => x.Priority).ThenBy(x => x.QueuedAt).FirstOrDefaultAsync(ct);
        if (job is null) { if (transaction is not null) await transaction.CommitAsync(ct); return null; }
        job.Assign(agent.AgentId);
        var execution = await db.AutomationExecutions.Include(x => x.AutomationCase).ThenInclude(x => x.TestCase).Include(x => x.AutomationVersion).SingleOrDefaultAsync(x => x.AutomationExecutionId == job.AutomationExecutionId, ct) ?? throw new InvalidOperationException("Execution for job not found.");
        execution.Start(agent.AgentId, DateTime.UtcNow);
        agent.SetCurrentExecution(execution.AutomationExecutionId);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        var build = await db.Builds.AsNoTracking().Where(b => b.BuildId == execution.BuildId).Select(b => new { b.BuildNumber }).SingleAsync(ct);
        var env = await db.TestEnvironments.AsNoTracking().Where(e => e.TestEnvironmentId == execution.EnvironmentId).Select(e => e.EnvironmentName).SingleAsync(ct);
        var actions = await ListActionCodesAsync(ct);
        var objects = await ListObjectsAsync(execution.AutomationCase.TestCase.ProjectId, null, ct);
        return new AutomationJobPackageDto(job.JobId, execution.AutomationExecutionId, execution.AutomationCaseId, execution.AutomationCase.AutomationCode, execution.AutomationVersionId, execution.AutomationVersion.VersionNo, execution.AutomationVersion.DslVersion, execution.AutomationVersion.DslJson, execution.BuildId, build.BuildNumber, execution.EnvironmentId, env, actions, objects);
    }

    public async Task<IReadOnlyList<AutomationJobDto>> ListJobsAsync(Guid? projectId, Guid? buildId, int take, CancellationToken ct)
    {
        var q = db.AutomationJobs.AsNoTracking();
        if (projectId.HasValue) q = q.Where(j => j.AutomationExecution.AutomationCase.TestCase.ProjectId == projectId);
        if (buildId.HasValue) q = q.Where(j => j.AutomationExecution.BuildId == buildId);
        return await q.OrderByDescending(j => j.QueuedAt).Take(take)
            .Select(j => new AutomationJobDto(j.JobId, j.AutomationExecutionId, j.Priority, j.RequestedAgentId, j.AssignedAgentId, j.AssignedAgent != null ? j.AssignedAgent.AgentCode : null, j.Status, j.QueuedAt, j.AssignedAt, j.StartedAt, j.CompletedAt, j.RetryCount, j.LastError)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AutomationExecutionDto>> ListExecutionsAsync(Guid projectId, Guid? buildId, int take, CancellationToken ct)
    {
        var rows = await db.AutomationExecutions.AsNoTracking()
            .Where(x => x.AutomationCase.TestCase.ProjectId == projectId && (!buildId.HasValue || x.BuildId == buildId))
            .OrderByDescending(x => x.CreatedAt).Take(take)
            .Select(x => new { x.AutomationExecutionId, x.AutomationCaseId, AutomationCode = x.AutomationCase.AutomationCode, TestCaseCode = x.AutomationCase.TestCase.TestCaseCode, TestCaseTitle = x.AutomationCase.TestCase.Title, x.AutomationVersionId, VersionNo = x.AutomationVersion.VersionNo, x.TestExecutionId, x.DefectId, x.TargetApp, x.AgentId, AgentCode = x.Agent != null ? x.Agent.AgentCode : null, x.BuildId, BuildNumber = x.Build.BuildNumber, x.EnvironmentId, EnvironmentName = x.Environment.EnvironmentName, x.JobId, x.Status, x.StartedAt, x.CompletedAt, x.DurationMs, x.FailureType, x.ErrorCode, x.ErrorMessage, x.ClassifiedFailureType, x.ClassifiedRecommendation, x.RetryOfExecutionId, x.RetryCount })
            .ToListAsync(ct);
        return rows.Select(r => new AutomationExecutionDto(r.AutomationExecutionId, r.AutomationCaseId, r.AutomationCode, r.TestCaseCode, r.TestCaseTitle, r.AutomationVersionId, r.VersionNo, r.TestExecutionId, r.DefectId, r.TargetApp, r.AgentId, r.AgentCode, r.BuildId, r.BuildNumber, r.EnvironmentId, r.EnvironmentName, r.JobId, r.Status, r.StartedAt, r.CompletedAt, r.DurationMs, r.FailureType, r.ErrorCode, r.ErrorMessage, [], [], r.ClassifiedFailureType, r.ClassifiedRecommendation, r.RetryOfExecutionId, r.RetryCount)).ToList();
    }

    public async Task<PagedResult<AutomationJobDto>> ListJobsPagedAsync(Guid? projectId, Guid? buildId, string? status, string? sortBy, int page, int size, CancellationToken ct)
    {
        var q = db.AutomationJobs.AsNoTracking();
        if (projectId.HasValue) q = q.Where(j => j.AutomationExecution.AutomationCase.TestCase.ProjectId == projectId);
        if (buildId.HasValue) q = q.Where(j => j.AutomationExecution.BuildId == buildId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(j => j.Status == status);
        var total = await q.CountAsync(ct);
        var p = Math.Max(1, page);
        var s = Math.Clamp(size, 1, 200);
        var ordered = sortBy switch
        {
            "status" => q.OrderBy(j => j.Status).ThenByDescending(j => j.QueuedAt),
            _ => q.OrderByDescending(j => j.QueuedAt),
        };
        var items = await ordered.Skip((p - 1) * s).Take(s)
            .Select(j => new AutomationJobDto(j.JobId, j.AutomationExecutionId, j.Priority, j.RequestedAgentId, j.AssignedAgentId, j.AssignedAgent != null ? j.AssignedAgent.AgentCode : null, j.Status, j.QueuedAt, j.AssignedAt, j.StartedAt, j.CompletedAt, j.RetryCount, j.LastError)).ToListAsync(ct);
        return new PagedResult<AutomationJobDto>(total, items);
    }

    public async Task<PagedResult<AutomationExecutionDto>> ListExecutionsPagedAsync(Guid projectId, Guid? buildId, Guid? environmentId, Guid? agentId, string? targetApp, string? status, string? failureType,
        DateTime? from, DateTime? to, string? search, string? sortBy, int page, int size, CancellationToken ct)
    {
        var q = db.AutomationExecutions.AsNoTracking().Where(x => x.AutomationCase.TestCase.ProjectId == projectId);
        if (buildId.HasValue) q = q.Where(x => x.BuildId == buildId);
        if (environmentId.HasValue) q = q.Where(x => x.EnvironmentId == environmentId);
        if (agentId.HasValue) q = q.Where(x => x.AgentId == agentId);
        if (!string.IsNullOrWhiteSpace(targetApp)) q = q.Where(x => x.TargetApp == targetApp);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(failureType)) q = q.Where(x => x.ClassifiedFailureType == failureType);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.AutomationCase.AutomationCode.Contains(search) || (x.Agent != null && x.Agent.AgentCode.Contains(search)));
        var total = await q.CountAsync(ct);
        var p = Math.Max(1, page);
        var s = Math.Clamp(size, 1, 200);
        var ordered = sortBy switch
        {
            "status" => q.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt),
            "duration" => q.OrderByDescending(x => x.DurationMs),
            _ => q.OrderByDescending(x => x.CreatedAt),
        };
        var rows = await ordered.Skip((p - 1) * s).Take(s)
            .Select(x => new { x.AutomationExecutionId, x.AutomationCaseId, AutomationCode = x.AutomationCase.AutomationCode, TestCaseCode = x.AutomationCase.TestCase.TestCaseCode, TestCaseTitle = x.AutomationCase.TestCase.Title, x.AutomationVersionId, VersionNo = x.AutomationVersion.VersionNo, x.TestExecutionId, x.DefectId, x.TargetApp, x.AgentId, AgentCode = x.Agent != null ? x.Agent.AgentCode : null, x.BuildId, BuildNumber = x.Build.BuildNumber, x.EnvironmentId, EnvironmentName = x.Environment.EnvironmentName, x.JobId, x.Status, x.StartedAt, x.CompletedAt, x.DurationMs, x.FailureType, x.ErrorCode, x.ErrorMessage, x.ClassifiedFailureType, x.ClassifiedRecommendation, x.RetryOfExecutionId, x.RetryCount })
            .ToListAsync(ct);
        var items = rows.Select(r => new AutomationExecutionDto(r.AutomationExecutionId, r.AutomationCaseId, r.AutomationCode, r.TestCaseCode, r.TestCaseTitle, r.AutomationVersionId, r.VersionNo, r.TestExecutionId, r.DefectId, r.TargetApp, r.AgentId, r.AgentCode, r.BuildId, r.BuildNumber, r.EnvironmentId, r.EnvironmentName, r.JobId, r.Status, r.StartedAt, r.CompletedAt, r.DurationMs, r.FailureType, r.ErrorCode, r.ErrorMessage, [], [], r.ClassifiedFailureType, r.ClassifiedRecommendation, r.RetryOfExecutionId, r.RetryCount)).ToList();
        return new PagedResult<AutomationExecutionDto>(total, items);
    }

    private sealed record ExecTrendRow(Guid AutomationExecutionId, Guid AutomationCaseId, string Status, DateTime CreatedAt, Guid BuildId, string BuildNumber, Guid ReleaseId, string ReleaseCode);

    public async Task<ExecutionTrendDto> GetExecutionTrendAsync(Guid projectId, string? groupBy, DateTime? from, DateTime? to, Guid? releaseId, CancellationToken ct)
    {
        var effectiveFrom = from ?? DateTime.UtcNow.AddDays(-90);
        var effectiveTo = to ?? DateTime.UtcNow;
        var mode = groupBy is "build" or "release" ? groupBy : "day";
        var q = db.AutomationExecutions.AsNoTracking()
            .Where(x => x.AutomationCase.TestCase.ProjectId == projectId && (x.Status == "Passed" || x.Status == "Failed") && x.CreatedAt >= effectiveFrom && x.CreatedAt <= effectiveTo);
        if (releaseId.HasValue) q = q.Where(x => x.Build.ReleaseId == releaseId.Value);
        var rows = await q.OrderBy(x => x.AutomationCaseId).ThenBy(x => x.CreatedAt)
            .Select(x => new ExecTrendRow(x.AutomationExecutionId, x.AutomationCaseId, x.Status, x.CreatedAt, x.BuildId, x.Build.BuildNumber, x.Build.ReleaseId, x.Build.Release.ReleaseCode))
            .ToListAsync(ct);

        // AUT-P2-003: "flaky" reuses GetFlakyCandidatesAsync's status-transition concept — a case whose status here
        // differs from its immediately preceding execution (within this fetched window) is a flake, attributed to
        // the bucket of this (the later) execution, since that's the run where the flip was actually observed.
        var flips = new HashSet<Guid>();
        Guid? prevCase = null;
        string? prevStatus = null;
        foreach (var r in rows)
        {
            if (prevCase == r.AutomationCaseId && prevStatus is not null && prevStatus != r.Status) flips.Add(r.AutomationExecutionId);
            prevCase = r.AutomationCaseId;
            prevStatus = r.Status;
        }

        var buckets = rows
            .GroupBy(r => mode switch { "build" => r.BuildId.ToString(), "release" => r.ReleaseId.ToString(), _ => r.CreatedAt.Date.ToString("yyyy-MM-dd") })
            .OrderBy(g => g.Min(x => x.CreatedAt))
            .Select(g =>
            {
                var first = g.First();
                var label = mode switch { "build" => first.BuildNumber, "release" => first.ReleaseCode, _ => first.CreatedAt.Date.ToString("dd MMM") };
                return new ExecutionTrendBucketDto(g.Key, label, g.Count(x => x.Status == "Passed"), g.Count(x => x.Status == "Failed"),
                    g.Where(x => flips.Contains(x.AutomationExecutionId)).Select(x => x.AutomationCaseId).Distinct().Count(), g.Count());
            })
            .ToList();

        return new ExecutionTrendDto(mode, buckets);
    }

    public async Task<IReadOnlyList<AutomationExecutionDto>> ListFailedExecutionsAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, int take, CancellationToken ct)
    {
        var q = db.AutomationExecutions.AsNoTracking().Where(x => x.AutomationCase.TestCase.ProjectId == projectId && x.Status == "Failed");
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);
        if (buildId.HasValue) q = q.Where(x => x.BuildId == buildId.Value);
        if (agentId.HasValue) q = q.Where(x => x.AgentId == agentId.Value);
        if (!string.IsNullOrWhiteSpace(failureType)) q = q.Where(x => x.ClassifiedFailureType == failureType);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Take(take)
            .Select(x => new { x.AutomationExecutionId, x.AutomationCaseId, AutomationCode = x.AutomationCase.AutomationCode, TestCaseCode = x.AutomationCase.TestCase.TestCaseCode, TestCaseTitle = x.AutomationCase.TestCase.Title, x.AutomationVersionId, VersionNo = x.AutomationVersion.VersionNo, x.TestExecutionId, x.DefectId, x.TargetApp, x.AgentId, AgentCode = x.Agent != null ? x.Agent.AgentCode : null, x.BuildId, BuildNumber = x.Build.BuildNumber, x.EnvironmentId, EnvironmentName = x.Environment.EnvironmentName, x.JobId, x.Status, x.StartedAt, x.CompletedAt, x.DurationMs, x.FailureType, x.ErrorCode, x.ErrorMessage, x.ClassifiedFailureType, x.ClassifiedRecommendation, x.RetryOfExecutionId, x.RetryCount })
            .ToListAsync(ct);
        return rows.Select(r => new AutomationExecutionDto(r.AutomationExecutionId, r.AutomationCaseId, r.AutomationCode, r.TestCaseCode, r.TestCaseTitle, r.AutomationVersionId, r.VersionNo, r.TestExecutionId, r.DefectId, r.TargetApp, r.AgentId, r.AgentCode, r.BuildId, r.BuildNumber, r.EnvironmentId, r.EnvironmentName, r.JobId, r.Status, r.StartedAt, r.CompletedAt, r.DurationMs, r.FailureType, r.ErrorCode, r.ErrorMessage, [], [], r.ClassifiedFailureType, r.ClassifiedRecommendation, r.RetryOfExecutionId, r.RetryCount)).ToList();
    }

    public async Task<FailureBreakdownDto> GetFailureBreakdownAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, CancellationToken ct)
    {
        var q = db.AutomationExecutions.AsNoTracking().Where(x => x.AutomationCase.TestCase.ProjectId == projectId && x.Status == "Failed");
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);
        if (buildId.HasValue) q = q.Where(x => x.BuildId == buildId.Value);
        if (agentId.HasValue) q = q.Where(x => x.AgentId == agentId.Value);
        if (!string.IsNullOrWhiteSpace(failureType)) q = q.Where(x => x.ClassifiedFailureType == failureType);
        var rows = await q.Select(x => new { x.ClassifiedFailureType, BuildNumber = x.Build.BuildNumber, AgentCode = x.Agent != null ? x.Agent.AgentCode : "(unassigned)", AutomationCode = x.AutomationCase.AutomationCode }).ToListAsync(ct);
        return new FailureBreakdownDto(
            rows.Count,
            rows.GroupBy(r => r.ClassifiedFailureType ?? "Unclassified").Select(g => new CountByKeyDto(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList(),
            rows.GroupBy(r => r.BuildNumber).Select(g => new CountByKeyDto(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList(),
            rows.GroupBy(r => r.AgentCode).Select(g => new CountByKeyDto(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList(),
            rows.GroupBy(r => r.AutomationCode).Select(g => new CountByKeyDto(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(10).ToList());
    }

    public Task AddStepResultAsync(AutomationStepResult entity, CancellationToken ct) => db.AutomationStepResults.AddAsync(entity, ct).AsTask();

    public async Task<AutomationDashboardDto> GetDashboardAsync(Guid projectId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var totalTestCases = await db.TestCases.AsNoTracking().CountAsync(x => x.ProjectId == projectId && !x.IsDeleted, ct);
        var candidates = await db.TestCases.AsNoTracking().CountAsync(x => x.ProjectId == projectId && !x.IsDeleted && x.AutomationCandidate, ct);
        var autoCases = await db.AutomationCases.AsNoTracking().CountAsync(x => !x.IsDeleted && x.TestCase.ProjectId == projectId, ct);
        var ready = await db.AutomationCases.AsNoTracking().CountAsync(x => !x.IsDeleted && x.TestCase.ProjectId == projectId && x.Status == "Ready", ct);
        var maintenance = await db.AutomationCases.AsNoTracking().CountAsync(x => !x.IsDeleted && x.TestCase.ProjectId == projectId && x.Status == "MaintenanceRequired", ct);
        var needsReview = await db.AutomationCases.AsNoTracking().CountAsync(x => !x.IsDeleted && x.TestCase.ProjectId == projectId && x.Status == "NeedsReview", ct);
        var inProgress = await db.AutomationCases.AsNoTracking().CountAsync(x => !x.IsDeleted && x.TestCase.ProjectId == projectId && (x.Status == "Draft" || x.Status == "NeedsReview" || x.Status == "Validated" || x.Status == "Approved"), ct);
        var running = await db.AutomationExecutions.AsNoTracking().CountAsync(x => x.AutomationCase.TestCase.ProjectId == projectId && x.Status == "Running", ct);
        var passToday = await db.AutomationExecutions.AsNoTracking().CountAsync(x => x.AutomationCase.TestCase.ProjectId == projectId && x.Status == "Passed" && x.CompletedAt != null && x.CompletedAt.Value.Date == today, ct);
        var failToday = await db.AutomationExecutions.AsNoTracking().CountAsync(x => x.AutomationCase.TestCase.ProjectId == projectId && x.Status == "Failed" && x.CompletedAt != null && x.CompletedAt.Value.Date == today, ct);
        var avgDuration = await db.AutomationExecutions.AsNoTracking().Where(x => x.AutomationCase.TestCase.ProjectId == projectId && x.DurationMs.HasValue && (x.Status == "Passed" || x.Status == "Failed")).Select(x => (double?)x.DurationMs).AverageAsync(ct);
        var now = DateTime.UtcNow;
        var agents = await db.AutomationAgents.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync(ct);
        var agentsOnline = agents.Count(x => x.IsEnabled && now - x.LastHeartbeatAt <= TimeSpan.FromSeconds(60));
        var readyCoverage = totalTestCases == 0 ? 0 : Math.Round(ready * 100m / totalTestCases, 1);
        var candidateCoverage = totalTestCases == 0 ? 0 : Math.Round(candidates * 100m / totalTestCases, 1);
        return new AutomationDashboardDto(totalTestCases, candidates, autoCases, ready, maintenance, needsReview, inProgress, running, passToday, failToday, avgDuration is null ? (long?)null : (long)Math.Round(avgDuration.Value), agentsOnline, agents.Count, readyCoverage, candidateCoverage);
    }

    public Task<AutomationStepResult?> FindStepResultAsync(Guid stepResultId, Guid executionId, CancellationToken ct)
        => db.AutomationStepResults.SingleOrDefaultAsync(x => x.AutomationStepResultId == stepResultId && x.AutomationExecutionId == executionId, ct);

    public async Task AttachStepEvidenceAsync(Guid executionId, int stepNo, string path, CancellationToken ct)
    {
        var result = await db.AutomationStepResults.SingleOrDefaultAsync(x => x.AutomationExecutionId == executionId && x.StepNo == stepNo, ct);
        if (result is null) throw new InvalidOperationException("Step result not found.");
        result.AttachEvidence(path);
        await db.SaveChangesAsync(ct);
    }

    public Task AddEvidenceAsync(AutomationEvidence entity, CancellationToken ct) => db.AutomationEvidences.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationEvidenceDto>> ListEvidenceAsync(Guid executionId, CancellationToken ct)
        => await db.AutomationEvidences.AsNoTracking().Where(x => x.AutomationExecutionId == executionId).OrderBy(x => x.CapturedAt)
            .Select(x => new AutomationEvidenceDto(x.AutomationEvidenceId, x.StepNo, x.EvidenceType, x.FilePath, x.CapturedBy, x.CapturedAt)).ToListAsync(ct);

    public Task<AutomationEvidence?> FindEvidenceAsync(Guid evidenceId, Guid executionId, CancellationToken ct)
        => db.AutomationEvidences.SingleOrDefaultAsync(x => x.AutomationEvidenceId == evidenceId && x.AutomationExecutionId == executionId, ct);

    public async Task<IReadOnlyList<AutomationObjectVerificationDto>> ListVerificationsAsync(Guid projectId, Guid? objectId, CancellationToken ct)
    {
        var q = db.AutomationObjectVerifications.AsNoTracking().Where(x => x.Object.ProjectId == projectId);
        if (objectId.HasValue) q = q.Where(x => x.AutomationObjectId == objectId.Value);
        return await q.OrderByDescending(x => x.RequestedAt)
            .Select(x => new AutomationObjectVerificationDto(x.AutomationObjectVerificationId, x.AutomationObjectId, x.Object.ObjectCode, x.Object.ScreenCode, x.Object.AutomationId, x.Object.ControlType, x.ActualAutomationId, x.ActualControlType, x.Status, x.AssignedAgentId, x.AssignedAgent != null ? x.AssignedAgent.AgentCode : null, x.RequestedAt, x.CompletedAt, x.Message))
            .ToListAsync(ct);
    }

    public Task AddVerificationsAsync(IReadOnlyList<AutomationObjectVerification> items, CancellationToken ct) => db.AutomationObjectVerifications.AddRangeAsync(items, ct);

    public Task<AutomationObjectVerification?> FindVerificationAsync(Guid id, CancellationToken ct)
        => db.AutomationObjectVerifications.SingleOrDefaultAsync(x => x.AutomationObjectVerificationId == id, ct);

    public async Task<VerificationBatchPackageDto?> ClaimVerificationBatchAsync(string agentCode, CancellationToken ct)
    {
        var agent = await db.AutomationAgents.SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);
        if (agent is null || !agent.IsEnabled || agent.IsDeleted) return null;
        // Serializable, same as ClaimNextJobAsync: without this, two agents polling concurrently can both read the
        // same "Pending" rows before either commits and both end up claiming (and reporting) the same verification.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var pending = await db.AutomationObjectVerifications.Include(x => x.Object)
            .Where(x => x.Status == "Pending" && (x.RequestedAgentId == null || x.RequestedAgentId == agent.AgentId))
            .OrderBy(x => x.RequestedAt).Take(100).ToListAsync(ct);
        if (pending.Count == 0) { if (transaction is not null) await transaction.CommitAsync(ct); return null; }
        foreach (var item in pending) item.Assign(agent.AgentId);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        var dtoItems = pending.Select(x => new VerificationObjectItemDto(x.AutomationObjectVerificationId, x.Object.ObjectCode, x.Object.ApplicationCode, x.Object.ScreenCode, x.Object.AutomationId, x.Object.ControlType)).ToList();
        return new VerificationBatchPackageDto(dtoItems);
    }

    public async Task<RetryPolicyDto> GetRetryPolicyAsync(CancellationToken ct)
    {
        var settings = await db.AutomationRetryPolicySettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Domain.Automation.AutomationRetryPolicySettings.SingletonId, ct);
        settings ??= new Domain.Automation.AutomationRetryPolicySettings(2, 30, true);
        return new RetryPolicyDto(settings.MaxAttempts, settings.BackoffSeconds, settings.Enabled, settings.UpdatedAt);
    }

    public async Task UpdateRetryPolicyAsync(int maxAttempts, int backoffSeconds, bool enabled, Guid? userId, CancellationToken ct)
    {
        var settings = await db.AutomationRetryPolicySettings.SingleOrDefaultAsync(x => x.Id == Domain.Automation.AutomationRetryPolicySettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new Domain.Automation.AutomationRetryPolicySettings(maxAttempts, backoffSeconds, enabled);
            await db.AutomationRetryPolicySettings.AddAsync(settings, ct);
        }
        else settings.Update(maxAttempts, backoffSeconds, enabled, userId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetUnsafeActionCodesAsync(IEnumerable<string> actionCodes, CancellationToken ct)
    {
        var codes = actionCodes.ToList();
        if (codes.Count == 0) return [];
        return await db.AutomationActions.AsNoTracking().Where(a => codes.Contains(a.ActionCode) && a.RetrySafety != "Safe").Select(a => a.ActionCode).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FlakyCandidateDto>> GetFlakyCandidatesAsync(Guid projectId, int lookback, CancellationToken ct)
    {
        var cases = await db.AutomationCases.AsNoTracking().Where(x => !x.IsDeleted && x.TestCase.ProjectId == projectId && !x.IsQuarantined)
            .Select(x => new { x.AutomationCaseId, x.AutomationCode }).ToListAsync(ct);
        var results = new List<FlakyCandidateDto>();
        foreach (var c in cases)
        {
            var recent = await db.AutomationExecutions.AsNoTracking()
                .Where(x => x.AutomationCaseId == c.AutomationCaseId && (x.Status == "Passed" || x.Status == "Failed"))
                .OrderByDescending(x => x.CreatedAt).Take(lookback)
                .Select(x => new { x.Status, x.CreatedAt }).ToListAsync(ct);
            if (recent.Count < 3) continue;
            var transitions = 0;
            for (var i = 0; i < recent.Count - 1; i++) if (recent[i].Status != recent[i + 1].Status) transitions++;
            if (transitions >= 2) results.Add(new FlakyCandidateDto(c.AutomationCaseId, c.AutomationCode, recent.Count, transitions, recent[0].CreatedAt));
        }
        return results.OrderByDescending(x => x.Transitions).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class AutomationCaseConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationCase>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationCase> b)
    {
        b.ToTable("AutomationCases");
        b.HasKey(x => x.AutomationCaseId);
        b.Property(x => x.AutomationCode).HasMaxLength(60);
        b.Property(x => x.AutomationType).HasMaxLength(30);
        b.Property(x => x.Status).HasMaxLength(30);
        b.Property(x => x.MaintenanceReason).HasMaxLength(2000);
        b.Property(x => x.QuarantineReason).HasMaxLength(2000);
        b.HasOne(x => x.TestCase).WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.AutomationCaseId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        b.HasIndex(x => x.AutomationCode).IsUnique();
        b.HasIndex(x => new { x.TestCaseId, x.IsDeleted });
    }
}

public sealed class AutomationVersionConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationVersion>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationVersion> b)
    {
        b.ToTable("AutomationVersions");
        b.HasKey(x => x.AutomationVersionId);
        b.Property(x => x.DslVersion).HasMaxLength(10);
        b.Property(x => x.DslJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ValidationStatus).HasMaxLength(20);
        b.Property(x => x.ValidationErrors).HasColumnType("nvarchar(max)");
        b.Property(x => x.AiProvider).HasMaxLength(50);
        b.Property(x => x.AiModel).HasMaxLength(100);
        b.HasIndex(x => new { x.AutomationCaseId, x.VersionNo });
    }
}

public sealed class AutomationActionConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationAction>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationAction> b)
    {
        b.ToTable("AutomationActions");
        b.HasKey(x => x.AutomationActionId);
        b.Property(x => x.ActionCode).HasMaxLength(60);
        b.Property(x => x.ActionName).HasMaxLength(120);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.ParameterSchemaJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.HandlerKey).HasMaxLength(60);
        b.Property(x => x.MinimumAgentVersion).HasMaxLength(20);
        b.Property(x => x.RetrySafety).HasMaxLength(20);
        b.HasIndex(x => x.ActionCode).IsUnique();
    }
}

public sealed class AutomationObjectConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationObject>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationObject> b)
    {
        b.ToTable("AutomationObjects");
        b.HasKey(x => x.AutomationObjectId);
        b.Property(x => x.ApplicationCode).HasMaxLength(30);
        b.Property(x => x.ScreenCode).HasMaxLength(80);
        b.Property(x => x.ObjectCode).HasMaxLength(120);
        b.Property(x => x.ObjectName).HasMaxLength(160);
        b.Property(x => x.ControlType).HasMaxLength(40);
        b.Property(x => x.AutomationId).HasMaxLength(200);
        b.Property(x => x.SelectorJson).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.ProjectId, x.ApplicationCode, x.ScreenCode, x.ObjectCode });
    }
}

public sealed class AutomationAgentConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationAgent>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationAgent> b)
    {
        b.ToTable("AutomationAgents");
        b.HasKey(x => x.AgentId);
        b.Property(x => x.AgentCode).HasMaxLength(60);
        b.Property(x => x.MachineName).HasMaxLength(120);
        b.Property(x => x.AgentVersion).HasMaxLength(30);
        b.Property(x => x.OperatingSystem).HasMaxLength(60);
        b.Property(x => x.Architecture).HasMaxLength(20);
        b.Property(x => x.Status).HasMaxLength(20);
        b.HasMany(x => x.Capabilities).WithOne(x => x.Agent).HasForeignKey(x => x.AgentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        b.HasIndex(x => x.AgentCode).IsUnique();
        b.HasIndex(x => new { x.Status, x.LastHeartbeatAt });
    }
}

public sealed class AutomationAgentCapabilityConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationAgentCapability>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationAgentCapability> b)
    {
        b.ToTable("AutomationAgentCapabilities");
        b.HasKey(x => new { x.AgentId, x.CapabilityCode });
        b.Property(x => x.CapabilityCode).HasMaxLength(40);
        b.Property(x => x.CapabilityVersion).HasMaxLength(20);
    }
}

public sealed class AutomationExecutionConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationExecution>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationExecution> b)
    {
        b.ToTable("AutomationExecutions");
        b.HasKey(x => x.AutomationExecutionId);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.FailureType).HasMaxLength(40);
        b.Property(x => x.ErrorCode).HasMaxLength(40);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.ClassifiedFailureType).HasMaxLength(40);
        b.Property(x => x.ClassifiedRecommendation).HasMaxLength(60);
        b.Property(x => x.RequestedBy).HasMaxLength(120);
        b.HasOne(x => x.AutomationCase).WithMany().HasForeignKey(x => x.AutomationCaseId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasOne(x => x.AutomationVersion).WithMany().HasForeignKey(x => x.AutomationVersionId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasOne(x => x.Build).WithMany().HasForeignKey(x => x.BuildId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasOne(x => x.Environment).WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasMany(x => x.StepResults).WithOne(x => x.Execution).HasForeignKey(x => x.AutomationExecutionId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AutomationCaseId, x.CreatedAt });
        b.HasIndex(x => x.ClassifiedFailureType);
        b.HasIndex(x => x.RetryOfExecutionId);
    }
}

public sealed class AutomationStepResultConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationStepResult>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationStepResult> b)
    {
        b.ToTable("AutomationStepResults");
        b.HasKey(x => x.AutomationStepResultId);
        b.Property(x => x.ActionCode).HasMaxLength(60);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.ActualResult).HasMaxLength(1000);
        b.Property(x => x.ErrorCode).HasMaxLength(40);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.EvidencePath).HasMaxLength(1000);
        b.HasIndex(x => new { x.AutomationExecutionId, x.StepNo });
    }
}

public sealed class AutomationJobConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationJob>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationJob> b)
    {
        b.ToTable("AutomationJobs");
        b.HasKey(x => x.JobId);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasOne(x => x.AutomationExecution).WithOne().HasForeignKey<AutomationJob>(x => x.AutomationExecutionId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasOne(x => x.AssignedAgent).WithMany().HasForeignKey(x => x.AssignedAgentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Status, x.Priority, x.QueuedAt });
    }
}

public sealed class AutomationEvidenceConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationEvidence>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationEvidence> b)
    {
        b.ToTable("AutomationEvidences");
        b.HasKey(x => x.AutomationEvidenceId);
        b.Property(x => x.EvidenceType).HasMaxLength(30);
        b.Property(x => x.FilePath).HasMaxLength(1000);
        b.Property(x => x.CapturedBy).HasMaxLength(100);
        b.HasOne(x => x.Execution).WithMany().HasForeignKey(x => x.AutomationExecutionId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AutomationExecutionId, x.EvidenceType });
    }
}

public sealed class AutomationObjectVerificationConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationObjectVerification>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationObjectVerification> b)
    {
        b.ToTable("AutomationObjectVerifications");
        b.HasKey(x => x.AutomationObjectVerificationId);
        b.Property(x => x.Status).HasMaxLength(30);
        b.Property(x => x.ActualControlType).HasMaxLength(40);
        b.Property(x => x.ActualAutomationId).HasMaxLength(200);
        b.Property(x => x.Message).HasMaxLength(2000);
        b.HasOne(x => x.Object).WithMany().HasForeignKey(x => x.AutomationObjectId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade);
        b.HasOne(x => x.AssignedAgent).WithMany().HasForeignKey(x => x.AssignedAgentId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AutomationObjectId, x.RequestedAt });
        b.HasIndex(x => x.Status);
    }
}

public sealed class AutomationRetryPolicySettingsConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<AutomationRetryPolicySettings>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AutomationRetryPolicySettings> b)
    {
        b.ToTable("AutomationRetryPolicySettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
    }
}
