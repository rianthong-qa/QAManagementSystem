namespace ProMaxx2.QA.Domain.Defects;

public sealed class Defect
{
    private Defect() { }
    public Defect(Guid projectId, Guid? releaseId, Guid? buildId, Guid? moduleId, string code, string title, string severity, string status, Guid? createdBy)
    {
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Project, defect code and title are required.");
        DefectId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;ModuleId=moduleId;DefectCode=code.Trim().ToUpperInvariant();Title=title.Trim();Severity=NormalizeSeverity(severity);Status=NormalizeStatus(status);CreatedBy=createdBy;CreatedAt=DateTime.UtcNow;
    }
    public Guid DefectId{get;private set;} public Guid ProjectId{get;private set;} public Guid? ReleaseId{get;private set;} public Guid? BuildId{get;private set;} public Guid? ModuleId{get;private set;} public string DefectCode{get;private set;}=string.Empty; public string Title{get;private set;}=string.Empty; public string Severity{get;private set;}="Medium"; public string Status{get;private set;}="Open"; public bool IsDeleted{get;private set;} public DateTime CreatedAt{get;private set;} public Guid? CreatedBy{get;private set;} public DateTime? UpdatedAt{get;private set;} public Guid? UpdatedBy{get;private set;}
    public void Update(string title,string severity,string status,Guid? userId){if(string.IsNullOrWhiteSpace(title))throw new ArgumentException("Title is required.");Title=title.Trim();Severity=NormalizeSeverity(severity);Status=NormalizeStatus(status);UpdatedAt=DateTime.UtcNow;UpdatedBy=userId;}
    public void SoftDelete(Guid?userId){IsDeleted=true;UpdatedAt=DateTime.UtcNow;UpdatedBy=userId;}
    private static string NormalizeSeverity(string value)=>new[]{"Critical","High","Medium","Low"}.SingleOrDefault(x=>x.Equals(value,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid defect severity.");
    private static string NormalizeStatus(string value)=>new[]{"Open","In Progress","Resolved","Closed","Rejected"}.SingleOrDefault(x=>x.Equals(value,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid defect status.");
}
