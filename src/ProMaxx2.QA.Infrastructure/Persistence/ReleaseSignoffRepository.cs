using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ReleaseSignoffRepository(QaDbContext db, ProjectAccessContext projectCtx) : IReleaseSignoffRepository
{
    public async Task<ReleaseGateDto> GetGateAsync(Guid releaseId, Guid? buildId, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var release = await db.Releases.AsNoTracking().Where(r => r.ReleaseId == releaseId).Select(r => new { r.ProjectId }).FirstOrDefaultAsync(ct);
        if (release is null || (allowed.Length > 0 && !allowed.Contains(release.ProjectId))) return new ReleaseGateDto(false, 0, 0, 0, 0, false, 0, "NO_DATA");

        var baseCases = db.TestCycleCases.AsNoTracking().Where(c => !c.Cycle.IsDeleted && c.Cycle.ReleaseId == releaseId);
        var openP0 = await baseCases.CountAsync(c => (c.CurrentStatus == "Fail" || c.CurrentStatus == "Blocked") && c.Priority == "P0", ct);
        var openP1 = await baseCases.CountAsync(c => (c.CurrentStatus == "Fail" || c.CurrentStatus == "Blocked") && c.Priority == "P1", ct);
        var executed = await baseCases.CountAsync(c => c.CurrentStatus != "NotRun", ct);
        var passed = await baseCases.CountAsync(c => c.CurrentStatus == "Pass", ct);
        var regPassRate = executed == 0 ? 0 : Math.Round(passed * 100m / executed, 1);

        var requirementIds = await db.Requirements.AsNoTracking().Where(r => !r.IsDeleted && r.IsInScope && r.ReleaseId == releaseId).Select(r => r.RequirementId).ToListAsync(ct);
        var covered = await db.RequirementTestCases.AsNoTracking().Where(x => requirementIds.Contains(x.RequirementId)).Select(x => x.RequirementId).Distinct().ToListAsync(ct);
        var coverage = requirementIds.Count == 0 ? 0 : Math.Round(covered.Count * 100m / requirementIds.Count, 1);

        var approvedRisks = await db.RiskAcceptances.AsNoTracking().CountAsync(r => r.ReleaseId == releaseId && r.Status == "Approved", ct);
        var gateBuild = buildId ?? await db.Builds.AsNoTracking().Where(b => b.ReleaseId == releaseId).OrderByDescending(b => b.BuildNumber).Select(b => b.BuildId).FirstOrDefaultAsync(ct);
        var smoke = gateBuild != Guid.Empty
            && await db.AutomationQualityGateRuns.AsNoTracking().AnyAsync(g => g.BuildId == gateBuild && g.TargetApp == "pos" && g.Status == "Passed", ct)
            && await db.AutomationQualityGateRuns.AsNoTracking().AnyAsync(g => g.BuildId == gateBuild && g.TargetApp == "app" && g.Status == "Passed", ct);

        var decision = ReleaseGate.Evaluate(new ReleaseGateInput(openP0, openP1, approvedRisks > 0, smoke));
        var decisionText = decision switch { ReleaseDecision.Go => "GO", ReleaseDecision.ConditionalGo => "CONDITIONAL_GO", _ => "NO_GO" };
        return new ReleaseGateDto(smoke, openP0, openP1, coverage, regPassRate, smoke, approvedRisks, decisionText);
    }

    public async Task<IReadOnlyList<ReleaseSignoffDto>> ListAsync(Guid releaseId, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var release = await db.Releases.AsNoTracking().Where(r => r.ReleaseId == releaseId).Select(r => new { r.ProjectId }).FirstOrDefaultAsync(ct);
        if (release is null || (allowed.Length > 0 && !allowed.Contains(release.ProjectId))) return [];
        return await db.ReleaseSignoffs.AsNoTracking().Where(x => x.ReleaseId == releaseId).OrderByDescending(x => x.CreatedAt).Select(x => new ReleaseSignoffDto(x.ReleaseSignoffId, x.ReleaseId, x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-", x.SignoffType, x.Decision, x.Comment, x.SignoffByUserId != null ? db.Users.Where(u => u.UserId == x.SignoffByUserId).Select(u => u.DisplayName).FirstOrDefault() : null, x.CreatedAt)).ToListAsync(ct);
    }

    public Task<bool> BuildBelongsToReleaseAsync(Guid releaseId, Guid buildId, CancellationToken ct) => db.Builds.AnyAsync(b => b.BuildId == buildId && b.ReleaseId == releaseId, ct);
    public Task<string?> GetBuildNumberAsync(Guid buildId, CancellationToken ct) => db.Builds.AsNoTracking().Where(b => b.BuildId == buildId).Select(b => b.BuildNumber).FirstOrDefaultAsync(ct);
    public Task AddAsync(ReleaseSignoff entity, CancellationToken ct) => db.ReleaseSignoffs.AddAsync(entity, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
