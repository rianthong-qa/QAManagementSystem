using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// ผลงานจาก CRM รายคน — จับคู่ผู้ใช้กับรหัสพนักงาน CRM ผ่าน <c>CrmConfigurations.Username</c> แล้วนับจาก 3 แหล่ง
/// 1) เส้นทาง Ticket จาก <c>CrmTicketEvents</c> (CrmMirrorSyncService ดึง CRM ทั้งทีมทุก 15 นาที + ประวัติ Flow Tracking เดิมที่นำเข้า)
///    ถ้ายังไม่มีข้อมูลในตารางนี้เลย ใช้วิธีเดิม: snapshot ใน <c>AuditLogs</c> EntityType <c>CrmTicketFlow</c> (บันทึกเมื่อมีคนเปิดดู Ticket ใน QA Hub)
/// 2) การอัปเดต Ticket ผ่าน QA Hub (<c>AuditLogs</c> EntityType <see cref="UpdateEntityType"/>, UserId = ผู้ทำจริง)
/// 3) Defect ที่สร้างจาก CRM (<c>DefectActivities</c> ActionType <c>CreatedFromCrm</c>)
/// "อยู่ในขั้น QA" = Assignto เป็นรหัสของผู้ใช้ และไม่ใช่เจ้าของเรื่อง (Support) หรือ Developer ของ Ticket — ตรรกะเดียวกับ <see cref="CrmFlowTrackingService.GetLastQaAsync"/>
/// </summary>
public sealed class CrmPerformanceSource(QaDbContext db) : ICrmPerformanceSource
{
    public const string UpdateEntityType = "CrmTicketUpdate";
    private static readonly string[] ClosedStatuses = ["Finish", "Close", "Closed"];

    public async Task<IReadOnlyDictionary<Guid, CrmWorkMetrics>> GetAsync(IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct)
    {
        var users = userIds.Distinct().ToArray();
        var codes = await db.CrmConfigurations.AsNoTracking()
            .Where(x => users.Contains(x.UserId) && x.Username != "")
            .Select(x => new { x.UserId, x.Username })
            .ToDictionaryAsync(x => x.UserId, x => x.Username.Trim(), ct);
        var nUsers = users.Select(x => (Guid?)x).ToArray();
        var updates = (await UpdateCounts(db, nUsers, fromUtc, toUtc).ToListAsync(ct)).Where(x => x.UserId != null).ToDictionary(x => x.UserId!.Value, x => x.Count);
        var defects = (await DefectCounts(db, nUsers, fromUtc, toUtc, projectIds).ToListAsync(ct)).Where(x => x.UserId != null).ToDictionary(x => x.UserId!.Value, x => x.Count);

        var useMirror = await db.CrmTicketEvents.AnyAsync(ct);
        var result = new Dictionary<Guid, CrmWorkMetrics>();
        foreach (var user in users)
        {
            var flow = !codes.TryGetValue(user, out var code) || code.Length == 0 ? new CrmFlowStats(0, 0, 0, 0, 0, 0, null)
                : useMirror ? await MirrorFlowAsync(code, fromUtc, toUtc, ct)
                : await LegacyFlowAsync(code, fromUtc, toUtc, ct);
            result[user] = new CrmWorkMetrics(code is { Length: > 0 }, flow.Handled, flow.ToDev, flow.ToSupport, flow.Closed,
                updates.GetValueOrDefault(user), defects.GetValueOrDefault(user), flow.EditErr, flow.Answers, flow.AvgQaHours);
        }
        return result;
    }

    private async Task<CrmFlowStats> MirrorFlowAsync(string code, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var jobNos = await InvolvedJobNos(db, code, toUtc).ToArrayAsync(ct);
        var events = new List<CrmTicketEvent>();
        foreach (var chunk in jobNos.Chunk(500)) events.AddRange(await RouteEvents(db, chunk, toUtc).ToListAsync(ct));
        var answers = await AnswerCount(db, code, fromUtc, toUtc).CountAsync(ct);
        return ComputeFlow(code, events, answers, fromUtc, toUtc);
    }

