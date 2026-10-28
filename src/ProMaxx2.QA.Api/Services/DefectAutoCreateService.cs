using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

// Skip (มี Defect เปิดอยู่แล้ว) กับ Error (ล้มเหลวจริง เช่น DB exception) ต้องแยกกันชัดเจน — ผู้เรียก
// (ExecutionsController) ใช้ผลนี้ map เข้า ExecutionHistoryDto เพื่อแจ้งผู้ใช้ต่างข้อความกัน แทนที่จะ
// เงียบไปเฉยๆ เหมือนเดิมที่คืนแค่ string? เดียวแล้วสองกรณีนี้แยกไม่ออกจากฝั่ง caller/UI เลย
// ReopenedDefectCode = Defect ที่ Resolved แล้ว Fail ซ้ำบน Build เดิมหรือหลัง Fix Build → เปิดตัวเดิมกลับ (Reopen) แทนการสร้างใหม่;
// RegressionOfDefectCode = Defect ที่ Closed แล้วปัญหากลับมา → สร้าง Defect ใหม่ที่อ้างตัวเดิม (DEFECT_BUILD_LIFECYCLE.md)
// ExistingDefectStatus = สถานะของ Defect ที่ใช้ตัวเดิม (Open/In Progress = ยังไม่แก้, Resolved = รอตรวจซ้ำบน Build ที่ใหม่กว่า)
public sealed record DefectAutoCreateResult(string? CreatedDefectCode, string? ExistingDefectCode, string? Error, string? ReopenedDefectCode = null, string? RegressionOfDefectCode = null, string? ExistingDefectStatus = null);

