using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ReleaseSignoffRepository(QaDbContext db, ProjectAccessContext projectCtx, IOptionsMonitor<DefectGateOptions> defectGate) : IReleaseSignoffRepository
{
    public async Task<ReleaseGateDto> GetGateAsync(Guid releaseId, Guid? buildId, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var release = await db.Releases.AsNoTracking().Where(r => r.ReleaseId == releaseId).Select(r => new { r.ProjectId }).FirstOrDefaultAsync(ct);
        if (release is null || (allowed.Length > 0 && !allowed.Contains(release.ProjectId))) return new ReleaseGateDto(0, 0, 0, 0, false, 0, "NO_DATA");

        // ขอบเขตเดียวกับ Dashboard: เลือก Build = นับเฉพาะ Cycle ของ Build นั้น และไม่นับ Test Case ที่ถูกลบ
        // Module ที่ซ่อน: ไม่นับใน Coverage/Pass Rate แต่ยังนับ P0/P1 ค้างและ Defect ค้าง — ไม่ให้ซ่อน Module แล้วเปลี่ยน NO-GO เป็น GO ได้
        var hiddenModules = db.Modules.Where(m => !m.IsActive);
        var scopeCases = db.TestCycleCases.AsNoTracking().Where(c => !c.Cycle.IsDeleted && c.Cycle.ReleaseId == releaseId && (!buildId.HasValue || c.Cycle.BuildId == buildId) && !c.TestCase.IsDeleted);
        var baseCases = scopeCases.Where(c => !hiddenModules.Any(m => m.ModuleId == c.TestCase.ModuleId));
        var openP0 = await scopeCases.CountAsync(c => (c.CurrentStatus == "Fail" || c.CurrentStatus == "Blocked") && c.Priority == "P0", ct);
        var openP1 = await scopeCases.CountAsync(c => (c.CurrentStatus == "Fail" || c.CurrentStatus == "Blocked") && c.Priority == "P1", ct);
        var executed = await baseCases.CountAsync(c => c.CurrentStatus != "NotRun", ct);
        var passed = await baseCases.CountAsync(c => c.CurrentStatus == "Pass", ct);
        var passRate = executed == 0 ? 0 : Math.Round(passed * 100m / executed, 1);

        var requirementIds = await db.Requirements.AsNoTracking().Where(r => !r.IsDeleted && r.IsInScope && r.ReleaseId == releaseId && !hiddenModules.Any(m => m.ModuleId == r.ModuleId)).Select(r => r.RequirementId).ToListAsync(ct);
        var covered = await ReleaseReadinessQueries.CoveredRequirementIds(db, requirementIds).CountAsync(ct);
        var coverage = requirementIds.Count == 0 ? 0 : Math.Round(covered * 100m / requirementIds.Count, 1);

        var approvedRisks = await db.RiskAcceptances.AsNoTracking().CountAsync(r => r.ReleaseId == releaseId && !r.IsDeleted && r.Status == "Approved", ct);
        var smoke = await ReleaseReadinessQueries.SmokeAsync(db, release.ProjectId, buildId, ct);
        // Defect ทั้ง Release (ไม่กรอง Build — Defect ที่ยังค้างจาก Build ก่อนก็ยังกันการปล่อย) รวม Module ที่ซ่อน
        var defects = await ReleaseReadinessQueries.DefectCountsAsync(db, db.Defects.Where(d => d.ReleaseId == releaseId), ct);
        var gateEnabled = defectGate.CurrentValue.GateEnabled;
        var (decision, reason) = ReleaseGate.Assess(new ReleaseGateInput(openP0, openP1, approvedRisks > 0, smoke.Passed, defects,
            requirementIds.Count == 0 ? null : coverage, executed == 0 ? null : passRate, gateEnabled));
        var decisionText = decision switch { ReleaseDecision.Go => "GO", ReleaseDecision.ConditionalGo => "CONDITIONAL_GO", _ => "NO_GO" };
        return new ReleaseGateDto(openP0, openP1, coverage, passRate, true, approvedRisks, decisionText, smoke.Status,
            gateEnabled, defects.OpenCritical, defects.OpenHigh, defects.UnverifiedResolved, defects.BlockersWithoutRisk, defects.BlockersWithRisk, reason);
    }

    public async Task<IReadOnlyList<ReleaseSignoffDto>> ListAsync(Guid releaseId, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var release = await db.Releases.AsNoTracking().Where(r => r.ReleaseId == releaseId).Select(r => new { r.ProjectId }).FirstOrDefaultAsync(ct);
        if (release is null || (allowed.Length > 0 && !allowed.Contains(release.ProjectId))) return [];
        return await db.ReleaseSignoffs.AsNoTracking().Where(x => x.ReleaseId == releaseId).OrderByDescending(x => x.CreatedAt).Select(x => new ReleaseSignoffDto(x.ReleaseSignoffId, x.ReleaseId, x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-", x.SignoffType, x.Decision, x.Comment, x.SignoffByUserId != null ? db.Users.Where(u => u.UserId == x.SignoffByUserId).Select(u => u.DisplayName).FirstOrDefault() : null, x.CreatedAt)).ToListAsync(ct);
    }

    public Task<bool> BuildBelongsToReleaseAsync(Guid releaseId, Guid buildId, CancellationToken ct) => db.Builds.AnyAsync(b => b.BuildId == buildId && b.ReleaseId == releaseId, ct);
    public Task<bool> ExistsForBuildAndTypeAsync(Guid releaseId, Guid buildId, string signoffType, CancellationToken ct) => db.ReleaseSignoffs.AnyAsync(x => x.ReleaseId == releaseId && x.BuildId == buildId && x.SignoffType == signoffType, ct);
    public Task<string?> GetBuildNumberAsync(Guid buildId, CancellationToken ct) => db.Builds.AsNoTracking().Where(b => b.BuildId == buildId).Select(b => b.BuildNumber).FirstOrDefaultAsync(ct);
    public Task AddAsync(ReleaseSignoff entity, CancellationToken ct) => db.ReleaseSignoffs.AddAsync(entity, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
