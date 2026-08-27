using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationDbRestoreDto(Guid AutomationDbRestoreId, Guid ProjectId, Guid AutomationDbSnapshotId, Guid EnvironmentId, string EnvironmentName, Guid BuildId, string BuildNumber,
    string Status, Guid? AgentId, string? AgentCode, bool ChecksumVerified, bool AvailabilityVerified, string? ErrorMessage,
    Guid? RequestedBy, DateTime RequestedAt, DateTime? StartedAt, DateTime? CompletedAt);

public sealed record RequestRestoreRequest(Guid AutomationDbSnapshotId);

/// <summary>AUT-DATA-002: handed to the agent that claimed a restore request. Unlike a snapshot claim package, this
/// one does carry the backup file location and its expected checksum — that is exactly the data being restored, and
/// it was the Hub that recorded it when the snapshot completed (AUT-DATA-001). The agent still supplies its own DB
/// connection profile locally, same as everywhere else in this module.</summary>
public sealed record ClaimRestorePackageDto(Guid AutomationDbRestoreId, Guid AutomationDbSnapshotId, string SnapshotPath, string ExpectedChecksum);

public sealed record ClaimRestoreRequest(string AgentCode, string AgentVersion);
public sealed record CompleteRestoreRequest(string Status, bool ChecksumVerified, bool AvailabilityVerified, string? ErrorMessage);

public interface IAutomationDataRestoreRepository
{
    Task<IReadOnlyList<AutomationDbRestoreDto>> ListRestoresAsync(Guid projectId, Guid? automationDbSnapshotId, CancellationToken ct);
    Task<AutomationDbRestoreDto?> GetRestoreAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationDbRestoreDto?> GetRestoreByIdAsync(Guid id, CancellationToken ct);
    Task AddRestoreAsync(AutomationDbRestore entity, CancellationToken ct);

    /// <summary>Atomically (Serializable transaction, same pattern as ClaimNextSnapshotRequestAsync) claims the
    /// oldest still-"Requested" restore whose underlying snapshot's <c>AgentId</c> matches the claiming agent —
    /// deliberately restricted this way, since the backup file only exists on the local disk of the agent that
    /// produced it; any other agent claiming it would fail immediately on "file not found".</summary>
    Task<ClaimRestorePackageDto?> ClaimNextRestoreRequestAsync(string agentCode, CancellationToken ct);
    Task<AutomationDbRestore?> FindRestoreAsync(Guid id, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-DATA-002: request/track restoring an Environment's DB from a previously completed snapshot.</summary>
public sealed class AutomationDataRestoreService(IAutomationDataRestoreRepository repository, IAutomationDataSnapshotRepository snapshots)
{
    public Task<IReadOnlyList<AutomationDbRestoreDto>> ListAsync(Guid projectId, Guid? snapshotId, CancellationToken ct)
        => repository.ListRestoresAsync(projectId, snapshotId, ct);

    public async Task<AutomationDbRestoreDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetRestoreAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Restore not found.");

    public async Task<AutomationDbRestoreDto> RequestAsync(Guid projectId, RequestRestoreRequest r, Guid? userId, CancellationToken ct)
    {
        var snapshot = await snapshots.GetSnapshotAsync(r.AutomationDbSnapshotId, projectId, ct) ?? throw new EntityNotFoundException("Snapshot not found.");
        if (snapshot.Status != "Succeeded") throw new ArgumentException("Only a successfully completed snapshot can be restored from.");
        var entity = new AutomationDbRestore(projectId, r.AutomationDbSnapshotId, userId);
        await repository.AddRestoreAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetRestoreAsync(entity.AutomationDbRestoreId, projectId, ct) ?? throw new EntityNotFoundException("Restore not found.");
    }

    public Task<ClaimRestorePackageDto?> ClaimNextAsync(string agentCode, CancellationToken ct) => repository.ClaimNextRestoreRequestAsync(agentCode, ct);

    /// <summary>Agent-facing, idempotent against a late/duplicate report — same pattern as
    /// <c>AutomationDataSnapshotService.CompleteAsync</c>.</summary>
    public async Task<AutomationDbRestoreDto> CompleteAsync(Guid id, CompleteRestoreRequest r, CancellationToken ct)
    {
        var entity = await repository.FindRestoreAsync(id, ct) ?? throw new EntityNotFoundException("Restore not found.");
        if (entity.Status != "Running")
            return await repository.GetRestoreByIdAsync(id, ct) ?? throw new EntityNotFoundException("Restore not found.");
        if (r.Status == "Succeeded") entity.Complete(r.ChecksumVerified, r.AvailabilityVerified);
        else entity.Fail(r.ChecksumVerified, r.AvailabilityVerified, r.ErrorMessage ?? "Restore failed.");
        await repository.SaveChangesAsync(ct);
        return await repository.GetRestoreByIdAsync(id, ct) ?? throw new EntityNotFoundException("Restore not found.");
    }
}