public sealed class DefectAutoCreateService(QaDbContext db, ILogger<DefectAutoCreateService> logger)
{
    public async Task<DefectAutoCreateResult> CreateFromFailAsync(Guid cycleCaseId, CreateExecutionRequest r, Guid? testerId, CancellationToken ct)
    {
        try
        {
            var cycleCase = await db.TestCycleCases.AsNoTracking()
                .Include(x => x.Cycle)
                .Include(x => x.TestCase).ThenInclude(x => x.Steps)
                .SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId && !x.Cycle.IsDeleted, ct);
            if (cycleCase is null) return new DefectAutoCreateResult(null, null, "ไม่พบข้อมูล Test Cycle Case สำหรับสร้าง Defect อัตโนมัติ");
            var tc = cycleCase.TestCase;
            var linked = await db.DefectTestCaseLinks.Where(l => l.TestCaseId == tc.TestCaseId)
                    .Join(db.Defects, l => l.DefectId, d => d.DefectId, (l, d) => d)
                    .Where(d => !d.IsDeleted)
                    .Select(d => new { d.DefectId, d.DefectCode, d.Status, d.FixedInBuildId, d.ResolutionType, d.VerifiedInBuildId, Changed = d.UpdatedAt ?? d.CreatedAt })
                    .ToListAsync(ct);
            // ยังไม่ได้แก้ (Open/In Progress) — ใช้ตัวเดิม ไม่สร้างซ้ำ
            var unresolved = linked.FirstOrDefault(d => Defect.UnresolvedStatuses.Contains(d.Status));
            if (unresolved is not null) return new DefectAutoCreateResult(null, unresolved.DefectCode, null, ExistingDefectStatus: unresolved.Status);
            // Dev แก้แล้วรอตรวจซ้ำ (Resolved) แต่ Fail อีก: Build นี้มีการแก้แล้ว (ตรงหรือหลัง Fix Build) = แก้ไม่ผ่าน → Reopen ตัวเดิม
            // Fail บน Build ที่เก่ากว่า Fix Build = ยังไม่มีการแก้ใน Build นี้ → ไม่ Reopen และไม่สร้างซ้ำ
            var resolved = linked.Where(d => d.Status == "Resolved").OrderByDescending(d => d.Changed).FirstOrDefault();
            if (resolved is not null)
            {
                if (!await DefectBuilds.IsSameOrLaterAsync(db, cycleCase.Cycle.BuildId, resolved.FixedInBuildId, ct))
                    return new DefectAutoCreateResult(null, resolved.DefectCode, null, ExistingDefectStatus: "Resolved");
                var reopen = await db.Defects.SingleAsync(d => d.DefectId == resolved.DefectId, ct);
                reopen.TransitionTo("Open", null, null, testerId);
                db.DefectActivities.Add(new DefectActivity(reopen.DefectId, "Reopened",
                    $"Status changed from Resolved to Open (Reopen ครั้งที่ {reopen.ReopenCount}) — {tc.TestCaseCode} Fail ซ้ำใน Cycle {cycleCase.Cycle.CycleCode}", testerId, "Resolved", "Open", cycleCase.Cycle.BuildId));
                await db.SaveChangesAsync(ct);
                return new DefectAutoCreateResult(null, null, null, reopen.DefectCode);
            }
            // ปิดไปแล้ว (Closed) แต่ปัญหากลับมา = regression → สร้างใหม่ที่อ้างตัวเดิม (ไม่เปิด Defect ที่ปิดในรอบก่อน ประวัติ Release เดิมไม่เปลี่ยน)
            // ปิดด้วยผลอื่น (Won't Fix, By Design ฯลฯ) ไม่เคยแก้ — Fail ใหม่เป็น Defect ใหม่ธรรมดา ไม่ใช่ regression
            var closed = linked.Where(d => d.Status == "Closed" && Defect.WasFixed(d.ResolutionType, d.VerifiedInBuildId)).OrderByDescending(d => d.Changed).FirstOrDefault();
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
            // แต่ละขั้นที่ Fail แสดงชื่อขั้น (Action ของ Test Step) เสมอ + ผลจริงถ้าผู้ทดสอบกรอก — เดิมใช้แค่ผลจริงของ step
            // ซึ่งส่วนใหญ่ว่าง (ผู้ทดสอบกรอกผลจริงระดับ Test Case แทน) เลยกลายเป็น "- ขั้นที่ 1: -" ทุกบรรทัด
            var stepActions = tc.Steps.Where(s => s.RevisionNo == cycleCase.TestCaseRevisionNo).GroupBy(s => s.StepNo).ToDictionary(g => g.Key, g => g.First().Action);
            if (failedSteps.Count > 0) lines.Add($"ขั้นตอนที่ Fail ({failedSteps.Count} ขั้น):");
            foreach (var s in failedSteps)
            {
                var action = stepActions.GetValueOrDefault(s.StepNo);
                var detail = string.Join(" — ", new[] { action, string.IsNullOrWhiteSpace(s.ActualResult) ? null : $"ผลจริง: {s.ActualResult.Trim()}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
                lines.Add($"- ขั้นที่ {s.StepNo}: {(detail.Length > 0 ? detail : "-")}");
            }
            lines.Add($"เวลาที่ทดสอบ: {FormatThaiTime(DateTime.UtcNow)}");
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
            if (closed is not null) defect.MarkRegressionOf(closed.DefectId);
            db.Defects.Add(defect);
            db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, tc.TestCaseId, testerId));
            db.DefectActivities.Add(new DefectActivity(defect.DefectId, "Created", $"สร้าง Defect อัตโนมัติจากผลการทดสอบ Fail ของ {tc.TestCaseCode} (Cycle {cycleCase.Cycle.CycleCode})", testerId));
            db.DefectActivities.Add(new DefectActivity(defect.DefectId, "TestLinked", $"เชื่อมโยง Test Case {tc.TestCaseCode} ({tc.Title})", testerId));
            if (closed is not null)
                db.DefectActivities.Add(new DefectActivity(defect.DefectId, "RegressionOf", $"ปัญหาเดิมของ {closed.DefectCode} (ปิดไปแล้ว) กลับมาอีก", testerId, null, null, cycleCase.Cycle.BuildId));
            await db.SaveChangesAsync(ct);
            return new DefectAutoCreateResult(code, null, null, null, closed?.DefectCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DefectAutoCreate] สร้าง Defect อัตโนมัติไม่สำเร็จสำหรับ TestCycleCaseId={CycleCaseId}", cycleCaseId);
            return new DefectAutoCreateResult(null, null, "เกิดข้อผิดพลาดขณะสร้าง Defect อัตโนมัติ กรุณาตรวจสอบกับผู้ดูแลระบบ หรือสร้าง Defect ด้วยตนเอง");
        }
    }
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // เวลาไทย (UTC+7) ปี พ.ศ. แบบเดียวกับที่หน้าเว็บแสดง (fmtDateTimeBE) เช่น "28/08/2569 11:02 น." — ใช้ InvariantCulture
    // แล้วบวก 543 เอง เพราะ culture ของเครื่อง server (th-TH) จะทำให้ "yyyy" เป็น พ.ศ. อยู่แล้วและบวกซ้ำ
    public static string FormatThaiTime(DateTime utcNow)
    {
        var th = utcNow.AddHours(7);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{th:dd/MM}/{th.Year + 543} {th:HH:mm} น.");
    }
}