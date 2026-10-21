namespace ProMaxx2.QA.Domain.Automation;

/// <summary>A persistent, named grouping of Automation Cases for repeatable regression/smoke runs (AUT-P1-001, case membership added in AUT-P1-002).</summary>
public sealed class AutomationSuite
{
    private AutomationSuite() { }
    public AutomationSuite(Guid projectId, string suiteCode, string suiteName, string? description, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        Validate(suiteCode, suiteName);
        AutomationSuiteId = Guid.NewGuid();
        ProjectId = projectId;
        SuiteCode = suiteCode.Trim().ToUpperInvariant();
        SuiteName = suiteName.Trim();
        Description = description?.Trim();
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        RevisionNo = 1;
    }

    public Guid AutomationSuiteId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string SuiteCode { get; private set; } = string.Empty;
    public string SuiteName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public int RevisionNo { get; private set; }
    public ICollection<AutomationSuiteCase> Cases { get; private set; } = [];
    public ICollection<AutomationSuiteRevision> Revisions { get; private set; } = [];

    /// <summary>AUT-P1-003: every mutation (header edit, close/reopen, case add/remove/reorder) bumps the revision
    /// counter; the caller then records an <see cref="AutomationSuiteRevision"/> against the returned number.</summary>
    public int BumpRevision() => ++RevisionNo;

    public void Update(string suiteName, string? description, Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Cannot edit a closed suite. Reopen it first.");
        Validate(SuiteCode, suiteName);
        SuiteName = suiteName.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void Close(Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Suite is already closed.");
        IsActive = false;
        ClosedAt = DateTime.UtcNow;
        ClosedBy = userId;
    }

    public void Reopen(Guid? userId)
    {
        if (IsActive) throw new InvalidOperationException("Suite is already open.");
        IsActive = true;
        ClosedAt = null;
        ClosedBy = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    private static void Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Suite code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Suite name is required.");
    }
}

/// <summary>AUT-P1-002: an Automation Case's membership in a Suite — its run order and whether it's Required (must pass) or Optional.</summary>
public sealed class AutomationSuiteCase
{
    private AutomationSuiteCase() { }
    public AutomationSuiteCase(Guid automationSuiteId, Guid automationCaseId, int sortOrder, bool isRequired)
    {
        if (sortOrder < 1) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        AutomationSuiteId = automationSuiteId;
        AutomationCaseId = automationCaseId;
        SortOrder = sortOrder;
        IsRequired = isRequired;
    }
    public Guid AutomationSuiteId { get; private set; }
    public Guid AutomationCaseId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsRequired { get; private set; }
    public AutomationSuite Suite { get; private set; } = null!;
    public AutomationCase AutomationCase { get; private set; } = null!;

    public void Update(int sortOrder, bool isRequired)
    {
        if (sortOrder < 1) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        SortOrder = sortOrder;
        IsRequired = isRequired;
    }
}

/// <summary>AUT-P1-003: an audit-trail entry for one change to an <see cref="AutomationSuite"/> — its header, its
/// lifecycle (close/reopen), or its case membership. <see cref="ChangeType"/> is a short machine-readable label
/// ("Created"/"Updated"/"Closed"/"Reopened"/"CasesAdded"/"CaseUpdated"/"CaseRemoved"); <see cref="Detail"/> is a
/// human-readable summary of what changed; <see cref="ChangeReason"/> is the optional reason the caller gave.</summary>
public sealed class AutomationSuiteRevision
{
    private AutomationSuiteRevision() { }
    public AutomationSuiteRevision(Guid automationSuiteId, int revisionNo, string changeType, string? detail, string? changeReason, Guid? changedBy)
    {
        if (string.IsNullOrWhiteSpace(changeType)) throw new ArgumentException("Change type is required.");
        AutomationSuiteRevisionId = Guid.NewGuid();
        AutomationSuiteId = automationSuiteId;
        RevisionNo = revisionNo;
        ChangeType = changeType.Trim();
        Detail = detail?.Trim();
        ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim();
        ChangedBy = changedBy;
        ChangedAt = DateTime.UtcNow;
    }
    public Guid AutomationSuiteRevisionId { get; private set; }
    public Guid AutomationSuiteId { get; private set; }
    public int RevisionNo { get; private set; }
    public string ChangeType { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? ChangeReason { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public AutomationSuite Suite { get; private set; } = null!;
}
