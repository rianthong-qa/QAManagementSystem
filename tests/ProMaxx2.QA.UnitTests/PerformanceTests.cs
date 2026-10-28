using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class PerformanceTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc); // 10:00 เวลาไทย

    [Fact]
    public void Probation_end_defaults_to_119_days_including_hire_date()
    {
        var profile = new EmployeeProfile(Guid.NewGuid(), null);
        profile.Update(null, "QA Engineer", "junior", null, new DateOnly(2026, 10, 1), "probation", null, null, null);

        Assert.Equal("Probation", profile.EmploymentStatus);
        Assert.Equal("Junior", profile.Level);
        Assert.Equal(new DateOnly(2027, 1, 27), profile.ProbationEndDate);
        Assert.Equal(1, profile.ProbationDay(new DateOnly(2026, 10, 1)));
        Assert.Equal(30, profile.ProbationDay(new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void Probation_requires_hire_date_and_permanent_has_no_probation_day()
    {
        var profile = new EmployeeProfile(Guid.NewGuid(), null);
        Assert.Throws<ArgumentException>(() => profile.Update(null, null, null, null, null, "Probation", null, null, null));

        profile.Update(null, null, null, null, new DateOnly(2025, 1, 1), "Permanent", null, null, null);
        Assert.Null(profile.ProbationDay(new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void Profile_rejects_self_as_supervisor()
    {
        var userId = Guid.NewGuid();
        var profile = new EmployeeProfile(userId, null);
        Assert.Throws<ArgumentException>(() => profile.Update(null, null, null, userId, null, "Permanent", null, null, null));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.test/file")]
    [InlineData("not a url")]
    public void Work_log_accepts_only_http_links(string url)
    {
        Assert.Throws<ArgumentException>(() => new WorkLogEntry(Guid.NewGuid(), new DateOnly(2026, 10, 6), "Testing", "WorkLog", "Regression test", null, 2, null, url, null));
    }

    [Fact]
    public async Task Employee_cannot_view_other_user_or_write_concern()
    {
        await using var db = CreateDb();
        var (tester, other) = await SeedUsersAsync(db);
        var service = CreateService(db);
        var viewer = new PerformanceViewer(tester, false, []);

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => service.GetUserAsync(viewer, other, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => service.GetTeamAsync(viewer, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => service.CreateWorkLogAsync(viewer, new SaveWorkLogRequest(tester, new DateOnly(2026, 10, 6), "Other", "Concern", "x", null, null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => service.CreateWorkLogAsync(viewer, new SaveWorkLogRequest(other, new DateOnly(2026, 10, 6), "Other", "WorkLog", "x", null, null, null, null), CancellationToken.None));

        var own = await service.GetUserAsync(viewer, tester, null, null, CancellationToken.None);
        Assert.Equal(tester, own.Profile.UserId);
    }

    [Fact]
    public async Task Concern_is_hidden_from_employee_and_not_counted_as_work()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var service = CreateService(db);
        var manager = new PerformanceViewer(lead, true, []);
        var employee = new PerformanceViewer(tester, false, []);
        var today = new DateOnly(2026, 10, 6);

        await service.CreateWorkLogAsync(employee, new SaveWorkLogRequest(tester, today, "Testing", "WorkLog", "ทดสอบ Regression", null, 3, null, null), CancellationToken.None);
        await service.CreateWorkLogAsync(employee, new SaveWorkLogRequest(tester, today, "Support", "Highlight", "ช่วยทีม Dev หา root cause", null, 1.5m, null, "https://example.test/x"), CancellationToken.None);
        var concern = await service.CreateWorkLogAsync(manager, new SaveWorkLogRequest(tester, today, "Other", "Concern", "ส่งงานช้า", null, null, null, null), CancellationToken.None);

        var seenByEmployee = await service.ListWorkLogsAsync(employee, tester, null, null, CancellationToken.None);
        var seenByManager = await service.ListWorkLogsAsync(manager, tester, null, null, CancellationToken.None);
        Assert.Equal(2, seenByEmployee.Count);
        Assert.DoesNotContain(seenByEmployee, x => x.Kind == "Concern");
        Assert.Equal(3, seenByManager.Count);

        // พนักงานแก้/ลบ Concern ของตัวเองไม่ได้ และต้องไม่รู้ว่ามีอยู่ (404)
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.DeleteWorkLogAsync(employee, concern.WorkLogEntryId, CancellationToken.None));

        var summary = await service.GetUserAsync(manager, tester, null, null, CancellationToken.None);
        Assert.Equal(1, summary.Metrics.WorkLogs);
        Assert.Equal(1, summary.Metrics.Highlights);
        Assert.Equal(4.5m, summary.Metrics.WorkLogHours);
    }

    [Fact]
    public async Task Defect_metrics_respect_project_access_and_date_range()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var allowedProject = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        db.Defects.Add(new Defect(allowedProject, null, null, null, "D-1", "Crash", "Critical", "Open", tester, null, null, null, null, null));
        db.Defects.Add(new Defect(allowedProject, null, null, null, "D-2", "Typo", "Low", "Rejected", tester, null, null, null, null, null));
        db.Defects.Add(new Defect(otherProject, null, null, null, "D-3", "Hidden", "High", "Open", tester, null, null, null, null, null));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var manager = new PerformanceViewer(lead, true, [allowedProject]);

        // Defect สร้างด้วย DateTime.UtcNow จริง — ช่วงที่ครอบวันนี้ต้องนับ ช่วงในอดีตต้องไม่นับ
        var realToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var current = await service.GetUserAsync(manager, tester, realToday.AddDays(-1), realToday.AddDays(1), CancellationToken.None);
        Assert.Equal(2, current.Metrics.DefectsReported);
        Assert.Equal(1, current.Metrics.DefectsCritical);
        Assert.Equal(1, current.Metrics.DefectsRejected);

        var past = await service.GetUserAsync(manager, tester, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31), CancellationToken.None);
        Assert.Equal(0, past.Metrics.DefectsReported);
    }

    [Fact]
    public async Task Team_lists_all_active_users_and_saving_profile_writes_audit()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var developer = new User("dev", "Developer", null, "hash");
        db.Users.Add(developer);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var manager = new PerformanceViewer(lead, true, []);

        var saved = await service.SaveProfileAsync(manager, tester, new SaveEmployeeProfileRequest("E001", "QA Engineer", "Junior", lead, new DateOnly(2026, 9, 1), "Probation", null, null), CancellationToken.None);
        Assert.Equal(36, saved.ProbationDay); // 1 ก.ย. → 6 ต.ค. 2026
        Assert.Equal("Lead", saved.SupervisorName);

        var team = await service.GetTeamAsync(manager, null, null, CancellationToken.None);
        Assert.Contains(team.Members, x => x.Profile.UserId == tester);
        Assert.Contains(team.Members, x => x.Profile.UserId == lead);
        Assert.Contains(team.Members, x => x.Profile.UserId == developer.UserId);
        Assert.Equal(3, team.Members.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), team.From);

        var audit = await db.AuditLogs.SingleAsync(x => x.EntityType == "EmployeeProfile");
        Assert.Equal("Created", audit.Action);
        Assert.Equal(lead, audit.UserId);
    }

    [Fact]
    public void Range_is_validated()
    {
        var service = new PerformanceService(null!, new FixedClock(Now));
        Assert.Throws<ArgumentException>(() => service.ResolveRange(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 1)));
        Assert.Throws<ArgumentException>(() => service.ResolveRange(new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1)));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 6)), service.ResolveRange(null, null));
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), PerformanceService.ToUtc(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Performance_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Guid[] users = [Guid.NewGuid()];
        Guid[] projects = [Guid.NewGuid()];
        var from = Now.AddDays(-30);

        var sql = new[]
        {
            PerformanceQueries.Executions(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.PendingAssignments(db, users, projects).ToQueryString(),
            PerformanceQueries.OverdueAssignments(db, users, projects, Now).ToQueryString(),
            PerformanceQueries.TestCasesCreated(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.TestCaseRevisions(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.RequirementsCreated(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.RequirementRevisions(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.TestSuitesCreated(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.DefectsReported(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.DefectComments(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.AutomationCasesCreated(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.AutomationVersionsCreated(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.AutomationVersionsApproved(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.AutomationRuns(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.RegressionActions(db, users, from, Now, projects).ToQueryString(),
            PerformanceQueries.WorkLogs(db, users, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)).ToQueryString(),
            PerformanceQueries.WorkLogList(db, users[0], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), false, users[0], false).ToQueryString(),
            PerformanceQueries.ToWorkLogDto(db, db.WorkLogEntries.Where(x => x.WorkLogEntryId == users[0]), users[0], true).ToQueryString(),
            PerformanceQueries.ActiveUserOptions(db).ToQueryString(),
            PerformanceQueries.Timeline(db, users[0], from, Now, projects).OrderByDescending(x => x.Timestamp).Skip(20).Take(20).ToQueryString(),
        };

        Assert.All(sql, s => Assert.Contains("SELECT", s));
        Assert.Contains("DISTINCT", sql[0]); // จำนวนเคสไม่ซ้ำต้องนับใน SQL ไม่ใช่ใน memory
        Assert.Contains("UNION ALL", sql[^1]);
    }

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
        // หัวหน้า (ผู้ประเมิน) ต้องมีสิทธิ์ PERFORMANCE.EDIT จาก Role
        var editPerformance = new Permission("PERFORMANCE.EDIT", "PERFORMANCE.EDIT", "PERFORMANCE");
        db.Permissions.Add(editPerformance);
        db.RolePermissions.Add(new RolePermission(leadRole.RoleId, editPerformance.PermissionId));
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
