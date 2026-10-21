namespace ProMaxx2.QA.Domain.Execution;

public sealed class RegressionProfile
{
    private RegressionProfile() { }
    public RegressionProfile(Guid projectId,string name,string visibility,Guid? ownerUserId,string settingsJson)
    { RegressionProfileId=Guid.NewGuid();ProjectId=projectId;Name=name.Trim();Visibility=visibility;OwnerUserId=ownerUserId;SettingsJson=settingsJson;IsActive=true;CreatedAt=DateTime.UtcNow; }
    public Guid RegressionProfileId{get;private set;} public Guid ProjectId{get;private set;} public string Name{get;private set;}=string.Empty; public string Visibility{get;private set;}="Private"; public Guid? OwnerUserId{get;private set;} public string SettingsJson{get;private set;}="{}"; public bool IsActive{get;private set;} public DateTime CreatedAt{get;private set;} public DateTime?UpdatedAt{get;private set;}
    public void Update(string name,string visibility,string settingsJson){Name=name.Trim();Visibility=visibility;SettingsJson=settingsJson;UpdatedAt=DateTime.UtcNow;}
    public void Deactivate(){IsActive=false;UpdatedAt=DateTime.UtcNow;}
}

public sealed class RegressionSchedule
{
    private RegressionSchedule() { }
    public RegressionSchedule(Guid projectId,Guid releaseId,Guid? profileId,string name,Guid? ownerUserId)
    { RegressionScheduleId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;RegressionProfileId=profileId;Name=name.Trim();OwnerUserId=ownerUserId;IsActive=true;CreatedAt=DateTime.UtcNow; }
    public Guid RegressionScheduleId{get;private set;} public Guid ProjectId{get;private set;} public Guid ReleaseId{get;private set;} public Guid?RegressionProfileId{get;private set;} public string Name{get;private set;}=string.Empty; public Guid?OwnerUserId{get;private set;} public Guid?LastNotifiedBuildId{get;private set;} public bool IsActive{get;private set;} public DateTime CreatedAt{get;private set;}
    public Guid?EnvironmentId{get;private set;} public int Priority{get;private set;}=5;
    public void Acknowledge(Guid buildId)=>LastNotifiedBuildId=buildId;
    public void Deactivate()=>IsActive=false;
    /// <summary>AUT-REG-002: opt-in target for the automatic Automation run fired when this schedule's release gets a
    /// new Release Candidate Build. Left <c>null</c> (the default), the schedule keeps today's passive-notification-only
    /// behavior — automation only fires once an Environment is configured.</summary>
    public void ConfigureAutomation(Guid?environmentId,int priority){EnvironmentId=environmentId;Priority=Math.Clamp(priority,1,10);}
}
