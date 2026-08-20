using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed class DefectAutoCreateService(QaDbContext db)
{
    public async Task<string?> CreateFromFailAsync(Guid cycleCaseId, CreateExecutionRequest r, Guid? testerId, CancellationToken ct)
    {
        try
        {
            var cycleCase = await db.TestCycleCases.AsNoTracking()
                .Include(x => x.Cycle)
                .Include(x => x.TestCase).ThenInclude(x => x.Steps)
                .SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId && !x.Cycle.IsDeleted, ct);
            if (cycleCase is null) return null;
            var tc = cycleCase.TestCase;
            if (await db.DefectTestCaseLinks.Where(l => l.TestCaseId == tc.TestCaseId)
                    .Join(db.Defects, l => l.DefectId, d => d.DefectId, (l, d) => d)
                    .AnyAsync(d => !d.IsDeleted && !new[] { "Resolved", "Closed", "Rejected" }.Contains(d.Status), ct))
                return null;
            var failedSteps = r.StepResults.Where(s => s.Status.Equals("Fail", StringComparison.OrdinalIgnoreCase)).ToList();
            var firstFailed = failedSteps.FirstOrDefault();
            var testerName = testerId.HasValue ? await db.Users.Where(u => u.UserId == testerId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null;
            var ownerName = tc.OwnerUserId.HasValue ? await db.Users.Where(u => u.UserId == tc.OwnerUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null;
            var envName = await db.TestEnvironments.Where(x => x.TestEnvironmentId == cycleCase.Cycle.EnvironmentId).Select(x => x.EnvironmentName).FirstOrDefaultAsync(ct);
            var buildNumber = await db.Builds.Where(x => x.BuildId == cycleCase.Cycle.BuildId).Select(x => x.BuildNumber).FirstOrDefaultAsync(ct);
            var runNo = await db.TestExecutions.Where(x => x.TestCycleCaseId == cycleCaseId && !x.IsDeleted).Select(x => (int?)x.ExecutionNo).MaxAsync(ct) ?? 0;
            var lines = new List<string>
            {
                $"Test Cycle: {cycleCase.Cycle.CycleCode} ({cycleCase.Cycle.CycleName}) — Run #{runNo}"
            };
            if (!string.IsNullOrWhiteSpace(cycleCase.Cycle.CycleType)) lines.Add($"ประเภท Cycle: {cycleCase.Cycle.CycleType}");
            if (!string.IsNullOrWhiteSpace(envName)) lines.Add($"Environment: {envName}");
            if (!string.IsNullOrWhiteSpace(buildNumber)) lines.Add($"Build: {buildNumber}");
            lines.Add($"Priority: {tc.Priority}{(string.IsNullOrWhiteSpace(tc.TestType) ? "" : $" | ประเภท Test Case: {tc.TestType}")}");
            if (!string.IsNullOrWhiteSpace(tc.Objective)) lines.Add($"วัตถุประสงค์: {tc.Objective}");
            if (!string.IsNullOrWhiteSpace(tc.Preconditions)) lines.Add($"เงื่อนไขก่อนเริ่ม: {tc.Preconditions}");
            if (!string.IsNullOrWhiteSpace(testerName)) lines.Add($"ผู้ทดสอบ: {testerName}");
            if (!string.IsNullOrWhiteSpace(ownerName)) lines.Add($"ผู้รับผิดชอบ Test Case: {ownerName}");
            if (!string.IsNullOrWhiteSpace(r.ActualResult)) lines.Add($"ผลลัพธ์จริง: {r.ActualResult}");
            if (!string.IsNullOrWhiteSpace(r.Comment)) lines.Add($"คอมเมนต์: {r.Comment}");
            if (failedSteps.Count > 0) lines.Add($"ขั้นตอนที่ Fail ({failedSteps.Count} ขั้น):");
            foreach (var s in failedSteps) lines.Add($"- ขั้นที่ {s.StepNo}: {s.ActualResult ?? "-"}");
            lines.Add($"เวลาที่ทดสอบ: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
            var description = Truncate(string.Join("\n", lines), 2000);
            var stepResultsByNo = r.StepResults.GroupBy(x => x.StepNo).ToDictionary(g => g.Key, g => g.First());
            var stepsText = Truncate(string.Join("\n", tc.Steps.Where(s => s.RevisionNo == cycleCase.TestCaseRevisionNo).OrderBy(s => s.StepNo)
                .Select(s =>
                {
                    var sr = stepResultsByNo.TryGetValue(s.StepNo, out var x) ? x : null;
                    var status = sr?.Status ?? "NotRun";
                    var failed = status.Equals("Fail", StringComparison.OrdinalIgnoreCase);
                    var parts = new List<string> { $"{s.StepNo}. {s.Action} ({status})" };
                    if (!string.IsNullOrWhiteSpace(s.TestDataText)) parts.Add($"ข้อมูล: {s.TestDataText}");
                    parts.Add($"คาดหวัง: {s.ExpectedResult}");
                    if (!string.IsNullOrWhiteSpace(sr?.ActualResult)) parts.Add($"ผลจริง: {sr.ActualResult}");
                    if (failed) parts.Add("[ขั้นนี้ล้มเหลว]");
                    return string.Join(" | ", parts);
                })), 4000);
            var expected = firstFailed is null ? null
                : tc.Steps.FirstOrDefault(s => s.RevisionNo == cycleCase.TestCaseRevisionNo && s.StepNo == firstFailed.StepNo)?.ExpectedResult;
            var projectCode = await db.Projects.Where(x => x.ProjectId == cycleCase.Cycle.ProjectId).Select(x => x.ProjectCode).SingleAsync(ct);
            var prefix = $"{projectCode}-DEF";
            var codes = await db.Defects.Where(x => x.ProjectId == cycleCase.Cycle.ProjectId && x.DefectCode.StartsWith(prefix + "-")).Select(x => x.DefectCode).ToListAsync(ct);
            var code = BusinessCodeGenerator.NextAvailable(prefix, codes);
            var title = Truncate($"{tc.TestCaseCode} ล้มเหลว: {tc.Title}", 300);
            var defect = new Defect(cycleCase.Cycle.ProjectId, cycleCase.Cycle.ReleaseId, cycleCase.Cycle.BuildId, tc.ModuleId, code, title, "High", "Open", testerId, description, stepsText, expected, r.ActualResult, tc.OwnerUserId);
            db.Defects.Add(defect);
            db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, tc.TestCaseId, testerId));
            db.DefectActivities.Add(new DefectActivity(defect.DefectId, "Created", $"สร้าง Defect อัตโนมัติจากผลการทดสอบ Fail ของ {tc.TestCaseCode} (Cycle {cycleCase.Cycle.CycleCode})", testerId));
            db.DefectActivities.Add(new DefectActivity(defect.DefectId, "TestLinked", $"เชื่อมโยง Test Case {tc.TestCaseCode} ({tc.Title})", testerId));
            await db.SaveChangesAsync(ct);
            return code;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DefectAutoCreate] สร้าง Defect อัตโนมัติไม่สำเร็จ: {ex.Message}");
            return null;
        }
    }
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}