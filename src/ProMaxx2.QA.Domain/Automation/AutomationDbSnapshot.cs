namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-DATA-001: a request/audit record for taking a real backup ("snapshot") of the physical database an
/// Environment's Windows Agent talks to, so a test run against a Build can start from a known data state and be
/// restored afterward (AUT-DATA-002). The actual backup (Firebird <c>gbak</c> / SQL Server <c>BACKUP DATABASE</c>) is
/// performed entirely on the Windows Agent — same architecture as DB assertions (<c>DatabaseValidator</c> in
/// agent/ProMaxx2.Automation.Core): the Hub never holds DB host/credentials, only which Agent claimed the request and
/// the result metadata it reports back. Lifecycle: Requested (created by a user/API call) → Running (an agent claimed
/// it) → Succeeded/Failed (agent reported the outcome).</summary>
public sealed class AutomationDbSnapshot
{
    private static readonly string[] AllowedStatuses = ["Requested", "Running", "Succeeded", "Failed"];

    private AutomationDbSnapshot() { }
    public AutomationDbSnapshot(Guid projectId, Guid environmentId, Guid buildId, Guid? requestedBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (environmentId == Guid.Empty) throw new ArgumentException("Environment is required.");
        if (buildId == Guid.Empty) throw new ArgumentException("Build is required.");
        AutomationDbSnapshotId = Guid.NewGuid();
        ProjectId = projectId;
        EnvironmentId = environmentId;
        BuildId = buildId;
        RequestedBy = requestedBy;
        Status = "Requested";
        RequestedAt = DateTime.UtcNow;
    }

    public Guid AutomationDbSnapshotId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public Guid BuildId { get; private set; }
    /// <summary>"Requested" / "Running" / "Succeeded" / "Failed" — see class summary.</summary>
    public string Status { get; private set; } = "Requested";
    /// <summary>"Firebird" / "SqlServer" — only known once an agent claims the request and reports which kind of DB
    /// its own local profile talks to; the Hub never asks for or stores this up front.</summary>
    public string? DbKind { get; private set; }
    public Guid? AgentId { get; private set; }
    public string? SnapshotPath { get; private set; }
    /// <summary>SHA-256 of the backup file, computed by the agent — used by AUT-DATA-002 to verify the backup was
    /// not corrupted/altered before restoring it.</summary>
    public string? Checksum { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    /// <summary>An agent has picked this request up and is about to run the backup.</summary>
    public void Claim(Guid agentId)
    {
        if (Status != "Requested") throw new InvalidOperationException("Snapshot request is not pending.");
        Status = "Running";
        AgentId = agentId;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete(string dbKind, string snapshotPath, string checksum, long sizeBytes)
    {
        if (Status != "Running") throw new InvalidOperationException("Snapshot request is not running.");
        Status = "Succeeded";
        DbKind = dbKind;
        SnapshotPath = snapshotPath;
        Checksum = checksum;
        SizeBytes = sizeBytes;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string? dbKind, string errorMessage)
    {
        if (Status != "Running") throw new InvalidOperationException("Snapshot request is not running.");
        Status = "Failed";
        DbKind = dbKind;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Snapshot failed." : errorMessage.Trim();
        CompletedAt = DateTime.UtcNow;
    }
}
