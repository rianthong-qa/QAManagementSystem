namespace ProMaxx2.QA.Domain.Defects;

public sealed class DefectActivity
{
    public Guid DefectActivityId { get; private set; }
    public Guid DefectId { get; private set; }
    public string ActionType { get; private set; } = "";
    public string Message { get; private set; } = "";
    public Guid? ActorUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    /// <summary>ค่าก่อน/หลัง (เช่นสถานะ) — ใช้นับ Reopen และเวลาแก้ไขได้แม่นยำ (DEFECT_KNOWLEDGE_PLAN §4.3); activity เก่ามีแค่ Message</summary>
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    /// <summary>Build ที่เกี่ยวข้องกับเหตุการณ์ (Fix Build ตอน Resolved, Build ที่ตรวจซ้ำ, Build ที่ Fail ซ้ำ)</summary>
    public Guid? BuildId { get; private set; }
    private DefectActivity() { }
    public DefectActivity(Guid defectId, string actionType, string message, Guid? actorUserId, string? fromValue = null, string? toValue = null, Guid? buildId = null)
    {
        DefectActivityId = Guid.NewGuid();
        DefectId = defectId;
        ActionType = actionType;
        Message = message;
        ActorUserId = actorUserId;
        CreatedAt = DateTime.UtcNow;
        FromValue = fromValue;
        ToValue = toValue;
        BuildId = buildId;
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
