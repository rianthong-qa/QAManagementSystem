using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Application.Projects;

public sealed record ProjectDto(Guid ProjectId,string ProjectCode,string ProjectName,string? Description,string Status,Guid? OwnerUserId,bool IsActive,DateTime CreatedAt);
public sealed record CreateProjectRequest(string ProjectCode,string ProjectName,string? Description,Guid? OwnerUserId);
public sealed record UpdateProjectRequest(string ProjectName,string? Description,Guid? OwnerUserId);
public sealed record ModuleDto(Guid ModuleId,Guid ProjectId,Guid? ParentModuleId,string ModuleCode,string ModuleName,string? Description,Guid? OwnerUserId,bool IsActive);
public sealed record CreateModuleRequest(string ModuleCode,string ModuleName,Guid? ParentModuleId,string? Description,Guid? OwnerUserId);
public sealed record UpdateModuleRequest(string ModuleName,Guid? ParentModuleId,string? Description,Guid? OwnerUserId);

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct); Task<ProjectDto?> GetAsync(Guid id,CancellationToken ct); Task<bool> ProjectCodeExistsAsync(string code,CancellationToken ct); Task AddAsync(Project project,CancellationToken ct); Task<Project?> FindAsync(Guid id,CancellationToken ct);
    Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId,CancellationToken ct); Task<bool> ModuleCodeExistsAsync(Guid projectId,string code,CancellationToken ct); Task<ProductModule?> FindModuleAsync(Guid id,CancellationToken ct); Task AddModuleAsync(ProductModule module,CancellationToken ct); Task SaveChangesAsync(CancellationToken ct);
}
public sealed class DuplicateCodeException(string message):Exception(message);
public sealed class EntityNotFoundException(string message):Exception(message);

public sealed class ProjectService(IProjectRepository repository)
{
    public Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct)=>repository.ListAsync(ct);
    public async Task<ProjectDto> GetAsync(Guid id,CancellationToken ct)=>await repository.GetAsync(id,ct)??throw new EntityNotFoundException("Project not found.");
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request,Guid? userId,CancellationToken ct){var code=request.ProjectCode.Trim().ToUpperInvariant();if(await repository.ProjectCodeExistsAsync(code,ct))throw new DuplicateCodeException("Project code already exists.");var entity=new Project(code,request.ProjectName,request.Description,request.OwnerUserId,userId);await repository.AddAsync(entity,ct);await repository.SaveChangesAsync(ct);return (await repository.GetAsync(entity.ProjectId,ct))!;}
    public async Task<ProjectDto> UpdateAsync(Guid id,UpdateProjectRequest request,Guid? userId,CancellationToken ct){var entity=await repository.FindAsync(id,ct)??throw new EntityNotFoundException("Project not found.");entity.Update(request.ProjectName,request.Description,request.OwnerUserId,userId);await repository.SaveChangesAsync(ct);return (await repository.GetAsync(id,ct))!;}
    public async Task DeactivateAsync(Guid id,Guid? userId,CancellationToken ct){var entity=await repository.FindAsync(id,ct)??throw new EntityNotFoundException("Project not found.");entity.Deactivate(userId);await repository.SaveChangesAsync(ct);}
    public Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId,CancellationToken ct)=>repository.ListModulesAsync(projectId,ct);
    public async Task<ModuleDto> CreateModuleAsync(Guid projectId,CreateModuleRequest request,Guid? userId,CancellationToken ct){_ = await repository.GetAsync(projectId,ct)??throw new EntityNotFoundException("Project not found.");var code=request.ModuleCode.Trim().ToUpperInvariant();if(await repository.ModuleCodeExistsAsync(projectId,code,ct))throw new DuplicateCodeException("Module code already exists in this project.");if(request.ParentModuleId.HasValue){var parent=await repository.FindModuleAsync(request.ParentModuleId.Value,ct);if(parent is null||parent.ProjectId!=projectId)throw new EntityNotFoundException("Parent module not found in this project.");}var entity=new ProductModule(projectId,code,request.ModuleName,request.ParentModuleId,request.Description,request.OwnerUserId,userId);await repository.AddModuleAsync(entity,ct);await repository.SaveChangesAsync(ct);return (await repository.ListModulesAsync(projectId,ct)).Single(x=>x.ModuleId==entity.ModuleId);}
    public async Task<ModuleDto> UpdateModuleAsync(Guid id,UpdateModuleRequest request,Guid? userId,CancellationToken ct){var entity=await repository.FindModuleAsync(id,ct)??throw new EntityNotFoundException("Module not found.");if(request.ParentModuleId.HasValue){var parent=await repository.FindModuleAsync(request.ParentModuleId.Value,ct);if(parent is null||parent.ProjectId!=entity.ProjectId)throw new EntityNotFoundException("Parent module not found in this project.");}entity.Update(request.ModuleName,request.ParentModuleId,request.Description,request.OwnerUserId,userId);await repository.SaveChangesAsync(ct);return (await repository.ListModulesAsync(entity.ProjectId,ct)).Single(x=>x.ModuleId==id);}
    public async Task DeactivateModuleAsync(Guid id,Guid? userId,CancellationToken ct){var entity=await repository.FindModuleAsync(id,ct)??throw new EntityNotFoundException("Module not found.");entity.Deactivate(userId);await repository.SaveChangesAsync(ct);}
}
