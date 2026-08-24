namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationAgent
{
    private AutomationAgent() { }
    public AutomationAgent(string agentCode, string machineName, string agentVersion, string operatingSystem, string architecture, Guid? approvedBy)
    {
        if (string.IsNullOrWhiteSpace(agentCode) || string.IsNullOrWhiteSpace(machineName)) throw new ArgumentException("Agent code and machine name are required.");
        AgentId = Guid.NewGuid();
        AgentCode = agentCode.Trim().ToUpperInvariant();
        MachineName = machineName.Trim();
        AgentVersion = agentVersion.Trim();
        OperatingSystem = operatingSystem?.Trim() ?? "Windows";
        Architecture = architecture?.Trim() ?? "x64";
        Status = "Online";
        LastHeartbeatAt = DateTime.UtcNow;
        RegisteredAt = DateTime.UtcNow;
        ApprovedBy = approvedBy;
        IsEnabled = true;
    }
    public Guid AgentId { get; private set; }
    public string AgentCode { get; private set; } = string.Empty;
    public string MachineName { get; private set; } = string.Empty;
    public string AgentVersion { get; private set; } = string.Empty;
    public string OperatingSystem { get; private set; } = "Windows";
    public string Architecture { get; private set; } = "x64";
    public string Status { get; private set; } = "Online";
    public DateTime LastHeartbeatAt { get; private set; }
    public Guid? CurrentExecutionId { get; private set; }
    public DateTime RegisteredAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsDeleted { get; private set; }
    public ICollection<AutomationAgentCapability> Capabilities { get; private set; } = [];

    public void Heartbeat(DateTime at, Guid? currentExecutionId)
    {
        LastHeartbeatAt = at;
        CurrentExecutionId = currentExecutionId;
        Status = "Online";
    }
    public void SetStatus(string status) => Status = status;
    public void SetCurrentExecution(Guid? executionId)
    {
        CurrentExecutionId = executionId;
        Status = executionId.HasValue ? "Busy" : "Idle";
    }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
    public void SoftDelete() { IsDeleted = true; IsEnabled = false; Status = "Disabled"; }
    public void Reactivate() { IsDeleted = false; IsEnabled = true; Status = "Online"; }
    public void ReplaceCapabilities(IEnumerable<AutomationAgentCapability> capabilities)
    {
        Capabilities.Clear();
        foreach (var capability in capabilities) Capabilities.Add(capability);
    }
}

public sealed class AutomationAgentCapability
{
    private AutomationAgentCapability() { }
    public AutomationAgentCapability(Guid agentId, string capabilityCode, string capabilityVersion)
    {
        AgentId = agentId;
        CapabilityCode = capabilityCode.Trim().ToUpperInvariant();
        CapabilityVersion = capabilityVersion?.Trim() ?? "1.0";
    }
    public Guid AgentId { get; private set; }
    public string CapabilityCode { get; private set; } = string.Empty;
    public string CapabilityVersion { get; private set; } = "1.0";
    public AutomationAgent Agent { get; private set; } = null!;
}