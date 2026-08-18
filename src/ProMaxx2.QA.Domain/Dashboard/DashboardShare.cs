namespace ProMaxx2.QA.Domain.Dashboard;

public sealed class DashboardShare
{
    private DashboardShare() { }
    public DashboardShare(string code,Guid? projectId,Guid? releaseId,Guid? buildId,DateTime expiresAt)
    {
        if(string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Share code is required.");
        DashboardShareId=Guid.NewGuid();Code=code;ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;CreatedAt=DateTime.UtcNow;ExpiresAt=expiresAt;
    }
    public Guid DashboardShareId{get;private set;}
    public string Code{get;private set;}=string.Empty;
    public Guid? ProjectId{get;private set;}
    public Guid? ReleaseId{get;private set;}
    public Guid? BuildId{get;private set;}
    public DateTime CreatedAt{get;private set;}
    public DateTime ExpiresAt{get;private set;}
}
