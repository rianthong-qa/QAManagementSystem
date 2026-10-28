namespace ProMaxx2.QA.Domain.Execution;

/// <summary>Assignment lifecycle metadata kept separate from execution result status.</summary>
public sealed class TestCycleCaseAssignment
{
    private TestCycleCaseAssignment() { }

    public TestCycleCaseAssignment(Guid testCycleCaseId)
    {
        if (testCycleCaseId == Guid.Empty) throw new ArgumentException("Test cycle case is required.");
        TestCycleCaseAssignmentId = Guid.NewGuid();
        TestCycleCaseId = testCycleCaseId;
        Status = "Unassigned";
    }

    public Guid TestCycleCaseAssignmentId { get; private set; }
    public Guid TestCycleCaseId { get; private set; }
    public string Status { get; private set; } = "Unassigned";
    public Guid? AssignedByUserId { get; private set; }
    public DateTime? AssignedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? DueDate { get; private set; }

    public void Assign(Guid actorUserId, DateTime? dueDate)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Assigning user is required.");
        Status = "Assigned";
        AssignedByUserId = actorUserId;
        AssignedAt = DateTime.UtcNow;
        DueDate = dueDate;
        AcceptedAt = null;
        StartedAt = null;
    }

    public void Accept()
    {
        if (Status != "Assigned") throw new InvalidOperationException("Only assigned work can be accepted.");
        Status = "Accepted";
        AcceptedAt = DateTime.UtcNow;
    }

    public void Start()
    {
        if (Status is not ("Assigned" or "Accepted")) throw new InvalidOperationException("Assignment is not ready to start.");
        Status = "InProgress";
        StartedAt ??= DateTime.UtcNow;
    }
}
