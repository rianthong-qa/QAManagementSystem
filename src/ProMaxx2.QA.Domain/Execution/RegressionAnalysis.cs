namespace ProMaxx2.QA.Domain.Execution;

public sealed class RegressionAnalysis
{
    private RegressionAnalysis() { }
    public RegressionAnalysis(Guid projectId, Guid releaseId, Guid buildId, int impactedModules, int recommendedCases, string minimumPriority, string? changeNotes, Guid? analyzedBy)
    { RegressionAnalysisId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;ImpactedModules=impactedModules;RecommendedCases=recommendedCases;MinimumPriority=minimumPriority;ChangeNotes=changeNotes?.Trim();AnalyzedBy=analyzedBy;AnalyzedAt=DateTime.UtcNow; }
    public Guid RegressionAnalysisId{get;private set;} public Guid ProjectId{get;private set;} public Guid ReleaseId{get;private set;} public Guid BuildId{get;private set;} public int ImpactedModules{get;private set;} public int RecommendedCases{get;private set;} public string MinimumPriority{get;private set;}="P1"; public string?ChangeNotes{get;private set;} public Guid?AnalyzedBy{get;private set;} public DateTime AnalyzedAt{get;private set;}
}
