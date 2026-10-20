using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class EvaluationTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Template_weights_must_total_100()
    {
        var t = new EvaluationTemplate("Quarterly", "Q", null);
        Assert.Throws<ArgumentException>(() => t.ReplaceCriteria("Q", [("คุณภาพงาน", "a", null, 60), ("พฤติกรรม", "b", null, 30)], null));
        t.ReplaceCriteria("Q", EvaluationTemplate.DefaultCriteria("Probation"), null);
        Assert.Equal(100, t.Criteria.Sum(x => x.Weight));
        Assert.Equal(100, EvaluationTemplate.DefaultCriteria("Quarterly").Sum(x => x.Weight));
    }

    [Fact]
    public void Complete_computes_weighted_score_and_grade()
    {
        var t = new EvaluationTemplate("Quarterly", "Q", null);
        t.ReplaceCriteria("Q", [("คุณภาพงาน", "a", null, 75), ("พฤติกรรม", "b", null, 25)], null);
        var userId = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var e = new Evaluation(userId, "Quarterly", "Q3", 2026, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 14), t, lead);
        var ids = e.Scores.OrderBy(x => x.SortOrder).Select(x => x.EvaluationScoreId).ToArray();

        Assert.Throws<InvalidOperationException>(() => e.SaveManager(new Dictionary<Guid, (int?, string?)>(), null, null, null, null, null, userId));
        e.SaveManager(new Dictionary<Guid, (int?, string?)> { [ids[0]] = (5, null) }, "ละเอียด", null, null, null, null, lead);
        Assert.Throws<ArgumentException>(() => e.Complete("{}", new DateOnly(2026, 9, 30), lead));
        Assert.Throws<ArgumentException>(() => e.SaveManager(new Dictionary<Guid, (int?, string?)>(), null, null, null, "Pass", null, lead));

        e.SaveManager(new Dictionary<Guid, (int?, string?)> { [ids[0]] = (5, null), [ids[1]] = (3, null) }, "ละเอียด", null, null, null, null, lead);
        e.Complete("{}", new DateOnly(2026, 9, 30), lead);
        Assert.Equal(4.5m, e.OverallScore);
        Assert.Equal("A", e.Grade);
        Assert.Throws<InvalidOperationException>(() => e.SaveManager(new Dictionary<Guid, (int?, string?)>(), null, null, null, null, null, lead));
    }

    [Fact]
    public async Task Probation_profile_creates_rounds_on_day_30_60_90_and_final()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (performance, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        var profile = new SaveEmployeeProfileRequest(null, "QA", "Junior", lead, new DateOnly(2026, 9, 1), "Probation", null, null);

        await performance.SaveProfileAsync(manager, tester, profile, CancellationToken.None);
        await evaluations.SyncProbationRoundsAsync(manager, tester, CancellationToken.None);
        await evaluations.SyncProbationRoundsAsync(manager, tester, CancellationToken.None); // เรียกซ้ำต้องไม่สร้างซ้ำ

        var rounds = await db.Evaluations.Where(x => x.UserId == tester).OrderBy(x => x.DueDate).ToListAsync();
        Assert.Equal(["P30", "P60", "P90", "PFinal"], rounds.Select(x => x.Round));
        Assert.Equal(new DateOnly(2026, 9, 30), rounds[0].DueDate);
        Assert.Equal(new DateOnly(2026, 10, 30), rounds[1].DueDate);
        Assert.Equal(new DateOnly(2026, 11, 29), rounds[2].DueDate);
        Assert.Equal(new DateOnly(2026, 12, 28), rounds[3].PeriodEnd); // วันที่ 119
        Assert.Equal(new DateOnly(2026, 12, 14), rounds[3].DueDate);
        Assert.All(rounds, r => Assert.Equal(7, r.Scores.Count));
        Assert.Single(db.EvaluationTemplates.Where(x => x.Type == "Probation"));

        // เลื่อนวันเริ่มงาน → รอบที่ยังไม่ปิดเลื่อนตาม
        await performance.SaveProfileAsync(manager, tester, profile with { HireDate = new DateOnly(2026, 9, 11) }, CancellationToken.None);
        await evaluations.SyncProbationRoundsAsync(manager, tester, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 10, 10), (await db.Evaluations.SingleAsync(x => x.UserId == tester && x.Round == "P30")).DueDate);
        Assert.Equal(4, await db.Evaluations.CountAsync(x => x.UserId == tester));
    }

    [Fact]
    public async Task Employee_sees_manager_scores_only_after_completion_and_can_acknowledge()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        var employee = new PerformanceViewer(tester, false, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None);
        var id = (await db.Evaluations.SingleAsync(x => x.UserId == tester)).EvaluationId;
        var detail = await evaluations.GetAsync(employee, id, CancellationToken.None);
        var all = detail.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 4, null)).ToList();

        await evaluations.SaveSelfAsync(employee, id, new SaveSelfEvaluationRequest(all, "พร้อมรับ feedback", true), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluations.SaveSelfAsync(employee, id, new SaveSelfEvaluationRequest(all, null, false), CancellationToken.None));
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.SaveManagerAsync(employee, id, new SaveManagerEvaluationRequest(all, null, null, null, null, null, false), CancellationToken.None));

        await evaluations.SaveManagerAsync(manager, id, new SaveManagerEvaluationRequest(all.Select(x => x with { Score = 3, Comment = "ต้องละเอียดขึ้น" }).ToList(), "ขยัน", "Automation", "เขียน DSL ได้", null, null, false), CancellationToken.None);
        var draftSeenByEmployee = await evaluations.GetAsync(employee, id, CancellationToken.None);
        Assert.All(draftSeenByEmployee.Scores, s => { Assert.Null(s.ManagerScore); Assert.Equal(4, s.SelfScore); });
        Assert.Null(draftSeenByEmployee.Strengths);
        Assert.Equal(4, (await evaluations.GetAsync(manager, id, CancellationToken.None)).Scores[0].SelfScore);

        await evaluations.SaveManagerAsync(manager, id, new SaveManagerEvaluationRequest(all.Select(x => x with { Score = 3 }).ToList(), "ขยัน", null, null, null, null, true), CancellationToken.None);
        var completed = await evaluations.GetAsync(employee, id, CancellationToken.None);
        Assert.Equal("Completed", completed.Summary.Status);
        Assert.Equal(3m, completed.Summary.OverallScore);
        Assert.Equal("C", completed.Summary.Grade);
        Assert.All(completed.Scores, s => Assert.Equal(3, s.ManagerScore));
        Assert.True(completed.MetricsIsSnapshot);
        Assert.True(completed.CanAcknowledge);

        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluations.AcknowledgeAsync(manager, id, new AcknowledgeEvaluationRequest(null), CancellationToken.None));
        var acknowledged = await evaluations.AcknowledgeAsync(employee, id, new AcknowledgeEvaluationRequest("รับทราบ"), CancellationToken.None);
        Assert.Equal("Acknowledged", acknowledged.Summary.Status);
        Assert.Equal(3, await db.AuditLogs.CountAsync(x => x.EntityType == "Evaluation"));
    }

    [Fact]
    public async Task Final_probation_pass_makes_permanent_and_extend_creates_new_final_round()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (performance, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        await performance.SaveProfileAsync(manager, tester, new SaveEmployeeProfileRequest(null, null, null, lead, new DateOnly(2026, 6, 1), "Probation", null, null), CancellationToken.None);
        await evaluations.SyncProbationRoundsAsync(manager, tester, CancellationToken.None);
        var final = await db.Evaluations.SingleAsync(x => x.UserId == tester && x.Round == "PFinal");
        var scores = final.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 3, null)).ToList();
        var end = (await db.EmployeeProfiles.SingleAsync(x => x.UserId == tester)).ProbationEndDate!.Value;

        await Assert.ThrowsAsync<ArgumentException>(() => evaluations.SaveManagerAsync(manager, final.EvaluationId, new SaveManagerEvaluationRequest(scores, null, null, null, null, null, true), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => evaluations.SaveManagerAsync(manager, final.EvaluationId, new SaveManagerEvaluationRequest(scores, null, null, null, "Extend", end, true), CancellationToken.None));

        var newEnd = end.AddDays(60);
        await evaluations.SaveManagerAsync(manager, final.EvaluationId, new SaveManagerEvaluationRequest(scores, null, null, null, "Extend", newEnd, true), CancellationToken.None);
        var profile = await db.EmployeeProfiles.SingleAsync(x => x.UserId == tester);
        Assert.Equal("Extended", profile.EmploymentStatus);
        Assert.Equal(newEnd, profile.ProbationEndDate);
        var second = await db.Evaluations.Where(x => x.UserId == tester && x.Round == "PFinal" && x.Status == "Open").SingleAsync();
        Assert.Equal(newEnd, second.PeriodEnd);

        await evaluations.SaveManagerAsync(manager, second.EvaluationId, new SaveManagerEvaluationRequest(second.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 4, null)).ToList(), null, null, null, "Pass", null, true), CancellationToken.None);
        Assert.Equal("Permanent", (await db.EmployeeProfiles.SingleAsync(x => x.UserId == tester)).EmploymentStatus);
    }

    [Fact]
    public async Task Quarterly_round_is_for_permanent_staff_and_idempotent()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (performance, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        await performance.SaveProfileAsync(manager, tester, new SaveEmployeeProfileRequest(null, null, null, lead, new DateOnly(2026, 9, 1), "Probation", null, null), CancellationToken.None);

        var first = await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 4), CancellationToken.None);
        var again = await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 4), CancellationToken.None);

        Assert.Equal(1, first.Created); // lead เท่านั้น — tester ยังทดลองงาน
        Assert.Equal(0, again.Created);
        var q4 = await db.Evaluations.SingleAsync(x => x.Round == "Q4");
        Assert.Equal(lead, q4.UserId);
        Assert.Equal(new DateOnly(2026, 12, 31), q4.PeriodEnd);
        await Assert.ThrowsAsync<ArgumentException>(() => evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 5), CancellationToken.None));
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.CreateQuarterlyAsync(new PerformanceViewer(tester, false, []), new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None));
    }

    [Fact]
    public async Task Employee_lists_only_own_evaluations()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 2), CancellationToken.None);
        var employee = new PerformanceViewer(tester, false, []);

        var own = await evaluations.ListAsync(employee, null, false, CancellationToken.None);
        Assert.All(own, x => Assert.Equal(tester, x.UserId));
        Assert.Single(own);
        Assert.True(own[0].IsOverdue); // Q2 ครบกำหนด 14 ก.ค. 2026 — วันนี้ 6 ต.ค.
    }

    [Fact]
    public async Task Team_comparison_shows_closed_scores_per_quarter_and_averages()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 1), CancellationToken.None);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 2), CancellationToken.None);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2025, 4), CancellationToken.None);
        foreach (var (round, year, score) in new[] { ("Q1", 2026, 4), ("Q4", 2025, 3) })
        {
            var e = await db.Evaluations.SingleAsync(x => x.UserId == tester && x.Round == round && x.Year == year);
            await evaluations.SaveManagerAsync(manager, e.EvaluationId, new SaveManagerEvaluationRequest(e.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, score, null)).ToList(), null, null, null, null, null, true), CancellationToken.None);
        }

        var result = await evaluations.CompareTeamAsync(manager, 2026, CancellationToken.None);

        var row = Assert.Single(result.Rows, x => x.UserId == tester);
        Assert.Equal(["Q1", "Q2", "Q3", "Q4"], row.Quarters.Select(x => x.Round));
        Assert.Equal((4m, "B"), (row.Quarters[0].OverallScore!.Value, row.Quarters[0].Grade!));
        Assert.Null(row.Quarters[1].OverallScore); // Q2 ยังไม่ปิด — มีแค่สถานะ
        Assert.Equal("Open", row.Quarters[1].Status);
        Assert.True(row.Quarters[1].IsOverdue); // Q2 ครบกำหนด 14 ก.ค. — วันนี้ 6 ต.ค.
        Assert.Null(row.Quarters[2].EvaluationId);
        Assert.Equal(4m, row.AverageScore);
        Assert.Equal(3m, row.PreviousYearQ4Score);
        Assert.Equal(4m, result.QuarterAverages[0]);
        Assert.Null(result.QuarterAverages[1]);

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.CompareTeamAsync(new PerformanceViewer(tester, false, []), 2026, CancellationToken.None));
    }

    [Fact]
    public void Quarterly_comparison_query_translates_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Assert.Contains("WHERE", EvaluationRepository.QuarterlyQuery(db, 2026).ToQueryString());
    }

    [Fact]
    public async Task Only_the_assigned_supervisor_can_evaluate_or_reopen()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var otherLead = new User("lead2", "Lead Two", null, "hash");
        var admin = new User("admin", "Admin", null, "hash");
        db.Users.AddRange(otherLead, admin);
        await db.SaveChangesAsync();
        var (performance, evaluations) = CreateServices(db);
        var supervisor = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(supervisor, new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None);
        var e = await db.Evaluations.SingleAsync(x => x.UserId == tester);
        var scores = e.Scores.Select(x => new EvaluationScoreInput(x.EvaluationScoreId, 3, null)).ToList();
        var request = new SaveManagerEvaluationRequest(scores, null, null, null, null, null, true);

        // ผู้มีสิทธิ์จัดการทีมคนอื่น (รวม SYS_ADMIN) ดูได้แต่ประเมินแทนไม่ได้
        foreach (var viewer in new[] { new PerformanceViewer(otherLead.UserId, true, []), new PerformanceViewer(admin.UserId, true, []) })
        {
            var seen = await evaluations.GetAsync(viewer, e.EvaluationId, CancellationToken.None);
            Assert.False(seen.CanManagerEdit);
            Assert.Contains("Lead", seen.ManagerRestriction);
            await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.SaveManagerAsync(viewer, e.EvaluationId, request, CancellationToken.None));
        }

        var done = await evaluations.SaveManagerAsync(supervisor, e.EvaluationId, request, CancellationToken.None);
        Assert.Equal("Completed", done.Summary.Status);
        Assert.True(done.CanReopen);
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => evaluations.ReopenAsync(new PerformanceViewer(otherLead.UserId, true, []), e.EvaluationId, new ReopenEvaluationRequest("แก้"), CancellationToken.None));

        // ไม่มีหัวหน้า = ประเมินไม่ได้จนกว่าจะตั้งหัวหน้า
        await performance.SaveProfileAsync(supervisor, tester, new SaveEmployeeProfileRequest(null, null, null, null, null, "Permanent", null, null), CancellationToken.None);
        var noSupervisor = await evaluations.GetAsync(supervisor, e.EvaluationId, CancellationToken.None);
        Assert.False(noSupervisor.CanReopen);
        Assert.Contains("ยังไม่ได้กำหนดหัวหน้า", noSupervisor.ManagerRestriction);
    }

    [Fact]
    public async Task Supervisor_must_have_evaluation_permission()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (performance, _) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);

        // tester ไม่มีสิทธิ์ PERFORMANCE.EDIT จึงเป็นหัวหน้าไม่ได้
        await Assert.ThrowsAsync<ArgumentException>(() => performance.SaveProfileAsync(manager, lead, new SaveEmployeeProfileRequest(null, null, null, tester, null, "Permanent", null, null), CancellationToken.None));
        var users = await performance.ListUsersAsync(manager, CancellationToken.None);
        Assert.True(users.Single(x => x.UserId == lead).CanEvaluate);
        Assert.False(users.Single(x => x.UserId == tester).CanEvaluate);
    }

    [Fact]
    public void Evaluation_list_query_translates_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        string[] open = ["Open", "SelfSubmitted"];
        var sql = db.Evaluations.AsNoTracking().Where(x => open.Contains(x.Status))
            .OrderBy(x => open.Contains(x.Status) ? 0 : 1).ThenBy(x => x.DueDate).Take(500).ToQueryString();
        Assert.Contains("ORDER BY", sql);
    }

    private static (PerformanceService Performance, EvaluationService Evaluations) CreateServices(QaDbContext db)
    {
        var clock = new FixedClock(Now);
        var repo = new PerformanceRepository(db, clock);
        var performance = new PerformanceService(repo, clock);
        return (performance, new EvaluationService(new EvaluationRepository(db), repo, performance));
    }

    [Fact]
    public async Task Team_views_hide_resigned_employees_but_personal_history_keeps_them()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, evaluations) = CreateServices(db);
        var manager = new PerformanceViewer(lead, true, []);
        await evaluations.CreateQuarterlyAsync(manager, new CreateQuarterlyEvaluationsRequest(2026, 2), CancellationToken.None);
        var profile = await db.EmployeeProfiles.SingleAsync(x => x.UserId == tester);
        profile.Update(null, null, null, lead, null, "Resigned", null, null, null);
        await db.SaveChangesAsync();

        Assert.DoesNotContain((await evaluations.CompareTeamAsync(manager, 2026, CancellationToken.None)).Rows, x => x.UserId == tester);
        Assert.DoesNotContain(await evaluations.ListAsync(manager, null, false, CancellationToken.None), x => x.UserId == tester);
        Assert.Single(await evaluations.ListAsync(manager, tester, false, CancellationToken.None));
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
        // หัวหน้า (ผู้ประเมิน) ต้องมีสิทธิ์ PERFORMANCE.EDIT จาก Role
        var editPerformance = new Permission("PERFORMANCE.EDIT", "PERFORMANCE.EDIT", "PERFORMANCE");
        db.Permissions.Add(editPerformance);
        db.RolePermissions.Add(new RolePermission(leadRole.RoleId, editPerformance.PermissionId));
        // ผู้ประเมินคือหัวหน้าที่ระบุในข้อมูลพนักงานเท่านั้น — ตั้ง lead เป็นหัวหน้าของ tester
        var profile = new EmployeeProfile(tester.UserId, null);
        profile.Update(null, null, null, lead.UserId, null, "Permanent", null, null, null);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
