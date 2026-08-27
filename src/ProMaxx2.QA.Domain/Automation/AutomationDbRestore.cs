namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-DATA-002: a request/audit record for restoring an Environment's physical database from a previously
/// taken <see cref="AutomationDbSnapshot"/> — e.g. after a run/before a retry, to start again from a known clean data
/// state. Same split of responsibility as the snapshot itself: the Hub only orchestrates request/claim/complete, the
/// actual restore (Firebird <c>gbak -rep</c> / SQL Server <c>RESTORE DATABASE ... WITH REPLACE</c>) runs entirely on
/// the Windows Agent. Per AC, every restore verifies two things before/after the restore command itself: the backup
/// file's checksum still matches what the snapshot recorded (it was not corrupted/replaced since), and the database
/// is actually reachable again once the restore completes — both are tracked here explicitly rather than folded into
/// a single pass/fail, so a failure clearly shows which of the two checks (or the restore command itself) failed.</summary>
public sealed class AutomationDbRestore
{
    private AutomationDbRestore() { }
    public AutomationDbRestore(Guid projectId, Guid automationDbSnapshotId, Guid? requestedBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (automationDbSnapshotId == Guid.Empty) throw new ArgumentException("Snapshot is required.");
        AutomationDbRestoreId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationDbSnapshotId = automationDbSnapshotId;
        RequestedBy = requestedBy;
        Status = "Requested";
        RequestedAt = DateTime.UtcNow;
    }

    public Guid AutomationDbRestoreId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationDbSnapshotId { get; private set; }
    /// <summary>"Requested" / "Running" / "Succeeded" / "Failed".</summary>
    public string Status { get; private set; } = "Requested";
    public Guid? AgentId { get; private set; }
    public bool ChecksumVerified { get; private set; }
    public bool AvailabilityVerified { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public AutomationDbSnapshot Snapshot { get; private set; } = null!;

    public void Claim(Guid agentId)
    {
        if (Status != "Requested") throw new InvalidOperationException("Restore request is not pending.");
        Status = "Running";
        AgentId = agentId;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete(bool checksumVerified, bool availabilityVerified)
    {
        if (Status != "Running") throw new InvalidOperationException("Restore request is not running.");
        if (!checksumVerified || !availabilityVerified) throw new ArgumentException("Complete() is for a fully-verified success — use Fail() otherwise.");
        Status = "Succeeded";
        ChecksumVerified = true;
        AvailabilityVerified = true;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(bool checksumVerified, bool availabilityVerified, string errorMessage)
    {
        if (Status != "Running") throw new InvalidOperationException("Restore request is not running.");
        Status = "Failed";
        ChecksumVerified = checksumVerified;
        AvailabilityVerified = availabilityVerified;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Restore failed." : errorMessage.Trim();
        CompletedAt = DateTime.UtcNow;
    }
}
