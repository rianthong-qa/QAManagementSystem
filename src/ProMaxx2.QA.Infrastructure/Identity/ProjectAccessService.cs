using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Infrastructure.Identity;

public sealed class ProjectAccessService(QaDbContext db)
{
 public async Task<Guid[]> GetAllowedProjectIdsAsync(Guid userId, CancellationToken ct)
  => await db.ProjectUsers.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.ProjectId).ToArrayAsync(ct);
 public async Task<bool> HasAccessAsync(Guid userId, Guid projectId, CancellationToken ct)
  => await db.ProjectUsers.AnyAsync(x => x.UserId == userId && x.ProjectId == projectId, ct);
}
