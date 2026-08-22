using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class RiskAcceptanceRepository(QaDbContext db, ProjectAccessContext projectCtx) : IRiskAcceptanceRepository
{
    public async Task<IReadOnlyList<RiskAcceptanceDto>> ListAsync(Guid? projectId, CancellationToken ct)
    {
        var q = db.RiskAcceptances.AsNoTracking().Where(x => !x.IsDeleted);
        if (projectId.HasValue) q = q.Where(x => x.ProjectId == projectId);
        else if (projectCtx.AllowedProjectIds.Length > 0) q = q.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        else q = q.Where(_ => false);
        return await q.OrderByDescending(x => x.CreatedAt).Select(Map()).ToListAsync(ct);
    }

    public async Task<RiskAcceptanceDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var q = db.RiskAcceptances.AsNoTracking().Where(x => x.RiskAcceptanceId == id && !x.IsDeleted);
        if (allowed.Length > 0) q = q.Where(x => allowed.Contains(x.ProjectId));
        return await q.Select(Map()).SingleOrDefaultAsync(ct);
    }

    public async Task<RiskAcceptance?> FindAsync(Guid id, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var e = await db.RiskAcceptances.SingleOrDefaultAsync(x => x.RiskAcceptanceId == id && !x.IsDeleted, ct);
        if (e is not null && allowed.Length > 0 && !allowed.Contains(e.ProjectId)) return null;
        return e;
    }

    public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct) => db.RiskAcceptances.AnyAsync(x => x.ProjectId == projectId && x.RiskCode == code, ct);
    public async Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct) => await db.RiskAcceptances.AsNoTracking().Where(x => x.ProjectId == projectId && x.RiskCode.StartsWith(prefix)).Select(x => x.RiskCode).ToListAsync(ct);
    public Task AddAsync(RiskAcceptance entity, CancellationToken ct) => db.RiskAcceptances.AddAsync(entity, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private System.Linq.Expressions.Expression<Func<RiskAcceptance, RiskAcceptanceDto>> Map() => x => new RiskAcceptanceDto(x.RiskAcceptanceId, x.ProjectId, x.ReleaseId, x.DefectId, x.RiskCode, x.Title, x.Issue, x.Impact, x.Probability, x.RiskLevel, x.Status, x.Workaround, x.TargetFix, x.QaRecommendation, x.OwnerUserId, x.OwnerUserId != null ? db.Users.Where(u => u.UserId == x.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null, db.Releases.Where(r => r.ReleaseId == x.ReleaseId).Select(r => r.ReleaseCode).FirstOrDefault(), db.Releases.Where(r => r.ReleaseId == x.ReleaseId).Select(r => r.Version).FirstOrDefault(), x.DefectId != null ? db.Defects.Where(d => d.DefectId == x.DefectId).Select(d => d.DefectCode).FirstOrDefault() : null, x.CreatedAt, x.ReviewDate, x.ReviewComment, x.ReviewedBy != null ? db.Users.Where(u => u.UserId == x.ReviewedBy).Select(u => u.DisplayName).FirstOrDefault() : null);
}
