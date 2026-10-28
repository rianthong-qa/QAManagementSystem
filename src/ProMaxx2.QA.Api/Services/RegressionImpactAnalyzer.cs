using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Regression;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>Regression Impact Analysis query — extracted (AUT-REG-002) from <c>RegressionController.Impact</c> so the
/// interactive endpoint and the automatic <see cref="RegressionScheduleTriggerService"/> run the exact same logic
/// instead of two copies drifting apart.</summary>
public static class RegressionImpactAnalyzer
{
    public static async Task<RegressionImpactDto> RunAsync(QaDbContext db, Release release, RegressionImpactRequest request, Guid? userId, CancellationToken ct)
    {
        var buildExists = await db.Builds.AnyAsync(x => x.BuildId == request.BuildId && x.ReleaseId == release.ReleaseId && x.IsActive, ct);
        if (!buildExists) throw new ArgumentException("Build ที่เลือกไม่ได้อยู่ใน Release นี้");

        var changedModules = request.ChangedModuleIds.Distinct().ToArray();
        var priorityLimit = PriorityRank(request.MinimumPriority);
        var specialImpact = request.DatabaseChange || request.ApiChange || request.CalculationChange || request.PermissionChange || request.InstallerChange;
        var linkedDefectCases = request.DefectFix
            ? await db.DefectTestCaseLinks.AsNoTracking().Where(link => db.Defects.Any(defect => defect.DefectId == link.DefectId && defect.ProjectId == release.ProjectId && !defect.IsDeleted)).Select(x => x.TestCaseId).Distinct().ToListAsync(ct)
            : [];

        var rows = await db.TestCases.AsNoTracking()
            .Where(x => x.ProjectId == release.ProjectId && !x.IsDeleted && x.Status != "Deprecated")
            .Where(x => changedModules.Contains(x.ModuleId)
                || linkedDefectCases.Contains(x.TestCaseId)
                || (request.IncludeSharedDependencies && (x.Priority == "P0" || x.Priority == "P1" || x.TestType == "Regression"))
                || (specialImpact && x.TestType == "Regression"))
            .Where(x => (x.Priority == "P0" ? 0 : x.Priority == "P1" ? 1 : x.Priority == "P2" ? 2 : 3) <= priorityLimit || changedModules.Contains(x.ModuleId) || linkedDefectCases.Contains(x.TestCaseId))
            .Select(x => new { x.TestCaseId, x.TestCaseCode, x.Title, x.ModuleId, ModuleName = db.Modules.Where(m => m.ModuleId == x.ModuleId).Select(m => m.ModuleName).FirstOrDefault() ?? "-", x.Priority, x.TestType, x.RevisionNo, x.Status })
            .OrderBy(x => x.Priority).ThenBy(x => x.TestCaseCode).ToListAsync(ct);

        var caseIds = rows.Select(x => x.TestCaseId).ToArray();
        var executionRows = await db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildId == request.BuildId && caseIds.Contains(x.CycleCase.TestCaseId))
            .Select(x => new { x.CycleCase.TestCaseId, x.Status, x.CompletedAt, x.ExecutionNo }).ToListAsync(ct);
        var lastResults = executionRows.GroupBy(x => x.TestCaseId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CompletedAt).ThenByDescending(y => y.ExecutionNo).First().Status);

        var allCases = rows.Select(x =>
        {
            var historical = linkedDefectCases.Contains(x.TestCaseId);
            var direct = changedModules.Contains(x.ModuleId);
            var critical = x.Priority is "P0" or "P1";
            var impactType = direct ? "Direct Impact" : historical ? "Historical Defect" : critical ? "Critical P0/P1" : "Shared Dependency";
            var reason = direct ? $"อยู่ใน Module ที่มีการเปลี่ยนแปลง: {x.ModuleName}"
                : historical ? "เคยเชื่อมโยงกับ Defect ในโครงการ"
                : critical ? $"Test Case ระดับ {x.Priority} ควรอยู่ใน Critical Regression"
                : "เป็น Regression case หรือเกี่ยวข้องกับ shared impact";
            var score = Math.Clamp((direct ? request.DirectImpactWeight : 0) + (historical ? request.HistoricalDefectWeight : 0) + (critical ? request.CriticalPriorityWeight : 0) + (!direct && !historical ? request.SharedDependencyWeight : 0), 0, 100);
            return new RegressionCaseDto(x.TestCaseId, x.TestCaseCode, x.Title, x.ModuleId, x.ModuleName, x.Priority, x.TestType, x.RevisionNo, x.Status, lastResults.GetValueOrDefault(x.TestCaseId), impactType, reason, direct || historical || x.Priority == "P0", score);
        }).OrderByDescending(x => x.RiskScore).ThenBy(x => x.Priority).ThenBy(x => x.TestCaseCode).ToList();
        var pageSize = Math.Clamp(request.PageSize, 10, 200); var totalPages = Math.Max(1, (int)Math.Ceiling(allCases.Count / (double)pageSize)); var page = Math.Clamp(request.Page, 1, totalPages);
        var cases = allCases.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var cycleCases = await db.TestCycleCases.AsNoTracking().Where(x => x.Cycle.ReleaseId == release.ReleaseId && x.Cycle.BuildId == request.BuildId && x.Cycle.CycleType == "Regression" && !x.Cycle.IsDeleted).Select(x => x.CurrentStatus).ToListAsync(ct);
        var cycleCount = await db.TestCycles.CountAsync(x => x.ReleaseId == release.ReleaseId && x.BuildId == request.BuildId && x.CycleType == "Regression" && !x.IsDeleted, ct);
        var executed = cycleCases.Count(x => x != "NotRun");
        var passed = cycleCases.Count(x => x == "Pass");
        var failed = cycleCases.Count(x => x is "Fail" or "Blocked");
        var openDefects = await db.Defects.CountAsync(x => x.ReleaseId == release.ReleaseId && !x.IsDeleted && x.Status != "Closed" && x.Status != "Rejected", ct);
        var progress = cycleCases.Count == 0 ? 0 : Math.Round(executed * 100m / cycleCases.Count, 1);
        var passRate = executed == 0 ? 0 : Math.Round(passed * 100m / executed, 1);
        var overall = cycleCases.Count == 0 ? "Not Started" : failed > 0 || openDefects > 0 ? "At Risk" : progress < 100 ? "In Progress" : "Passed";
        var metrics = new RegressionMetricsDto(changedModules.Length, allCases.Count, cycleCount, cycleCases.Count, executed, passed, failed, progress, passRate, openDefects, overall);
        if (request.RecordAnalysis)
        {
            db.RegressionAnalyses.Add(new RegressionAnalysis(release.ProjectId, release.ReleaseId, request.BuildId, changedModules.Length, allCases.Count, request.MinimumPriority, request.ChangeNotes, userId));
            db.RegressionActivities.Add(new RegressionActivity(release.ProjectId, release.ReleaseId, request.BuildId, "ImpactAnalyzed", $"{changedModules.Length} modules, {allCases.Count} recommended cases", userId));
            await db.SaveChangesAsync(ct);
        }
        return new RegressionImpactDto(release.ReleaseId, request.BuildId, metrics, cases, page, pageSize, allCases.Count, totalPages, request.IncludeAllCaseIds ? allCases.Select(x => x.TestCaseId).ToArray() : []);
    }

    private static int PriorityRank(string value) => value.ToUpperInvariant() switch { "P0" => 0, "P1" => 1, "P2" => 2, "P3" => 3, _ => 1 };
}
