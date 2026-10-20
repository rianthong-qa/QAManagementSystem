using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ยอดรวมของทีม — Current/Previous เป็นผลรวมจริง ส่วน Typical เป็นค่ากลาง (median) รายสัปดาห์ของ 4 สัปดาห์ก่อนหน้า จึงเป็นทศนิยมได้</summary>
public sealed record WeeklyTotalsDto(
    decimal Executions, decimal Passed, decimal Failed, decimal Blocked, decimal DefectsReported, decimal DefectsCriticalHigh, decimal DefectsRejected,
    decimal TestCasesCreated, decimal TestCaseRevisions, decimal CrmTicketsHandled, decimal CrmClosed, decimal CrmHandedToDev, decimal CrmAnswers,
    decimal AutomationRuns, decimal WorkLogs, decimal Highlights);
public sealed record WeeklyWorkItemDto(DateOnly WorkDate, string Kind, string Category, string Title, string? Description, string? ProjectName);
public sealed record WeeklyMemberDto(Guid UserId, string DisplayName, string? Position, string EmploymentStatus, int? ProbationDay, int ProbationTotalDays, PerformanceMetrics Current, PerformanceMetrics Previous, IReadOnlyList<WeeklyWorkItemDto> WorkItems);
public sealed record WeeklyEvaluationDueDto(Guid EvaluationId, Guid UserId, string? UserName, string Type, string Round, int Year, DateOnly DueDate, string Status, bool IsOverdue);
public sealed record WeeklyProbationDto(Guid UserId, string DisplayName, string EmploymentStatus, int ProbationDay, int ProbationTotalDays, DateOnly? ProbationEndDate);
/// <summary>วันที่มีการบันทึกจำนวนมากผิดปกติ (มักเป็นการนำเข้าข้อมูล) — TypicalDaily = ค่ากลางของวันอื่นที่มีข้อมูลในช่วงเดียวกัน</summary>
public sealed record WeeklyAnomalyDto(DateOnly Date, string Kind, int Count, decimal TypicalDaily, bool InReportWeek);
public sealed record WeeklyNarrativeDto(string? Health, string? Headline, string? KeyRisk, string? Ask, string? NextWeekPlan, IReadOnlyDictionary<Guid, string> MemberNotes, DateTime UpdatedAt, string? UpdatedByName);
public sealed record WeeklyReportDto(
    DateOnly WeekStart, DateOnly WeekEnd, bool IsCurrentWeek, DateTime GeneratedAt,
    WeeklyTotalsDto Current, WeeklyTotalsDto Previous, WeeklyTotalsDto Typical, DateOnly TypicalFrom, DateOnly TypicalTo,
    int AssignedPending, int AssignedOverdue,
    IReadOnlyList<WeeklyMemberDto> Members, IReadOnlyList<WeeklyProbationDto> Probation, IReadOnlyList<WeeklyEvaluationDueDto> EvaluationsDue,
    IReadOnlyList<WeeklyAnomalyDto> Anomalies, DateOnly? CrmDataSince, WeeklyNarrativeDto? Narrative);
public sealed record DailyCount(DateTime Day, int Count);
public sealed record SaveWeeklyReportNoteRequest(string? Health, string? Headline, string? KeyRisk, string? Ask, string? NextWeekPlan, Dictionary<Guid, string?>? MemberNotes);

