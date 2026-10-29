using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.TestPlanning;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class TestPlanRepository(QaDbContext db, ProjectAccessContext projectCtx) : ITestPlanRepository
{
    /// <summary>ผล Execution ที่จบแล้ว — ใช้นับ Retest (ไม่นับแถว InProgress/NotRun จาก Save Progress)</summary>
    private static readonly string[] FinalExecutionStatuses = ["Pass", "Fail", "Blocked", "Skipped"];

    public async Task<TestPlanListResultDto> ListAsync(Guid? projectId, Guid? releaseId, string? status, string? planType, Guid? ownerUserId, string? search, int page, int size, CancellationToken ct)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);
        var plans = db.TestPlans.AsNoTracking().Where(x => !x.IsDeleted);
        if (projectId.HasValue) plans = plans.Where(x => x.ProjectId == projectId);
        else if (projectCtx.AllowedProjectIds.Length > 0) plans = plans.Where(x => projectCtx.AllowedProjectIds.Contains(x.ProjectId));
        else plans = plans.Where(_ => false);
        if (releaseId.HasValue) plans = plans.Where(x => x.ReleaseId == releaseId);
        if (!string.IsNullOrWhiteSpace(planType)) plans = plans.Where(x => x.PlanType == planType.Trim());
        if (ownerUserId.HasValue) plans = plans.Where(x => x.OwnerUserId == ownerUserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            plans = plans.Where(x => x.PlanCode.Contains(term) || x.PlanName.Contains(term));
        }

        var rows = WithStatus(plans);
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(x => x.Status == status.Trim());

        var total = await rows.CountAsync(ct);
        var pageRows = await rows.OrderByDescending(x => x.Row.Plan.CreatedAt).ThenBy(x => x.Row.Plan.PlanCode)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new
            {
                x.Row.Plan.TestPlanId, x.Row.Plan.ProjectId, x.Row.Plan.ReleaseId, x.Row.Plan.PlanCode, x.Row.Plan.PlanName, x.Row.Plan.PlanType,
                x.Row.Plan.StartDate, x.Row.Plan.EndDate, x.Row.Plan.OwnerUserId, x.Row.Plan.LifecycleStatus, x.Row.Plan.IsFrozen, x.Row.Plan.CreatedAt,
                ReleaseCode = db.Releases.Where(r => r.ReleaseId == x.Row.Plan.ReleaseId).Select(r => r.ReleaseCode).FirstOrDefault(),
                OwnerName = x.Row.Plan.OwnerUserId != null ? db.Users.Where(u => u.UserId == x.Row.Plan.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null,
                x.Status, x.Row.RunCount, x.Row.TotalCases, x.Row.Untested, x.Row.Passed, x.Row.Failed, x.Row.Blocked, x.Row.Skipped,
            })
            .ToListAsync(ct);

        var items = pageRows.Select(x =>
        {
            var completed = x.Passed + x.Failed + x.Blocked + x.Skipped;
            var touched = x.TotalCases - x.Untested;
            return new TestPlanListItemDto(x.TestPlanId, x.ProjectId, x.ReleaseId, x.ReleaseCode, x.PlanCode, x.PlanName, x.PlanType, x.StartDate, x.EndDate, x.OwnerUserId, x.OwnerName,
                x.Status, x.LifecycleStatus, x.IsFrozen, x.RunCount, x.TotalCases, touched, completed, x.Passed, Percent(touched, x.TotalCases), completed == 0 ? null : Percent(x.Passed, completed), x.CreatedAt);
        }).ToList();

        var summary = await SummaryAsync(rows.Select(x => x.Row.Plan.TestPlanId), ct);
        return new TestPlanListResultDto(new PagedResult<TestPlanListItemDto>(total, items), summary);
    }

    public async Task<TestPlanDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var plans = db.TestPlans.AsNoTracking().Where(x => x.TestPlanId == id && !x.IsDeleted);
        if (allowed.Length > 0) plans = plans.Where(x => allowed.Contains(x.ProjectId));
        var plan = await WithStatus(plans)
            .Select(x => new
            {
                x.Row.Plan, x.Status,
                ReleaseCode = db.Releases.Where(r => r.ReleaseId == x.Row.Plan.ReleaseId).Select(r => r.ReleaseCode).FirstOrDefault(),
                OwnerName = x.Row.Plan.OwnerUserId != null ? db.Users.Where(u => u.UserId == x.Row.Plan.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null,
            })
            .SingleOrDefaultAsync(ct);
        if (plan is null) return null;

        var runs = await MapRunsAsync(RunsOf(id).OrderBy(r => r.CreatedAt).ThenBy(r => r.RunCode), ct);
        var summary = await SummaryAsync(db.TestPlans.Where(x => x.TestPlanId == id).Select(x => x.TestPlanId), ct);
        var p = plan.Plan;
        return new TestPlanDto(p.TestPlanId, p.ProjectId, p.ReleaseId, plan.ReleaseCode, p.PlanCode, p.PlanName, p.PlanType, p.StartDate, p.EndDate, p.OwnerUserId, plan.OwnerName,
            plan.Status, p.LifecycleStatus, p.IsFrozen, p.FrozenAt, p.FrozenByUserId, p.CopiedFromTestPlanId, p.Notes, p.CreatedAt, p.UpdatedAt, summary, runs);
    }

    public async Task<PagedResult<TestPlanRunDto>> ListRunsAsync(Guid planId, string? status, string? configurationKey, Guid? ownerUserId, int page, int size, CancellationToken ct)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 500);
        var runs = RunsOf(planId);
        if (!string.IsNullOrWhiteSpace(status)) runs = runs.Where(r => r.Cycle.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(configurationKey)) runs = runs.Where(r => r.ConfigurationKey == configurationKey.Trim().ToLowerInvariant());
        if (ownerUserId.HasValue) runs = runs.Where(r => r.OwnerUserId == ownerUserId);
        var total = await runs.CountAsync(ct);
        var rows = await MapRunsAsync(runs.OrderBy(r => r.CreatedAt).ThenBy(r => r.RunCode).Skip((page - 1) * size).Take(size), ct);
        return new PagedResult<TestPlanRunDto>(total, rows);
    }

    public async Task<TestPlanRunDto?> GetRunAsync(Guid planId, Guid runId, CancellationToken ct) => (await MapRunsAsync(RunsOf(planId).Where(r => r.TestPlanRunId == runId), ct)).SingleOrDefault();

    public async Task<TestPlan?> FindAsync(Guid id, CancellationToken ct)
    {
        var allowed = projectCtx.AllowedProjectIds;
        var e = await db.TestPlans.SingleOrDefaultAsync(x => x.TestPlanId == id && !x.IsDeleted, ct);
        if (e is not null && allowed.Length > 0 && !allowed.Contains(e.ProjectId)) return null;
        return e;
    }

    public Task<TestPlanRun?> FindRunAsync(Guid planId, Guid runId, CancellationToken ct) => db.TestPlanRuns.SingleOrDefaultAsync(x => x.TestPlanId == planId && x.TestPlanRunId == runId, ct);

    public async Task<(int Total, int NotTerminal)> RunStatusCountsAsync(Guid testPlanId, CancellationToken ct)
    {
        var counts = await db.TestPlanRuns.AsNoTracking().Where(r => r.TestPlanId == testPlanId)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), NotTerminal = g.Count(r => r.Cycle.Status != "Completed" && r.Cycle.Status != "Closed" && r.Cycle.Status != "Cancelled") })
            .FirstOrDefaultAsync(ct);
        return counts is null ? (0, 0) : (counts.Total, counts.NotTerminal);
    }

    public Task<bool> CycleHasExecutionsAsync(Guid testCycleId, CancellationToken ct) => db.TestExecutions.AnyAsync(e => e.CycleCase.TestCycleId == testCycleId, ct);
    public void RemoveRun(TestPlanRun entity) => db.TestPlanRuns.Remove(entity);
    public void AddAudit(AuditLog log) => db.AuditLogs.Add(log);

    public async Task RunInTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) { await work(); return; }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await work();
        await transaction.CommitAsync(ct);
    }

    public Task<bool> CodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct) => db.TestPlans.AnyAsync(x => x.ProjectId == projectId && x.PlanCode == code && (excludeId == null || x.TestPlanId != excludeId), ct);
    public async Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct) => await db.TestPlans.AsNoTracking().Where(x => x.ProjectId == projectId && x.PlanCode.StartsWith(prefix)).Select(x => x.PlanCode).ToListAsync(ct);
    public Task<string?> ReleaseStatusAsync(Guid projectId, Guid releaseId, CancellationToken ct) => db.Releases.AsNoTracking().Where(x => x.ReleaseId == releaseId && x.ProjectId == projectId).Select(x => x.Status).FirstOrDefaultAsync(ct);
    public Task<TestPlanCycleRef?> FindCycleAsync(Guid testCycleId, CancellationToken ct) => db.TestCycles.AsNoTracking().Where(x => x.TestCycleId == testCycleId).Select(x => new TestPlanCycleRef(x.TestCycleId, x.ProjectId, x.ReleaseId, x.CycleCode, x.CycleName, x.IsDeleted)).FirstOrDefaultAsync(ct);
    public Task<bool> IsCycleLinkedAsync(Guid testCycleId, CancellationToken ct) => db.TestPlanRuns.AnyAsync(x => x.TestCycleId == testCycleId, ct);
    public Task<bool> RunCodeExistsAsync(Guid testPlanId, string runCode, CancellationToken ct) => db.TestPlanRuns.AnyAsync(x => x.TestPlanId == testPlanId && x.RunCode == runCode, ct);
    public Task AddAsync(TestPlan entity, CancellationToken ct) => db.TestPlans.AddAsync(entity, ct).AsTask();
    public Task AddRunAsync(TestPlanRun entity, CancellationToken ct) => db.TestPlanRuns.AddAsync(entity, ct).AsTask();
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            // ตรวจซ้ำก่อนบันทึกแล้ว แต่คำขอที่มาพร้อมกันยังชน Unique index ได้ — แปลงเป็น 409 ที่อ่านเข้าใจได้
            if (sql.Message.Contains("IX_TestPlanRuns_TestCycleId", StringComparison.Ordinal)) throw new TestPlanRuleException(TestPlanErrorCodes.CycleLinked, "Test cycle นี้เพิ่งถูกเพิ่มเข้า Test Plan อื่น");
            throw new TestPlanRuleException(TestPlanErrorCodes.DuplicateCode, "รหัส Test Plan หรือ Run ซ้ำกับรายการที่เพิ่งบันทึก");
        }
    }

    /// <summary>
    /// ตัวนับต่อ Plan + สถานะที่คำนวณจาก Run (§2.3) ใน Query เดียว — ใช้ทั้ง filter status และค่าที่แสดง
    /// ลำดับ: Cancelled → Closed → Completed (ทุก Run จบ) → InProgress (มี Run กำลังทำหรือมีเคสถูกแตะ) → Draft
    /// </summary>
    public static IQueryable<PlanRowWithStatus> WithStatus(IQueryable<TestPlan> plans) => plans
        .Select(p => new PlanRow
        {
            Plan = p,
            RunCount = p.Runs.Count(),
            InProgressRuns = p.Runs.Count(r => r.Cycle.Status == "InProgress"),
            TerminalRuns = p.Runs.Count(r => r.Cycle.Status == "Completed" || r.Cycle.Status == "Closed" || r.Cycle.Status == "Cancelled"),
            TotalCases = p.Runs.SelectMany(r => r.Cycle.Cases).Count(),
            Untested = p.Runs.SelectMany(r => r.Cycle.Cases).Count(c => c.CurrentStatus == "NotRun"),
            Passed = p.Runs.SelectMany(r => r.Cycle.Cases).Count(c => c.CurrentStatus == "Pass"),
            Failed = p.Runs.SelectMany(r => r.Cycle.Cases).Count(c => c.CurrentStatus == "Fail"),
            Blocked = p.Runs.SelectMany(r => r.Cycle.Cases).Count(c => c.CurrentStatus == "Blocked"),
            Skipped = p.Runs.SelectMany(r => r.Cycle.Cases).Count(c => c.CurrentStatus == "Skipped"),
        })
        .Select(x => new PlanRowWithStatus
        {
            Row = x,
            Status = x.Plan.LifecycleStatus == TestPlanLifecycleStatuses.Cancelled ? TestPlanStatuses.Cancelled
                : x.Plan.LifecycleStatus == TestPlanLifecycleStatuses.Closed ? TestPlanStatuses.Closed
                : x.RunCount > 0 && x.TerminalRuns == x.RunCount ? TestPlanStatuses.Completed
                : x.InProgressRuns > 0 || x.TotalCases > x.Untested ? TestPlanStatuses.InProgress
                : TestPlanStatuses.Draft,
        });

    /// <summary>ตัวนับรวมของหลาย Plan — 3 Query แบบ aggregate ไม่ยิงต่อ Run (§3.4)</summary>
    private async Task<TestPlanSummaryDto> SummaryAsync(IQueryable<Guid> planIds, CancellationToken ct)
    {
        var runs = db.TestPlanRuns.Where(r => planIds.Contains(r.TestPlanId));
        var runCount = await runs.CountAsync(ct);
        if (runCount == 0) return TestPlanSummaryDto.Empty;
        var statusCounts = await runs.SelectMany(r => r.Cycle.Cases).GroupBy(c => c.CurrentStatus).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var retestCounts = await RetestCountsQuery(db, runs).ToListAsync(ct);
        return Summarize(runCount, statusCounts, retestCounts);
    }

    /// <summary>
    /// จำนวน Execution ที่ไม่ถูกลบและมีผลจบแล้ว ของแต่ละเคสที่มีมากกว่า 1 แถว (= เคส Retest)
    /// ไม่ใช้ ExecutionNo เพราะนับรวมแถวที่ลบและแถว Save Progress (§3.4)
    /// </summary>
    public static IQueryable<int> RetestCountsQuery(QaDbContext db, IQueryable<TestPlanRun> runs) => db.TestExecutions
        .Where(e => !e.IsDeleted && FinalExecutionStatuses.Contains(e.Status) && runs.Any(r => r.TestCycleId == e.CycleCase.TestCycleId))
        .GroupBy(e => e.TestCycleCaseId)
        .Where(g => g.Count() > 1)
        .Select(g => g.Count());

    /// <summary>สูตรตาม §3.4 — แยกออกมาเพื่อทดสอบได้โดยไม่ต้องมีฐานข้อมูล</summary>
    public static TestPlanSummaryDto Summarize(int runCount, IReadOnlyDictionary<string, int> statusCounts, IReadOnlyCollection<int> finalExecutionCountsAboveOne)
    {
        int Count(string status) => statusCounts.TryGetValue(status, out var n) ? n : 0;
        var total = statusCounts.Values.Sum();
        var untested = Count("NotRun");
        var inProgress = Count("InProgress");
        var passed = Count("Pass");
        var failed = Count("Fail");
        var blocked = Count("Blocked");
        var skipped = Count("Skipped");
        var completed = passed + failed + blocked + skipped;
        var touched = inProgress + completed;
        return new TestPlanSummaryDto(runCount, total, untested, inProgress, touched, completed, passed, failed, blocked, skipped,
            finalExecutionCountsAboveOne.Count, finalExecutionCountsAboveOne.Sum(n => n - 1),
            Percent(touched, total), Percent(completed, total), completed == 0 ? null : Percent(passed, completed));
    }

    private IQueryable<TestPlanRun> RunsOf(Guid planId) => db.TestPlanRuns.AsNoTracking().Where(r => r.TestPlanId == planId);

    /// <summary>แถว Matrix พร้อมตัวนับต่อ Run ใน Query เดียว (ไม่ยิงต่อ Run)</summary>
    private async Task<IReadOnlyList<TestPlanRunDto>> MapRunsAsync(IQueryable<TestPlanRun> runs, CancellationToken ct)
    {
        var rows = await runs
            .Select(r => new
            {
                r.TestPlanRunId, r.TestPlanId, r.TestCycleId, r.Cycle.CycleCode, r.Cycle.CycleName, r.Cycle.Status, r.RunCode, r.RunName,
                r.ConfigurationKey, r.ConfigurationName, r.ConfigurationJson, r.OwnerUserId, r.Cycle.BuildId, r.Cycle.EnvironmentId, r.CreatedAt,
                OwnerName = r.OwnerUserId != null ? db.Users.Where(u => u.UserId == r.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault() : null,
                BuildNumber = db.Builds.Where(b => b.BuildId == r.Cycle.BuildId).Select(b => b.BuildNumber).FirstOrDefault(),
                EnvironmentName = db.TestEnvironments.Where(e => e.TestEnvironmentId == r.Cycle.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault(),
                Total = r.Cycle.Cases.Count(),
                Untested = r.Cycle.Cases.Count(c => c.CurrentStatus == "NotRun"),
                InProgress = r.Cycle.Cases.Count(c => c.CurrentStatus == "InProgress"),
                Passed = r.Cycle.Cases.Count(c => c.CurrentStatus == "Pass"),
                Failed = r.Cycle.Cases.Count(c => c.CurrentStatus == "Fail"),
                Blocked = r.Cycle.Cases.Count(c => c.CurrentStatus == "Blocked"),
                Skipped = r.Cycle.Cases.Count(c => c.CurrentStatus == "Skipped"),
            })
            .ToListAsync(ct);
        return rows.Select(x =>
        {
            var completed = x.Passed + x.Failed + x.Blocked + x.Skipped;
            return new TestPlanRunDto(x.TestPlanRunId, x.TestPlanId, x.TestCycleId, x.CycleCode, x.CycleName, x.Status, x.RunCode, x.RunName, x.ConfigurationKey, x.ConfigurationName, x.ConfigurationJson,
                x.OwnerUserId, x.OwnerName, x.BuildId, x.BuildNumber, x.EnvironmentId, x.EnvironmentName, x.Total, x.Untested, x.InProgress, x.Passed, x.Failed, x.Blocked, x.Skipped,
                Percent(x.Total - x.Untested, x.Total), completed == 0 ? null : Percent(x.Passed, completed), x.CreatedAt);
        }).ToList();
    }

    private static decimal Percent(int value, int total) => total == 0 ? 0m : Math.Round(value * 100m / total, 1);

    public sealed class PlanRow
    {
        public TestPlan Plan { get; init; } = null!;
        public int RunCount { get; init; }
        public int InProgressRuns { get; init; }
        public int TerminalRuns { get; init; }
        public int TotalCases { get; init; }
        public int Untested { get; init; }
        public int Passed { get; init; }
        public int Failed { get; init; }
        public int Blocked { get; init; }
        public int Skipped { get; init; }
    }

    public sealed class PlanRowWithStatus
    {
        public PlanRow Row { get; init; } = null!;
        public string Status { get; init; } = TestPlanStatuses.Draft;
    }
}
