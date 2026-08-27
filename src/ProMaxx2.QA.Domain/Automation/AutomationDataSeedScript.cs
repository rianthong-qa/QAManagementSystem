namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-DATA-003: a reusable, named SQL script for seeding known baseline test data into an Environment's
/// database before a run — e.g. required master data (products/prices/promotions) a scenario depends on. Tied to a
/// <see cref="DbKind"/> at authoring time (Firebird SQL and T-SQL are different dialects, so a script can't be
/// dialect-agnostic), stored and re-run as-is every time (no versioning here — unlike <see cref="AutomationSuite"/>,
/// re-running the same idempotent script is the normal, expected usage rather than something to be tracked as
/// drift). Per AC "ไม่เก็บ credential ใน DSL": there is deliberately no field anywhere on this entity for a DB
/// host/user/password — the script is pure SQL text and the agent that executes it always connects using its own
/// local <c>DbProfile</c> (same architecture as snapshot/restore), so a credential structurally cannot end up stored
/// here even by mistake. "Repeatable/idempotent" is a property of the SQL the author writes (e.g. MERGE/UPSERT,
/// delete-then-insert, existence checks) — the system's job is to store/re-run it safely, not to enforce
/// idempotency of arbitrary SQL, which isn't something a script's text alone can be verified to guarantee.</summary>
public sealed class AutomationDataSeedScript
{
    private static readonly string[] AllowedDbKinds = ["Firebird", "SqlServer"];

    private AutomationDataSeedScript() { }
    public AutomationDataSeedScript(Guid projectId, string name, string? description, string dbKind, string sqlScript, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        Validate(name, dbKind, sqlScript);
        AutomationDataSeedScriptId = Guid.NewGuid();
        ProjectId = projectId;
        Name = name.Trim();
        Description = description?.Trim();
        DbKind = dbKind;
        SqlScript = sqlScript;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid AutomationDataSeedScriptId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    /// <summary>"Firebird" / "SqlServer" — which SQL dialect this script is written in.</summary>
    public string DbKind { get; private set; } = string.Empty;
    public string SqlScript { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(string name, string? description, string dbKind, string sqlScript, Guid? userId)
    {
        Validate(name, dbKind, sqlScript);
        Name = name.Trim();
        Description = description?.Trim();
        DbKind = dbKind;
        SqlScript = sqlScript;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void SetActive(bool active, Guid? userId)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    private static void Validate(string name, string dbKind, string sqlScript)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Seed script name is required.");
        if (!AllowedDbKinds.Contains(dbKind)) throw new ArgumentException("DB kind must be Firebird or SqlServer.");
        if (string.IsNullOrWhiteSpace(sqlScript)) throw new ArgumentException("SQL script is required.");
    }
}

/// <summary>AUT-DATA-003: a request/audit record for one execution of an <see cref="AutomationDataSeedScript"/>
/// against an Environment's DB. Same Requested→Running→Succeeded/Failed lifecycle as
/// <see cref="AutomationDbSnapshot"/>; unlike a snapshot, running the same script against the same environment
/// repeatedly is the normal, expected usage (that's what "idempotent" means), so there is no uniqueness constraint
/// here at all — each run is just another audit row.</summary>
public sealed class AutomationDataSeedRun
{
    private AutomationDataSeedRun() { }
    public AutomationDataSeedRun(Guid projectId, Guid automationDataSeedScriptId, Guid environmentId, Guid buildId, Guid? requestedBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (automationDataSeedScriptId == Guid.Empty) throw new ArgumentException("Seed script is required.");
        if (environmentId == Guid.Empty || buildId == Guid.Empty) throw new ArgumentException("Environment and build are required.");
        AutomationDataSeedRunId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationDataSeedScriptId = automationDataSeedScriptId;
        EnvironmentId = environmentId;
        BuildId = buildId;
        RequestedBy = requestedBy;
        Status = "Requested";
        RequestedAt = DateTime.UtcNow;
    }

    public Guid AutomationDataSeedRunId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationDataSeedScriptId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public Guid BuildId { get; private set; }
    public string Status { get; private set; } = "Requested";
    public Guid? AgentId { get; private set; }
    public int? RowsAffected { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public AutomationDataSeedScript Script { get; private set; } = null!;

    public void Claim(Guid agentId)
    {
        if (Status != "Requested") throw new InvalidOperationException("Seed run is not pending.");
        Status = "Running";
        AgentId = agentId;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete(int rowsAffected)
    {
        if (Status != "Running") throw new InvalidOperationException("Seed run is not running.");
        Status = "Succeeded";
        RowsAffected = rowsAffected;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string errorMessage)
    {
        if (Status != "Running") throw new InvalidOperationException("Seed run is not running.");
        Status = "Failed";
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Seed run failed." : errorMessage.Trim();
        CompletedAt = DateTime.UtcNow;
    }
}
