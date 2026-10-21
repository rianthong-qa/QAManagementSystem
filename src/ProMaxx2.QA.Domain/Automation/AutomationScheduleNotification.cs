namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-P1-009: an in-app notification about one lifecycle event of an execution that a persistent
/// <see cref="AutomationSchedule"/> fired — "Started" (execution created by the schedule), "Completed" (that
/// execution finished Passed), "Failed" (finished with any other terminal status) or "NoAgent" (the schedule fired
/// and created execution(s) but no enabled automation agent currently exists to pick the job up). Always carries an
/// <see cref="AutomationExecutionId"/> so the UI can link straight to that Execution, per AC "แจ้ง ... พร้อมลิงก์ Execution".
/// Scope: only executions created directly by a schedule fire are tracked here — an auto-retry execution spawned
/// later by <c>AutomationAgentService.CompleteExecutionAsync</c> is a separate execution with no Started row of its
/// own, so it does not get its own Completed/Failed notification (its outcome is still visible on the original
/// execution's retry chain in the Automation UI).</summary>
public sealed class AutomationScheduleNotification
{
    private static readonly string[] AllowedEventTypes = ["Started", "Completed", "Failed", "NoAgent"];

    private AutomationScheduleNotification() { }
    public AutomationScheduleNotification(Guid projectId, Guid automationScheduleId, Guid automationExecutionId, string eventType, string message)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (automationScheduleId == Guid.Empty) throw new ArgumentException("Schedule is required.");
        if (automationExecutionId == Guid.Empty) throw new ArgumentException("Execution is required.");
        if (!AllowedEventTypes.Contains(eventType)) throw new ArgumentException("Event type must be Started, Completed, Failed or NoAgent.");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message is required.");
        AutomationScheduleNotificationId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationScheduleId = automationScheduleId;
        AutomationExecutionId = automationExecutionId;
        EventType = eventType;
        Message = message.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid AutomationScheduleNotificationId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationScheduleId { get; private set; }
    public Guid AutomationExecutionId { get; private set; }
    /// <summary>"Started" / "Completed" / "Failed" / "NoAgent" — see class summary.</summary>
    public string EventType { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public AutomationSchedule Schedule { get; private set; } = null!;

    public void MarkRead(DateTime at)
    {
        if (IsRead) return; // idempotent — re-marking an already-read notification is a no-op, not an error
        IsRead = true;
        ReadAtUtc = at;
    }
}
