using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed record UserCount(Guid? UserId, int Count);
public sealed record ExecutionAggregate(Guid UserId, int Total, int Passed, int Failed, int Blocked, int Skipped, int Retests, int DistinctTestCases);
public sealed record DefectAggregate(Guid? UserId, int Total, int Critical, int High, int Rejected);
public sealed record WorkLogAggregate(Guid UserId, int WorkLogs, int Highlights, decimal Hours);

/// <summary>
/// Query รวมผลงานรายคนในช่วงวันที่ — group ฝั่ง SQL ทั้งหมด (ไม่โหลดทั้งตารางเข้า memory)
/// และกรองเฉพาะ Project ที่ผู้ดูเป็นสมาชิก แยกเป็น static เพื่อให้ unit test ตรวจการแปลงเป็น SQL ได้ (ToQueryString)
/// </summary>
public static class PerformanceQueries
{
    // TestCycleCase.CurrentStatus = ผลล่าสุดของเคส — ยังไม่จบเมื่อยังไม่รัน, กำลังรัน หรือติด Blocked
    private static readonly string[] PendingCaseStatuses = ["NotRun", "InProgress", "Blocked"];

    public static IQueryable<ExecutionAggregate> Executions(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
        => db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && users.Contains(x.TesterUserId) && x.CompletedAt >= from && x.CompletedAt < to
                && !x.CycleCase.Cycle.IsDeleted && projects.Contains(x.CycleCase.Cycle.ProjectId))
            .Select(x => new { x.TesterUserId, x.Status, x.ExecutionNo, x.CycleCase.TestCaseId })
            .GroupBy(x => x.TesterUserId)
            .Select(g => new ExecutionAggregate(g.Key, g.Count(), g.Count(x => x.Status == "Pass"), g.Count(x => x.Status == "Fail"), g.Count(x => x.Status == "Blocked"), g.Count(x => x.Status == "Skipped"), g.Count(x => x.ExecutionNo > 1), g.Select(x => x.TestCaseId).Distinct().Count()));

    public static IQueryable<UserCount> PendingAssignments(QaDbContext db, Guid[] users, Guid[] projects)
        => CountBy(PendingCases(db, users, projects).Select(x => x.AssignedTesterUserId));

    public static IQueryable<UserCount> OverdueAssignments(QaDbContext db, Guid[] users, Guid[] projects, DateTime now)
        => CountBy(PendingCases(db, users, projects)
            .Where(x => db.TestCycleCaseAssignments.Any(a => a.TestCycleCaseId == x.TestCycleCaseId && a.DueDate != null && a.DueDate < now))
            .Select(x => x.AssignedTesterUserId));

    private static IQueryable<ProMaxx2.QA.Domain.Execution.TestCycleCase> PendingCases(QaDbContext db, Guid[] users, Guid[] projects)
    {
        var nullableUsers = Nullable(users);
        return db.TestCycleCases.AsNoTracking().Where(x => nullableUsers.Contains(x.AssignedTesterUserId) && PendingCaseStatuses.Contains(x.CurrentStatus)
            && !x.Cycle.IsDeleted && projects.Contains(x.Cycle.ProjectId));
    }