    /// <summary>
    /// คำนวณจากเหตุการณ์เส้นทาง (ไม่มี IO) — ไล่เหตุการณ์ของแต่ละ Ticket ตามเวลาแล้วสร้างช่วงที่ Ticket "อยู่ขั้น QA ของผู้ใช้"
    /// (Assignto = รหัสผู้ใช้ และไม่ใช่เจ้าของเรื่อง/Dev): ดูแล = มีช่วงซ้อนกับช่วงวันที่, ส่งต่อ/ปิด/EditErr = การเปลี่ยนที่เกิดในช่วงวันที่
    /// เวลาเฉลี่ยใน QA = ช่วงที่จบในช่วงวันที่ และเริ่มจากการเปลี่ยนจริง (ไม่นับช่วงที่เริ่มจาก Snapshot เพราะไม่รู้เวลาเริ่มจริง)
    /// </summary>
    public static CrmFlowStats ComputeFlow(string code, IEnumerable<CrmTicketEvent> routeEvents, int answers, DateTime fromUtc, DateTime toUtc)
    {
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toDev = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toSupport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var editErr = 0;
        var durations = new List<double>();
        bool InRange(DateTime at) => at >= fromUtc && at < toUtc;

        foreach (var ticket in routeEvents.Where(e => e.Kind != "Answer").GroupBy(e => e.JobNo, StringComparer.OrdinalIgnoreCase))
        {
            RouteState? state = null;
            DateTime? qaStart = null;
            var qaStartIsReal = false;
            foreach (var e in ticket.OrderBy(x => x.OccurredAt).ThenBy(x => x.CrmTicketEventId))
            {
                var prev = state ?? (e.Kind == "Changed" ? new RouteState(e.FromStatus, e.FromAssigneeCode, e.FromDeveloperCode, e.OwnerCode) : null);
                var next = new RouteState(e.ToStatus, e.ToAssigneeCode, e.ToDeveloperCode, e.OwnerCode ?? prev?.Owner);
                var wasMine = prev is not null && IsQaStage(prev, code);
                var isMine = IsQaStage(next, code);
                if (prev is not null && e.Kind == "Changed" && InRange(e.OccurredAt))
                {
                    if (wasMine && !isMine && !Same(next.Assignee, code))
                    {
                        if (Same(next.Assignee, next.Owner)) toSupport.Add(ticket.Key);
                        else if (Same(next.Assignee, next.Developer) || Same(next.Status, "Develop")) toDev.Add(ticket.Key);
                    }
                    if ((wasMine || isMine) && IsClosed(next.Status) && !IsClosed(prev.Status)) closed.Add(ticket.Key);
                    if (wasMine && Same(next.Status, "EditErr") && !Same(prev.Status, "EditErr")) editErr++;
                }
                if (!wasMine && isMine) { qaStart = e.OccurredAt; qaStartIsReal = e.Kind == "Changed" && prev is not null; }
                else if (wasMine && !isMine)
                {
                    var start = qaStart ?? e.OccurredAt;
                    if (start < toUtc && e.OccurredAt > fromUtc) handled.Add(ticket.Key);
                    if (qaStart is not null && qaStartIsReal && InRange(e.OccurredAt)) durations.Add((e.OccurredAt - qaStart.Value).TotalHours);
                    qaStart = null;
                }
                state = next;
            }
            if (qaStart is not null && qaStart < toUtc) handled.Add(ticket.Key); // ยังอยู่กับ QA จนถึงตอนนี้
        }
        return new CrmFlowStats(handled.Count, toDev.Count, toSupport.Count, closed.Count, editErr, answers,
            durations.Count > 0 ? Math.Round((decimal)durations.Average(), 1) : null);
    }

    private sealed record RouteState(string? Status, string? Assignee, string? Developer, string? Owner);
    private static bool IsQaStage(RouteState s, string code) => Same(s.Assignee, code) && !Same(s.Assignee, s.Owner) && !Same(s.Assignee, s.Developer);
    private static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static IQueryable<string> InvolvedJobNos(QaDbContext db, string code, DateTime toUtc)
        => db.CrmTicketEvents.AsNoTracking()
            .Where(x => x.Kind != "Answer" && x.OccurredAt < toUtc && (x.ToAssigneeCode == code || x.FromAssigneeCode == code))
            .Select(x => x.JobNo).Distinct();

