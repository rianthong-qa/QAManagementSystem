using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class PerformanceShareLinkTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly CreatePerformanceShareLinkRequest Request = new(new DateOnly(2026, 10, 1), Today, 30, true, true, "HR — ผ่านทดลองงาน");

    [Fact]
    public async Task Only_managers_create_links_with_allowed_lifetime_and_creation_is_audited()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, shares, _) = CreateServices(db, new MutableClock(Now()));

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => shares.CreateAsync(new PerformanceViewer(tester, false, []), tester, Request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => shares.CreateAsync(Manager(lead), tester, Request with { ExpiresInDays = 365 }, CancellationToken.None));

        var link = await shares.CreateAsync(Manager(lead), tester, Request, CancellationToken.None);
        var code = link.Url.Split("?p=")[1];
        Assert.StartsWith("https://hub.test/?p=", link.Url);
        Assert.Equal(10, code.Length);
        Assert.Equal("Active", link.Status);
        Assert.Equal(Now().AddDays(30), link.ExpiresAt);
        Assert.Equal("Lead", link.CreatedByName);
        Assert.Single(await db.AuditLogs.Where(x => x.EntityType == PerformanceShareLinkService.AuditEntityType && x.Action == "Created").ToListAsync());
    }

    [Fact]
    public async Task Shared_report_never_includes_concerns_and_counts_views()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (performance, shares, _) = CreateServices(db, new MutableClock(Now()));
        var employee = new PerformanceViewer(tester, false, []);
        await performance.CreateWorkLogAsync(employee, new SaveWorkLogRequest(tester, Today, "Testing", "WorkLog", "ทดสอบ Regression", null, 3, null, null), CancellationToken.None);
        await performance.CreateWorkLogAsync(employee, new SaveWorkLogRequest(tester, Today, "Support", "Highlight", "ช่วยทีม Dev หา root cause", null, 1, null, null), CancellationToken.None);
        await performance.CreateWorkLogAsync(Manager(lead), new SaveWorkLogRequest(tester, Today, "Other", "Concern", "ส่งงานช้า", null, null, null, null), CancellationToken.None);
        var link = await shares.CreateAsync(Manager(lead), tester, Request, CancellationToken.None);

        var shared = await shares.OpenAsync(link.Url.Split("?p=")[1], CancellationToken.None);

        Assert.NotNull(shared);
        Assert.Equal("Tester", shared.Report.Profile.DisplayName);
        Assert.Equal(new DateOnly(2026, 10, 1), shared.Report.From);
        Assert.Equal(["Highlight", "WorkLog"], shared.Report.WorkLogs.Select(x => x.Kind));
        Assert.DoesNotContain(shared.Report.WorkLogs, x => x.Title == "ส่งงานช้า");
        Assert.Equal("HR — ผ่านทดลองงาน", shared.Note);
        Assert.Equal("Lead", shared.SharedByName);
        Assert.Equal(1, (await db.PerformanceShareLinks.SingleAsync()).ViewCount);
    }

    [Fact]
    public async Task Report_omits_sections_that_were_not_shared_and_hides_scores_of_open_rounds()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var (_, shares, evaluations) = CreateServices(db, new MutableClock(Now()));
        await evaluations.CreateQuarterlyAsync(Manager(lead), new CreateQuarterlyEvaluationsRequest(2026, 3), CancellationToken.None);
        var withEvaluations = await shares.CreateAsync(Manager(lead), tester, Request, CancellationToken.None);
        var withoutSections = await shares.CreateAsync(Manager(lead), tester, Request with { IncludeEvaluations = false, IncludeWorkLogs = false }, CancellationToken.None);

        var full = (await shares.OpenAsync(withEvaluations.Url.Split("?p=")[1], CancellationToken.None))!.Report;
        var round = Assert.Single(full.Evaluations);
        Assert.Equal("Q3", round.Round);
        Assert.Null(round.OverallScore);
        Assert.Null(round.Grade);

        var limited = (await shares.OpenAsync(withoutSections.Url.Split("?p=")[1], CancellationToken.None))!.Report;
        Assert.False(limited.IncludesEvaluations);
        Assert.False(limited.IncludesWorkLogs);
        Assert.Empty(limited.Evaluations);
        Assert.Null(limited.Comparison);
    }

    [Fact]
    public async Task Expired_revoked_or_orphaned_links_are_gone_and_unknown_codes_are_not_found()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var clock = new MutableClock(Now());
        var (_, shares, _) = CreateServices(db, clock);
        var expiring = await shares.CreateAsync(Manager(lead), tester, Request with { ExpiresInDays = 7 }, CancellationToken.None);
        var revoked = await shares.CreateAsync(Manager(lead), tester, Request, CancellationToken.None);
        var orphaned = await shares.CreateAsync(Manager(lead), tester, Request, CancellationToken.None);

        Assert.Null(await shares.OpenAsync("unknown123", CancellationToken.None));

        var after = await shares.RevokeAsync(Manager(lead), revoked.ShareLinkId, CancellationToken.None);
        Assert.Equal("Revoked", after.Status);
        Assert.Equal("Lead", after.RevokedByName);
        await Assert.ThrowsAsync<PerformanceShareLinkGoneException>(() => shares.OpenAsync(revoked.Url.Split("?p=")[1], CancellationToken.None));

        clock.Now = Now().AddDays(8);
        await Assert.ThrowsAsync<PerformanceShareLinkGoneException>(() => shares.OpenAsync(expiring.Url.Split("?p=")[1], CancellationToken.None));
        Assert.Equal("Expired", (await shares.ListAsync(Manager(lead), tester, CancellationToken.None)).Single(x => x.ShareLinkId == expiring.ShareLinkId).Status);

        // ผู้สร้างหมดสิทธิ์ PERFORMANCE.EDIT → ลิงก์ที่เคยสร้างใช้ไม่ได้ทันที
        Assert.NotNull(await shares.OpenAsync(orphaned.Url.Split("?p=")[1], CancellationToken.None));
        db.RolePermissions.RemoveRange(db.RolePermissions);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PerformanceShareLinkGoneException>(() => shares.OpenAsync(orphaned.Url.Split("?p=")[1], CancellationToken.None));
    }

    private static DateTime Now() => new(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);
    private static PerformanceViewer Manager(Guid lead) => new(lead, true, []);

    private static (PerformanceService Performance, PerformanceShareLinkService Shares, EvaluationService Evaluations) CreateServices(QaDbContext db, TimeProvider clock)
    {
        var repo = new PerformanceRepository(db, clock);
        var performance = new PerformanceService(repo, clock);
        var evaluations = new EvaluationService(new EvaluationRepository(db), repo, performance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicWebBaseUrl"] = "https://hub.test/" }).Build();
        var shares = new PerformanceShareLinkService(db, performance, new PerformanceReportService(performance, evaluations, repo, clock), new ProjectAccessService(db), config, clock);
        return (performance, shares, evaluations);
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
        var editPerformance = new Permission("PERFORMANCE.EDIT", "PERFORMANCE.EDIT", "PERFORMANCE");
        db.Permissions.Add(editPerformance);
        db.RolePermissions.Add(new RolePermission(leadRole.RoleId, editPerformance.PermissionId));
        var profile = new EmployeeProfile(tester.UserId, null);
        profile.Update("E001", "QA", "Junior", lead.UserId, new DateOnly(2026, 1, 5), "Permanent", null, "บันทึกภายในของหัวหน้า", null);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class MutableClock(DateTime utcNow) : TimeProvider
    {
        public DateTime Now { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
