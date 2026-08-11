using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ProjectRepository(QaDbContext db):IProjectRepository
{
    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct)=>await db.Projects.AsNoTracking().OrderBy(x=>x.ProjectCode).Select(x=>new ProjectDto(x.ProjectId,x.ProjectCode,x.ProjectName,x.Description,x.Status,x.OwnerUserId,x.IsActive,x.CreatedAt)).ToListAsync(ct);
    public Task<ProjectDto?> GetAsync(Guid id,CancellationToken ct)=>db.Projects.AsNoTracking().Where(x=>x.ProjectId==id).Select(x=>new ProjectDto(x.ProjectId,x.ProjectCode,x.ProjectName,x.Description,x.Status,x.OwnerUserId,x.IsActive,x.CreatedAt)).SingleOrDefaultAsync(ct);
    public Task<bool> ProjectCodeExistsAsync(string code,CancellationToken ct)=>db.Projects.AnyAsync(x=>x.ProjectCode==code,ct); public Task AddAsync(Project project,CancellationToken ct)=>db.Projects.AddAsync(project,ct).AsTask(); public Task<Project?> FindAsync(Guid id,CancellationToken ct)=>db.Projects.SingleOrDefaultAsync(x=>x.ProjectId==id,ct);
    public async Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId,CancellationToken ct)=>await db.Modules.AsNoTracking().Where(x=>x.ProjectId==projectId).OrderBy(x=>x.ModuleCode).Select(x=>new ModuleDto(x.ModuleId,x.ProjectId,x.ParentModuleId,x.ModuleCode,x.ModuleName,x.Description,x.OwnerUserId,x.IsActive)).ToListAsync(ct);
    public Task<bool> ModuleCodeExistsAsync(Guid projectId,string code,CancellationToken ct)=>db.Modules.AnyAsync(x=>x.ProjectId==projectId&&x.ModuleCode==code,ct);public Task<ProductModule?> FindModuleAsync(Guid id,CancellationToken ct)=>db.Modules.SingleOrDefaultAsync(x=>x.ModuleId==id,ct);public Task AddModuleAsync(ProductModule module,CancellationToken ct)=>db.Modules.AddAsync(module,ct).AsTask();public Task SaveChangesAsync(CancellationToken ct)=>db.SaveChangesAsync(ct);
}