/// <summary>
/// "สรุปประจำสัปดาห์" สำหรับส่งผู้บริหารทุกวันศุกร์ — สัปดาห์ = จันทร์–อาทิตย์ตามเวลาไทย
///
/// ตัวเลขใช้ <see cref="PerformanceService.CollectMetricsAsync"/> ชุดเดียวกับหน้าผลงาน (กรองตาม Project ของผู้ดู) แล้วเทียบกับ
/// สัปดาห์ก่อน และค่ากลาง (median) ของ 4 สัปดาห์ก่อนหน้า — ใช้ median แทนค่าเฉลี่ยเพราะข้อมูลจริงมีวันที่นำเข้าทีละหลายร้อยรายการ
/// ซึ่งทำให้ค่าเฉลี่ยเพี้ยน และแจ้งวันเหล่านั้นใน <see cref="WeeklyAnomalyDto"/> ให้หัวหน้าอธิบายในรายงาน
/// ไม่มีคะแนนหรือการจัดอันดับรายคน (ตัวเลขเป็นหลักฐานประกอบ ไม่ใช่ผลประเมิน — ดู PERFORMANCE_EVALUATION_PLAN §1)
/// </summary>
public sealed class WeeklyReportService(QaDbContext db, PerformanceService performance, EvaluationService evaluations, IPerformanceRepository repository, TimeProvider clock)
{
    public const string AuditEntityType = "WeeklyReport";
    private const int TypicalWeeks = 4;
    // วันที่ถือว่าผิดปกติ: อย่างน้อย 100 รายการ และ ≥ 4 เท่าของค่ากลางวันอื่น
    private const int AnomalyMinCount = 100;
    private const decimal AnomalyFactor = 4m;
    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public async Task<WeeklyReportDto> BuildAsync(PerformanceViewer viewer, DateOnly? weekStart, CancellationToken ct)
    {
        RequireManage(viewer);
        var thisWeek = MondayOf(performance.Today);
        var week = ResolveWeek(weekStart, thisWeek);
        var weekEnd = week.AddDays(6);

        var members = (await repository.ListMembersAsync(null, performance.Today, ct)).Where(m => m.EmploymentStatus != "Resigned").ToList();
        var ids = members.Select(m => m.UserId).ToList();

        // สัปดาห์ที่รายงาน + 4 สัปดาห์ก่อนหน้า (สัปดาห์แรกของชุดนี้คือ "สัปดาห์ก่อน")
        var current = await performance.CollectMetricsAsync(ids, Utc(week), Utc(week.AddDays(7)), viewer.ProjectIds, ct);
        var prior = new List<IReadOnlyDictionary<Guid, PerformanceMetrics>>();
        for (var i = 1; i <= TypicalWeeks; i++)
            prior.Add(await performance.CollectMetricsAsync(ids, Utc(week.AddDays(-7 * i)), Utc(week.AddDays(-7 * (i - 1))), viewer.ProjectIds, ct));

        var workItems = await WorkItemsAsync(ids, week, weekEnd, ct);
        var memberDtos = members.Select(m => new WeeklyMemberDto(m.UserId, m.DisplayName, m.Position, m.EmploymentStatus, m.ProbationDay, m.ProbationTotalDays,
            current.GetValueOrDefault(m.UserId) ?? new PerformanceMetrics(), prior[0].GetValueOrDefault(m.UserId) ?? new PerformanceMetrics(),
            workItems.TryGetValue(m.UserId, out var items) ? items : [])).ToList();

        var openEvaluations = await evaluations.ListAsync(viewer, null, true, ct);
        var memberIds = ids.ToHashSet();
        var due = openEvaluations
            .Where(e => memberIds.Contains(e.UserId) && (e.IsOverdue || e.DueDate <= weekEnd.AddDays(14)))
            .OrderBy(e => e.DueDate)
            .Select(e => new WeeklyEvaluationDueDto(e.EvaluationId, e.UserId, e.UserName, e.Type, e.Round, e.Year, e.DueDate, e.Status, e.IsOverdue))
            .ToList();
        var probation = members.Where(m => m.ProbationDay is not null)
            .Select(m => new WeeklyProbationDto(m.UserId, m.DisplayName, m.EmploymentStatus, m.ProbationDay!.Value, m.ProbationTotalDays, m.ProbationEndDate))
            .ToList();

        var currentMetrics = current.Values.ToList();
        return new WeeklyReportDto(week, weekEnd, week == thisWeek, clock.GetUtcNow().UtcDateTime,
            Sum(currentMetrics), Sum(prior[0].Values.ToList()), Median(prior.Select(p => Sum(p.Values.ToList())).ToList()),
            week.AddDays(-7 * TypicalWeeks), week.AddDays(-1),
            currentMetrics.Sum(m => m.AssignedPending), currentMetrics.Sum(m => m.AssignedOverdue),
            memberDtos, probation, due,
            await AnomaliesAsync(ids, week.AddDays(-7 * TypicalWeeks), weekEnd, week, viewer.ProjectIds, ct),
            await CrmDataSinceAsync(ct),
            await NarrativeAsync(week, ct));
    }

    public async Task<WeeklyNarrativeDto> SaveNoteAsync(PerformanceViewer viewer, DateOnly weekStart, SaveWeeklyReportNoteRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        var week = ResolveWeek(weekStart, MondayOf(performance.Today));
        if (week != weekStart) throw new ArgumentException("สัปดาห์ต้องเริ่มวันจันทร์");
        var now = clock.GetUtcNow().UtcDateTime;
        var note = await db.WeeklyReportNotes.SingleOrDefaultAsync(x => x.WeekStart == week, ct);
        var before = note is null ? null : Snapshot(note);
        // validate ก่อน Add — ถ้า Update โยน error ต้องไม่มีแถวใหม่ค้างใน DbContext
        var isNew = note is null;
        note ??= new WeeklyReportNote(week, viewer.UserId, now);
        note.Update(r.Health, r.Headline, r.KeyRisk, r.Ask, r.NextWeekPlan, r.MemberNotes, viewer.UserId, now);
        if (isNew) db.WeeklyReportNotes.Add(note);
        db.AuditLogs.Add(new AuditLog(viewer.UserId, before is null ? "Created" : "Updated", AuditEntityType, week.ToString("yyyy-MM-dd"),
            $"บันทึกสรุปประจำสัปดาห์ {week:yyyy-MM-dd} (สถานะ {note.Health ?? "ยังไม่ระบุ"})", before, Snapshot(note), null));
        await db.SaveChangesAsync(ct);
        return (await NarrativeAsync(week, ct))!;
    }