    public static IQueryable<CrmTicketEvent> RouteEvents(QaDbContext db, string[] jobNos, DateTime toUtc)
        => db.CrmTicketEvents.AsNoTracking().Where(x => x.Kind != "Answer" && x.OccurredAt < toUtc && jobNos.Contains(x.JobNo));

    public static IQueryable<CrmTicketEvent> AnswerCount(QaDbContext db, string code, DateTime fromUtc, DateTime toUtc)
        => db.CrmTicketEvents.AsNoTracking().Where(x => x.Kind == "Answer" && x.ActorCode == code && x.OccurredAt >= fromUtc && x.OccurredAt < toUtc);

    private async Task<CrmFlowStats> LegacyFlowAsync(string code, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // LIKE กรองเบื้องต้นให้เหลือแถวที่มีรหัสนี้ แล้วตรวจรหัสแบบตรงตัวด้านล่าง (6101 ต้องไม่ match 61010)
        var rows = await FlowRows(db, code, fromUtc, toUtc).ToListAsync(ct);

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toDev = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toSupport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var before = CrmFlowTrackingService.Deserialize(row.BeforeJson);
            var after = CrmFlowTrackingService.Deserialize(row.AfterJson);
            if (after is null) continue;
            var wasMine = IsQaStageOf(before, code);
            var isMine = IsQaStageOf(after, code);
            if (wasMine || isMine) handled.Add(row.EntityId);
            if (wasMine && !isMine && !CrmTicketListParser.IsAssigneeInScope(after.Qa, code))
            {
                if (CrmFlowTrackingService.SameStaff(after.Qa, after.Support)) toSupport.Add(row.EntityId);
                else if (CrmFlowTrackingService.SameStaff(after.Qa, after.Developer) || string.Equals(after.Status, "Develop", StringComparison.OrdinalIgnoreCase)) toDev.Add(row.EntityId);
            }
            if ((wasMine || isMine) && IsClosed(after.Status) && !IsClosed(before?.Status)) closed.Add(row.EntityId);
        }
        return new CrmFlowStats(handled.Count, toDev.Count, toSupport.Count, closed.Count, 0, 0, null);
    }

    // query แยกเป็น static เพื่อให้ unit test ตรวจการแปลงเป็น SQL ได้ (ToQueryString)
    public static IQueryable<CrmUserCount> UpdateCounts(QaDbContext db, Guid?[] users, DateTime fromUtc, DateTime toUtc)
        => db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == UpdateEntityType && users.Contains(x.UserId) && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc)
            .GroupBy(x => x.UserId).Select(g => new CrmUserCount(g.Key, g.Count()));

    public static IQueryable<CrmUserCount> DefectCounts(QaDbContext db, Guid?[] users, DateTime fromUtc, DateTime toUtc, Guid[] projectIds)
        => db.DefectActivities.AsNoTracking()
            .Where(x => x.ActionType == "CreatedFromCrm" && users.Contains(x.ActorUserId) && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc
                && db.Defects.Any(d => d.DefectId == x.DefectId && !d.IsDeleted && projectIds.Contains(d.ProjectId)))
            .GroupBy(x => x.ActorUserId).Select(g => new CrmUserCount(g.Key, g.Count()));

    public static IQueryable<CrmFlowRow> FlowRows(QaDbContext db, string code, DateTime fromUtc, DateTime toUtc)
        => db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == CrmFlowTrackingService.EntityType && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc
                && ((x.AfterJson != null && x.AfterJson.Contains(code)) || (x.BeforeJson != null && x.BeforeJson.Contains(code))))
            .Select(x => new CrmFlowRow(x.EntityId, x.BeforeJson, x.AfterJson));

    private static bool IsQaStageOf(CrmFlowState? state, string code)
        => state is not null
            && CrmTicketListParser.IsAssigneeInScope(state.Qa, code)
            && !CrmFlowTrackingService.SameStaff(state.Qa, state.Support)
            && !CrmFlowTrackingService.SameStaff(state.Qa, state.Developer);

    private static bool IsClosed(string? status) => status is not null && ClosedStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed record CrmUserCount(Guid? UserId, int Count);
public sealed record CrmFlowStats(int Handled, int ToDev, int ToSupport, int Closed, int EditErr, int Answers, decimal? AvgQaHours);
public sealed record CrmFlowRow(string EntityId, string? BeforeJson, string? AfterJson);
