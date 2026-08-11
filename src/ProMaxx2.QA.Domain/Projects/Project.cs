namespace ProMaxx2.QA.Domain.Projects;

public sealed class Project
{
    private Project() { }
    public Project(string code,string name,string? description,Guid? ownerUserId,Guid? createdBy)
    {
        if(string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Project code is required.",nameof(code));
        if(string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.",nameof(name));
        ProjectId=Guid.NewGuid(); ProjectCode=code.Trim().ToUpperInvariant(); ProjectName=name.Trim(); Description=description?.Trim(); OwnerUserId=ownerUserId; CreatedBy=createdBy; Status="Active"; IsActive=true; CreatedAt=DateTime.UtcNow;
    }
    public Guid ProjectId {get;private set;} public string ProjectCode {get;private set;}=string.Empty; public string ProjectName {get;private set;}=string.Empty; public string? Description {get;private set;} public string Status {get;private set;}="Active"; public Guid? OwnerUserId {get;private set;} public bool IsActive {get;private set;} public DateTime CreatedAt {get;private set;} public Guid? CreatedBy {get;private set;} public DateTime? UpdatedAt {get;private set;} public Guid? UpdatedBy {get;private set;} public ICollection<ProductModule> Modules {get;private set;}=[];
    public void Update(string name,string? description,Guid? ownerUserId,Guid? updatedBy) { if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Project name is required.",nameof(name)); ProjectName=name.Trim();Description=description?.Trim();OwnerUserId=ownerUserId;UpdatedBy=updatedBy;UpdatedAt=DateTime.UtcNow; }
    public void Deactivate(Guid? updatedBy) { IsActive=false;Status="Inactive";UpdatedBy=updatedBy;UpdatedAt=DateTime.UtcNow; }
}

public sealed class ProductModule
{
    private ProductModule() { }
    public ProductModule(Guid projectId,string code,string name,Guid? parentModuleId,string? description,Guid? ownerUserId,Guid? createdBy) { if(string.IsNullOrWhiteSpace(code)||string.IsNullOrWhiteSpace(name))throw new ArgumentException("Module code and name are required."); ModuleId=Guid.NewGuid();ProjectId=projectId;ModuleCode=code.Trim().ToUpperInvariant();ModuleName=name.Trim();ParentModuleId=parentModuleId;Description=description?.Trim();OwnerUserId=ownerUserId;CreatedBy=createdBy;IsActive=true;CreatedAt=DateTime.UtcNow; }
    public Guid ModuleId {get;private set;} public Guid ProjectId {get;private set;} public Guid? ParentModuleId {get;private set;} public string ModuleCode {get;private set;}=string.Empty; public string ModuleName {get;private set;}=string.Empty; public string? Description {get;private set;} public Guid? OwnerUserId {get;private set;} public bool IsActive {get;private set;} public DateTime CreatedAt {get;private set;} public Guid? CreatedBy {get;private set;} public DateTime? UpdatedAt {get;private set;} public Guid? UpdatedBy {get;private set;} public Project Project {get;private set;}=null!; public ProductModule? ParentModule {get;private set;}
    public void Update(string name,Guid? parentModuleId,string? description,Guid? ownerUserId,Guid? updatedBy) { if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Module name is required.",nameof(name)); if(parentModuleId==ModuleId)throw new InvalidOperationException("A module cannot be its own parent.");ModuleName=name.Trim();ParentModuleId=parentModuleId;Description=description?.Trim();OwnerUserId=ownerUserId;UpdatedBy=updatedBy;UpdatedAt=DateTime.UtcNow; }
    public void Deactivate(Guid? updatedBy){IsActive=false;UpdatedBy=updatedBy;UpdatedAt=DateTime.UtcNow;}
}
