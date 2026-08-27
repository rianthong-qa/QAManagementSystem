using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationDbSnapshotDto(Guid AutomationDbSnapshotId, Guid ProjectId, Guid EnvironmentId, string EnvironmentName, Guid BuildId, string BuildNumber,
    string Status, string? DbKind, Guid? AgentId, string? AgentCode, string? SnapshotPath, string? Checksum, long? SizeBytes, string? ErrorMessage,
    Guid? RequestedBy, DateTime RequestedAt, DateTime? StartedAt, DateTime? CompletedAt);

public sealed record RequestSnapshotRequest(Guid EnvironmentId, Guid BuildId);

/// <summary>AUT-DATA-001: handed to the agent that claimed a snapshot request — just enough for it to name/log the
/// backup file sensibly. The agent already knows which DB to back up (and how) from its own local <c>DbProfile</c>,
/// same as DB assertions do; the Hub never sends connection details.</summary>
public sealed record ClaimSnapshotPackageDto(Guid AutomationDbSnapshotId, Guid EnvironmentId, string EnvironmentName, Guid BuildId, string BuildNumber);

public sealed record ClaimSnapshotRequest(string AgentCode, string AgentVersion);
public sealed record CompleteSnapshotRequest(string Status, string? DbKind, string? SnapshotPath, string? Checksum, long? SizeBytes, string? ErrorMessage);

public interface IAutomationDataSnapshotRepository
{
    Task<IReadOnlyList<AutomationDbSnapshotDto>> ListSnapshotsAsync(Guid projectId, Guid? environmentId, Guid? buildId, int take, CancellationToken ct);
    Task<AutomationDbSnapshotDto?> GetSnapshotAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<bool> EnvironmentExistsAsync(Guid environmentId, Guid projectId, CancellationToken ct);
    Task<bool> BuildExistsAsync(Guid buildId, CancellationToken ct);
    Task AddSnapshotAsync(AutomationDbSnapshot entity, CancellationToken ct);

    /// <summary>Atomically (Serializable transaction, same pattern as ClaimNextJobAsync) claims the oldest still-
    /// "Requested" snapshot — one at a time, deliberately: a DB backup is heavy/serial work, not something to hand
    /// out in a batch the way <c>ClaimVerificationBatchAsync</c> does.</summary>
    Task<ClaimSnapshotPackageDto?> ClaimNextSnapshotRequestAsync(string agentCode, CancellationToken ct);
    Task<AutomationDbSnapshot?> FindSnapshotAsync(Guid id, CancellationToken ct);
    Task<AutomationDbSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-DATA-001: request/track real DB backups of an Environment's physical database before a test run.
/// Actually performing the backup is entirely on the Windows Agent (see <c>DatabaseSnapshotService</c> in
/// agent/ProMaxx2.Automation.Core) — this service only orchestrates the request/claim/complete lifecycle, mirroring
/// the existing Job claim/complete pattern used for automation test execution.</summary>
public sealed class AutomationDataSnapshotService(IAutomationDataSnapshotRepository repository)
{
    public Task<IReadOnlyList<AutomationDbSnapshotDto>> ListAsync(Guid projectId, Guid? environmentId, Guid? buildId, int take, CancellationToken ct)
        => repository.ListSnapshotsAsync(projectId, environmentId, buildId, Math.Clamp(take, 1, 200), ct);

    public async Task<AutomationDbSnapshotDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetSnapshotAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Snapshot not found.");

    public async Task<AutomationDbSnapshotDto> RequestAsync(Guid projectId, RequestSnapshotRequest r, Guid? userId, CancellationToken ct)
    {
        if (!await repository.EnvironmentExistsAsync(r.EnvironmentId, projectId, ct)) throw new EntityNotFoundException("Environment not found.");
        if (!await repository.BuildExistsAsync(r.BuildId, ct)) throw new EntityNotFoundException("Build not found.");
        var entity = new AutomationDbSnapshot(projectId, r.EnvironmentId, r.BuildId, userId);
        await repository.AddSnapshotAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetSnapshotAsync(entity.AutomationDbSnapshotId, projectId, ct) ?? throw new EntityNotFoundException("Snapshot not found.");
    }

    public Task<ClaimSnapshotPackageDto?> ClaimNextAsync(string agentCode, CancellationToken ct) => repository.ClaimNextSnapshotRequestAsync(agentCode, ct);

    /// <summary>Agent-facing — no projectId, matching <c>CompleteExecutionAsync</c>'s pattern: the entity itself
    /// already knows which project it belongs to. Idempotent against a late/duplicate report: once the request has
    /// already reached a terminal state ("Succeeded"/"Failed"), a second report is ignored and the current state is
    /// returned unchanged instead of throwing or overwriting.</summary>
    public async Task<AutomationDbSnapshotDto> CompleteAsync(Guid id, CompleteSnapshotRequest r, CancellationToken ct)
    {
        var entity = await repository.FindSnapshotAsync(id, ct) ?? throw new EntityNotFoundException("Snapshot not found.");
        if (entity.Status != "Running")
            return await repository.GetSnapshotByIdAsync(id, ct) ?? throw new EntityNotFoundException("Snapshot not found.");
        if (r.Status == "Succeeded")
        {
            if (string.IsNullOrWhiteSpace(r.DbKind) || string.IsNullOrWhiteSpace(r.SnapshotPath) || string.IsNullOrWhiteSpace(r.Checksum) || r.SizeBytes is null)
                throw new ArgumentException("dbKind, snapshotPath, checksum and sizeBytes are required when reporting Succeeded.");
            entity.Complete(r.DbKind, r.SnapshotPath, r.Checksum, r.SizeBytes.Value);
        }
        else
        {
            entity.Fail(r.DbKind, r.ErrorMessage ?? "Snapshot failed.");
        }
        await repository.SaveChangesAsync(ct);
        return await repository.GetSnapshotByIdAsync(id, ct) ?? throw new EntityNotFoundException("Snapshot not found.");
    }
}