    private static DateOnly ResolveWeek(DateOnly? requested, DateOnly thisWeek)
    {
        var week = requested is { } r ? MondayOf(r) : thisWeek;
        if (week > thisWeek) throw new ArgumentException("ยังไม่ถึงสัปดาห์ที่เลือก");
        if (week < thisWeek.AddDays(-7 * 104)) throw new ArgumentException("ดูย้อนหลังได้ไม่เกิน 2 ปี");
        return week;
    }

    /// <summary>บันทึกงาน/ผลงานเด่นของสัปดาห์ — ไม่รวมข้อสังเกต (Concern) เพราะรายงานนี้ส่งออกถึงผู้บริหาร</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<WeeklyWorkItemDto>>> WorkItemsAsync(List<Guid> userIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.WorkLogEntries.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId) && x.WorkDate >= from && x.WorkDate <= to && x.Kind != "Concern")
            .OrderByDescending(x => x.Kind == "Highlight").ThenBy(x => x.WorkDate).ThenBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.UserId, x.WorkDate, x.Kind, x.Category, x.Title, x.Description,
                ProjectName = x.ProjectId != null ? db.Projects.Where(p => p.ProjectId == x.ProjectId).Select(p => p.ProjectName).FirstOrDefault() : null,
            })
            .ToListAsync(ct);
        return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<WeeklyWorkItemDto>)g.Select(x => new WeeklyWorkItemDto(x.WorkDate, x.Kind, x.Category, x.Title, x.Description, x.ProjectName)).ToList());
    }

    private async Task<IReadOnlyList<WeeklyAnomalyDto>> AnomaliesAsync(List<Guid> userIds, DateOnly from, DateOnly to, DateOnly reportWeek, Guid[] projects, CancellationToken ct)
    {
        var users = userIds.ToArray();
        DateTime f = Utc(from), t = Utc(to.AddDays(1));
        var executions = await DailyExecutions(db, users, f, t, projects).ToListAsync(ct);
        var defects = await DailyDefects(db, users, f, t, projects).ToListAsync(ct);
        var testCases = await DailyTestCases(db, users, f, t, projects).ToListAsync(ct);
        static IEnumerable<(DateOnly, int)> Days(List<DailyCount> rows) => rows.Select(x => (DateOnly.FromDateTime(x.Day), x.Count));
        return FindAnomalies("Execution", Days(executions), reportWeek)
            .Concat(FindAnomalies("Defect", Days(defects), reportWeek))
            .Concat(FindAnomalies("TestCase", Days(testCases), reportWeek))
            .OrderByDescending(x => x.Date).ToList();
    }

    // จำนวนรายการต่อวันตามเวลาไทย (group ฝั่ง SQL) — public static เพื่อให้ unit test ตรวจการแปลงเป็น SQL ได้
    public static IQueryable<DailyCount> DailyExecutions(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
        => db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && users.Contains(x.TesterUserId) && x.CompletedAt >= from && x.CompletedAt < to && !x.CycleCase.Cycle.IsDeleted && projects.Contains(x.CycleCase.Cycle.ProjectId))
            .GroupBy(x => x.CompletedAt!.Value.AddHours(7).Date).Select(g => new DailyCount(g.Key, g.Count()));

    public static IQueryable<DailyCount> DailyDefects(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = users.Select(x => (Guid?)x).ToArray();
        return db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId))
            .GroupBy(x => x.CreatedAt.AddHours(7).Date).Select(g => new DailyCount(g.Key, g.Count()));
    }

    public static IQueryable<DailyCount> DailyTestCases(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = users.Select(x => (Guid?)x).ToArray();
        return db.TestCases.AsNoTracking()
            .Where(x => !x.IsDeleted && u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId))
            .GroupBy(x => x.CreatedAt.AddHours(7).Date).Select(g => new DailyCount(g.Key, g.Count()));
    }

    /// <summary>วันที่จำนวนรายการ ≥ 100 และ ≥ 4 เท่าของค่ากลางวันอื่นที่มีข้อมูล (วันเดียวในช่วง = ไม่มีฐานเทียบ ไม่แจ้ง)</summary>
    public static IEnumerable<WeeklyAnomalyDto> FindAnomalies(string kind, IEnumerable<(DateOnly Day, int Count)> daily, DateOnly reportWeek)
    {
        var days = daily.Where(x => x.Count > 0).ToList();
        foreach (var d in days.Where(x => x.Count >= AnomalyMinCount))
        {
            var others = days.Where(x => x.Day != d.Day).Select(x => (decimal)x.Count).ToList();
            if (others.Count == 0) continue;
            var typical = MedianOf(others);
            if (d.Count >= typical * AnomalyFactor)
                yield return new WeeklyAnomalyDto(d.Day, kind, d.Count, Math.Round(typical, 1), d.Day >= reportWeek && d.Day <= reportWeek.AddDays(6));
        }
    }

    /// <summary>วันแรกที่ดึง CRM ทั้งทีมสำเร็จ — ก่อนวันนี้ตัวเลข CRM มาจาก snapshot ที่เก็บเฉพาะ Ticket ที่เปิดใน QA Hub จึงไม่ครบ</summary>
    private async Task<DateOnly?> CrmDataSinceAsync(CancellationToken ct)
    {
        var first = await db.CrmMirrorRuns.AsNoTracking().Where(x => x.Status == "Succeeded").OrderBy(x => x.StartedAt).Select(x => (DateTime?)x.StartedAt).FirstOrDefaultAsync(ct);
        return first is { } at ? DateOnly.FromDateTime(at + ThaiOffset) : null;
    }

    private async Task<WeeklyNarrativeDto?> NarrativeAsync(DateOnly week, CancellationToken ct)
    {
        var note = await db.WeeklyReportNotes.AsNoTracking().SingleOrDefaultAsync(x => x.WeekStart == week, ct);
        if (note is null) return null;
        var by = await db.Users.AsNoTracking().Where(u => u.UserId == note.UpdatedBy).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new WeeklyNarrativeDto(note.Health, note.Headline, note.KeyRisk, note.Ask, note.NextWeekPlan, note.MemberNotes(), note.UpdatedAt, by);
    }

    public static WeeklyTotalsDto Sum(IReadOnlyCollection<PerformanceMetrics> m) => new(
        m.Sum(x => x.Executions), m.Sum(x => x.Passed), m.Sum(x => x.Failed), m.Sum(x => x.Blocked),
        m.Sum(x => x.DefectsReported), m.Sum(x => x.DefectsCritical + x.DefectsHigh), m.Sum(x => x.DefectsRejected),
        m.Sum(x => x.TestCasesCreated), m.Sum(x => x.TestCaseRevisions),
        m.Sum(x => x.CrmTicketsHandled), m.Sum(x => x.CrmClosed), m.Sum(x => x.CrmHandedToDev), m.Sum(x => x.CrmAnswers),
        m.Sum(x => x.AutomationRuns), m.Sum(x => x.WorkLogs), m.Sum(x => x.Highlights));

    /// <summary>ค่ากลางรายช่อง — สัปดาห์ที่มีการนำเข้าข้อมูลจำนวนมากจะไม่ดึงค่าทั้งชุดเหมือนค่าเฉลี่ย</summary>
    public static WeeklyTotalsDto Median(IReadOnlyList<WeeklyTotalsDto> weeks)
    {
        decimal M(Func<WeeklyTotalsDto, decimal> f) => MedianOf(weeks.Select(f).ToList());
        return new WeeklyTotalsDto(
            M(x => x.Executions), M(x => x.Passed), M(x => x.Failed), M(x => x.Blocked), M(x => x.DefectsReported), M(x => x.DefectsCriticalHigh), M(x => x.DefectsRejected),
            M(x => x.TestCasesCreated), M(x => x.TestCaseRevisions), M(x => x.CrmTicketsHandled), M(x => x.CrmClosed), M(x => x.CrmHandedToDev), M(x => x.CrmAnswers),
            M(x => x.AutomationRuns), M(x => x.WorkLogs), M(x => x.Highlights));
    }

    private static decimal MedianOf(IReadOnlyCollection<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static DateTime Utc(DateOnly thaiDate) => PerformanceService.ToUtc(thaiDate);

    private static void RequireManage(PerformanceViewer viewer)
    {
        if (!viewer.CanManage) throw new PerformanceAccessDeniedException("ต้องมีสิทธิ์จัดการผลงานทีม (PERFORMANCE.EDIT)");
    }

    private static string Snapshot(WeeklyReportNote n) => System.Text.Json.JsonSerializer.Serialize(new { n.Health, n.Headline, n.KeyRisk, n.Ask, n.NextWeekPlan, n.MemberNotesJson });
}
