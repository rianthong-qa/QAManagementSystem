using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Application.Performance;

/// <summary>ผู้ที่กำลังดูข้อมูล — CanManage = หัวหน้า (ดูทุกคน/แก้โปรไฟล์/เห็น Concern), ProjectIds = Project ที่เป็นสมาชิก (ใช้กรอง metric)</summary>
public sealed record PerformanceViewer(Guid UserId, bool CanManage, Guid[] ProjectIds);

public sealed record EmployeeProfileDto(Guid UserId, string Username, string DisplayName, string? Email, bool IsActive, IReadOnlyList<string> Roles, bool HasProfile, string? EmployeeCode, string? Position, string? Level, Guid? SupervisorUserId, string? SupervisorName, DateOnly? HireDate, string EmploymentStatus, DateOnly? ProbationEndDate, int? ProbationDay, int ProbationTotalDays, string? Notes);
public sealed record SaveEmployeeProfileRequest(string? EmployeeCode, string? Position, string? Level, Guid? SupervisorUserId, DateOnly? HireDate, string EmploymentStatus, DateOnly? ProbationEndDate, string? Notes);

public sealed record PerformanceMetrics
{
    public int Executions { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Blocked { get; init; }
    public int Skipped { get; init; }
    public int Retests { get; init; }
    public int DistinctTestCases { get; init; }
    public int AssignedPending { get; init; }
    public int AssignedOverdue { get; init; }
    public int TestCasesCreated { get; init; }
    public int TestCaseRevisions { get; init; }
    public int RequirementsCreated { get; init; }
    public int RequirementRevisions { get; init; }
    public int TestSuitesCreated { get; init; }
    public int DefectsReported { get; init; }
    public int DefectsCritical { get; init; }
    public int DefectsHigh { get; init; }
    public int DefectsRejected { get; init; }
    public int DefectComments { get; init; }
    public int AutomationCasesCreated { get; init; }
    public int AutomationVersionsCreated { get; init; }
    public int AutomationVersionsApproved { get; init; }
    public int AutomationRuns { get; init; }
    public int RegressionActions { get; init; }
    public int WorkLogs { get; init; }
    public int Highlights { get; init; }
    public decimal WorkLogHours { get; init; }
    /// <summary>ผู้ใช้ผูกบัญชี CRM แล้ว — false = ตัวเลข CRM เป็น 0 เพราะจับคู่ Ticket กับผู้ใช้ไม่ได้ ไม่ใช่เพราะไม่มีงาน</summary>
    public bool HasCrmAccount { get; init; }
    public int CrmTicketsHandled { get; init; }
    public int CrmHandedToDev { get; init; }
    public int CrmReturnedToSupport { get; init; }
    public int CrmClosed { get; init; }
    public int CrmUpdates { get; init; }
    public int CrmDefectsCreated { get; init; }
    /// <summary>ครั้งที่ QA ส่ง Ticket กลับไปแก้ (สถานะ EditErr)</summary>
    public int CrmEditErr { get; init; }
    /// <summary>จำนวนครั้งที่ตอบใน Ticket (จากข้อความตอบใน CRM)</summary>
    public int CrmAnswers { get; init; }
    /// <summary>เวลาเฉลี่ย (ชั่วโมง) ที่ Ticket อยู่กับ QA ก่อนส่งต่อ — null = ยังไม่มีช่วงที่วัดได้</summary>
    public decimal? CrmAvgQaHours { get; init; }
}

public sealed record CrmWorkMetrics(bool HasCrmAccount, int TicketsHandled, int HandedToDev, int ReturnedToSupport, int Closed, int Updates, int DefectsCreated, int EditErr = 0, int Answers = 0, decimal? AvgQaHours = null);

/// <summary>ผลงานจาก CRM รายคน — implement ใน Api เพราะต้องใช้ประวัติเส้นทาง Ticket (CrmFlowTrackingService) และรหัสพนักงาน CRM ของแต่ละคน</summary>
public interface ICrmPerformanceSource
{
    Task<IReadOnlyDictionary<Guid, CrmWorkMetrics>> GetAsync(IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct);
}

public sealed record TeamMemberDto(EmployeeProfileDto Profile, PerformanceMetrics Metrics);
public sealed record TeamOverviewDto(DateOnly From, DateOnly To, IReadOnlyList<TeamMemberDto> Members);
public sealed record WeeklyTrendPoint(DateOnly WeekStart, int Executions, int Defects, int TestCases);
public sealed record UserPerformanceDto(DateOnly From, DateOnly To, EmployeeProfileDto Profile, PerformanceMetrics Metrics, IReadOnlyList<WeeklyTrendPoint> Weekly);
public sealed record ActivityItemDto(DateTime Timestamp, string Source, string Action, string? Summary, string? Reference);
public sealed record ActivityPageDto(IReadOnlyList<ActivityItemDto> Items, int Total, int Page, int Size);
/// <param name="CanEvaluate">มีสิทธิ์ PERFORMANCE.EDIT จาก Role — ตั้งเป็นหัวหน้า (ผู้ประเมิน) ได้</param>
public sealed record UserOptionDto(Guid UserId, string DisplayName, bool CanEvaluate = false);

public sealed record WorkLogAttachmentDto(Guid AttachmentId, string FileName, long Size, string ContentType);
public sealed record WorkLogDto(Guid WorkLogEntryId, Guid UserId, DateOnly WorkDate, string Category, string Kind, string Title, string? Description, decimal? EffortHours, Guid? ProjectId, string? ProjectName, string? ReferenceUrl, DateTime CreatedAt, Guid? CreatedBy, string? CreatedByName, bool CanEdit, IReadOnlyList<WorkLogAttachmentDto> Attachments);
public sealed record SaveWorkLogRequest(Guid UserId, DateOnly WorkDate, string Category, string Kind, string Title, string? Description, decimal? EffortHours, Guid? ProjectId, string? ReferenceUrl);

public sealed class PerformanceAccessDeniedException(string message) : Exception(message);

public interface IPerformanceRepository
{
    Task<IReadOnlyList<EmployeeProfileDto>> ListMembersAsync(Guid? onlyUserId, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<UserOptionDto>> ListActiveUsersAsync(CancellationToken ct);
    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, PerformanceMetrics>> GetMetricsAsync(IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct);
    Task<IReadOnlyList<WeeklyTrendPoint>> GetWeeklyTrendAsync(Guid userId, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct);
    Task<ActivityPageDto> GetTimelineAsync(Guid userId, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, int page, int size, CancellationToken ct);
    Task<EmployeeProfile?> FindProfileAsync(Guid userId, CancellationToken ct);
    Task AddProfileAsync(EmployeeProfile profile, CancellationToken ct);
    Task<IReadOnlyList<WorkLogDto>> ListWorkLogsAsync(Guid userId, DateOnly from, DateOnly to, bool includeConcern, Guid viewerId, bool viewerCanManage, CancellationToken ct);
    Task<WorkLogDto?> GetWorkLogAsync(Guid id, Guid viewerId, bool viewerCanManage, CancellationToken ct);
    Task<WorkLogEntry?> FindWorkLogAsync(Guid id, CancellationToken ct);
    Task AddWorkLogAsync(WorkLogEntry entry, CancellationToken ct);
    void RemoveWorkLog(WorkLogEntry entry);
    Task<IReadOnlyList<WorkLogAttachment>> ListAttachmentsAsync(Guid workLogEntryId, CancellationToken ct);
    Task AddAttachmentsAsync(IEnumerable<WorkLogAttachment> attachments, CancellationToken ct);
    void RemoveAttachment(WorkLogAttachment attachment);
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken ct);
    void AddAudit(AuditLog log);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class PerformanceService(IPerformanceRepository repository, TimeProvider clock, ICrmPerformanceSource? crm = null)
{
    // ช่วงวันที่ที่ผู้ใช้เลือกเป็นวันตามเวลาไทย (UTC+7 ไม่มี DST) ส่วนข้อมูลในฐานข้อมูลเก็บเป็น UTC
    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);
    public const int MaxRangeDays = 366;

    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime + ThaiOffset);

