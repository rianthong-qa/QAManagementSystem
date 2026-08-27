namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-P1-007: a configurable policy that automatically runs an <see cref="AutomationSuite"/> whenever a
/// new Build event happens in the project — <see cref="Pack"/> "Smoke" fires on every new Build (fast sanity check
/// on the newest code); "Regression" fires only when a Build is marked a Release Candidate (the heavier, slower
/// suite that only needs to run before an actual release, not on every build). Firing itself is best-effort and
/// happens in <c>AutomationBuildTriggerService.FireForBuildAsync</c>, called from <c>ReleaseService</c>.</summary>
public sealed class AutomationBuildTriggerPolicy
{
    private static readonly string[] AllowedPacks = ["Smoke", "Regression"];

    private AutomationBuildTriggerPolicy() { }

    public AutomationBuildTriggerPolicy(Guid projectId, Guid automationSuiteId, string pack, Guid environmentId, Guid? agentId, int priority, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (automationSuiteId == Guid.Empty) throw new ArgumentException("Automation suite is required.");
        AutomationBuildTriggerPolicyId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationSuiteId = automationSuiteId;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        SetTarget(pack, environmentId, agentId, priority);
    }

    public Guid AutomationBuildTriggerPolicyId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationSuiteId { get; private set; }
    /// <summary>"Smoke" (fires on every new Build) or "Regression" (fires only when a Build is marked Release Candidate).</summary>
    public string Pack { get; private set; } = "Smoke";
    public Guid EnvironmentId { get; private set; }
    public Guid? AgentId { get; private set; }
    public int Priority { get; private set; } = 5;
    public bool IsActive { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public AutomationSuite Suite { get; private set; } = null!;

    public void Update(Guid automationSuiteId, string pack, Guid environmentId, Guid? agentId, int priority, Guid? userId)
    {
        if (automationSuiteId == Guid.Empty) throw new ArgumentException("Automation suite is required.");
        AutomationSuiteId = automationSuiteId;
        SetTarget(pack, environmentId, agentId, priority);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void Activate(Guid? userId)
    {
        if (IsActive) throw new InvalidOperationException("Policy is already active.");
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void Deactivate(Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Policy is already inactive.");
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    private void SetTarget(string pack, Guid environmentId, Guid? agentId, int priority)
    {
        if (!AllowedPacks.Contains(pack)) throw new ArgumentException("Pack must be Smoke or Regression.");
        if (environmentId == Guid.Empty) throw new ArgumentException("Environment is required.");
        Pack = pack;
        EnvironmentId = environmentId;
        AgentId = agentId;
        Priority = priority is >= 1 and <= 10 ? priority : 5;
    }
}

/// <summary>AUT-P1-007: an audit-trail entry for one time a Build event tried to fire an
/// <see cref="AutomationBuildTriggerPolicy"/> — recorded regardless of outcome, same shape and reasoning as
/// <see cref="AutomationScheduleRun"/> (AUT-P1-006) but keyed off the Build that caused it instead of a poll tick.</summary>
public sealed class AutomationBuildTriggerRun
{
    private AutomationBuildTriggerRun() { }
    public AutomationBuildTriggerRun(Guid automationBuildTriggerPolicyId, Guid buildId, DateTime firedAtUtc, string status, int executionsCreated, int skippedCount, string? errorMessage)
    {
        AutomationBuildTriggerRunId = Guid.NewGuid();
        AutomationBuildTriggerPolicyId = automationBuildTriggerPolicyId;
        BuildId = buildId;
        FiredAtUtc = firedAtUtc;
        Status = status;
        ExecutionsCreated = executionsCreated;
        SkippedCount = skippedCount;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
    }
    public Guid AutomationBuildTriggerRunId { get; private set; }
    public Guid AutomationBuildTriggerPolicyId { get; private set; }
    public Guid BuildId { get; private set; }
    public DateTime FiredAtUtc { get; private set; }
    /// <summary>"Succeeded" (created at least one execution) / "NoReadyCases" (ran, nothing to create) / "Failed" (the run threw — suite closed/deleted, etc.).</summary>
    public string Status { get; private set; } = string.Empty;
    public int ExecutionsCreated { get; private set; }
    public int SkippedCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public AutomationBuildTriggerPolicy Policy { get; private set; } = null!;
}