    public static IQueryable<UserCount> TestCasesCreated(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.TestCases.AsNoTracking().Where(x => !x.IsDeleted && u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId)).Select(x => x.CreatedBy));
    }

    public static IQueryable<UserCount> TestCaseRevisions(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.TestCaseRevisions.AsNoTracking().Where(x => u.Contains(x.ChangedBy) && x.ChangedAt >= from && x.ChangedAt < to && !x.TestCase.IsDeleted && projects.Contains(x.TestCase.ProjectId)).Select(x => x.ChangedBy));
    }

    public static IQueryable<UserCount> RequirementsCreated(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.Requirements.AsNoTracking().Where(x => !x.IsDeleted && u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId)).Select(x => x.CreatedBy));
    }

    // Requirement สร้าง revision 1 ตอนสร้างเสมอ — นับเฉพาะการแก้ (RevisionNo > 1)
    public static IQueryable<UserCount> RequirementRevisions(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.RequirementRevisions.AsNoTracking().Where(x => x.RevisionNo > 1 && u.Contains(x.ChangedBy) && x.ChangedAt >= from && x.ChangedAt < to && !x.Requirement.IsDeleted && projects.Contains(x.Requirement.ProjectId)).Select(x => x.ChangedBy));
    }

    public static IQueryable<UserCount> TestSuitesCreated(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.TestSuites.AsNoTracking().Where(x => u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId)).Select(x => x.CreatedBy));
    }

    public static IQueryable<DefectAggregate> DefectsReported(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId))
            .GroupBy(x => x.CreatedBy)
            .Select(g => new DefectAggregate(g.Key, g.Count(), g.Count(x => x.Severity == "Critical"), g.Count(x => x.Severity == "High"), g.Count(x => x.Status == "Rejected")));
    }

    public static IQueryable<UserCount> DefectComments(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.DefectActivities.AsNoTracking()
            .Where(x => x.ActionType == "Comment" && u.Contains(x.ActorUserId) && x.CreatedAt >= from && x.CreatedAt < to
                && db.Defects.Any(d => d.DefectId == x.DefectId && !d.IsDeleted && projects.Contains(d.ProjectId)))
            .Select(x => x.ActorUserId));
    }

    // Automation case ผูก Project ผ่าน Test Case
    public static IQueryable<UserCount> AutomationCasesCreated(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.AutomationCases.AsNoTracking()
            .Where(x => u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to && db.TestCases.Any(tc => tc.TestCaseId == x.TestCaseId && projects.Contains(tc.ProjectId)))
            .Select(x => x.CreatedBy));
    }

    public static IQueryable<UserCount> AutomationVersionsCreated(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.AutomationVersions.AsNoTracking()
            .Where(x => u.Contains(x.CreatedBy) && x.CreatedAt >= from && x.CreatedAt < to
                && db.AutomationCases.Any(c => c.AutomationCaseId == x.AutomationCaseId && db.TestCases.Any(tc => tc.TestCaseId == c.TestCaseId && projects.Contains(tc.ProjectId))))
            .Select(x => x.CreatedBy));
    }

    public static IQueryable<UserCount> AutomationVersionsApproved(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.AutomationVersions.AsNoTracking()
            .Where(x => u.Contains(x.ApprovedBy) && x.ApprovedAt >= from && x.ApprovedAt < to
                && db.AutomationCases.Any(c => c.AutomationCaseId == x.AutomationCaseId && db.TestCases.Any(tc => tc.TestCaseId == c.TestCaseId && projects.Contains(tc.ProjectId))))
            .Select(x => x.ApprovedBy));
    }

    // AutomationExecution.RequestedBy เก็บ userId เป็น string ("system:auto-retry" สำหรับงานที่ระบบสั่งเอง)
    public static IQueryable<StringCount> AutomationRuns(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var keys = users.Select(x => x.ToString()).ToArray();
        return db.AutomationExecutions.AsNoTracking()
            .Where(x => x.RequestedBy != null && keys.Contains(x.RequestedBy) && x.CreatedAt >= from && x.CreatedAt < to
                && db.AutomationCases.Any(c => c.AutomationCaseId == x.AutomationCaseId && db.TestCases.Any(tc => tc.TestCaseId == c.TestCaseId && projects.Contains(tc.ProjectId))))
            .GroupBy(x => x.RequestedBy)
            .Select(g => new StringCount(g.Key, g.Count()));
    }

    public static IQueryable<UserCount> RegressionActions(QaDbContext db, Guid[] users, DateTime from, DateTime to, Guid[] projects)
    {
        var u = Nullable(users);
        return CountBy(db.RegressionActivities.AsNoTracking().Where(x => u.Contains(x.ActorUserId) && x.CreatedAt >= from && x.CreatedAt < to && projects.Contains(x.ProjectId)).Select(x => x.ActorUserId));
    }

    // Work Log เป็นบันทึกส่วนตัว ไม่ผูกสิทธิ์ Project — ไม่นับ Concern (ข้อสังเกตของหัวหน้า ไม่ใช่ผลงาน)
    public static IQueryable<WorkLogAggregate> WorkLogs(QaDbContext db, Guid[] users, DateOnly from, DateOnly toExclusive)
        => db.WorkLogEntries.AsNoTracking()
            .Where(x => users.Contains(x.UserId) && x.WorkDate >= from && x.WorkDate < toExclusive && x.Kind != "Concern")
            .GroupBy(x => x.UserId)
            .Select(g => new WorkLogAggregate(g.Key, g.Count(x => x.Kind == "WorkLog"), g.Count(x => x.Kind == "Highlight"), g.Sum(x => x.EffortHours ?? 0)));

    /// <summary>บันทึกงานของผู้ใช้หนึ่งคนในช่วงวันที่ — กรองที่ entity ก่อนแล้วค่อย project เป็น DTO
    /// (กรองหลัง project ด้วย constructor ของ record ทำให้ EF แปลงเป็น SQL ไม่ได้)</summary>
    public static IQueryable<WorkLogDto> WorkLogList(QaDbContext db, Guid userId, DateOnly from, DateOnly to, bool includeConcern, Guid viewerId, bool viewerCanManage)
        => ToWorkLogDto(db, db.WorkLogEntries.AsNoTracking()
            .Where(x => x.UserId == userId && x.WorkDate >= from && x.WorkDate <= to && (includeConcern || x.Kind != "Concern"))
            .OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAt), viewerId, viewerCanManage);

    public static IQueryable<WorkLogDto> ToWorkLogDto(QaDbContext db, IQueryable<WorkLogEntry> source, Guid viewerId, bool viewerCanManage)
        => source.Select(x => new WorkLogDto(x.WorkLogEntryId, x.UserId, x.WorkDate, x.Category, x.Kind, x.Title, x.Description, x.EffortHours, x.ProjectId,
            x.ProjectId != null ? db.Projects.Where(pr => pr.ProjectId == x.ProjectId).Select(pr => pr.ProjectName).FirstOrDefault() : null,
            x.ReferenceUrl, x.CreatedAt, x.CreatedBy,
            x.CreatedBy != null ? db.Users.Where(u => u.UserId == x.CreatedBy).Select(u => u.DisplayName).FirstOrDefault() : null,
            viewerCanManage || x.CreatedBy == viewerId,
            db.WorkLogAttachments.Where(a => a.WorkLogEntryId == x.WorkLogEntryId).OrderBy(a => a.UploadedAt)
                .Select(a => new WorkLogAttachmentDto(a.WorkLogAttachmentId, a.FileName, a.SizeBytes, a.ContentType)).ToList()));

    /// <summary>ผู้ใช้ที่ยังใช้งาน + CanEvaluate = ได้สิทธิ์ PERFORMANCE.EDIT จาก Role ที่ยังใช้งาน (ตั้งเป็นหัวหน้า/ผู้ประเมินได้)</summary>
    public static IQueryable<UserOptionDto> ActiveUserOptions(QaDbContext db)
        => db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new UserOptionDto(u.UserId, u.DisplayName,
                db.UserRoles.Any(ur => ur.UserId == u.UserId && db.Roles.Any(r => r.RoleId == ur.RoleId && r.IsActive)
                    && db.RolePermissions.Any(rp => rp.RoleId == ur.RoleId && db.Permissions.Any(p => p.PermissionId == rp.PermissionId && p.PermissionCode == "PERFORMANCE.EDIT")))));

    /// <summary>ไทม์ไลน์กิจกรรมของผู้ใช้หนึ่งคนจากหลายตาราง เรียงตามเวลาได้ทั้งชุดใน SQL</summary>
    public static IQueryable<TimelineRow> Timeline(QaDbContext db, Guid userId, DateTime from, DateTime to, Guid[] projects)
    {
        var nUser = (Guid?)userId;
        // Concat ต้อง project เป็น anonymous type (ดูเหตุผลใน AuditLogsController) แล้วค่อยแปลงเป็น TimelineRow ด้วย member-init ตอนท้าย
        return db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TesterUserId == userId && x.CompletedAt != null && !x.CycleCase.Cycle.IsDeleted && projects.Contains(x.CycleCase.Cycle.ProjectId))
            .Select(x => new { Timestamp = x.CompletedAt!.Value, Source = "Execution", Action = x.Status, Summary = (string?)(x.CycleCase.TestCase.TestCaseCode + " " + x.CycleCase.TestCase.Title), Reference = (string?)x.CycleCase.Cycle.CycleCode })
            .Concat(from a in db.DefectActivities.AsNoTracking()
                    join d in db.Defects.AsNoTracking() on a.DefectId equals d.DefectId
                    where a.ActorUserId == nUser && !d.IsDeleted && projects.Contains(d.ProjectId)
                    select new { Timestamp = a.CreatedAt, Source = "Defect", Action = a.ActionType, Summary = (string?)a.Message, Reference = (string?)d.DefectCode })
            .Concat(db.TestCases.AsNoTracking()
                .Where(x => x.CreatedBy == nUser && !x.IsDeleted && projects.Contains(x.ProjectId))
                .Select(x => new { Timestamp = x.CreatedAt, Source = "TestCase", Action = "Created", Summary = (string?)x.Title, Reference = (string?)x.TestCaseCode }))
            .Concat(db.TestCaseRevisions.AsNoTracking()
                .Where(x => x.ChangedBy == nUser && !x.TestCase.IsDeleted && projects.Contains(x.TestCase.ProjectId))
                .Select(x => new { Timestamp = x.ChangedAt, Source = "TestCase", Action = "Revised", Summary = (string?)x.ChangeReason, Reference = (string?)x.TestCase.TestCaseCode }))
            .Concat(db.RequirementRevisions.AsNoTracking()
                .Where(x => x.ChangedBy == nUser && !x.Requirement.IsDeleted && projects.Contains(x.Requirement.ProjectId))
                .Select(x => new { Timestamp = x.ChangedAt, Source = "Requirement", Action = x.RevisionNo == 1 ? "Created" : "Revised", Summary = (string?)x.Title, Reference = (string?)x.Requirement.RequirementCode }))
            .Concat(db.RegressionActivities.AsNoTracking()
                .Where(x => x.ActorUserId == nUser && projects.Contains(x.ProjectId))
                .Select(x => new { Timestamp = x.CreatedAt, Source = "Regression", x.Action, Summary = x.Details, Reference = (string?)null }))
            // การอัปเดต CRM Ticket ผ่าน QA Hub (CrmController บันทึก EntityType CrmTicketUpdate โดยผู้ทำจริง)
            .Concat(db.AuditLogs.AsNoTracking()
                .Where(x => x.EntityType == "CrmTicketUpdate" && x.UserId == nUser)
                .Select(x => new { Timestamp = x.CreatedAt, Source = "CRM", x.Action, Summary = x.ChangeSummary, Reference = (string?)x.EntityId }))
            .Where(x => x.Timestamp >= from && x.Timestamp < to)
            .Select(x => new TimelineRow { Timestamp = x.Timestamp, Source = x.Source, Action = x.Action, Summary = x.Summary, Reference = x.Reference });
    }

    private static IQueryable<UserCount> CountBy(IQueryable<Guid?> keys) => keys.GroupBy(x => x).Select(g => new UserCount(g.Key, g.Count()));
    private static Guid?[] Nullable(Guid[] users) => users.Select(x => (Guid?)x).ToArray();
}

public sealed record StringCount(string? Key, int Count);

public sealed class TimelineRow
{
    public DateTime Timestamp { get; init; }
    public string Source { get; init; } = "";
    public string Action { get; init; } = "";
    public string? Summary { get; init; }
    public string? Reference { get; init; }
}
