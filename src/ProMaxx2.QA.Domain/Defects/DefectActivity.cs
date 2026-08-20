namespace ProMaxx2.QA.Domain.Defects;

public sealed class DefectActivity
{
    public Guid DefectActivityId { get; private set; }
    public Guid DefectId { get; private set; }
    public string ActionType { get; private set; } = "";
    public string Message { get; private set; } = "";
    public Guid? ActorUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    private DefectActivity() { }
    public DefectActivity(Guid defectId, string actionType, string message, Guid? actorUserId)
    {
        DefectActivityId = Guid.NewGuid();
        DefectId = defectId;
        ActionType = actionType;
        Message = message;
        ActorUserId = actorUserId;
        CreatedAt = DateTime.UtcNow;
    }
}

public sealed class DefectTestCaseLink
{
    public Guid DefectId { get; private set; }
    public Guid TestCaseId { get; private set; }
    public Guid? LinkedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    private DefectTestCaseLink() { }
    public DefectTestCaseLink(Guid defectId, Guid testCaseId, Guid? linkedByUserId)
    {
        DefectId = defectId;
        TestCaseId = testCaseId;
        LinkedByUserId = linkedByUserId;
        CreatedAt = DateTime.UtcNow;
    }
}
