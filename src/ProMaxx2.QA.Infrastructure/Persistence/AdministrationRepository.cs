using Microsoft.EntityFrameworkCore;using ProMaxx2.QA.Application.Common;using ProMaxx2.QA.Application.Identity;using ProMaxx2.QA.Domain.Identity;
namespace ProMaxx2.QA.Infrastructure.Persistence;
public sealed class AdministrationRepository(QaDbContext db):IAdministrationRepository
{
 public async Task<IReadOnlyList<UserDto>>UsersAsync(CancellationToken ct)
{
    var userList=await db.Users.AsNoTracking().OrderBy(x=>x.Username).Select(x=>new{ x.UserId,x.Username,x.DisplayName,x.Email,x.IsActive,x.LastLoginAt,Roles=x.UserRoles.Select(r=>r.Role.RoleCode).OrderBy(c=>c).ToList()}).ToListAsync(ct);
    var userIds=userList.Select(x=>x.UserId).ToList();
    Dictionary<Guid, List<Guid>> projectMap;
    try
    {
        projectMap=await db.ProjectUsers.AsNoTracking().Where(pu=>userIds.Contains(pu.UserId)).GroupBy(pu=>pu.UserId).ToDictionaryAsync(g=>g.Key,g=>g.Select(pu=>pu.ProjectId).ToList(),ct);
    }
    catch
    {
        projectMap=new();
    }
    return userList.Select(x=>new UserDto(x.UserId,x.Username,x.DisplayName,x.Email,x.IsActive,x.LastLoginAt,x.Roles,projectMap.TryGetValue(x.UserId,out var pids)?pids:[])).ToList();
}public Task<User?>FindUserAsync(Guid id,CancellationToken ct)=>db.Users.SingleOrDefaultAsync(x=>x.UserId==id,ct);public Task<bool>UsernameExistsAsync(string username,CancellationToken ct)=>db.Users.AnyAsync(x=>x.Username==username,ct);public Task AddUserAsync(User user,CancellationToken ct)=>db.Users.AddAsync(user,ct).AsTask();public async Task AssignRolesAsync(Guid userId,IReadOnlyList<Guid>roleIds,CancellationToken ct){await db.UserRoles.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);db.UserRoles.AddRange(roleIds.Distinct().Select(x=>new UserRole(userId,x)));}public async Task AssignProjectsAsync(Guid userId,IReadOnlyList<Guid>projectIds,CancellationToken ct){await db.ProjectUsers.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);db.ProjectUsers.AddRange(projectIds.Distinct().Select(x=>new ProjectUser(x,userId,null)));}
  public async Task<PagedResult<UserDto>>SearchUsersAsync(string?search,bool?active,int page,int size,CancellationToken ct){var q=db.Users.AsNoTracking().AsQueryable();if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DisplayName.Contains(search)||x.Username.Contains(search));if(active.HasValue)q=q.Where(x=>x.IsActive==active);var total=await q.CountAsync(ct);var p=Math.Max(1,page);var s=Math.Clamp(size,20,100);var users=await q.OrderBy(x=>x.Username).Skip((p-1)*s).Take(s).Select(x=>new{ x.UserId,x.Username,x.DisplayName,x.Email,x.IsActive,x.LastLoginAt,Roles=x.UserRoles.Select(r=>r.Role.RoleCode).OrderBy(c=>c).ToList()}).ToListAsync(ct);var userIds=users.Select(x=>x.UserId).ToList();Dictionary<Guid, List<Guid>> projectMap;try{projectMap=await db.ProjectUsers.AsNoTracking().Where(pu=>userIds.Contains(pu.UserId)).GroupBy(pu=>pu.UserId).ToDictionaryAsync(g=>g.Key,g=>g.Select(pu=>pu.ProjectId).ToList(),ct);}catch{projectMap=new();}var items=users.Select(x=>new UserDto(x.UserId,x.Username,x.DisplayName,x.Email,x.IsActive,x.LastLoginAt,x.Roles,projectMap.TryGetValue(x.UserId,out var pids)?pids:[])).ToList();return new(total,items);}
  public async Task<IReadOnlyList<RoleDto>>RolesAsync(CancellationToken ct)=>await db.Roles.AsNoTracking().OrderBy(x=>x.RoleCode).Select(x=>new RoleDto(x.RoleId,x.RoleCode,x.RoleName,x.Description,x.IsActive,x.RolePermissions.Select(p=>p.Permission.PermissionCode).OrderBy(c=>c).ToList())).ToListAsync(ct);public Task<Role?>FindRoleAsync(Guid id,CancellationToken ct)=>db.Roles.SingleOrDefaultAsync(x=>x.RoleId==id,ct);public Task<bool>RoleCodeExistsAsync(string code,CancellationToken ct)=>db.Roles.AnyAsync(x=>x.RoleCode==code,ct);public Task AddRoleAsync(Role role,CancellationToken ct)=>db.Roles.AddAsync(role,ct).AsTask();public async Task AssignPermissionsAsync(Guid roleId,IReadOnlyList<Guid>permissionIds,CancellationToken ct){await db.RolePermissions.Where(x=>x.RoleId==roleId).ExecuteDeleteAsync(ct);db.RolePermissions.AddRange(permissionIds.Distinct().Select(x=>new RolePermission(roleId,x)));}public async Task<IReadOnlyList<PermissionDto>>PermissionsAsync(CancellationToken ct)=>await db.Permissions.AsNoTracking().OrderBy(x=>x.ModuleArea).ThenBy(x=>x.PermissionCode).Select(x=>new PermissionDto(x.PermissionId,x.PermissionCode,x.PermissionName,x.ModuleArea)).ToListAsync(ct);public Task SaveAsync(CancellationToken ct)=>db.SaveChangesAsync(ct);
}
