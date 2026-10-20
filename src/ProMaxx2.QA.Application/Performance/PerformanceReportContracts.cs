namespace ProMaxx2.QA.Application.Performance;

/// <summary>ข้อมูลพนักงานในรายงาน — ไม่มี UserId/อีเมล/บันทึกภายในของหัวหน้า (Notes) เพราะรายงานส่งออกนอกระบบได้ (ลิงก์ HR)</summary>
public sealed record ReportProfileDto(string DisplayName, string? EmployeeCode, string? Position, string? Level, string? SupervisorName, DateOnly? HireDate, string EmploymentStatus, DateOnly? ProbationEndDate, int? ProbationDay, int ProbationTotalDays);
public sealed record ReportWorkLogDto(DateOnly WorkDate, string Kind, string Category, string Title, string? Description, decimal? EffortHours, string? ProjectName);
/// <summary>รอบที่ยังไม่ปิดไม่มีคะแนน/เกรด/ผล — คะแนนร่างของหัวหน้าไม่ออกนอกหน้าประเมิน</summary>
public sealed record ReportEvaluationDto(string Type, string Round, int Year, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate, string Status, decimal? OverallScore, string? Grade, string? Recommendation, string? EvaluatorName, DateTime? CompletedAt, DateTime? AcknowledgedAt, bool IsOverdue);
public sealed record EmployeeReportDto(
    DateOnly From, DateOnly To, DateTime GeneratedAt, ReportProfileDto Profile, PerformanceMetrics Metrics, IReadOnlyList<WeeklyTrendPoint> Weekly,
    bool IncludesWorkLogs, IReadOnlyList<ReportWorkLogDto> WorkLogs,
    bool IncludesEvaluations, IReadOnlyList<ReportEvaluationDto> Evaluations, EvaluationComparisonDto? Comparison);

/// <summary>
/// สรุปผลงานรายคนในหน้าเดียว (หน้า "สรุปผลงาน" และลิงก์แชร์ให้ HR) — ประกอบจาก service เดิมจึงใช้กฎสิทธิ์เดิมทั้งหมด:
/// metric กรองตาม Project ของผู้ดู (ลิงก์แชร์ = ผู้สร้างลิงก์), บันทึกงานไม่รวมข้อสังเกต (Concern) เสมอ, การประเมินแสดงคะแนนเฉพาะรอบที่ปิดแล้ว
/// </summary>
public sealed class PerformanceReportService(PerformanceService performance, EvaluationService evaluations, IPerformanceRepository repository, TimeProvider clock)
{
    public async Task<EmployeeReportDto> BuildAsync(PerformanceViewer viewer, Guid userId, DateOnly? from, DateOnly? to, bool includeEvaluations, bool includeWorkLogs, CancellationToken ct)
    {
        var summary = await performance.GetUserAsync(viewer, userId, from, to, ct);
        var p = summary.Profile;
        var profile = new ReportProfileDto(p.DisplayName, p.EmployeeCode, p.Position, p.Level, p.SupervisorName, p.HireDate, p.EmploymentStatus, p.ProbationEndDate, p.ProbationDay, p.ProbationTotalDays);

        IReadOnlyList<ReportWorkLogDto> workLogs = [];
        if (includeWorkLogs)
            workLogs = (await repository.ListWorkLogsAsync(userId, summary.From, summary.To, false, viewer.UserId, viewer.CanManage, ct))
                .Where(x => x.Kind != "Concern")
                .OrderByDescending(x => x.Kind == "Highlight").ThenByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAt)
                .Select(x => new ReportWorkLogDto(x.WorkDate, x.Kind, x.Category, x.Title, x.Description, x.EffortHours, x.ProjectName))
                .ToList();

        IReadOnlyList<ReportEvaluationDto> rounds = [];
        EvaluationComparisonDto? comparison = null;
        if (includeEvaluations)
        {
            rounds = (await evaluations.ListAsync(viewer, userId, false, ct))
                .OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.DueDate)
                .Select(x =>
                {
                    var closed = x.Status is "Completed" or "Acknowledged";
                    return new ReportEvaluationDto(x.Type, x.Round, x.Year, x.PeriodStart, x.PeriodEnd, x.DueDate, x.Status,
                        closed ? x.OverallScore : null, closed ? x.Grade : null, closed ? x.Recommendation : null,
                        x.EvaluatorName, x.CompletedAt, x.AcknowledgedAt, x.IsOverdue);
                })
                .ToList();
            comparison = await evaluations.CompareAsync(viewer, userId, ct);
        }

        return new EmployeeReportDto(summary.From, summary.To, clock.GetUtcNow().UtcDateTime, profile, summary.Metrics, summary.Weekly,
            includeWorkLogs, workLogs, includeEvaluations, rounds, comparison);
    }
}
