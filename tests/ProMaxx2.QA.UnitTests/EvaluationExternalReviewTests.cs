using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>ผู้ประเมินจากแผนกอื่นผ่านลิงก์ (ไม่ต้อง login) และการถ่วงน้ำหนักกับคะแนนหัวหน้าตรง</summary>
public sealed class EvaluationExternalReviewTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Combine_scores_uses_weight_and_falls_back_to_manager_only()
    {
        Assert.Equal(3.6m, Evaluation.CombineScores(3m, 5m, 30));      // 3×0.7 + 5×0.3
        Assert.Equal(3m, Evaluation.CombineScores(3m, null, 30));       // ไม่มีใครส่ง = หัวหน้าตรง 100%
        Assert.Equal(3m, Evaluation.CombineScores(3m, 5m, 0));          // น้ำหนัก 0 = อ้างอิงอย่างเดียว
        Assert.Equal(4m, Evaluation.CombineScores(3m, 5m, 50));
        Assert.Throws<ArgumentException>(() => EvaluationTemplate.ValidateExternalWeight(51));
        Assert.Throws<ArgumentException>(() => EvaluationTemplate.ValidateExternalWeight(-1));
    }

    [Fact]
    public void Template_external_weight_is_copied_to_new_rounds()
    {
        var t = new EvaluationTemplate("Quarterly", "Q", null);
        t.ReplaceCriteria("Q", EvaluationTemplate.DefaultCriteria("Quarterly"), null, 40);
        var e = new Evaluation(Guid.NewGuid(), "Quarterly", "Q3", 2026, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 14), t, null);
        Assert.Equal(40, e.ExternalWeight);
        t.ReplaceCriteria("Q", EvaluationTemplate.DefaultCriteria("Quarterly"), null); // ไม่ส่งน้ำหนัก = คงค่าเดิม
        Assert.Equal(40, t.ExternalWeight);
    }

    [Fact]
    public async Task External_reviewer_submits_once_and_score_is_weighted_into_overall()
    {
        await using var db = CreateDb();
        var (tester, lead, other) = await SeedUsersAsync(db);
        var evaluations = CreateService(db);
        var manager = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None);
        var id = (await db.Evaluations.SingleAsync(x => x.UserId == tester)).EvaluationId;

        // เฉพาะหัวหน้าตรงสร้างลิงก์ได้ — คนที่มีสิทธิ์จัดการแต่ไม่ใช่หัวหน้าของพนักงานสร้างไม่ได้
        var request = new CreateExternalReviewRequest("คุณสมชาย", "ฝ่ายพัฒนา", "ช่วยประเมินงานที่ทำร่วมกัน", 14);
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.CreateExternalReviewAsync(new PerformanceViewer(other, true, []), id, request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => evaluations.CreateExternalReviewAsync(manager, id, request with { ExpiresInDays = 90 }, CancellationToken.None));
        var first = await evaluations.CreateExternalReviewAsync(manager, id, request, CancellationToken.None);
        var second = await evaluations.CreateExternalReviewAsync(manager, id, request with { ReviewerName = "คุณสมหญิง", ReviewerDepartment = "ฝ่ายขาย" }, CancellationToken.None);
        var third = await evaluations.CreateExternalReviewAsync(manager, id, request with { ReviewerName = "คุณสมศักดิ์" }, CancellationToken.None);
        Assert.Equal("Pending", first.Status);
        Assert.Equal(12, first.Code!.Length);

        // แบบฟอร์มสาธารณะ: เห็นเกณฑ์ แต่ไม่มีคะแนนตนเอง/หัวหน้าตรง
        var form = await evaluations.OpenExternalReviewAsync(first.Code, CancellationToken.None);
        Assert.NotNull(form);
        Assert.Equal("Tester", form!.EmployeeName);
        Assert.Equal("Lead", form.RequestedByName);
        Assert.Equal(6, form.Criteria.Count);
        Assert.Null(await evaluations.OpenExternalReviewAsync("ไม่มีรหัสนี้", CancellationToken.None));

        // ต้องให้คะแนนครบทุกเกณฑ์
        var all5 = form.Criteria.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 5, null)).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => evaluations.SubmitExternalReviewAsync(first.Code, new SubmitExternalReviewRequest(all5.Take(2).ToList(), null), CancellationToken.None));
        var submitted = await evaluations.SubmitExternalReviewAsync(first.Code, new SubmitExternalReviewRequest(all5, "ทำงานร่วมกันดีมาก"), CancellationToken.None);
        Assert.Equal("Submitted", submitted!.Status);
        Assert.Empty(submitted.Criteria);
        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluations.SubmitExternalReviewAsync(first.Code, new SubmitExternalReviewRequest(all5, null), CancellationToken.None));
        Assert.Equal("Submitted", (await evaluations.OpenExternalReviewAsync(first.Code, CancellationToken.None))!.Status);

        // คนที่สองให้ 3 ทุกเกณฑ์ → ค่าเฉลี่ยแผนกอื่น (5 + 3) / 2 = 4
        var all3 = form.Criteria.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 3, null)).ToList();
        await evaluations.SubmitExternalReviewAsync(second.Code!, new SubmitExternalReviewRequest(all3, null), CancellationToken.None);

        // ยกเลิกลิงก์แล้วใช้ไม่ได้ (410)
        await evaluations.RevokeExternalReviewAsync(manager, id, third.ReviewId, CancellationToken.None);
        await Assert.ThrowsAsync<ExternalReviewGoneException>(() => evaluations.OpenExternalReviewAsync(third.Code!, CancellationToken.None));

        // หัวหน้าตรงเห็นค่าเฉลี่ยรายเกณฑ์ระหว่างร่าง; พนักงานยังไม่เห็น
        var draft = await evaluations.GetAsync(manager, id, CancellationToken.None);
        Assert.All(draft.Scores, s => { Assert.Equal(4m, s.ExternalAverage); Assert.Equal(2, s.ExternalCount); });
        Assert.Equal(4m, draft.ExternalScore);
        Assert.Equal(3, draft.ExternalReviews!.Count);
        Assert.Null(draft.ExternalReviews.Single(x => x.Status == "Submitted" && x.ReviewerName == "คุณสมชาย").Code); // ลิงก์ที่ส่งแล้วไม่ส่ง code กลับ
        var employeeView = await evaluations.GetAsync(new PerformanceViewer(tester, false, []), id, CancellationToken.None);
        Assert.Null(employeeView.ExternalReviews);
        Assert.All(employeeView.Scores, s => Assert.Null(s.ExternalAverage));

        // หัวหน้าตรงให้ 3 ทุกเกณฑ์ → ผลรวม 3×0.7 + 4×0.3 = 3.3 (เกรด C)
        var manager3 = draft.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 3, null)).ToList();
        var completed = await evaluations.SaveManagerAsync(manager, id, new SaveManagerEvaluationRequest(manager3, null, null, null, null, null, true), CancellationToken.None);
        Assert.Equal(3.3m, completed.Summary.OverallScore);
        Assert.Equal(3m, completed.ManagerScore);
        Assert.Equal(4m, completed.ExternalScore);
        Assert.Equal("C", completed.Summary.Grade);
        var stored = await db.Evaluations.SingleAsync(x => x.EvaluationId == id);
        Assert.Equal(2, stored.ExternalReviewCount);

        // หลังส่งผล ลิงก์ใช้ไม่ได้แล้ว
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityType == EvaluationService.ExternalReviewAuditEntity && x.Action == "Revoked"));
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.EntityType == EvaluationService.ExternalReviewAuditEntity && x.Action == "Submitted"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluations.CreateExternalReviewAsync(manager, id, request, CancellationToken.None));

        // เปิดใหม่ล้างผลรวม — ส่งผลอีกครั้งใช้คะแนนแผนกอื่นที่ส่งไว้เดิม
        await evaluations.ReopenAsync(manager, id, new ReopenEvaluationRequest("แก้คะแนน"), CancellationToken.None);
        Assert.Null((await db.Evaluations.SingleAsync(x => x.EvaluationId == id)).ExternalScore);
        var again = await evaluations.SaveManagerAsync(manager, id, new SaveManagerEvaluationRequest(manager3, null, null, null, null, null, true), CancellationToken.None);
        Assert.Equal(3.3m, again.Summary.OverallScore);
    }

    [Fact]
    public async Task Pending_links_are_closed_when_manager_completes_without_them()
    {
        await using var db = CreateDb();
        var (tester, lead, _) = await SeedUsersAsync(db);
        var evaluations = CreateService(db);
        var manager = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None);
        var id = (await db.Evaluations.SingleAsync(x => x.UserId == tester)).EvaluationId;
        var link = await evaluations.CreateExternalReviewAsync(manager, id, new CreateExternalReviewRequest("คุณสมชาย", null, null, 7), CancellationToken.None);

        var detail = await evaluations.GetAsync(manager, id, CancellationToken.None);
        var scores = detail.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 4, null)).ToList();
        var completed = await evaluations.SaveManagerAsync(manager, id, new SaveManagerEvaluationRequest(scores, null, null, null, null, null, true), CancellationToken.None);

        Assert.Equal(4m, completed.Summary.OverallScore); // ไม่มีใครส่ง = หัวหน้าตรง 100%
        Assert.Null(completed.ExternalScore);
        Assert.Equal("Revoked", completed.ExternalReviews!.Single().Status);
        await Assert.ThrowsAsync<ExternalReviewGoneException>(() => evaluations.OpenExternalReviewAsync(link.Code!, CancellationToken.None));
    }

    private static EvaluationService CreateService(QaDbContext db)
    {
        var clock = new FixedClock(Now);
        var repo = new PerformanceRepository(db, clock);
        return new EvaluationService(new EvaluationRepository(db), repo, new PerformanceService(repo, clock));
    }

    private static async Task<(Guid Tester, Guid Lead, Guid OtherLead)> SeedUsersAsync(QaDbContext db)
    {
        var testerRole = new Role("QA_TESTER", "QA Tester");
        var leadRole = new Role("QA_LEAD", "QA Lead");
        var tester = new User("tester", "Tester", null, "hash");
        var lead = new User("lead", "Lead", null, "hash");
        var other = new User("other", "Other Lead", null, "hash");
        db.Roles.AddRange(testerRole, leadRole);
        db.Users.AddRange(tester, lead, other);
        db.UserRoles.AddRange(new UserRole(tester.UserId, testerRole.RoleId), new UserRole(lead.UserId, leadRole.RoleId), new UserRole(other.UserId, leadRole.RoleId));
        var editPerformance = new Permission("PERFORMANCE.EDIT", "PERFORMANCE.EDIT", "PERFORMANCE");
        db.Permissions.Add(editPerformance);
        db.RolePermissions.Add(new RolePermission(leadRole.RoleId, editPerformance.PermissionId));
        var profile = new EmployeeProfile(tester.UserId, null);
        profile.Update(null, "QA Engineer", null, lead.UserId, null, "Permanent", null, null, null);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId, other.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
