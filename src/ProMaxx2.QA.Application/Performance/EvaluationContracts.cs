using System.Text.Json;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Application.Performance;

public sealed record EvaluationSummaryDto(Guid EvaluationId, Guid UserId, string? UserName, string Type, string Round, int Year, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate, string Status, decimal? OverallScore, string? Grade, string? Recommendation, string? EvaluatorName, DateTime? CompletedAt, DateTime? AcknowledgedAt, bool IsOverdue);
/// <summary>ExternalAverage/ExternalCount = ค่าเฉลี่ยคะแนนเกณฑ์นี้จากหัวหน้าแผนกอื่นที่ส่งแล้ว (เห็นเมื่อเห็นคะแนนหัวหน้าตรง)</summary>
public sealed record EvaluationScoreDto(Guid EvaluationScoreId, string Group, string Name, string? Description, int Weight, int? SelfScore, string? SelfComment, int? ManagerScore, string? ManagerComment, decimal? ExternalAverage = null, int ExternalCount = 0);
public sealed record EvaluationEmployeeDto(string? EmployeeCode, string? Position, string? Level, DateOnly? HireDate, string? SupervisorName);
public sealed record EvaluationDetailDto(EvaluationSummaryDto Summary, EvaluationEmployeeDto Employee, string TemplateName, string? SelfComment, DateTime? SelfSubmittedAt, string? Strengths, string? Improvements, string? NextGoals, DateOnly? ExtendToDate, string? EmployeeComment, string? ReopenReason, DateOnly? ProbationEndDate, IReadOnlyList<EvaluationScoreDto> Scores, PerformanceMetrics Metrics, bool MetricsIsSnapshot, bool CanSelfEdit, bool CanManagerEdit, bool CanAcknowledge, bool CanReopen, string? ManagerRestriction = null,
    int ExternalWeight = EvaluationTemplate.DefaultExternalWeight, decimal? ManagerScore = null, decimal? ExternalScore = null, IReadOnlyList<EvaluationExternalReviewDto>? ExternalReviews = null, bool CanManageExternal = false);
/// <summary>ผู้ประเมินจากแผนกอื่นหนึ่งคน — Code ส่งเฉพาะหัวหน้าตรงและเฉพาะลิงก์ที่ยังรอส่ง (หน้าเว็บสร้างลิงก์ `/?er=code`)</summary>
public sealed record EvaluationExternalReviewDto(Guid ReviewId, string ReviewerName, string? ReviewerDepartment, string? Note, string Status, DateTime ExpiresAt, DateTime CreatedAt, DateTime? SubmittedAt, decimal? WeightedScore, string? Comment, int ViewCount, string? Code);
public sealed record CreateExternalReviewRequest(string ReviewerName, string? ReviewerDepartment, string? Note, int ExpiresInDays);
/// <summary>แบบฟอร์มสำหรับผู้ประเมินภายนอก (anonymous) — ไม่มีคะแนนตนเอง/หัวหน้าตรง</summary>
public sealed record ExternalReviewFormDto(string ReviewerName, string? ReviewerDepartment, string? Note, string? RequestedByName, string EmployeeName, string? Position, string Type, string Round, int Year, DateOnly PeriodStart, DateOnly PeriodEnd, string TemplateName, DateTime ExpiresAt, string Status, DateTime? SubmittedAt, IReadOnlyList<ExternalReviewCriterionDto> Criteria);
public sealed record ExternalReviewCriterionDto(Guid EvaluationScoreId, string Group, string Name, string? Description, int Weight);
public sealed record SubmitExternalReviewRequest(IReadOnlyList<EvaluationScoreInput> Scores, string? Comment);
/// <summary>ลิงก์ประเมินถูกยกเลิก/หมดอายุ/การประเมินปิดแล้ว — ตอบ 410</summary>
public sealed class ExternalReviewGoneException(string message) : Exception(message);
public sealed record EvaluationScoreInput(Guid EvaluationScoreId, int? Score, string? Comment);
public sealed record SaveSelfEvaluationRequest(IReadOnlyList<EvaluationScoreInput> Scores, string? Comment, bool Submit);
public sealed record SaveManagerEvaluationRequest(IReadOnlyList<EvaluationScoreInput> Scores, string? Strengths, string? Improvements, string? NextGoals, string? Recommendation, DateOnly? ExtendToDate, bool Complete);
public sealed record AcknowledgeEvaluationRequest(string? Comment);
public sealed record ReopenEvaluationRequest(string Reason);
public sealed record CreateQuarterlyEvaluationsRequest(int Year, int Quarter);
public sealed record CreateQuarterlyEvaluationsResult(int Created, int Skipped);
public sealed record EvaluationCriterionDto(string Group, string Name, string? Description, int Weight);
public sealed record EvaluationTemplateDto(Guid EvaluationTemplateId, string Type, string Name, bool IsActive, IReadOnlyList<EvaluationCriterionDto> Criteria, int ExternalWeight = EvaluationTemplate.DefaultExternalWeight);
public sealed record SaveEvaluationTemplateRequest(string Name, IReadOnlyList<EvaluationCriterionDto> Criteria, int? ExternalWeight = null);
public sealed record EvaluationGroupScoreDto(string Group, decimal Score);
public sealed record EvaluationComparisonRoundDto(Guid EvaluationId, string Type, string Round, int Year, DateOnly PeriodStart, DateOnly PeriodEnd, decimal OverallScore, string Grade, IReadOnlyList<EvaluationGroupScoreDto> Groups);
public sealed record EvaluationComparisonDto(IReadOnlyList<string> Groups, IReadOnlyList<EvaluationComparisonRoundDto> Rounds);
/// <summary>ผลรอบไตรมาสหนึ่งช่อง — คะแนน/เกรดมีเฉพาะรอบที่ปิดแล้ว; รอบที่ยังไม่ปิดมีแค่สถานะ</summary>
public sealed record TeamQuarterCellDto(string Round, Guid? EvaluationId, string? Status, decimal? OverallScore, string? Grade, bool IsOverdue);
public sealed record TeamComparisonRowDto(Guid UserId, string DisplayName, string? Position, string EmploymentStatus, IReadOnlyList<TeamQuarterCellDto> Quarters, decimal? AverageScore, decimal? PreviousYearQ4Score);
public sealed record TeamComparisonDto(int Year, IReadOnlyList<TeamComparisonRowDto> Rows, IReadOnlyList<decimal?> QuarterAverages);

