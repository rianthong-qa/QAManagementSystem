namespace ProMaxx2.QA.Domain.Execution;

public sealed class AutomationRun
{
    private AutomationRun() { }
    public AutomationRun(Guid projectId,Guid? releaseId,Guid? buildId,Guid? testCycleId,string targetApp,string? runnerName,DateTime startedAt,IEnumerable<AutomationCaseResultInput> results)
    {
        if(targetApp is not ("pos" or "app"))throw new ArgumentException("Target app must be pos or app.");
        AutomationRunId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;TestCycleId=testCycleId;TargetApp=targetApp;RunnerName=runnerName?.Trim();StartedAt=startedAt;CompletedAt=DateTime.UtcNow;
        Results=results.Select(x=>new AutomationRunCase(AutomationRunId,x.TestCaseId,x.TestCaseCode,x.Status,x.DurationMs,x.ErrorMessage,x.EvidencePath)).ToList();
        TotalCount=Results.Count;PassedCount=Results.Count(x=>x.Status=="Passed");FailedCount=Results.Count(x=>x.Status=="Failed");SkippedCount=Results.Count(x=>x.Status=="Skipped");Status=FailedCount>0?"Failed":TotalCount>0&&PassedCount==TotalCount?"Passed":"Completed";
    }
    public Guid AutomationRunId{get;private set;}public Guid ProjectId{get;private set;}public Guid?ReleaseId{get;private set;}public Guid?BuildId{get;private set;}public Guid?TestCycleId{get;private set;}public string TargetApp{get;private set;}="pos";public string Status{get;private set;}="Completed";public string?RunnerName{get;private set;}public DateTime StartedAt{get;private set;}public DateTime CompletedAt{get;private set;}public int TotalCount{get;private set;}public int PassedCount{get;private set;}public int FailedCount{get;private set;}public int SkippedCount{get;private set;}public TestCycle?Cycle{get;private set;}public ICollection<AutomationRunCase>Results{get;private set;}=[];
}
public sealed record AutomationCaseResultInput(Guid? TestCaseId,string TestCaseCode,string Status,long DurationMs,string? ErrorMessage,string? EvidencePath);
public sealed class AutomationRunCase
{
    private AutomationRunCase() { }
    public AutomationRunCase(Guid runId,Guid?testCaseId,string code,string status,long durationMs,string?error,string?evidence){string[]allowed=["Passed","Failed","Skipped","Blocked"];AutomationRunCaseId=Guid.NewGuid();AutomationRunId=runId;TestCaseId=testCaseId;TestCaseCode=code.Trim().ToUpperInvariant();Status=allowed.SingleOrDefault(x=>x.Equals(status,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid automation result status.");DurationMs=Math.Max(0,durationMs);ErrorMessage=error?.Trim();EvidencePath=evidence?.Trim();}
    public Guid AutomationRunCaseId{get;private set;}public Guid AutomationRunId{get;private set;}public Guid?TestCaseId{get;private set;}public Guid?TestExecutionId{get;private set;}public string TestCaseCode{get;private set;}="";public string Status{get;private set;}="Skipped";public long DurationMs{get;private set;}public string?ErrorMessage{get;private set;}public string?EvidencePath{get;private set;}public AutomationRun Run{get;private set;}=null!;public TestExecution?Execution{get;private set;}public void LinkExecution(Guid executionId){if(TestExecutionId.HasValue)throw new InvalidOperationException("Automation result is already linked to an execution.");TestExecutionId=executionId;}public void AttachEvidence(string path){if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Evidence path is required.");EvidencePath=path.Trim();}
}
