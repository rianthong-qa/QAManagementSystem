using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationDataSeedScriptDto(Guid AutomationDataSeedScriptId, Guid ProjectId, string Name, string? Description, string ScriptType, string DbKind, string SqlScript,
    bool IsActive, string ApprovalStatus, Guid? ReviewedBy, DateTime? ReviewedAt, string? RejectionReason, Guid? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record AutomationDataSeedScriptListDto(Guid AutomationDataSeedScriptId, Guid ProjectId, string Name, string? Description, string ScriptType, string DbKind, bool IsActive, string ApprovalStatus, DateTime CreatedAt);

public sealed record CreateSeedScriptRequest(string Name, string? Description, string ScriptType, string DbKind, string SqlScript);
public sealed record UpdateSeedScriptRequest(string Name, string? Description, string ScriptType, string DbKind, string SqlScript);
/// <summary>AUT-DATA-005: reason is optional but strongly encouraged in the UI — recorded verbatim for audit.</summary>
public sealed record RejectSeedScriptRequest(string? Reason);

public sealed record AutomationDataSeedRunDto(Guid AutomationDataSeedRunId, Guid ProjectId, Guid AutomationDataSeedScriptId, string ScriptName, string ScriptType, Guid EnvironmentId, string EnvironmentName,
    Guid BuildId, string BuildNumber, string Status, Guid? AgentId, string? AgentCode, int? RowsAffected, string? ErrorMessage,
    Guid? RequestedBy, DateTime RequestedAt, DateTime? StartedAt, DateTime? CompletedAt);

public sealed record RequestSeedRunRequest(Guid AutomationDataSeedScriptId, Guid EnvironmentId, Guid BuildId);

/// <summary>AUT-DATA-003: handed to the agent that claimed a seed run. Carries the raw SQL text and which dialect it
/// is written in — the agent compares <see cref="DbKind"/> against its own local <c>DbProfile.Kind</c> before
/// attempting anything, and fails fast with a clear message on a mismatch instead of running the wrong dialect's SQL
/// against the wrong provider.</summary>
public sealed record ClaimSeedRunPackageDto(Guid AutomationDataSeedRunId, string ScriptName, string DbKind, string SqlScript);

public sealed record ClaimSeedRunRequest(string AgentCode, string AgentVersion);
public sealed record CompleteSeedRunRequest(string Status, int? RowsAffected, string? ErrorMessage);

public interface IAutomationDataSeedRepository
{
    Task<IReadOnlyList<AutomationDataSeedScriptListDto>> ListScriptsAsync(Guid projectId, string? scriptType, bool? isActive, CancellationToken ct);
    Task<AutomationDataSeedScriptDto?> GetScriptAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationDataSeedScript?> FindScriptAsync(Guid id, Guid projectId, CancellationToken ct);
    Task AddScriptAsync(AutomationDataSeedScript entity, CancellationToken ct);

    Task<IReadOnlyList<AutomationDataSeedRunDto>> ListRunsAsync(Guid projectId, Guid? scriptId, CancellationToken ct);
    Task<AutomationDataSeedRunDto?> GetRunAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationDataSeedRunDto?> GetRunByIdAsync(Guid id, CancellationToken ct);
    Task AddRunAsync(AutomationDataSeedRun entity, CancellationToken ct);

    /// <summary>Atomically (Serializable transaction, same pattern used throughout this module) first reclaims any
    /// stale "Running" runs (AUT-DATA-004 — see <see cref="AutomationDataSeedRun.ReclaimIfStale"/>), then claims the
    /// oldest still-"Requested" run — one at a time, same rationale as snapshots. No agent-affinity restriction
    /// (unlike restore claims): any enabled agent can run a script against the environment it manages, there is no
    /// explicit Environment→Agent mapping in the Hub to filter on (same known scope limitation already recorded for
    /// job claiming in AUT-TEST-006 — capability/target-based routing isn't implemented).</summary>
    Task<ClaimSeedRunPackageDto?> ClaimNextSeedRunRequestAsync(string agentCode, CancellationToken ct);
    Task<AutomationDataSeedRun?> FindRunAsync(Guid id, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-DATA-003/AUT-DATA-004: manage reusable Seed/Cleanup scripts and request/track their execution
/// against an Environment.</summary>
public sealed class AutomationDataSeedService(IAutomationDataSeedRepository repository, IAutomationEnvironmentDataProfileRepository profiles)
{
    public Task<IReadOnlyList<AutomationDataSeedScriptListDto>> ListScriptsAsync(Guid projectId, string? scriptType, bool? isActive, CancellationToken ct)
        => repository.ListScriptsAsync(projectId, scriptType, isActive, ct);

    public async Task<AutomationDataSeedScriptDto> GetScriptAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");

    public async Task<AutomationDataSeedScriptDto> CreateScriptAsync(Guid projectId, CreateSeedScriptRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = new AutomationDataSeedScript(projectId, r.Name, r.Description, r.ScriptType, r.DbKind, r.SqlScript, userId);
        await repository.AddScriptAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScriptAsync(entity.AutomationDataSeedScriptId, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
    }

    public async Task<AutomationDataSeedScriptDto> UpdateScriptAsync(Guid id, Guid projectId, UpdateSeedScriptRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
        entity.Update(r.Name, r.Description, r.ScriptType, r.DbKind, r.SqlScript, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
    }

    public async Task<AutomationDataSeedScriptDto> SetScriptActiveAsync(Guid id, Guid projectId, bool active, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
        entity.SetActive(active, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
    }

    /// <summary>AUT-DATA-005.</summary>
    public async Task<AutomationDataSeedScriptDto> ApproveScriptAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
        entity.Approve(userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
    }

    /// <summary>AUT-DATA-005.</summary>
    public async Task<AutomationDataSeedScriptDto> RejectScriptAsync(Guid id, Guid projectId, RejectSeedScriptRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
        entity.Reject(userId, r.Reason);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScriptAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
    }

    public Task<IReadOnlyList<AutomationDataSeedRunDto>> ListRunsAsync(Guid projectId, Guid? scriptId, CancellationToken ct)
        => repository.ListRunsAsync(projectId, scriptId, ct);

    public async Task<AutomationDataSeedRunDto> GetRunAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetRunAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Seed run not found.");

    public async Task<AutomationDataSeedRunDto> RequestRunAsync(Guid projectId, RequestSeedRunRequest r, Guid? userId, CancellationToken ct)
    {
        var script = await repository.FindScriptAsync(r.AutomationDataSeedScriptId, projectId, ct) ?? throw new EntityNotFoundException("Seed script not found.");
        if (!script.IsActive) throw new ArgumentException("Cannot run an inactive seed script. Activate it first.");
        // AUT-DATA-005: master data prep ahead of a POS scenario must be reviewed before it can run — Seed/Cleanup
        // scripts are never gated on ApprovalStatus (see class summary on AutomationDataSeedScript).
        if (script.ScriptType == "MasterData" && script.ApprovalStatus != "Approved")
            throw new ArgumentException("Master data script must be approved before it can be run.");
        // AUT-DATA-006: catch an obvious dialect mismatch before creating the request, not after an agent claims it
        // and fails partway through. Skipped entirely when the Environment has no data profile yet (opt-in check).
        var profiledDbKind = await profiles.GetDataProfileDbKindForEnvironmentAsync(r.EnvironmentId, ct);
        if (profiledDbKind is not null && profiledDbKind != script.DbKind)
            throw new ArgumentException($"Script is written for {script.DbKind} but this Environment's data profile is {profiledDbKind}.");
        var entity = new AutomationDataSeedRun(projectId, script.AutomationDataSeedScriptId, r.EnvironmentId, r.BuildId, userId);
        await repository.AddRunAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetRunAsync(entity.AutomationDataSeedRunId, projectId, ct) ?? throw new EntityNotFoundException("Seed run not found.");
    }

    public Task<ClaimSeedRunPackageDto?> ClaimNextAsync(string agentCode, CancellationToken ct) => repository.ClaimNextSeedRunRequestAsync(agentCode, ct);

    /// <summary>Agent-facing, idempotent against a late/duplicate report — same pattern as everywhere else.</summary>
    public async Task<AutomationDataSeedRunDto> CompleteRunAsync(Guid id, CompleteSeedRunRequest r, CancellationToken ct)
    {
        var entity = await repository.FindRunAsync(id, ct) ?? throw new EntityNotFoundException("Seed run not found.");
        if (entity.Status != "Running")
            return await repository.GetRunByIdAsync(id, ct) ?? throw new EntityNotFoundException("Seed run not found.");
        if (r.Status == "Succeeded") entity.Complete(r.RowsAffected ?? 0);
        else entity.Fail(r.ErrorMessage ?? "Seed run failed.");
        await repository.SaveChangesAsync(ct);
        return await repository.GetRunByIdAsync(id, ct) ?? throw new EntityNotFoundException("Seed run not found.");
    }
}
