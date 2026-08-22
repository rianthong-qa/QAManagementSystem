namespace ProMaxx2.QA.Domain.Execution;

public sealed class AutomationRunnerAgent
{
    private AutomationRunnerAgent() { }
    public AutomationRunnerAgent(Guid projectId,string runnerName,string machineName,string version,string capabilities,DateTime now)
    {AutomationRunnerAgentId=Guid.NewGuid();ProjectId=projectId;RunnerName=Required(runnerName,"Runner name");MachineName=Required(machineName,"Machine name");Version=Required(version,"Version");Capabilities=Required(capabilities,"Capabilities");RegisteredAt=now;LastHeartbeatAt=now;State="Idle";}
    public Guid AutomationRunnerAgentId{get;private set;}public Guid ProjectId{get;private set;}public string RunnerName{get;private set;}="";public string MachineName{get;private set;}="";public string Version{get;private set;}="";public string Capabilities{get;private set;}="";public string State{get;private set;}="Idle";public Guid?CurrentJobId{get;private set;}public DateTime RegisteredAt{get;private set;}public DateTime LastHeartbeatAt{get;private set;}
    public void Heartbeat(string machineName,string version,string capabilities,string state,Guid?currentJobId,DateTime now){if(state is not ("Idle" or "Busy"))throw new ArgumentException("Runner state must be Idle or Busy.");MachineName=Required(machineName,"Machine name");Version=Required(version,"Version");Capabilities=Required(capabilities,"Capabilities");State=state;CurrentJobId=currentJobId;LastHeartbeatAt=now;}
    private static string Required(string value,string name)=>string.IsNullOrWhiteSpace(value)?throw new ArgumentException($"{name} is required."):value.Trim();
}
