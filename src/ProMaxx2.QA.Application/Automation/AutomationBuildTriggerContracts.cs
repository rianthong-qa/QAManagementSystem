using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationBuildTriggerPolicyDto(Guid AutomationBuildTriggerPolicyId, Guid ProjectId, Guid AutomationSuiteId, string SuiteCode, string SuiteName, string Pack,
    Guid EnvironmentId, string EnvironmentName, Guid? AgentId, string? AgentCode, int Priority, bool IsActive, Guid? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record CreateAutomationBuildTriggerPolicyRequest(Guid AutomationSuiteId, string Pack, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record UpdateAutomationBuildTriggerPolicyRequest(Guid AutomationSuiteId, string Pack, Guid EnvironmentId, Guid? AgentId, int Priority);

public sealed record AutomationBuildTriggerRunDto(Guid AutomationBuildTriggerRunId, Guid AutomationBuildTriggerPolicyId, Guid BuildId, string BuildNumber, DateTime FiredAtUtc, string Status, int ExecutionsCreated, int SkippedCount, string? ErrorMessage);

public interface IAutomationBuildTriggerRepository
{
    Task<IReadOnlyList<AutomationBuildTriggerPolicyDto>> ListPoliciesAsync(Guid projectId, CancellationToken ct);
    Task<AutomationBuildTriggerPolicyDto?> GetPolicyAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationBuildTriggerPolicy?> FindPolicyAsync(Guid id, Guid projectId, CancellationToken ct);
    Task AddPolicyAsync(AutomationBuildTriggerPolicy entity, CancellationToken ct);

    /// <summary>AUT-P1-007: every active policy for <paramref name="projectId"/> whose <see cref="AutomationBuildTriggerPolicy.Pack"/>
    /// matches — the set a Build event of that pack should fire.</summary>
    Task<IReadOnlyList<AutomationBuildTriggerPolicy>> ListActivePoliciesForPackAsync(Guid projectId, string pack, CancellationToken ct);
    Task AddTriggerRunAsync(AutomationBuildTriggerRun entity, CancellationToken ct);
    Task<IReadOnlyList<AutomationBuildTriggerRunDto>> ListTriggerRunsAsync(Guid policyId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-P1-007: create/edit/activate/deactivate a Build Trigger policy, and fire policies when a Build event
/// happens. Called from <c>ReleaseService</c> — "Smoke" policies fire on every new Build, "Regression" policies fire
/// only when a Build is marked a Release Candidate. Firing is best-effort: one policy's suite being closed/deleted
/// must never fail the Build creation/RC-marking request that triggered it, so every failure is caught and recorded
/// as an audit row (<see cref="AutomationBuildTriggerRun"/>) instead of thrown.</summary>
public sealed class AutomationBuildTriggerService(IAutomationBuildTriggerRepository repository, IAutomationSuiteRepository suites, AutomationAgentService agentService)
{
    public Task<IReadOnlyList<AutomationBuildTriggerPolicyDto>> ListAsync(Guid projectId, CancellationToken ct)
        => repository.ListPoliciesAsync(projectId, ct);

    public async Task<AutomationBuildTriggerPolicyDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");

    public async Task<IReadOnlyList<AutomationBuildTriggerRunDto>> ListRunsAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        _ = await repository.FindPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
        return await repository.ListTriggerRunsAsync(id, ct);
    }

    public async Task<AutomationBuildTriggerPolicyDto> CreateAsync(Guid projectId, CreateAutomationBuildTriggerPolicyRequest r, Guid? userId, CancellationToken ct)
    {
        var suite = await suites.FindSuiteAsync(r.AutomationSuiteId, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        var entity = new AutomationBuildTriggerPolicy(projectId, suite.AutomationSuiteId, r.Pack, r.EnvironmentId, r.AgentId, r.Priority, userId);
        await repository.AddPolicyAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetPolicyAsync(entity.AutomationBuildTriggerPolicyId, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
    }

    public async Task<AutomationBuildTriggerPolicyDto> UpdateAsync(Guid id, Guid projectId, UpdateAutomationBuildTriggerPolicyRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
        if (entity.AutomationSuiteId != r.AutomationSuiteId)
            _ = await suites.FindSuiteAsync(r.AutomationSuiteId, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        entity.Update(r.AutomationSuiteId, r.Pack, r.EnvironmentId, r.AgentId, r.Priority, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
    }

    public async Task<AutomationBuildTriggerPolicyDto> ActivateAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
        entity.Activate(userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
    }

    public async Task<AutomationBuildTriggerPolicyDto> DeactivateAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
        entity.Deactivate(userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetPolicyAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Build trigger policy not found.");
    }

    /// <summary>Called from <c>ReleaseService</c> right after a Build event ("BuildCreated" → pack "Smoke",
    /// "BuildMarkedReleaseCandidate" → pack "Regression"). Fires every active policy for that pack in the project.</summary>
    public async Task FireForBuildAsync(Guid projectId, Guid buildId, string pack, CancellationToken ct)
    {
        var policies = await repository.ListActivePoliciesForPackAsync(projectId, pack, ct);
        foreach (var policy in policies)
        {
            AutomationBuildTriggerRun run;
            var firedAtUtc = DateTime.UtcNow;
            try
            {
                var result = await agentService.RunSuiteAsync(projectId, new RunSuiteRequest(policy.AutomationSuiteId, buildId, policy.EnvironmentId, policy.AgentId, policy.Priority), null, ct);
                run = new AutomationBuildTriggerRun(policy.AutomationBuildTriggerPolicyId, buildId, firedAtUtc, result.Created.Count > 0 ? "Succeeded" : "NoReadyCases", result.Created.Count, result.SkippedCodes.Count, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                run = new AutomationBuildTriggerRun(policy.AutomationBuildTriggerPolicyId, buildId, firedAtUtc, "Failed", 0, 0, ex.Message);
            }
            await repository.AddTriggerRunAsync(run, ct);
            await repository.SaveChangesAsync(ct);
        }
    }
}
