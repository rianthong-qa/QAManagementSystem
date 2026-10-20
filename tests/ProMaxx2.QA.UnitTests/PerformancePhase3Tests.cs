using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>เฟส 3 ของหน้า ผลงานและการประเมิน: อีเมลเตือน, ไฟล์หลักฐานของบันทึกงาน, เทียบผลข้ามรอบ</summary>
public sealed class PerformancePhase3Tests
{
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc); // 10:00 เวลาไทย
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void Reminder_goes_to_supervisor_or_falls_back_to_managers_but_never_the_employee()
    {
        var employee = Guid.NewGuid();
        var supervisor = new PerformanceReminderService.Recipient(Guid.NewGuid(), "Sup", "sup@test");
        var lead = new PerformanceReminderService.Recipient(Guid.NewGuid(), "Lead", "lead@test");
        var selfLead = new PerformanceReminderService.Recipient(employee, "Self", "self@test");
        var withSupervisor = Open(employee, Today.AddDays(3), supervisor.UserId);
        var withoutSupervisor = Open(employee, Today.AddDays(-2), null);
        var supervisorHasNoEmail = Open(employee, Today.AddDays(1), Guid.NewGuid());

        var plan = PerformanceReminderService.Plan([withSupervisor, withoutSupervisor, supervisorHasNoEmail],
            new Dictionary<Guid, PerformanceReminderService.Recipient> { [supervisor.UserId] = supervisor }, [lead, selfLead], new HashSet<(Guid, string, DateOnly, Guid)>(), Today);

        Assert.Equal([supervisor.UserId], plan.Where(x => x.Evaluation == withSupervisor).Select(x => x.Recipient.UserId));
        var overdue = Assert.Single(plan, x => x.Evaluation == withoutSupervisor);
        Assert.Equal((lead.UserId, "Overdue", -2), (overdue.Recipient.UserId, overdue.Kind, overdue.DaysLeft));
        Assert.Equal([lead.UserId], plan.Where(x => x.Evaluation == supervisorHasNoEmail).Select(x => x.Recipient.UserId));
        Assert.DoesNotContain(plan, x => x.Recipient.UserId == employee);
    }

    [Fact]
    public void Reminder_skips_far_due_dates_and_already_sent_but_resends_after_reschedule()
    {
        var lead = new PerformanceReminderService.Recipient(Guid.NewGuid(), "Lead", "lead@test");
        var due = Open(Guid.NewGuid(), Today.AddDays(7), null);
        var far = Open(Guid.NewGuid(), Today.AddDays(8), null);
        var sent = new HashSet<(Guid, string, DateOnly, Guid)> { (due.EvaluationId, "DueSoon", due.DueDate, lead.UserId) };
        var noRecipients = new Dictionary<Guid, PerformanceReminderService.Recipient>();

        Assert.Empty(PerformanceReminderService.Plan([due, far], noRecipients, [lead], sent, Today));

        // กำหนดส่งถูกเลื่อน → เตือนใหม่; เลยกำหนดแล้ว → เตือนแบบ Overdue อีกครั้ง
        var rescheduled = due with { DueDate = Today.AddDays(5) };
        Assert.Equal("DueSoon", Assert.Single(PerformanceReminderService.Plan([rescheduled], noRecipients, [lead], sent, Today)).Kind);
        Assert.Equal("Overdue", Assert.Single(PerformanceReminderService.Plan([due], noRecipients, [lead], sent, Today.AddDays(8))).Kind);
    }

    [Fact]
    public async Task Reminder_without_smtp_config_sends_nothing_and_records_nothing()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var permission = new Permission("PERFORMANCE.EDIT", "Performance edit", "Performance");
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermission(db.UserRoles.Single(x => x.UserId == lead).RoleId, permission.PermissionId));
        db.Evaluations.Add(new Evaluation(tester, "Quarterly", "Q3", 2026, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), Today.AddDays(2), Template(), lead));
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var email = new EmailSenderService(new EmailConfigurationService(db, new EphemeralDataProtectionProvider()));
        var service = new PerformanceReminderService(db, email, config, new FixedClock(Now), NullLogger<PerformanceReminderService>.Instance);

        Assert.Equal(0, await service.SendDueRemindersAsync(CancellationToken.None));
        Assert.Empty(db.EvaluationNotifications);
        // ผู้รับสำรองต้องมีอีเมล — Lead ที่ seed ไม่มีอีเมลจึงไม่อยู่ในรายการ
        Assert.Empty(await PerformanceReminderService.ManagersQuery(db).ToListAsync());
        Assert.Single(await PerformanceReminderService.OpenEvaluationsQuery(db, Today.AddDays(7)).ToListAsync());

        // ก่อน 08:00 เวลาไทยไม่ตรวจเลย
        var early = new PerformanceReminderService(db, email, config, new FixedClock(new DateTime(2026, 10, 6, 0, 30, 0, DateTimeKind.Utc)), NullLogger<PerformanceReminderService>.Instance);
        Assert.Equal(0, await early.SendDueRemindersAsync(CancellationToken.None));
    }

    [Fact]
    public void Reminder_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Assert.Contains("LEFT JOIN", PerformanceReminderService.OpenEvaluationsQuery(db, Today).ToQueryString());
        Assert.Contains("EXISTS", PerformanceReminderService.ManagersQuery(db).ToQueryString());
        Assert.Contains("SELECT", PerformanceQueries.ToWorkLogDto(db, db.WorkLogEntries, Guid.NewGuid(), false).ToQueryString());
    }

    [Fact]
    public async Task Evidence_attachments_follow_work_log_visibility_and_edit_rules()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var service = CreateService(db);
        var employee = new PerformanceViewer(tester, false, []);
        var manager = new PerformanceViewer(lead, true, []);
        var own = await service.CreateWorkLogAsync(employee, new SaveWorkLogRequest(tester, Today, "Training", "Highlight", "ผ่านอบรม ISTQB", null, null, null, null), CancellationToken.None);
        var concern = await service.CreateWorkLogAsync(manager, new SaveWorkLogRequest(tester, Today, "Other", "Concern", "ส่งงานช้า", null, null, null, null), CancellationToken.None);

        var updated = await service.AddAttachmentsAsync(employee, own.WorkLogEntryId, [Attachment(own.WorkLogEntryId, tester, "cert.pdf")], CancellationToken.None);
        var attachment = Assert.Single(updated.Attachments);
        Assert.Equal(("cert.pdf", "application/pdf"), (attachment.FileName, attachment.ContentType));
        Assert.Single((await service.ListWorkLogsAsync(employee, tester, Today, Today, CancellationToken.None)).Single(x => x.WorkLogEntryId == own.WorkLogEntryId).Attachments);
        Assert.Equal(attachment.AttachmentId, (await service.FindVisibleAttachmentAsync(manager, own.WorkLogEntryId, attachment.AttachmentId, CancellationToken.None)).WorkLogAttachmentId);

        // ไฟล์ของ Concern: พนักงานต้องไม่รู้ว่ามีอยู่ (404) และแนบเพิ่มไม่ได้
        var concernFile = await service.AddAttachmentsAsync(manager, concern.WorkLogEntryId, [Attachment(concern.WorkLogEntryId, lead, "note.png")], CancellationToken.None);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.FindVisibleAttachmentAsync(employee, concern.WorkLogEntryId, concernFile.Attachments[0].AttachmentId, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.PrepareAttachmentUploadAsync(employee, concern.WorkLogEntryId, CancellationToken.None));

        // บันทึกที่หัวหน้าสร้างให้พนักงาน — พนักงานเห็นไฟล์ได้แต่ลบไม่ได้
        var byLead = await service.CreateWorkLogAsync(manager, new SaveWorkLogRequest(tester, Today, "Testing", "Highlight", "หา Defect สำคัญ", null, null, null, null), CancellationToken.None);
        var leadFile = (await service.AddAttachmentsAsync(manager, byLead.WorkLogEntryId, [Attachment(byLead.WorkLogEntryId, lead, "x.png")], CancellationToken.None)).Attachments[0];
        await service.FindVisibleAttachmentAsync(employee, byLead.WorkLogEntryId, leadFile.AttachmentId, CancellationToken.None);
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => service.DeleteAttachmentAsync(employee, byLead.WorkLogEntryId, leadFile.AttachmentId, CancellationToken.None));

        var removed = await service.DeleteAttachmentAsync(employee, own.WorkLogEntryId, attachment.AttachmentId, CancellationToken.None);
        Assert.Equal("cert.pdf", removed.FileName);
        Assert.Empty((await service.ListWorkLogsAsync(employee, tester, Today, Today, CancellationToken.None)).Single(x => x.WorkLogEntryId == own.WorkLogEntryId).Attachments);
    }

    [Fact]
    public async Task Evidence_storage_validates_type_signature_and_quota()
    {
        var storage = new PerformanceEvidenceStorage(null!);
        var pdf = File("ใบประกาศ.pdf", "%PDF-1.7 test"u8.ToArray());
        var fakePdf = File("virus.pdf", "MZ......"u8.ToArray());
        var exe = File("tool.exe", "MZ......"u8.ToArray());

        var (ok, error) = await storage.ReadAsync([pdf], [], CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(("ใบประกาศ.pdf", "application/pdf"), (ok[0].DisplayName, ok[0].ContentType));
        Assert.NotNull((await storage.ReadAsync([fakePdf], [], CancellationToken.None)).Error);
        Assert.NotNull((await storage.ReadAsync([exe], [], CancellationToken.None)).Error);
        Assert.Equal("แนบไฟล์ได้สูงสุด 5 ไฟล์ต่อบันทึก", (await storage.ReadAsync([pdf, pdf], [1, 1, 1, 1], CancellationToken.None)).Error);
        Assert.NotNull((await storage.ReadAsync([pdf], [19_999_990], CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Comparison_lists_closed_rounds_oldest_first_with_weighted_group_scores()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var clock = new FixedClock(Now);
        var repo = new PerformanceRepository(db, clock);
        var performance = new PerformanceService(repo, clock);
        var evaluations = new EvaluationService(new EvaluationRepository(db), repo, performance);

        // สร้าง Q3 ก่อน Q2 เพื่อยืนยันว่าเรียงตามช่วงประเมิน ไม่ใช่ลำดับที่สร้าง
        Completed(db, tester, lead, "Q3", new DateOnly(2026, 7, 1), [5, 3, 4]);
        Completed(db, tester, lead, "Q2", new DateOnly(2026, 4, 1), [3, 3, 2]);
        db.Evaluations.Add(new Evaluation(tester, "Quarterly", "Q4", 2026, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 14), Template(), lead));
        await db.SaveChangesAsync();

        var result = await evaluations.CompareAsync(new PerformanceViewer(tester, false, []), tester, CancellationToken.None);
        Assert.Equal(["Q2", "Q3"], result.Rounds.Select(x => x.Round));
        Assert.Equal(["คุณภาพงาน", "พฤติกรรม"], result.Groups);
        var q3 = result.Rounds[1];
        Assert.Equal(4.6m, q3.Groups.Single(x => x.Group == "คุณภาพงาน").Score); // (5×40 + 3×10) / 50
        Assert.Equal(4m, q3.Groups.Single(x => x.Group == "พฤติกรรม").Score);

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.CompareAsync(new PerformanceViewer(tester, false, []), lead, CancellationToken.None));
    }

    private static void Completed(QaDbContext db, Guid user, Guid lead, string round, DateOnly start, int[] scores)
    {
        var e = new Evaluation(user, "Quarterly", round, 2026, start, start.AddMonths(3).AddDays(-1), start.AddMonths(3).AddDays(13), Template(), lead);
        var ids = e.Scores.OrderBy(x => x.SortOrder).Select(x => x.EvaluationScoreId).ToArray();
        e.SaveManager(ids.Select((id, i) => (id, scores[i])).ToDictionary(x => x.id, x => ((int?)x.Item2, (string?)null)), null, null, null, null, null, lead);
        e.Complete("{}", start, lead);
        db.Evaluations.Add(e);
    }

    // คุณภาพงาน: 40% + 10%, พฤติกรรม: 50%
    private static EvaluationTemplate Template()
    {
        var t = new EvaluationTemplate("Quarterly", "Q", null);
        t.ReplaceCriteria("Q", [("คุณภาพงาน", "a", null, 40), ("คุณภาพงาน", "b", null, 10), ("พฤติกรรม", "c", null, 50)], null);
        return t;
    }

    [Fact]
    public void Self_reminder_goes_to_employee_only_while_self_assessment_is_not_submitted()
    {
        var employee = Guid.NewGuid();
        var dueSoon = Open(employee, Today.AddDays(2), null) with { EmployeeEmail = "tester@test" };
        var overdue = Open(employee, Today.AddDays(-1), null) with { EmployeeEmail = "tester@test" };
        var submitted = dueSoon with { EvaluationId = Guid.NewGuid(), Status = "SelfSubmitted" };
        var noEmail = dueSoon with { EvaluationId = Guid.NewGuid(), EmployeeEmail = null };
        var far = dueSoon with { EvaluationId = Guid.NewGuid(), DueDate = Today.AddDays(8) };
        var sent = new HashSet<(Guid, string, DateOnly, Guid)> { (overdue.EvaluationId, "SelfOverdue", overdue.DueDate, employee) };

        var plan = PerformanceReminderService.PlanSelf([dueSoon, overdue, submitted, noEmail, far], sent, Today);

        var only = Assert.Single(plan);
        Assert.Equal((dueSoon.EvaluationId, "SelfDueSoon", employee, "tester@test"), (only.Evaluation.EvaluationId, only.Kind, only.Recipient.UserId, only.Recipient.Email));
        Assert.Equal("SelfOverdue", Assert.Single(PerformanceReminderService.PlanSelf([overdue], new HashSet<(Guid, string, DateOnly, Guid)>(), Today)).Kind);
    }

    private static PerformanceReminderService.OpenEvaluation Open(Guid userId, DateOnly due, Guid? supervisor)
        => new(Guid.NewGuid(), userId, "Tester", "Q3", 2026, due, "Open", supervisor);

    private static WorkLogAttachment Attachment(Guid workLogId, Guid by, string name)
        => new(Guid.NewGuid(), workLogId, name, $"{Guid.NewGuid():N}{Path.GetExtension(name)}", 100, PerformanceEvidenceStorage.ContentTypeFor(Path.GetExtension(name)), by, Now);

    private static FormFile File(string name, byte[] data) => new(new MemoryStream(data), 0, data.Length, "files", name);

    private static PerformanceService CreateService(QaDbContext db)
    {
        var clock = new FixedClock(Now);
        return new PerformanceService(new PerformanceRepository(db, clock), clock);
    }

    private static async Task<(Guid Tester, Guid Lead)> SeedUsersAsync(QaDbContext db)
    {
        var testerRole = new Role("QA_TESTER", "QA Tester");
        var leadRole = new Role("QA_LEAD", "QA Lead");
        var tester = new User("tester", "Tester", null, "hash");
        var lead = new User("lead", "Lead", null, "hash");
        db.Roles.AddRange(testerRole, leadRole);
        db.Users.AddRange(tester, lead);
        db.UserRoles.AddRange(new UserRole(tester.UserId, testerRole.RoleId), new UserRole(lead.UserId, leadRole.RoleId));
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
