using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class PerformanceRepository(QaDbContext db, TimeProvider clock) : IPerformanceRepository
{
    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    /// <summary>สมาชิกทีม = ผู้ใช้ที่ Active ทุกคน โดยยึดสถานะบัญชีเป็นหลัก</summary>
    public async Task<IReadOnlyList<EmployeeProfileDto>> ListMembersAsync(Guid? onlyUserId, DateOnly today, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        users = onlyUserId is { } id
            ? users.Where(u => u.UserId == id)
            : users.Where(u => u.IsActive);
        var rows = await users.OrderBy(u => u.DisplayName).Select(u => new { u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive }).ToListAsync(ct);
        var ids = rows.Select(x => x.UserId).ToList();
        var profiles = await db.EmployeeProfiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var roles = (await (from ur in db.UserRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId where ids.Contains(ur.UserId) select new { ur.UserId, r.RoleCode }).ToListAsync(ct))
            .ToLookup(x => x.UserId, x => x.RoleCode);
        var supervisorIds = profiles.Values.Where(p => p.SupervisorUserId != null).Select(p => p.SupervisorUserId!.Value).Distinct().ToList();
        var supervisorNames = await db.Users.AsNoTracking().Where(u => supervisorIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName, ct);

        return rows.Select(u =>
        {
            profiles.TryGetValue(u.UserId, out var p);
            return new EmployeeProfileDto(u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive, roles[u.UserId].OrderBy(x => x).ToList(), p is not null,
                p?.EmployeeCode, p?.Position, p?.Level, p?.SupervisorUserId,
                p?.SupervisorUserId is { } s && supervisorNames.TryGetValue(s, out var sn) ? sn : null,
                p?.HireDate, p?.EmploymentStatus ?? "Permanent", p?.ProbationEndDate, p?.ProbationDay(today),
                p?.HireDate is { } h && p.ProbationEndDate is { } e ? e.DayNumber - h.DayNumber + 1 : EmployeeProfile.ProbationDays,
                p?.Notes);
        }).ToList();
    }

    public async Task<IReadOnlyList<UserOptionDto>> ListActiveUsersAsync(CancellationToken ct)
        => await PerformanceQueries.ActiveUserOptions(db).ToListAsync(ct);

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) => db.Users.AnyAsync(u => u.UserId == userId, ct);

    public async Task<IReadOnlyDictionary<Guid, PerformanceMetrics>> GetMetricsAsync(IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct)
    {
        var users = userIds.Distinct().ToArray();
        if (users.Length == 0) return new Dictionary<Guid, PerformanceMetrics>();
        var p = projectIds;
        var now = clock.GetUtcNow().UtcDateTime;

        // DbContext ไม่รองรับ query พร้อมกัน — รันทีละ query (ทีม QA มีไม่กี่คน แต่ละ query group ฝั่ง SQL)
        var executions = (await PerformanceQueries.Executions(db, users, fromUtc, toUtc, p).ToListAsync(ct)).ToDictionary(x => x.UserId);
        var pending = await ToMap(PerformanceQueries.PendingAssignments(db, users, p), ct);
        var overdue = await ToMap(PerformanceQueries.OverdueAssignments(db, users, p, now), ct);
        var testCases = await ToMap(PerformanceQueries.TestCasesCreated(db, users, fromUtc, toUtc, p), ct);
        var testCaseRevisions = await ToMap(PerformanceQueries.TestCaseRevisions(db, users, fromUtc, toUtc, p), ct);
        var requirements = await ToMap(PerformanceQueries.RequirementsCreated(db, users, fromUtc, toUtc, p), ct);
        var requirementRevisions = await ToMap(PerformanceQueries.RequirementRevisions(db, users, fromUtc, toUtc, p), ct);
        var suites = await ToMap(PerformanceQueries.TestSuitesCreated(db, users, fromUtc, toUtc, p), ct);
        var defects = (await PerformanceQueries.DefectsReported(db, users, fromUtc, toUtc, p).ToListAsync(ct)).Where(x => x.UserId != null).ToDictionary(x => x.UserId!.Value);
        var comments = await ToMap(PerformanceQueries.DefectComments(db, users, fromUtc, toUtc, p), ct);
        var autoCases = await ToMap(PerformanceQueries.AutomationCasesCreated(db, users, fromUtc, toUtc, p), ct);
        var autoVersions = await ToMap(PerformanceQueries.AutomationVersionsCreated(db, users, fromUtc, toUtc, p), ct);
        var autoApproved = await ToMap(PerformanceQueries.AutomationVersionsApproved(db, users, fromUtc, toUtc, p), ct);
        var autoRuns = (await PerformanceQueries.AutomationRuns(db, users, fromUtc, toUtc, p).ToListAsync(ct))
            .Select(x => (Ok: Guid.TryParse(x.Key, out var id), Id: id, x.Count)).Where(x => x.Ok)
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var regression = await ToMap(PerformanceQueries.RegressionActions(db, users, fromUtc, toUtc, p), ct);
        var workLogs = (await PerformanceQueries.WorkLogs(db, users, ThaiDate(fromUtc), ThaiDate(toUtc)).ToListAsync(ct)).ToDictionary(x => x.UserId);

        return users.ToDictionary(u => u, u =>
        {
            executions.TryGetValue(u, out var e);
            defects.TryGetValue(u, out var d);
            workLogs.TryGetValue(u, out var w);
            return new PerformanceMetrics
            {
                Executions = e?.Total ?? 0, Passed = e?.Passed ?? 0, Failed = e?.Failed ?? 0, Blocked = e?.Blocked ?? 0, Skipped = e?.Skipped ?? 0,
                Retests = e?.Retests ?? 0, DistinctTestCases = e?.DistinctTestCases ?? 0,
                AssignedPending = pending.GetValueOrDefault(u), AssignedOverdue = overdue.GetValueOrDefault(u),
                TestCasesCreated = testCases.GetValueOrDefault(u), TestCaseRevisions = testCaseRevisions.GetValueOrDefault(u),
                RequirementsCreated = requirements.GetValueOrDefault(u), RequirementRevisions = requirementRevisions.GetValueOrDefault(u),
                TestSuitesCreated = suites.GetValueOrDefault(u),
                DefectsReported = d?.Total ?? 0, DefectsCritical = d?.Critical ?? 0, DefectsHigh = d?.High ?? 0, DefectsRejected = d?.Rejected ?? 0,
                DefectComments = comments.GetValueOrDefault(u),
                AutomationCasesCreated = autoCases.GetValueOrDefault(u), AutomationVersionsCreated = autoVersions.GetValueOrDefault(u),
                AutomationVersionsApproved = autoApproved.GetValueOrDefault(u), AutomationRuns = autoRuns.GetValueOrDefault(u),
                RegressionActions = regression.GetValueOrDefault(u),
                WorkLogs = w?.WorkLogs ?? 0, Highlights = w?.Highlights ?? 0, WorkLogHours = w?.Hours ?? 0,
            };
        });
    }

    public async Task<IReadOnlyList<WeeklyTrendPoint>> GetWeeklyTrendAsync(Guid userId, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct)
    {
        var p = projectIds;
        var nUser = (Guid?)userId;
        var executions = await db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TesterUserId == userId && x.CompletedAt >= fromUtc && x.CompletedAt < toUtc && !x.CycleCase.Cycle.IsDeleted && p.Contains(x.CycleCase.Cycle.ProjectId))
            .Select(x => x.CompletedAt!.Value).ToListAsync(ct);
        var defects = await db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedBy == nUser && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc && p.Contains(x.ProjectId))
            .Select(x => x.CreatedAt).ToListAsync(ct);
        var testCases = await db.TestCases.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CreatedBy == nUser && x.CreatedAt >= fromUtc && x.CreatedAt < toUtc && p.Contains(x.ProjectId))
            .Select(x => x.CreatedAt).ToListAsync(ct);

        // สัปดาห์เริ่มวันจันทร์ตามเวลาไทย — ใส่ทุกสัปดาห์ในช่วง แม้ไม่มีงาน เพื่อให้กราฟต่อเนื่อง
        var first = WeekStart(ThaiDate(fromUtc));
        var lastDay = ThaiDate(toUtc).AddDays(-1);
        var points = new List<WeeklyTrendPoint>();
        for (var w = first; w <= lastDay; w = w.AddDays(7))
        {
            var week = w;
            points.Add(new WeeklyTrendPoint(week,
                executions.Count(x => WeekStart(ThaiDate(x)) == week),
                defects.Count(x => WeekStart(ThaiDate(x)) == week),
                testCases.Count(x => WeekStart(ThaiDate(x)) == week)));
        }
        return points;
    }

    public async Task<ActivityPageDto> GetTimelineAsync(Guid userId, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, int page, int size, CancellationToken ct)
    {
        var query = PerformanceQueries.Timeline(db, userId, fromUtc, toUtc, projectIds);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Timestamp).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new ActivityPageDto(items.Select(x => new ActivityItemDto(x.Timestamp, x.Source, x.Action, x.Summary, x.Reference)).ToList(), total, page, size);
    }

    public Task<EmployeeProfile?> FindProfileAsync(Guid userId, CancellationToken ct) => db.EmployeeProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task AddProfileAsync(EmployeeProfile profile, CancellationToken ct) => db.EmployeeProfiles.AddAsync(profile, ct).AsTask();

    public async Task<IReadOnlyList<WorkLogDto>> ListWorkLogsAsync(Guid userId, DateOnly from, DateOnly to, bool includeConcern, Guid viewerId, bool viewerCanManage, CancellationToken ct)
        => await PerformanceQueries.WorkLogList(db, userId, from, to, includeConcern, viewerId, viewerCanManage).ToListAsync(ct);

    public Task<WorkLogDto?> GetWorkLogAsync(Guid id, Guid viewerId, bool viewerCanManage, CancellationToken ct)
        => PerformanceQueries.ToWorkLogDto(db, db.WorkLogEntries.AsNoTracking().Where(x => x.WorkLogEntryId == id && !x.IsDeleted), viewerId, viewerCanManage).SingleOrDefaultAsync(ct);

    public Task<WorkLogEntry?> FindWorkLogAsync(Guid id, CancellationToken ct) => db.WorkLogEntries.SingleOrDefaultAsync(x => x.WorkLogEntryId == id && !x.IsDeleted, ct);
    public Task AddWorkLogAsync(WorkLogEntry entry, CancellationToken ct) => db.WorkLogEntries.AddAsync(entry, ct).AsTask();
    public async Task<IReadOnlyList<WorkLogAttachment>> ListAttachmentsAsync(Guid workLogEntryId, CancellationToken ct)
        => await db.WorkLogAttachments.Where(x => x.WorkLogEntryId == workLogEntryId).OrderBy(x => x.UploadedAt).ToListAsync(ct);
    public Task AddAttachmentsAsync(IEnumerable<WorkLogAttachment> attachments, CancellationToken ct) => db.WorkLogAttachments.AddRangeAsync(attachments, ct);
    public void RemoveAttachment(WorkLogAttachment attachment) => db.WorkLogAttachments.Remove(attachment);
    public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.ProjectId == projectId, ct);
    public void AddAudit(AuditLog log) => db.AuditLogs.Add(log);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private static async Task<Dictionary<Guid, int>> ToMap(IQueryable<UserCount> query, CancellationToken ct)
        => (await query.ToListAsync(ct)).Where(x => x.UserId != null).ToDictionary(x => x.UserId!.Value, x => x.Count);

    private static DateOnly ThaiDate(DateTime utc) => DateOnly.FromDateTime(utc + ThaiOffset);
    private static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
