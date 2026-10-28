namespace ProMaxx2.QA.Domain.Execution;

public sealed class QaSkillMatrixEntry
{
    private QaSkillMatrixEntry() { }
    public QaSkillMatrixEntry(Guid userId, string skillCode, int level) { if (userId == Guid.Empty) throw new ArgumentException("User is required."); if (string.IsNullOrWhiteSpace(skillCode)) throw new ArgumentException("Skill is required."); if (level is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(level)); QaSkillMatrixEntryId = Guid.NewGuid(); UserId = userId; SkillCode = skillCode.Trim().ToUpperInvariant(); Level = level; IsActive = true; UpdatedAt = DateTime.UtcNow; }
    public Guid QaSkillMatrixEntryId { get; private set; } public Guid UserId { get; private set; } public string SkillCode { get; private set; } = string.Empty; public int Level { get; private set; } public bool IsActive { get; private set; } public DateTime UpdatedAt { get; private set; }
    public void Update(int level, bool active) { if (level is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(level)); Level = level; IsActive = active; UpdatedAt = DateTime.UtcNow; }
}

public sealed class QaAvailability
{
    private QaAvailability() { }
    public QaAvailability(Guid userId, DateOnly date, string status, int capacityMinutes) { if (userId == Guid.Empty) throw new ArgumentException("User is required."); if (capacityMinutes < 0) throw new ArgumentOutOfRangeException(nameof(capacityMinutes)); QaAvailabilityId = Guid.NewGuid(); UserId = userId; Date = date; Status = Normalize(status); CapacityMinutes = capacityMinutes; }
    public Guid QaAvailabilityId { get; private set; } public Guid UserId { get; private set; } public DateOnly Date { get; private set; } public string Status { get; private set; } = "Available"; public int CapacityMinutes { get; private set; }
    public bool IsAssignable => Status == "Available" && CapacityMinutes > 0;
    private static string Normalize(string value) => new[] { "Available", "Busy", "Leave", "Unavailable" }.Single(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
}

public sealed class AssignmentPreview
{
    private AssignmentPreview() { }
    public AssignmentPreview(Guid cycleId, Guid requestedBy, DateTime expiresAt) { AssignmentPreviewId = Guid.NewGuid(); TestCycleId = cycleId; RequestedBy = requestedBy; CreatedAt = DateTime.UtcNow; ExpiresAt = expiresAt; Version = Guid.NewGuid().ToString("N"); Status = "Open"; }
    public Guid AssignmentPreviewId { get; private set; } public Guid TestCycleId { get; private set; } public Guid RequestedBy { get; private set; } public DateTime CreatedAt { get; private set; } public DateTime ExpiresAt { get; private set; } public string Version { get; private set; } = string.Empty; public string Status { get; private set; } = "Open";
    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt || Status != "Open";
    public void Confirm() { if (IsExpired(DateTime.UtcNow)) throw new InvalidOperationException("PreviewExpired"); Status = "Confirmed"; }
}

public sealed class AssignmentHistory
{
    private AssignmentHistory() { }
    public AssignmentHistory(Guid cycleCaseId, Guid? suggested, Guid? final, int weight, int score, string reason, string action, Guid actor, string algorithmVersion) { AssignmentHistoryId = Guid.NewGuid(); TestCycleCaseId = cycleCaseId; SuggestedTesterUserId = suggested; FinalTesterUserId = final; Weight = weight; Score = score; Reason = reason; Action = action; ActorUserId = actor; AlgorithmVersion = algorithmVersion; CreatedAt = DateTime.UtcNow; }
    public Guid AssignmentHistoryId { get; private set; } public Guid TestCycleCaseId { get; private set; } public TestCycleCase TestCycleCase { get; private set; } = null!; public Guid? SuggestedTesterUserId { get; private set; } public Guid? FinalTesterUserId { get; private set; } public int Weight { get; private set; } public int Score { get; private set; } public string Reason { get; private set; } = string.Empty; public string Action { get; private set; } = string.Empty; public Guid ActorUserId { get; private set; } public string AlgorithmVersion { get; private set; } = string.Empty; public DateTime CreatedAt { get; private set; }
}
