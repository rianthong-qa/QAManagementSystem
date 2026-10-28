using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// Defect ของ Build (DEFECT_BUILD_LIFECYCLE.md เฟส 3) — เมื่อได้ Build ใหม่ QA เปิดดู 3 กลุ่ม:
/// เจอใน Build นี้ / ยังไม่แก้ที่ค้างจาก Build ก่อน / คิวตรวจซ้ำ (Resolved ที่ Fix Build คือ Build นี้หรือก่อนหน้า หรือไม่ระบุ)
/// แล้วสร้าง Retest Cycle จาก Test Case ที่ผูกกับคิวตรวจซ้ำ
/// </summary>
[ApiController, Route("api/v1/builds/{buildId:guid}"), Authorize(Policy = "DefectView"), RequireProjectAccess]
public sealed class BuildDefectsController(QaDbContext db, ProjectAccessContext projectCtx) : ControllerBase
{
    /// <summary>จำนวนรายการต่อกลุ่มที่ส่งกลับ (นับรวมยังครบ) — กลุ่มใหญ่ดูต่อที่หน้า Defect</summary>
    public const int ItemsPerGroup = 100;

    [HttpGet("defect-carryover")]
    public async Task<ActionResult<BuildDefectCarryoverDto>> Carryover(Guid buildId, CancellationToken ct)
    {
        var order = await DefectBuilds.ReleaseOrderAsync(db, buildId, ct);
        if (order is null || !CanAccess(order.ProjectId)) return NotFound();
        var rows = await CarryoverQuery(db, order).ToListAsync(ct);
        return Ok(new BuildDefectCarryoverDto(order.BuildId, order.BuildNumber, order.ReleaseId, order.ReleaseCode,
            Group(rows.Where(x => x.FoundHere)), Group(rows.Where(x => x.CarriedOver)), Group(rows.Where(x => x.InRetestQueue))));
    }

    /// <summary>
    /// Defect ของ Release ที่อยู่อย่างน้อย 1 กลุ่ม พร้อมธงกลุ่ม — query เดียว (ไม่ใช่ 2 query ต่อกลุ่ม):
    /// เจอใน Build นี้ (ทุกสถานะ) / ยังไม่แก้และเจอใน Build ก่อนหน้า / คิวตรวจซ้ำ (DefectBuilds.RetestQueue)
    /// </summary>
    public static IQueryable<CarryoverRow> CarryoverQuery(QaDbContext db, ReleaseBuildOrder order)
    {
        var buildId = order.BuildId;
        var earlier = order.Earlier;
        var release = db.Defects.AsNoTracking().Where(d => !d.IsDeleted && d.ReleaseId == order.ReleaseId);
        var queue = DefectBuilds.RetestQueue(release, order).Select(d => d.DefectId);
        return release
            .Where(d => d.BuildId == buildId || DefectStatusGroups.NotDone.Contains(d.Status))
            .Select(d => new CarryoverRow(
                new BuildDefectItemDto(d.DefectId, d.DefectCode, d.Title, d.Severity, d.Status, d.BuildId, d.FixedInBuildId, d.ReopenCount, d.AssigneeUserId, d.CreatedAt),
                d.BuildId == buildId,
                DefectStatusGroups.Unresolved.Contains(d.Status) && d.BuildId.HasValue && earlier.Contains(d.BuildId.Value),
                queue.Contains(d.DefectId)));
    }

