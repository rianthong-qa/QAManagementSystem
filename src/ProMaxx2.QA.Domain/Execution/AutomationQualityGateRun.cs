namespace ProMaxx2.QA.Domain.Execution;

public sealed class AutomationQualityGateRun
{
    private AutomationQualityGateRun() { }
    public AutomationQualityGateRun(Guid projectId,Guid releaseId,Guid buildId,string targetApp,string baselineBuild,string currentBuild,bool passed,int newMissingCount,int newDuplicateCount,int removedCount,int changedCount,string?messages,string?runnerName,DateTime completedAt)
    {
        if(targetApp is not ("pos" or "app"))throw new ArgumentException("Target app must be pos or app.");
        if(string.IsNullOrWhiteSpace(baselineBuild)||string.IsNullOrWhiteSpace(currentBuild))throw new ArgumentException("Baseline and current build are required.");
        if(newMissingCount<0||newDuplicateCount<0||removedCount<0||changedCount<0)throw new ArgumentException("Finding counts cannot be negative.");
        AutomationQualityGateRunId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;TargetApp=targetApp;BaselineBuild=baselineBuild.Trim();CurrentBuild=currentBuild.Trim();Status=passed?"Passed":"Failed";NewMissingCount=newMissingCount;NewDuplicateCount=newDuplicateCount;RemovedCount=removedCount;ChangedCount=changedCount;Messages=messages?.Trim();RunnerName=runnerName?.Trim();CompletedAt=completedAt;
    }
    public Guid AutomationQualityGateRunId{get;private set;}public Guid ProjectId{get;private set;}public Guid ReleaseId{get;private set;}public Guid BuildId{get;private set;}public string TargetApp{get;private set;}="pos";public string BaselineBuild{get;private set;}="";public string CurrentBuild{get;private set;}="";public string Status{get;private set;}="Failed";public int NewMissingCount{get;private set;}public int NewDuplicateCount{get;private set;}public int RemovedCount{get;private set;}public int ChangedCount{get;private set;}public string?Messages{get;private set;}public string?RunnerName{get;private set;}public DateTime CompletedAt{get;private set;}
}