    public async Task<TeamOverviewDto> GetTeamAsync(PerformanceViewer viewer, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        RequireManage(viewer);
        var (f, t) = ResolveRange(from, to);
        var members = await repository.ListMembersAsync(null, Today, ct);
        var metrics = await CollectMetricsAsync(members.Select(x => x.UserId).ToList(), ToUtc(f), ToUtc(t.AddDays(1)), viewer.ProjectIds, ct);
        return new TeamOverviewDto(f, t, members.Select(m => new TeamMemberDto(m, metrics.GetValueOrDefault(m.UserId) ?? new PerformanceMetrics())).ToList());
    }

    public async Task<UserPerformanceDto> GetUserAsync(PerformanceViewer viewer, Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        RequireSelfOrManage(viewer, userId);
        var (f, t) = ResolveRange(from, to);
        var profile = (await repository.ListMembersAsync(userId, Today, ct)).SingleOrDefault() ?? throw new EntityNotFoundException("ไม่พบผู้ใช้");
        var fromUtc = ToUtc(f); var toUtc = ToUtc(t.AddDays(1));
        var metrics = await CollectMetricsAsync([userId], fromUtc, toUtc, viewer.ProjectIds, ct);
        var weekly = await repository.GetWeeklyTrendAsync(userId, fromUtc, toUtc, viewer.ProjectIds, ct);
        return new UserPerformanceDto(f, t, profile, metrics.GetValueOrDefault(userId) ?? new PerformanceMetrics(), weekly);
    }

