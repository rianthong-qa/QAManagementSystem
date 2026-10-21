namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-P2-004: one recorded heartbeat/registration check-in for an <see cref="AutomationAgent"/>. Before
/// this, the Hub only ever kept <c>AutomationAgent.LastHeartbeatAt</c> — a single field overwritten on every
/// heartbeat, with no way to see check-in history. Rather than adding a background worker to watch for
/// online/offline transitions (a bigger, separate piece of infrastructure this system doesn't have yet), this is
/// literally a history of heartbeat calls — capped per agent (see <c>AutomationRepository.RecordHeartbeatEventAsync</c>)
/// so it stays a bounded "recent activity" log rather than an unbounded table growing by one row every ~15 seconds
/// per agent forever.</summary>
public sealed class AutomationAgentHeartbeatEvent
{
    private AutomationAgentHeartbeatEvent() { }
    public AutomationAgentHeartbeatEvent(Guid agentId, string status, Guid? currentExecutionId)
    {
        if (agentId == Guid.Empty) throw new ArgumentException("Agent is required.");
        AutomationAgentHeartbeatEventId = Guid.NewGuid();
        AgentId = agentId;
        Status = string.IsNullOrWhiteSpace(status) ? "Online" : status.Trim();
        CurrentExecutionId = currentExecutionId;
        OccurredAt = DateTime.UtcNow;
    }

    public Guid AutomationAgentHeartbeatEventId { get; private set; }
    public Guid AgentId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public Guid? CurrentExecutionId { get; private set; }
    public DateTime OccurredAt { get; private set; }
}
