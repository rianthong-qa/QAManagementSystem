namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-DATA-006: non-secret, per-Environment "what kind of database does this Environment run" metadata.
/// Deliberately NOT a place for connection strings/credentials — same architectural stance as
/// <see cref="AutomationDataSeedScript"/> ("ไม่เก็บ credential ใน DSL"), applied here again: DB host/user/password
/// only ever exist as local environment variables on the Windows Agent that connects to them (its own machine-local
/// "secure store" — env vars set directly on that box — entirely outside the Hub's reach and never transmitted to
/// it). What this profile DOES give the Hub is enough non-secret config to catch an obvious Environment/script (or
/// Environment/snapshot) DbKind mismatch *before* creating a request, instead of only discovering it after an agent
/// claims the work and fails partway through — see the cross-check in
/// <c>AutomationDataSeedService.RequestRunAsync</c> and <c>AutomationDataRestoreService.RequestAsync</c>. One
/// profile per Environment (enforced in the service layer via <c>ExistsForEnvironmentAsync</c>, same
/// check-then-create pattern used elsewhere in this module rather than relying solely on a DB constraint) — an
/// Environment maps to exactly one physical database by definition, so a second profile for the same Environment
/// would just be conflicting metadata, not a legitimate second data source.</summary>
public sealed class AutomationEnvironmentDataProfile
{
    private static readonly string[] AllowedDbKinds = ["Firebird", "SqlServer"];

    private AutomationEnvironmentDataProfile() { }
    public AutomationEnvironmentDataProfile(Guid projectId, Guid environmentId, string dbKind, string? notes, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (environmentId == Guid.Empty) throw new ArgumentException("Environment is required.");
        Validate(dbKind);
        AutomationEnvironmentDataProfileId = Guid.NewGuid();
        ProjectId = projectId;
        EnvironmentId = environmentId;
        DbKind = dbKind;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid AutomationEnvironmentDataProfileId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    /// <summary>"Firebird" / "SqlServer" — which physical database this Environment runs, so scripts/snapshots
    /// authored for the wrong dialect can be rejected at request time.</summary>
    public string DbKind { get; private set; } = string.Empty;
    /// <summary>Free-text, non-secret only (e.g. purpose, owning team, server room) — never validated/scanned for
    /// credential-looking content for the same reason <see cref="AutomationDataSeedScript"/> doesn't: it would be
    /// false-positive-prone and adds no real protection, since this field is never used to build a connection.</summary>
    public string? Notes { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(string dbKind, string? notes, Guid? userId)
    {
        Validate(dbKind);
        DbKind = dbKind;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    private static void Validate(string dbKind)
    {
        if (!AllowedDbKinds.Contains(dbKind)) throw new ArgumentException("DB kind must be Firebird or SqlServer.");
    }
}