    public async Task<ActivityPageDto> GetTimelineAsync(PerformanceViewer viewer, Guid userId, DateOnly? from, DateOnly? to, int page, int size, CancellationToken ct)
    {
        RequireSelfOrManage(viewer, userId);
        var (f, t) = ResolveRange(from, to);
        return await repository.GetTimelineAsync(userId, ToUtc(f), ToUtc(t.AddDays(1)), viewer.ProjectIds, Math.Clamp(page, 1, 10000), Math.Clamp(size, 1, 100), ct);
    }

    public async Task<IReadOnlyList<UserOptionDto>> ListUsersAsync(PerformanceViewer viewer, CancellationToken ct)
    {
        RequireManage(viewer);
        return await repository.ListActiveUsersAsync(ct);
    }

    public async Task<EmployeeProfileDto> SaveProfileAsync(PerformanceViewer viewer, Guid userId, SaveEmployeeProfileRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        if (!await repository.UserExistsAsync(userId, ct)) throw new EntityNotFoundException("ไม่พบผู้ใช้");
        if (r.SupervisorUserId is { } candidate && candidate != Guid.Empty && !(await repository.ListActiveUsersAsync(ct)).Any(u => u.UserId == candidate && u.CanEvaluate))
            throw new ArgumentException("หัวหน้าต้องเป็นผู้ใช้ที่ยังใช้งานอยู่และมีสิทธิ์ประเมิน (PERFORMANCE.EDIT) เพราะผู้ประเมินคือหัวหน้าที่ระบุเท่านั้น");
        if (r.SupervisorUserId is { } supervisor && supervisor != Guid.Empty && !await repository.UserExistsAsync(supervisor, ct)) throw new ArgumentException("ไม่พบหัวหน้าที่เลือก");
        var profile = await repository.FindProfileAsync(userId, ct);
        var before = profile is null ? null : Snapshot(profile);
        if (profile is null) { profile = new EmployeeProfile(userId, viewer.UserId); await repository.AddProfileAsync(profile, ct); }
        profile.Update(r.EmployeeCode, r.Position, r.Level, r.SupervisorUserId, r.HireDate, r.EmploymentStatus, r.ProbationEndDate, r.Notes, viewer.UserId);
        repository.AddAudit(new AuditLog(viewer.UserId, before is null ? "Created" : "Updated", "EmployeeProfile", userId.ToString(), $"สถานะ {profile.EmploymentStatus}", before, Snapshot(profile), null));
        await repository.SaveChangesAsync(ct);
        return (await repository.ListMembersAsync(userId, Today, ct)).Single();
    }

    public async Task<IReadOnlyList<WorkLogDto>> ListWorkLogsAsync(PerformanceViewer viewer, Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        RequireSelfOrManage(viewer, userId);
        var (f, t) = ResolveRange(from, to);
        return await repository.ListWorkLogsAsync(userId, f, t, viewer.CanManage, viewer.UserId, viewer.CanManage, ct);
    }