public interface IEvaluationRepository
{
    Task<IReadOnlyList<EvaluationTemplate>> ListTemplatesAsync(CancellationToken ct);
    Task<EvaluationTemplate?> FindActiveTemplateAsync(string type, CancellationToken ct);
    Task<EvaluationTemplate?> FindTemplateAsync(Guid id, CancellationToken ct);
    Task AddTemplateAsync(EvaluationTemplate template, CancellationToken ct);
    Task<IReadOnlyList<Evaluation>> ListAsync(Guid? userId, bool openOnly, CancellationToken ct);
    Task<Evaluation?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Evaluation>> FindByUserAsync(Guid userId, IReadOnlyCollection<string> rounds, CancellationToken ct);
    Task<IReadOnlyList<Evaluation>> ListClosedWithScoresAsync(Guid userId, int take, CancellationToken ct);
    /// <summary>รอบไตรมาสของปีที่ระบุ (+ Q4 ของปีก่อนไว้เทียบ) ไม่รวม Scores</summary>
    Task<IReadOnlyList<Evaluation>> ListQuarterlyAsync(int year, CancellationToken ct);
    Task<IReadOnlyCollection<Guid>> UsersWithRoundAsync(string round, int year, CancellationToken ct);
    Task AddAsync(Evaluation evaluation, CancellationToken ct);
    /// <summary>ลิงก์ผู้ประเมินแผนกอื่นของการประเมินหนึ่งรอบ (tracked + Scores)</summary>
    Task<IReadOnlyList<EvaluationExternalReview>> ListExternalReviewsAsync(Guid evaluationId, CancellationToken ct);
    Task<EvaluationExternalReview?> FindExternalReviewAsync(Guid reviewId, CancellationToken ct);
    Task<EvaluationExternalReview?> FindExternalReviewByCodeAsync(string code, CancellationToken ct);
    Task<bool> ExternalReviewCodeExistsAsync(string code, CancellationToken ct);
    Task AddExternalReviewAsync(EvaluationExternalReview review, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}

public sealed partial class EvaluationService(IEvaluationRepository repository, IPerformanceRepository performance, PerformanceService performanceService)
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);
    private DateOnly Today => performanceService.Today;

    public async Task<IReadOnlyList<EvaluationSummaryDto>> ListAsync(PerformanceViewer viewer, Guid? userId, bool openOnly, CancellationToken ct)
    {
        if (!viewer.CanManage)
        {
            if (userId is { } other && other != viewer.UserId) throw new PerformanceAccessDeniedException("ดูได้เฉพาะการประเมินของตัวเอง");
            userId = viewer.UserId;
        }
        var items = await repository.ListAsync(userId, openOnly, ct);
        if (userId is null)
        {
            // มุมมองทีมไม่แสดงคนที่ลาออกแล้ว/บัญชีถูกปิด — ประวัติรายบุคคล (ส่ง userId) ยังเปิดดูได้
            var current = (await performance.ListMembersAsync(null, Today, ct)).Where(x => x.EmploymentStatus != "Resigned").Select(x => x.UserId).ToHashSet();
            items = items.Where(x => current.Contains(x.UserId)).ToList();
        }
        var names = await repository.UserNamesAsync(items.SelectMany(x => new[] { x.UserId, x.EvaluatorUserId ?? Guid.Empty }), ct);
        return items.Select(x => Summary(x, names)).ToList();
    }

    public async Task<EvaluationDetailDto> GetAsync(PerformanceViewer viewer, Guid id, CancellationToken ct)
    {
        var e = await FindVisibleAsync(viewer, id, ct);
        return await DetailAsync(viewer, e, ct);
    }

    public async Task<EvaluationDetailDto> SaveSelfAsync(PerformanceViewer viewer, Guid id, SaveSelfEvaluationRequest r, CancellationToken ct)
    {
        var e = await FindVisibleAsync(viewer, id, ct);
        if (e.UserId != viewer.UserId) throw new PerformanceAccessDeniedException("ประเมินตนเองได้เฉพาะเจ้าของการประเมิน");
        e.SaveSelf(ToMap(r.Scores), r.Comment, r.Submit, viewer.UserId);
        if (r.Submit) Audit(viewer, e, "SelfSubmitted", "ส่งแบบประเมินตนเอง");
        await performance.SaveChangesAsync(ct);
        return await DetailAsync(viewer, e, ct);
    }

    public async Task<EvaluationDetailDto> SaveManagerAsync(PerformanceViewer viewer, Guid id, SaveManagerEvaluationRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        var e = await FindVisibleAsync(viewer, id, ct);
        await RequireSupervisorAsync(viewer, e, ct);
        e.SaveManager(ToMap(r.Scores), r.Strengths, r.Improvements, r.NextGoals, r.Recommendation, r.ExtendToDate, viewer.UserId);
        if (r.Complete)
        {
            var profile = await performance.FindProfileAsync(e.UserId, ct);
            var metrics = await MetricsAsync(viewer, e, ct);
            var probationEnd = profile?.ProbationEndDate ?? e.PeriodEnd;
            // คะแนนแผนกอื่นที่ส่งแล้วนำมาคำนวณ; ลิงก์ที่ยังไม่ส่งถูกปิด (หน้าเว็บเตือนก่อนยืนยันส่งผล)
            var reviews = await repository.ListExternalReviewsAsync(e.EvaluationId, ct);
            var external = reviews.Where(x => x.IsSubmitted && x.WeightedScore is not null).Select(x => x.WeightedScore!.Value).ToList();
            e.Complete(JsonSerializer.Serialize(metrics, SnapshotJson), probationEnd, viewer.UserId, external);
            var now = DateTime.UtcNow;
            var closedLinks = 0;
            foreach (var pending in reviews.Where(x => x.IsUsable(now))) { pending.Revoke(viewer.UserId, now); closedLinks++; }
            Audit(viewer, e, "Completed", $"ส่งผลประเมิน {e.Round}/{e.Year} คะแนน {e.OverallScore} เกรด {e.Grade}" +
                (external.Count > 0 ? $" (หัวหน้าตรง {e.ManagerScore} · แผนกอื่น {e.ExternalScore} จาก {external.Count} คน น้ำหนัก {e.ExternalWeight}%)" : "") +
                (closedLinks > 0 ? $" ปิดลิงก์แผนกอื่นที่ยังไม่ส่ง {closedLinks} ลิงก์" : "") +
                (e.Recommendation is null ? "" : $" ผลทดลองงาน {e.Recommendation}"));
            if (e.IsFinalProbation && profile is not null) await ApplyProbationDecisionAsync(viewer, e, profile, ct);
        }
        await performance.SaveChangesAsync(ct);
        return await DetailAsync(viewer, e, ct);
    }

    public async Task<EvaluationDetailDto> AcknowledgeAsync(PerformanceViewer viewer, Guid id, AcknowledgeEvaluationRequest r, CancellationToken ct)
    {
        var e = await FindVisibleAsync(viewer, id, ct);
        e.Acknowledge(r.Comment, viewer.UserId);
        Audit(viewer, e, "Acknowledged", "พนักงานรับทราบผลการประเมิน");
        await performance.SaveChangesAsync(ct);
        return await DetailAsync(viewer, e, ct);
    }

    /// <summary>เปิดผลที่ปิดแล้วกลับมาแก้ — ไม่ย้อนผลต่อโปรไฟล์ที่เกิดจากรอบสรุปผลไปแล้ว (หัวหน้าแก้สถานะพนักงานเองถ้าจำเป็น)</summary>
    public async Task<EvaluationDetailDto> ReopenAsync(PerformanceViewer viewer, Guid id, ReopenEvaluationRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        var e = await FindVisibleAsync(viewer, id, ct);
        await RequireSupervisorAsync(viewer, e, ct);
        e.Reopen(r.Reason, viewer.UserId);
        Audit(viewer, e, "Reopened", $"เปิดการประเมินใหม่: {e.ReopenReason}");
        await performance.SaveChangesAsync(ct);
        return await DetailAsync(viewer, e, ct);
    }

    /// <summary>
    /// เทียบผลการประเมินที่ปิดแล้วของพนักงานหนึ่งคน (เก่า → ใหม่) — คะแนนรวม + คะแนนหัวหน้าเฉลี่ยถ่วงน้ำหนักรายกลุ่มเกณฑ์
    /// เฉพาะรอบที่ปิดแล้ว พนักงานจึงดูของตัวเองได้โดยไม่เห็นคะแนนร่างของหัวหน้า
    /// </summary>
    public async Task<EvaluationComparisonDto> CompareAsync(PerformanceViewer viewer, Guid userId, CancellationToken ct)
    {
        if (!viewer.CanManage && userId != viewer.UserId) throw new PerformanceAccessDeniedException("ดูได้เฉพาะการประเมินของตัวเอง");
        var items = await repository.ListClosedWithScoresAsync(userId, MaxComparisonRounds, ct);
        var rounds = items.OrderBy(x => x.PeriodEnd).ThenBy(x => x.CompletedAt).Select(e => new EvaluationComparisonRoundDto(
            e.EvaluationId, e.Type, e.Round, e.Year, e.PeriodStart, e.PeriodEnd, e.OverallScore ?? 0, e.Grade ?? "-",
            e.Scores.Where(x => x.ManagerScore is not null).GroupBy(x => x.Group)
                .Select(g => new EvaluationGroupScoreDto(g.Key, Math.Round(g.Sum(x => (decimal)x.ManagerScore!.Value * x.Weight) / Math.Max(1, g.Sum(x => x.Weight)), 2)))
                .ToList())).ToList();
        // กลุ่มมาตรฐานก่อน แล้วต่อด้วยกลุ่มที่ไม่อยู่ในรายการ (แบบประเมินเก่าที่อาจใช้ชื่ออื่น)
        var present = rounds.SelectMany(r => r.Groups.Select(g => g.Group)).Distinct().ToList();
        var groups = EvaluationTemplate.Groups.Where(present.Contains).Concat(present.Where(g => !EvaluationTemplate.Groups.Contains(g))).ToList();
        return new EvaluationComparisonDto(groups, rounds);
    }

    public const int MaxComparisonRounds = 8;
    private static readonly string[] Quarters = ["Q1", "Q2", "Q3", "Q4"];

    /// <summary>
    /// เทียบผลรายไตรมาสของทั้งทีมในปีเดียว (หัวหน้าเท่านั้น) — แถว = สมาชิกทีมที่ยังไม่ลาออก (ไม่รวมคนที่ลาออก/บัญชีถูกปิด แม้มีผลในปีนั้น), คอลัมน์ = Q1–Q4
    /// ค่าเฉลี่ยคิดจากรอบที่ปิดแล้วเท่านั้น; มี Q4 ของปีก่อนไว้ดูแนวโน้มของ Q1
    /// </summary>
    public async Task<TeamComparisonDto> CompareTeamAsync(PerformanceViewer viewer, int year, CancellationToken ct)
    {
        RequireManage(viewer);
        if (year is < 2000 or > 2100) throw new ArgumentException("ปีไม่ถูกต้อง");
        var evaluations = await repository.ListQuarterlyAsync(year, ct);
        var members = (await performance.ListMembersAsync(null, Today, ct)).ToDictionary(x => x.UserId);
        var userIds = members.Keys.Concat(evaluations.Where(x => x.Year == year).Select(x => x.UserId)).Distinct().ToList();
        var names = await repository.UserNamesAsync(userIds.Where(id => !members.ContainsKey(id)), ct);

        static decimal? Score(Evaluation? e) => e is { IsClosed: true } ? e.OverallScore : null;
        var rows = userIds.Select(id =>
        {
            var mine = evaluations.Where(x => x.UserId == id).ToList();
            // รอบเดียวกันอาจมีมากกว่าหนึ่งแถว (เช่น สร้างซ้ำหลังลบ) — ใช้แถวที่สร้างล่าสุด
            Evaluation? Pick(string round, int y) => mine.Where(x => x.Round == round && x.Year == y).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            var cells = Quarters.Select(q =>
            {
                var e = Pick(q, year);
                return new TeamQuarterCellDto(q, e?.EvaluationId, e?.Status, Score(e), e is { IsClosed: true } ? e.Grade : null, e is { IsClosed: false } && e.DueDate < Today);
            }).ToList();
            var closed = cells.Where(c => c.OverallScore is not null).Select(c => c.OverallScore!.Value).ToList();
            members.TryGetValue(id, out var m);
            return new TeamComparisonRowDto(id, m?.DisplayName ?? names.GetValueOrDefault(id) ?? "-", m?.Position, m?.EmploymentStatus ?? "Resigned", cells,
                closed.Count > 0 ? Math.Round(closed.Average(), 2) : null, Score(Pick("Q4", year - 1)));
        })
        // ไม่แสดงคนที่ไม่มีรอบไตรมาสเลยและไม่ใช่พนักงานประจำ (เช่น ยังทดลองงาน) เพื่อไม่ให้ตารางเต็มไปด้วยช่องว่าง
        .Where(r => r.EmploymentStatus != "Resigned")
        .Where(r => r.Quarters.Any(c => c.EvaluationId is not null) || r.EmploymentStatus == "Permanent")
        .OrderBy(r => r.DisplayName, StringComparer.CurrentCulture).ToList();
        var averages = Quarters.Select((q, i) =>
        {
            var scores = rows.Select(r => r.Quarters[i].OverallScore).Where(s => s is not null).Select(s => s!.Value).ToList();
            return scores.Count > 0 ? Math.Round(scores.Average(), 2) : (decimal?)null;
        }).ToList();
        return new TeamComparisonDto(year, rows, averages);
    }

    /// <summary>สร้างรอบไตรมาสให้พนักงานประจำทุกคนในทีม — คนที่มีรอบนี้อยู่แล้วข้ามไป (กดซ้ำได้)</summary>
    public async Task<CreateQuarterlyEvaluationsResult> CreateQuarterlyAsync(PerformanceViewer viewer, CreateQuarterlyEvaluationsRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        if (r.Quarter is < 1 or > 4) throw new ArgumentException("ไตรมาสต้องเป็น 1–4");
        if (r.Year is < 2000 or > 2100) throw new ArgumentException("ปีไม่ถูกต้อง");
        var start = new DateOnly(r.Year, (r.Quarter - 1) * 3 + 1, 1);
        var end = start.AddMonths(3).AddDays(-1);
        var round = $"Q{r.Quarter}";
        var template = await EnsureTemplateAsync("Quarterly", viewer.UserId, ct);
        var members = (await performance.ListMembersAsync(null, Today, ct)).Where(x => x.EmploymentStatus == "Permanent").ToList();
        var existing = await repository.UsersWithRoundAsync(round, r.Year, ct);
        var created = 0;
        foreach (var m in members.Where(m => !existing.Contains(m.UserId)))
        {
            await repository.AddAsync(new Evaluation(m.UserId, "Quarterly", round, r.Year, start, end, end.AddDays(14), template, viewer.UserId), ct);
            created++;
        }
        await performance.SaveChangesAsync(ct);
        return new CreateQuarterlyEvaluationsResult(created, members.Count - created);
    }

    /// <summary>
    /// ให้รอบทดลองงานตรงกับโปรไฟล์ — Probation: P30/P60/P90 (ครบวันที่ 30/60/90) + PFinal (ครบก่อนสิ้นสุด 14 วัน), Extended: PFinal รอบใหม่
    /// สร้างเฉพาะรอบที่ยังไม่มี และเลื่อนวันของรอบที่ยังไม่ปิดเมื่อวันเริ่มงาน/วันสิ้นสุดเปลี่ยน — เรียกซ้ำได้
    /// </summary>
    public async Task SyncProbationRoundsAsync(PerformanceViewer viewer, Guid userId, CancellationToken ct)
    {
        RequireManage(viewer);
        var profile = await performance.FindProfileAsync(userId, ct);
        if (profile is not { IsOnProbation: true, HireDate: { } hire, ProbationEndDate: { } end }) return;
        var finalDue = end.AddDays(-14) < hire ? hire : end.AddDays(-14);
        var existing = await repository.FindByUserAsync(userId, Evaluation.ProbationRounds, ct);
        var plan = new List<(string Round, DateOnly Start, DateOnly End, DateOnly Due)>();
        if (profile.EmploymentStatus == "Probation")
        {
            foreach (var (round, day) in new[] { ("P30", 30), ("P60", 60), ("P90", 90) })
            {
                var due = hire.AddDays(day - 1);
                if (due >= finalDue) continue; // ทดลองงานสั้นกว่าปกติ — ข้ามรอบที่ชนรอบสรุป
                plan.Add((round, hire.AddDays(day - 30), due, due));
            }
        }
        plan.Add(("PFinal", hire, end, finalDue));

        foreach (var p in plan)
        {
            var current = existing.Where(x => x.Round == p.Round).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            if (current is not null && !current.IsClosed) { current.Reschedule(p.Start, p.End, p.Due); continue; }
            // รอบกลางที่ปิดแล้วไม่สร้างซ้ำ; PFinal สร้างใหม่ได้เมื่อรอบเดิมปิดแล้ว (กรณีขยายทดลองงาน)
            if (current is not null && (p.Round != "PFinal" || profile.EmploymentStatus != "Extended" || current.Recommendation != "Extend")) continue;
            var t = await EnsureTemplateAsync("Probation", viewer.UserId, ct);
            await repository.AddAsync(new Evaluation(userId, "Probation", p.Round, p.End.Year, p.Start, p.End, p.Due, t, viewer.UserId), ct);
        }
        await performance.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EvaluationTemplateDto>> ListTemplatesAsync(PerformanceViewer viewer, CancellationToken ct)
    {
        RequireManage(viewer);
        foreach (var type in EvaluationTemplate.Types) await EnsureTemplateAsync(type, viewer.UserId, ct);
        await performance.SaveChangesAsync(ct);
        return (await repository.ListTemplatesAsync(ct)).Select(TemplateDto).ToList();
    }

    public async Task<EvaluationTemplateDto> SaveTemplateAsync(PerformanceViewer viewer, Guid id, SaveEvaluationTemplateRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        var t = await repository.FindTemplateAsync(id, ct) ?? throw new EntityNotFoundException("ไม่พบแบบประเมิน");
        t.ReplaceCriteria(r.Name, (r.Criteria ?? []).Select(x => (x.Group, x.Name, x.Description, x.Weight)).ToList(), viewer.UserId, r.ExternalWeight);
        performance.AddAudit(new AuditLog(viewer.UserId, "Updated", "EvaluationTemplate", t.EvaluationTemplateId.ToString(), $"แก้แบบประเมิน {t.Name} ({t.Criteria.Count} เกณฑ์, น้ำหนักแผนกอื่น {t.ExternalWeight}%)", null, null, null));
        await performance.SaveChangesAsync(ct);
        return TemplateDto(t);
    }

    private async Task ApplyProbationDecisionAsync(PerformanceViewer viewer, Evaluation e, EmployeeProfile profile, CancellationToken ct)
    {
        switch (e.Recommendation)
        {
            case "Pass":
                profile.ConfirmPermanent(viewer.UserId);
                performance.AddAudit(new AuditLog(viewer.UserId, "ProbationPassed", "EmployeeProfile", profile.UserId.ToString(), "ผ่านทดลองงาน — เปลี่ยนเป็นพนักงานประจำ", null, null, null));
                break;
            case "Extend":
                profile.ExtendProbation(e.ExtendToDate!.Value, viewer.UserId);
                performance.AddAudit(new AuditLog(viewer.UserId, "ProbationExtended", "EmployeeProfile", profile.UserId.ToString(), $"ขยายทดลองงานถึง {e.ExtendToDate:yyyy-MM-dd}", null, null, null));
                var hire = profile.HireDate ?? e.PeriodStart;
                var due = e.ExtendToDate.Value.AddDays(-14) < Today ? Today : e.ExtendToDate.Value.AddDays(-14);
                var t = await EnsureTemplateAsync("Probation", viewer.UserId, ct);
                await repository.AddAsync(new Evaluation(e.UserId, "Probation", "PFinal", e.ExtendToDate.Value.Year, hire, e.ExtendToDate.Value, due, t, viewer.UserId), ct);
                break;
            case "NotPass":
                performance.AddAudit(new AuditLog(viewer.UserId, "ProbationNotPassed", "EmployeeProfile", profile.UserId.ToString(), "ไม่ผ่านทดลองงาน", null, null, null));
                break;
        }
    }

    private async Task<EvaluationTemplate> EnsureTemplateAsync(string type, Guid actor, CancellationToken ct)
    {
        var existing = await repository.FindActiveTemplateAsync(type, ct);
        if (existing is not null) return existing;
        var t = new EvaluationTemplate(type, type == "Probation" ? "แบบประเมินทดลองงาน" : "แบบประเมินรายไตรมาส", actor);
        t.ReplaceCriteria(t.Name, EvaluationTemplate.DefaultCriteria(type), actor);
        await repository.AddTemplateAsync(t, ct);
        return t;
    }

    private async Task<Evaluation> FindVisibleAsync(PerformanceViewer viewer, Guid id, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("ไม่พบการประเมิน");
        if (!viewer.CanManage && e.UserId != viewer.UserId) throw new EntityNotFoundException("ไม่พบการประเมิน");
        return e;
    }

    private async Task<PerformanceMetrics> MetricsAsync(PerformanceViewer viewer, Evaluation e, CancellationToken ct)
    {
        var metrics = await performanceService.CollectMetricsAsync([e.UserId], PerformanceService.ToUtc(e.PeriodStart), PerformanceService.ToUtc(e.PeriodEnd.AddDays(1)), viewer.ProjectIds, ct);
        return metrics.GetValueOrDefault(e.UserId) ?? new PerformanceMetrics();
    }

    /// <summary>
    /// ผู้ประเมิน (ให้คะแนน/ส่งผล/เปิดใหม่) = หัวหน้าที่ระบุในข้อมูลพนักงานเท่านั้น — ผู้มีสิทธิ์ดูทีมคนอื่น (รวม SYS_ADMIN) ดูได้แต่ประเมินแทนไม่ได้
    /// พนักงานที่ยังไม่มีหัวหน้าประเมินไม่ได้จนกว่าจะตั้งหัวหน้า; คืน null = ประเมินได้
    /// </summary>
    private static string? ManagerRestriction(PerformanceViewer viewer, Evaluation e, Domain.Performance.EmployeeProfile? profile, string? supervisorName)
    {
        if (!viewer.CanManage || e.UserId == viewer.UserId) return "ประเมินผลตัวเองในฐานะหัวหน้าไม่ได้";
        if (profile?.SupervisorUserId is not { } supervisor) return "ยังไม่ได้กำหนดหัวหน้าของพนักงานคนนี้ — ตั้งหัวหน้าในข้อมูลพนักงานก่อนจึงจะประเมินได้";
        return supervisor == viewer.UserId ? null : $"ประเมินได้เฉพาะหัวหน้าของพนักงาน ({supervisorName ?? "หัวหน้าที่ระบุ"})";
    }

    private async Task RequireSupervisorAsync(PerformanceViewer viewer, Evaluation e, CancellationToken ct)
    {
        var profile = await performance.FindProfileAsync(e.UserId, ct);
        var name = profile?.SupervisorUserId is { } sup ? (await repository.UserNamesAsync([sup], ct)).GetValueOrDefault(sup) : null;
        if (ManagerRestriction(viewer, e, profile, name) is { } reason) throw new PerformanceAccessDeniedException(reason);
    }

    /// <summary>พนักงานไม่เห็นคะแนน/ความเห็นของหัวหน้าจนกว่าหัวหน้าจะส่งผล</summary>
    private async Task<EvaluationDetailDto> DetailAsync(PerformanceViewer viewer, Evaluation e, CancellationToken ct)
    {
        var profile = await performance.FindProfileAsync(e.UserId, ct);
        var names = await repository.UserNamesAsync([e.UserId, e.EvaluatorUserId ?? Guid.Empty, profile?.SupervisorUserId ?? Guid.Empty], ct);
        var isOwner = e.UserId == viewer.UserId;
        var showManager = (viewer.CanManage && !isOwner) || e.IsClosed;
        var snapshot = e.MetricsSnapshotJson is { Length: > 0 } json ? JsonSerializer.Deserialize<PerformanceMetrics>(json, SnapshotJson) : null;
        var metrics = snapshot ?? await MetricsAsync(viewer, e, ct);
        var employee = new EvaluationEmployeeDto(profile?.EmployeeCode, profile?.Position, profile?.Level, profile?.HireDate,
            profile?.SupervisorUserId is { } sup ? names.GetValueOrDefault(sup) : null);
        var restriction = ManagerRestriction(viewer, e, profile, employee.SupervisorName);
        var reviews = showManager ? await repository.ListExternalReviewsAsync(e.EvaluationId, ct) : [];
        var submitted = reviews.Where(x => x.IsSubmitted).ToList();
        var externalByCriterion = submitted.SelectMany(x => x.Scores).GroupBy(x => x.EvaluationScoreId)
            .ToDictionary(g => g.Key, g => (Avg: Math.Round((decimal)g.Average(x => x.Score), 2), Count: g.Count()));
        var scores = e.Scores.OrderBy(x => x.SortOrder).Select(s =>
        {
            var has = externalByCriterion.TryGetValue(s.EvaluationScoreId, out var ext);
            return new EvaluationScoreDto(s.EvaluationScoreId, s.Group, s.Name, s.Description, s.Weight, s.SelfScore, s.SelfComment, showManager ? s.ManagerScore : null, showManager ? s.ManagerComment : null,
                has ? ext.Avg : null, has ? ext.Count : 0);
        }).ToList();
        var canManageExternal = restriction is null && !isOwner;
        var now = DateTime.UtcNow;
        var externalDtos = reviews.Select(x => ExternalReviewDto(x, now, canManageExternal)).ToList();
        // ระหว่างร่างแสดงค่าเฉลี่ยแผนกอื่นตอนนี้ — หลังส่งผลใช้ค่าที่ตรึงไว้ในการประเมิน
        var liveExternal = submitted.Count > 0 ? Math.Round(submitted.Average(x => x.WeightedScore ?? 0), 2) : (decimal?)null;
        return new EvaluationDetailDto(Summary(e, names), employee, e.TemplateName, e.SelfComment, e.SelfSubmittedAt,
            showManager ? e.Strengths : null, showManager ? e.Improvements : null, showManager ? e.NextGoals : null, showManager ? e.ExtendToDate : null,
            e.EmployeeComment, e.ReopenReason, profile?.ProbationEndDate, scores, metrics, snapshot is not null,
            CanSelfEdit: isOwner && e.Status == "Open",
            CanManagerEdit: restriction is null && !e.IsClosed,
            CanAcknowledge: isOwner && e.Status == "Completed",
            CanReopen: restriction is null && e.IsClosed,
            ManagerRestriction: isOwner ? null : restriction,
            ExternalWeight: e.ExternalWeight,
            ManagerScore: showManager ? e.ManagerScore : null,
            ExternalScore: showManager ? (e.IsClosed ? e.ExternalScore : liveExternal) : null,
            ExternalReviews: showManager ? externalDtos : null,
            CanManageExternal: canManageExternal);
    }

    private EvaluationSummaryDto Summary(Evaluation e, IReadOnlyDictionary<Guid, string> names)
    {
        // พนักงานเห็นผลรอบสรุปเมื่อปิดแล้วเท่านั้น — Recommendation ระหว่างร่างของหัวหน้าไม่ส่งออกไปในรายการ
        var recommendation = e.IsClosed ? e.Recommendation : null;
        return new EvaluationSummaryDto(e.EvaluationId, e.UserId, names.GetValueOrDefault(e.UserId), e.Type, e.Round, e.Year, e.PeriodStart, e.PeriodEnd, e.DueDate, e.Status,
            e.OverallScore, e.Grade, recommendation, e.EvaluatorUserId is { } ev ? names.GetValueOrDefault(ev) : null, e.CompletedAt, e.AcknowledgedAt, !e.IsClosed && e.DueDate < Today);
    }

    private void Audit(PerformanceViewer viewer, Evaluation e, string action, string summary)
        => performance.AddAudit(new AuditLog(viewer.UserId, action, "Evaluation", e.EvaluationId.ToString(), summary, null, null, null));

    private static Dictionary<Guid, (int? Score, string? Comment)> ToMap(IReadOnlyList<EvaluationScoreInput>? scores)
        => (scores ?? []).GroupBy(x => x.EvaluationScoreId).ToDictionary(g => g.Key, g => (g.Last().Score, g.Last().Comment));

    private static EvaluationTemplateDto TemplateDto(EvaluationTemplate t)
        => new(t.EvaluationTemplateId, t.Type, t.Name, t.IsActive, t.Criteria.OrderBy(x => x.SortOrder).Select(x => new EvaluationCriterionDto(x.Group, x.Name, x.Description, x.Weight)).ToList(), t.ExternalWeight);

    private static void RequireManage(PerformanceViewer viewer)
    {
        if (!viewer.CanManage) throw new PerformanceAccessDeniedException("ต้องมีสิทธิ์จัดการผลงานทีม (PERFORMANCE.EDIT)");
    }
}
