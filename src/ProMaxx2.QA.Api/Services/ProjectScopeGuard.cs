using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ตรวจสิทธิ์ระดับ Project สำหรับ endpoint ที่รับเพียง ID ของ record (เช่น <c>modules/{id}</c>,
/// <c>test-suites/{id}</c>) ซึ่ง <see cref="ProjectAccessFilter"/> มองไม่เห็น Project จาก route/query/body ได้เอง.
/// แต่ละ method คืน <c>true</c> เมื่อ record ไม่มีอยู่ เพื่อให้ service เดิมตอบ 404 ตามปกติ และคืน <c>false</c>
/// เฉพาะเมื่อ record อยู่ใน Project ที่ผู้ใช้ปัจจุบันไม่ได้เป็นสมาชิก (controller ควรตอบ 404 เพื่อไม่เปิดเผยว่ามี record อยู่)</summary>
public sealed class ProjectScopeGuard(QaDbContext db, ProjectAccessContext projectCtx)
{
    public bool Allows(Guid projectId) => projectCtx.AllowedProjectIds.Contains(projectId);

    public Task<bool> ProjectAsync(Guid projectId, CancellationToken ct) => Task.FromResult(Allows(projectId));

    public async Task<bool> ModuleAsync(Guid moduleId, CancellationToken ct)
        => Check(await db.Modules.AsNoTracking().Where(x => x.ModuleId == moduleId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> TestSuiteAsync(Guid testSuiteId, CancellationToken ct)
        => Check(await db.TestSuites.AsNoTracking().Where(x => x.TestSuiteId == testSuiteId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> ReleaseAsync(Guid releaseId, CancellationToken ct)
        => Check(await db.Releases.AsNoTracking().Where(x => x.ReleaseId == releaseId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> TestCycleAsync(Guid testCycleId, CancellationToken ct)
        => Check(await db.TestCycles.AsNoTracking().Where(x => x.TestCycleId == testCycleId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> TestCycleCaseAsync(Guid testCycleCaseId, CancellationToken ct)
        => Check(await db.TestCycleCases.AsNoTracking().Where(x => x.TestCycleCaseId == testCycleCaseId).Select(x => (Guid?)x.Cycle.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> DefectAsync(Guid defectId, CancellationToken ct)
        => Check(await db.Defects.AsNoTracking().Where(x => x.DefectId == defectId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> TestCaseAsync(Guid testCaseId, CancellationToken ct)
        => Check(await db.TestCases.AsNoTracking().Where(x => x.TestCaseId == testCaseId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> RequirementAsync(Guid requirementId, CancellationToken ct)
        => Check(await db.Requirements.AsNoTracking().Where(x => x.RequirementId == requirementId).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct));

    public async Task<bool> BuildAsync(Guid buildId, CancellationToken ct)
        => Check(await db.Builds.AsNoTracking().Where(x => x.BuildId == buildId).Join(db.Releases, b => b.ReleaseId, r => r.ReleaseId, (b, r) => (Guid?)r.ProjectId).FirstOrDefaultAsync(ct));

    /// <summary>ตรวจ route value ที่ชื่อบอกชนิด record ชัดเจน (releaseId, cycleId, cycleCaseId ฯลฯ) — ใช้จาก
    /// <see cref="ProjectAccessFilter"/> เพื่อปิด IDOR ของ endpoint ที่ไม่มี projectId ใน route/query</summary>
    public Task<bool> RouteValueAsync(string name, Guid value, CancellationToken ct) => name.ToLowerInvariant() switch
    {
        "releaseid" => ReleaseAsync(value, ct),
        "cycleid" or "testcycleid" or "sourcecycleid" => TestCycleAsync(value, ct),
        "cyclecaseid" or "testcyclecaseid" => TestCycleCaseAsync(value, ct),
        "defectid" => DefectAsync(value, ct),
        "testcaseid" => TestCaseAsync(value, ct),
        "requirementid" => RequirementAsync(value, ct),
        "buildid" => BuildAsync(value, ct),
        "testsuiteid" => TestSuiteAsync(value, ct),
        _ => Task.FromResult(true),
    };

    private bool Check(Guid? projectId) => projectId is not { } id || Allows(id);
}