    public async Task<WorkLogDto> CreateWorkLogAsync(PerformanceViewer viewer, SaveWorkLogRequest r, CancellationToken ct)
    {
        RequireSelfOrManage(viewer, r.UserId);
        if (r.Kind?.Equals("Concern", StringComparison.OrdinalIgnoreCase) == true) RequireManage(viewer);
        if (!await repository.UserExistsAsync(r.UserId, ct)) throw new EntityNotFoundException("ไม่พบผู้ใช้");
        await ValidateProjectAsync(viewer, r.ProjectId, ct);
        var entry = new WorkLogEntry(r.UserId, r.WorkDate, r.Category, r.Kind ?? "WorkLog", r.Title, r.Description, r.EffortHours, r.ProjectId, r.ReferenceUrl, viewer.UserId);
        await repository.AddWorkLogAsync(entry, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.GetWorkLogAsync(entry.WorkLogEntryId, viewer.UserId, viewer.CanManage, ct))!;
    }

    public async Task<WorkLogDto> UpdateWorkLogAsync(PerformanceViewer viewer, Guid id, SaveWorkLogRequest r, CancellationToken ct)
    {
        var entry = await FindEditableWorkLogAsync(viewer, id, ct);
        await ValidateProjectAsync(viewer, r.ProjectId, ct);
        entry.Update(r.WorkDate, r.Category, r.Title, r.Description, r.EffortHours, r.ProjectId, r.ReferenceUrl, viewer.UserId);
        await repository.SaveChangesAsync(ct);
        return (await repository.GetWorkLogAsync(id, viewer.UserId, viewer.CanManage, ct))!;
    }