    /// <summary>
    /// Retest Cycle บน Build นี้ จาก Test Case ที่ผูกกับคิวตรวจซ้ำ (revision ล่าสุดของ Test Case) — ผลผ่าน/ไม่ผ่านของเคส QA ปิด/Reopen Defect เอง
    /// (เคส Fail ระบบ Reopen ให้อัตโนมัติตามกฎ DefectAutoCreateService)
    /// </summary>
    [HttpPost("retest-cycle"), Authorize(Policy = "ExecutionRun")]
    public async Task<ActionResult<RetestCycleResultDto>> CreateRetestCycle(Guid buildId, CreateRetestCycleRequest r, CancellationToken ct)
    {
        var order = await DefectBuilds.ReleaseOrderAsync(db, buildId, ct);
        if (order is null || !CanAccess(order.ProjectId)) return NotFound();
        if (!await db.Builds.AnyAsync(b => b.BuildId == buildId && b.IsActive, ct)) return BadRequest("Build นี้ถูกปิดใช้งานแล้ว");
        if (await db.Releases.AnyAsync(x => x.ReleaseId == order.ReleaseId && (x.Status == "Released" || x.Status == "Cancelled"), ct)) return BadRequest("Release นี้ปิดแล้ว สร้าง Test Cycle ไม่ได้");
        if (!await db.TestEnvironments.AnyAsync(x => x.TestEnvironmentId == r.EnvironmentId && x.ProjectId == order.ProjectId && x.IsActive, ct)) return BadRequest("ไม่พบ Environment ที่ใช้งานได้ใน Project นี้");
        var queue = DefectBuilds.RetestQueue(db.Defects.Where(d => !d.IsDeleted && d.ReleaseId == order.ReleaseId), order);
        var cases = await db.DefectTestCaseLinks.Where(l => queue.Any(d => d.DefectId == l.DefectId))
            .Join(db.TestCases.Where(t => !t.IsDeleted), l => l.TestCaseId, t => t.TestCaseId, (l, t) => new { t.TestCaseId, t.TestCaseCode, t.RevisionNo, t.Priority })
            .Distinct().ToListAsync(ct);
        if (cases.Count == 0) return BadRequest("คิวตรวจซ้ำของ Build นี้ไม่มี Defect ที่ผูก Test Case — ตรวจซ้ำจากหน้า Defect แทน");
        var projectCode = await db.Projects.Where(p => p.ProjectId == order.ProjectId).Select(p => p.ProjectCode).SingleAsync(ct);
        var prefix = $"{projectCode}-CYC";
        var code = BusinessCodeGenerator.Next(prefix, await db.TestCycles.Where(c => c.ProjectId == order.ProjectId && c.CycleCode.StartsWith(prefix)).Select(c => c.CycleCode).ToListAsync(ct));
        var name = string.IsNullOrWhiteSpace(r.CycleName) ? $"Retest Build {order.BuildNumber}" : r.CycleName.Trim();
        var userId = UserId();
        var cycle = new TestCycle(order.ProjectId, order.ReleaseId, buildId, r.EnvironmentId, null, code, name.Length > 200 ? name[..200] : name, "Regression", DateTime.UtcNow.Date, null, userId,
            $"ตรวจซ้ำ Defect ที่ Resolved ({cases.Count} Test Case) — สร้างจากหน้า Defect ของ Build", userId);
        cycle.ChangeStatus("InProgress"); // เปิดใช้ทันที — Execution Workspace แสดงเฉพาะ Cycle ที่ InProgress และหน้า Build พาไปรันต่อเลย
        foreach (var (item, index) in cases.OrderBy(x => x.Priority).ThenBy(x => x.TestCaseCode).Select((x, i) => (x, i)))
            cycle.Cases.Add(new TestCycleCase(cycle.TestCycleId, item.TestCaseId, item.RevisionNo, item.Priority, index + 1));
        db.TestCycles.Add(cycle);
        db.AuditLogs.Add(new Domain.Governance.AuditLog(userId, "Created", "TestCycle", cycle.TestCycleId.ToString(), $"สร้าง Retest Cycle {code} บน Build {order.BuildNumber} จากคิวตรวจซ้ำ ({cases.Count} Test Case)", null, null, null));
        await db.SaveChangesAsync(ct);
        return Ok(new RetestCycleResultDto(cycle.TestCycleId, code, name, cases.Count));
    }

    private static BuildDefectGroupDto Group(IEnumerable<CarryoverRow> rows)
    {
        var items = rows.Select(x => x.Item).ToList();
        // รุนแรงสุดก่อน แล้วเก่าสุดก่อน (ค้างนานสุด)
        var top = items.OrderBy(d => d.Severity switch { "Critical" => 0, "High" => 1, "Medium" => 2, _ => 3 }).ThenBy(d => d.CreatedAt).Take(ItemsPerGroup).ToList();
        return new(items.Count, items.GroupBy(d => d.Severity).ToDictionary(g => g.Key, g => g.Count()), top);
    }

    private bool CanAccess(Guid projectId) => projectCtx.AllowedProjectIds.Length == 0 || projectCtx.AllowedProjectIds.Contains(projectId);
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}

public sealed record CarryoverRow(BuildDefectItemDto Item, bool FoundHere, bool CarriedOver, bool InRetestQueue);
public sealed record BuildDefectItemDto(Guid DefectId, string DefectCode, string Title, string Severity, string Status, Guid? FoundInBuildId, Guid? FixedInBuildId, int ReopenCount, Guid? AssigneeUserId, DateTime CreatedAt);
public sealed record BuildDefectGroupDto(int Total, IReadOnlyDictionary<string, int> BySeverity, IReadOnlyList<BuildDefectItemDto> Items);
/// <summary>FoundHere = เจอใน Build นี้ (ทุกสถานะ), CarriedOver = ยังไม่แก้และเจอใน Build ก่อนหน้า, RetestQueue = Resolved ที่ต้องตรวจซ้ำบน Build นี้</summary>
public sealed record BuildDefectCarryoverDto(Guid BuildId, string BuildNumber, Guid ReleaseId, string ReleaseCode, BuildDefectGroupDto FoundHere, BuildDefectGroupDto CarriedOver, BuildDefectGroupDto RetestQueue);
public sealed record CreateRetestCycleRequest(Guid EnvironmentId, string? CycleName = null);
public sealed record RetestCycleResultDto(Guid TestCycleId, string CycleCode, string CycleName, int CaseCount);