    public async Task DeleteWorkLogAsync(PerformanceViewer viewer, Guid id, CancellationToken ct)
    {
        var entry = await FindEditableWorkLogAsync(viewer, id, ct);
        repository.RemoveWorkLog(entry);
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>ไฟล์หลักฐานของบันทึกงานที่ผู้ดูเห็นได้ (กฎเดียวกับการเห็นบันทึกงาน) — ใช้ตอนดาวน์โหลด</summary>
    public async Task<WorkLogAttachment> FindVisibleAttachmentAsync(PerformanceViewer viewer, Guid workLogId, Guid attachmentId, CancellationToken ct)
    {
        var entry = await repository.FindWorkLogAsync(workLogId, ct) ?? throw new EntityNotFoundException("ไม่พบบันทึกงาน");
        if (!viewer.CanManage && (entry.UserId != viewer.UserId || entry.Kind == "Concern")) throw new EntityNotFoundException("ไม่พบบันทึกงาน");
        return (await repository.ListAttachmentsAsync(workLogId, ct)).SingleOrDefault(x => x.WorkLogAttachmentId == attachmentId) ?? throw new EntityNotFoundException("ไม่พบไฟล์แนบ");
    }

    /// <summary>ตรวจสิทธิ์แนบไฟล์ (เหมือนสิทธิ์แก้บันทึกงาน) แล้วคืนขนาดไฟล์ที่แนบอยู่แล้ว — storage ใช้ตรวจโควตา</summary>
    public async Task<IReadOnlyList<long>> PrepareAttachmentUploadAsync(PerformanceViewer viewer, Guid workLogId, CancellationToken ct)
    {
        await FindEditableWorkLogAsync(viewer, workLogId, ct);
        return (await repository.ListAttachmentsAsync(workLogId, ct)).Select(x => x.SizeBytes).ToList();
    }

    public async Task<WorkLogDto> AddAttachmentsAsync(PerformanceViewer viewer, Guid workLogId, IReadOnlyList<WorkLogAttachment> attachments, CancellationToken ct)
    {
        await FindEditableWorkLogAsync(viewer, workLogId, ct);
        await repository.AddAttachmentsAsync(attachments, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.GetWorkLogAsync(workLogId, viewer.UserId, viewer.CanManage, ct))!;
    }

    /// <summary>ลบไฟล์แนบหนึ่งไฟล์ — คืน entity ที่ลบเพื่อให้ผู้เรียกลบไฟล์บนดิสก์หลังบันทึกฐานข้อมูลสำเร็จ</summary>
    public async Task<WorkLogAttachment> DeleteAttachmentAsync(PerformanceViewer viewer, Guid workLogId, Guid attachmentId, CancellationToken ct)
    {
        await FindEditableWorkLogAsync(viewer, workLogId, ct);
        var attachment = (await repository.ListAttachmentsAsync(workLogId, ct)).SingleOrDefault(x => x.WorkLogAttachmentId == attachmentId) ?? throw new EntityNotFoundException("ไม่พบไฟล์แนบ");
        repository.RemoveAttachment(attachment);
        await repository.SaveChangesAsync(ct);
        return attachment;
    }

    /// <summary>ผู้สร้างแก้/ลบบันทึกของตัวเองได้ หัวหน้าแก้ได้ทุกรายการ — Concern ที่พนักงานมองไม่เห็นต้องตอบ 404 ไม่ใช่ 403</summary>
    private async Task<WorkLogEntry> FindEditableWorkLogAsync(PerformanceViewer viewer, Guid id, CancellationToken ct)
    {
        var entry = await repository.FindWorkLogAsync(id, ct) ?? throw new EntityNotFoundException("ไม่พบบันทึกงาน");
        if (viewer.CanManage) return entry;
        if (entry.UserId != viewer.UserId || entry.Kind == "Concern") throw new EntityNotFoundException("ไม่พบบันทึกงาน");
        if (entry.CreatedBy != viewer.UserId) throw new PerformanceAccessDeniedException("แก้ไขได้เฉพาะบันทึกที่คุณสร้างเอง");
        return entry;
    }

    private async Task ValidateProjectAsync(PerformanceViewer viewer, Guid? projectId, CancellationToken ct)
    {
        if (projectId is not { } id || id == Guid.Empty) return;
        if (!viewer.ProjectIds.Contains(id) || !await repository.ProjectExistsAsync(id, ct)) throw new ArgumentException("ไม่พบ Project ที่เลือก หรือคุณไม่ได้เป็นสมาชิก");
    }

    /// <summary>metric จากข้อมูลในระบบ + ผลงานจาก CRM (ถ้ามี source) — ใช้ทั้งหน้าผลงานและ snapshot ตอนส่งผลประเมิน</summary>
    public async Task<IReadOnlyDictionary<Guid, PerformanceMetrics>> CollectMetricsAsync(IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc, Guid[] projectIds, CancellationToken ct)
    {
        var metrics = await repository.GetMetricsAsync(userIds, fromUtc, toUtc, projectIds, ct);
        if (crm is null || userIds.Count == 0) return metrics;
        var crmMetrics = await crm.GetAsync(userIds, fromUtc, toUtc, projectIds, ct);
        return metrics.ToDictionary(x => x.Key, x => crmMetrics.TryGetValue(x.Key, out var c)
            ? x.Value with
            {
                HasCrmAccount = c.HasCrmAccount, CrmTicketsHandled = c.TicketsHandled, CrmHandedToDev = c.HandedToDev, CrmReturnedToSupport = c.ReturnedToSupport,
                CrmClosed = c.Closed, CrmUpdates = c.Updates, CrmDefectsCreated = c.DefectsCreated,
                CrmEditErr = c.EditErr, CrmAnswers = c.Answers, CrmAvgQaHours = c.AvgQaHours,
            }
            : x.Value);
    }

    public (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var today = Today;
        var t = to ?? today;
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (f > t) throw new ArgumentException("วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด");
        if (t.DayNumber - f.DayNumber + 1 > MaxRangeDays) throw new ArgumentException($"ช่วงวันที่ต้องไม่เกิน {MaxRangeDays} วัน");
        return (f, t);
    }

    public static DateTime ToUtc(DateOnly thaiDate) => DateTime.SpecifyKind(thaiDate.ToDateTime(TimeOnly.MinValue) - ThaiOffset, DateTimeKind.Utc);

    private static void RequireManage(PerformanceViewer viewer)
    {
        if (!viewer.CanManage) throw new PerformanceAccessDeniedException("ต้องมีสิทธิ์จัดการผลงานทีม (PERFORMANCE.EDIT)");
    }

    private static void RequireSelfOrManage(PerformanceViewer viewer, Guid userId)
    {
        if (!viewer.CanManage && userId != viewer.UserId) throw new PerformanceAccessDeniedException("ดูได้เฉพาะข้อมูลของตัวเอง");
    }

    private static string Snapshot(EmployeeProfile p) => System.Text.Json.JsonSerializer.Serialize(new { p.EmployeeCode, p.Position, p.Level, p.SupervisorUserId, p.HireDate, p.EmploymentStatus, p.ProbationEndDate });
}
