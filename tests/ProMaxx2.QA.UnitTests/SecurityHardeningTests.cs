using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>ครอบคลุมการปิดช่องโหว่จากการตรวจทั้งระบบ 2026-09-23: project isolation ของ <see cref="ProjectAccessFilter"/>
/// (body ProjectId + route id), ลิงก์แชร์ Dashboard ที่ไม่มี Project, worker-status ที่เคยเปิด anonymous,
/// การลบคอมเมนต์ Defect ของผู้อื่น, Defect attachment และ SQL translation ของ Audit Log union</summary>
public sealed class SecurityHardeningTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "qa-security-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] PngBytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed record FilterBody(Guid ProjectId, string Title);

    private static async Task<(QaDbContext Db, Guid UserId, Guid AllowedProject, Guid OtherProject)> SeedMembershipAsync()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var userId = Guid.NewGuid();
        var allowed = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.ProjectUsers.Add(new ProjectUser(allowed, userId, null));
        await db.SaveChangesAsync();
        return (db, userId, allowed, other);
    }

    private static async Task<(bool NextCalled, IActionResult? Result)> RunFilterAsync(QaDbContext db, Guid userId, IDictionary<string, object?> arguments, RouteValueDictionary? routeValues = null)
    {
        var projectCtx = new ProjectAccessContext();
        var services = new ServiceCollection().AddSingleton(db).AddSingleton(projectCtx).AddScoped<ProjectScopeGuard>().BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            RequestServices = services,
        };
        if (routeValues is not null) http.Request.RouteValues = routeValues;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor { EndpointMetadata = [new RequireProjectAccessAttribute()] });
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, new object());
        var nextCalled = false;
        await new ProjectAccessFilter(new ProjectAccessService(db), projectCtx).OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (nextCalled, context.Result);
    }

    [Fact]
    public async Task Filter_forbids_a_request_body_that_targets_a_project_the_user_is_not_a_member_of()
    {
        var (db, userId, _, other) = await SeedMembershipAsync();
        var (nextCalled, result) = await RunFilterAsync(db, userId, new Dictionary<string, object?> { ["r"] = new FilterBody(other, "x") });
        Assert.False(nextCalled);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Filter_allows_a_request_body_for_a_member_project()
    {
        var (db, userId, allowed, _) = await SeedMembershipAsync();
        var (nextCalled, result) = await RunFilterAsync(db, userId, new Dictionary<string, object?> { ["r"] = new FilterBody(allowed, "x") });
        Assert.True(nextCalled);
        Assert.Null(result);
    }

    [Fact]
    public async Task Filter_returns_not_found_for_a_route_id_that_belongs_to_another_project()
    {
        var (db, userId, _, other) = await SeedMembershipAsync();
        var defect = new Defect(other, null, null, null, "OTH-DEF-001", "secret", "High", "Open", null, null, null, null, null, null);
        db.Defects.Add(defect);
        await db.SaveChangesAsync();

        var (nextCalled, result) = await RunFilterAsync(db, userId, new Dictionary<string, object?>(), new RouteValueDictionary { ["defectId"] = defect.DefectId.ToString() });

        Assert.False(nextCalled);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Filter_passes_a_route_id_in_a_member_project_and_unknown_ids_through_to_the_action()
    {
        var (db, userId, allowed, _) = await SeedMembershipAsync();
        var defect = new Defect(allowed, null, null, null, "OWN-DEF-001", "mine", "High", "Open", null, null, null, null, null, null);
        db.Defects.Add(defect);
        await db.SaveChangesAsync();

        Assert.True((await RunFilterAsync(db, userId, new Dictionary<string, object?>(), new RouteValueDictionary { ["defectId"] = defect.DefectId.ToString() })).NextCalled);
        // ID ที่ไม่มีอยู่จริงต้องผ่านไปให้ action ตอบ 404 เอง ไม่ใช่ถูก filter ตัดสินแทน
        Assert.True((await RunFilterAsync(db, userId, new Dictionary<string, object?>(), new RouteValueDictionary { ["defectId"] = Guid.NewGuid().ToString() })).NextCalled);
    }

    private sealed class FakeDashboardRepository(DashboardShareScope? share) : IDashboardRepository
    {
        public Task<DashboardSummary> GetAsync(Guid? projectId, Guid? releaseId, Guid? buildId, CancellationToken ct) => throw new InvalidOperationException("dashboard data must not be read");
        public Task<DashboardTimeline> GetTimelineAsync(Guid? projectId, Guid? releaseId, Guid? buildId, CancellationToken ct) => throw new InvalidOperationException("dashboard data must not be read");
        public Task<DashboardShareScope> CreateShareAsync(Guid? projectId, Guid? releaseId, Guid? buildId, DateTime expiresAt, CancellationToken ct) => Task.FromResult(new DashboardShareScope("ABCDEFGH", projectId, releaseId, buildId, expiresAt));
        public Task<DashboardShareScope?> FindShareAsync(string code, CancellationToken ct) => Task.FromResult(share);
        public Task<bool> RevokeShareAsync(string code, IReadOnlyCollection<Guid> allowedProjectIds, CancellationToken ct) => Task.FromResult(share?.ProjectId is { } p && allowedProjectIds.Contains(p));
    }

    private static DashboardController MakeDashboard(DashboardShareScope? share, params Guid[] allowed) =>
        new(new DashboardService(new FakeDashboardRepository(share)), DataProtectionProvider.Create("tests"), new ProjectAccessContext { AllowedProjectIds = allowed });

    [Fact]
    public async Task Dashboard_share_link_requires_a_project_the_creator_can_access()
    {
        var allowed = Guid.NewGuid();
        var controller = MakeDashboard(null, allowed);

        Assert.IsType<BadRequestObjectResult>((await controller.CreateShareLink(new DashboardShareRequest(null, null, null), CancellationToken.None)).Result);
        Assert.IsType<ForbidResult>((await controller.CreateShareLink(new DashboardShareRequest(Guid.NewGuid(), null, null), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.CreateShareLink(new DashboardShareRequest(allowed, null, null), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Anonymous_dashboard_share_without_a_project_is_rejected_instead_of_exposing_every_project()
    {
        var controller = MakeDashboard(new DashboardShareScope("LEGACY01", null, null, null, DateTime.UtcNow.AddDays(1)));

        Assert.IsType<UnauthorizedObjectResult>((await controller.GetShortShared("LEGACY01", CancellationToken.None)).Result);
        Assert.IsType<UnauthorizedObjectResult>((await controller.GetShortSharedTimeline("LEGACY01", CancellationToken.None)).Result);
    }

    private static AutomationSchedulesController MakeScheduleController(IPAddress remote, params (string Name, string Value)[] headers)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = remote;
        foreach (var (name, value) in headers) http.Request.Headers[name] = value;
        return new AutomationSchedulesController(null!) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public void Worker_status_rejects_remote_and_tunnelled_anonymous_callers()
    {
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(MakeScheduleController(IPAddress.Parse("203.0.113.10")).SetWorkerStatus(false)).StatusCode);
        // Cloudflare Tunnel ส่งต่อมาจาก loopback เสมอ แต่แนบ CF-Connecting-IP — ต้องถูกปฏิเสธเหมือน remote
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(MakeScheduleController(IPAddress.Loopback, ("CF-Connecting-IP", "203.0.113.10")).SetWorkerStatus(false)).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(MakeScheduleController(IPAddress.Parse("192.168.200.50")).WorkerStatus()).StatusCode);
    }

    [Fact]
    public void Worker_status_still_serves_the_local_service_manager()
    {
        Assert.IsType<OkObjectResult>(MakeScheduleController(IPAddress.Loopback).WorkerStatus());
        Assert.IsType<OkObjectResult>(MakeScheduleController(IPAddress.IPv6Loopback).WorkerStatus());
    }

    [Fact]
    public async Task Only_the_comment_author_or_an_admin_can_delete_a_defect_comment_and_the_deletion_is_audited()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var author = Guid.NewGuid();
        var defectId = Guid.NewGuid();
        var comment = new DefectActivity(defectId, "Comment", "สวัสดี", author);
        db.DefectActivities.Add(comment);
        await db.SaveChangesAsync();
        var service = new DefectActivityService(db);

        Assert.False(await service.DeleteCommentAsync(defectId, comment.DefectActivityId, Guid.NewGuid(), false, CancellationToken.None));
        Assert.True(await db.DefectActivities.AnyAsync(x => x.DefectActivityId == comment.DefectActivityId));

        Assert.True(await service.DeleteCommentAsync(defectId, comment.DefectActivityId, author, false, CancellationToken.None));
        Assert.False(await db.DefectActivities.AnyAsync(x => x.DefectActivityId == comment.DefectActivityId));
        Assert.True(await db.DefectActivities.AnyAsync(x => x.DefectId == defectId && x.ActionType == "CommentDeleted"));
    }

    private sealed class FakeWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRoot;
    }

    private DefectsController MakeDefects(QaDbContext db, params Guid[] allowed) =>
        new(db, new ProjectAccessContext { AllowedProjectIds = allowed }, new DefectActivityService(db), null!, new FakeWebHostEnvironment(_tempRoot), NullLogger<DefectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")) } },
        };

    private static IFormFile MakeImage(string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);

    private static async Task<Defect> SeedDefectAsync(QaDbContext db, Guid projectId, string code)
    {
        var defect = new Defect(projectId, null, null, null, code, "title", "Medium", "Open", null, null, null, null, null, null);
        db.Defects.Add(defect);
        await db.SaveChangesAsync();
        return defect;
    }

    [Fact]
    public async Task Defect_attachment_upload_rejects_files_whose_content_is_not_the_declared_image_type()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var project = Guid.NewGuid();
        var defect = await SeedDefectAsync(db, project, "P-DEF-001");

        var result = await MakeDefects(db, project).UploadAttachments(defect.DefectId, [MakeImage("fake.png", [1, 2, 3, 4, 5, 6, 7, 8, 9])], CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "App_Data", "DefectAttachments")) && Directory.EnumerateFiles(Path.Combine(_tempRoot, "App_Data", "DefectAttachments"), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Defect_attachment_upload_enforces_the_five_image_limit_across_requests()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var project = Guid.NewGuid();
        var defect = await SeedDefectAsync(db, project, "P-DEF-002");
        var controller = MakeDefects(db, project);

        var first = await controller.UploadAttachments(defect.DefectId, Enumerable.Range(1, 4).Select(i => MakeImage($"s{i}.png", PngBytes)).ToList(), CancellationToken.None);
        Assert.Equal(4, Assert.IsAssignableFrom<IReadOnlyList<DefectAttachmentDto>>(Assert.IsType<OkObjectResult>(first.Result).Value).Count);

        var second = await controller.UploadAttachments(defect.DefectId, [MakeImage("a.png", PngBytes), MakeImage("b.png", PngBytes)], CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(second.Result);
    }

    [Fact]
    public async Task Defect_attachments_of_another_project_are_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var mine = Guid.NewGuid();
        var defect = await SeedDefectAsync(db, Guid.NewGuid(), "O-DEF-001");

        var controller = MakeDefects(db, mine);

        Assert.IsType<NotFoundResult>((await controller.Attachments(defect.DefectId, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.UploadAttachments(defect.DefectId, [MakeImage("a.png", PngBytes)], CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Legacy_attachment_files_on_disk_are_imported_as_metadata_on_first_list()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var project = Guid.NewGuid();
        var defect = await SeedDefectAsync(db, project, "L-DEF-001");
        var legacyId = Guid.NewGuid();
        var directory = Path.Combine(_tempRoot, "App_Data", "DefectAttachments", defect.DefectId.ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{legacyId:N}_screen.png"), PngBytes);

        var rows = Assert.IsAssignableFrom<IReadOnlyList<DefectAttachmentDto>>(Assert.IsType<OkObjectResult>((await MakeDefects(db, project).Attachments(defect.DefectId, CancellationToken.None)).Result).Value);

        var item = Assert.Single(rows);
        Assert.Equal(legacyId, item.AttachmentId);
        Assert.Equal("screen.png", item.FileName);
        Assert.Equal(1, await db.DefectAttachments.CountAsync(x => x.DefectId == defect.DefectId));
    }

    [Fact]
    public async Task Deleting_an_attachment_removes_the_metadata_row_and_the_file_and_is_audited()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var project = Guid.NewGuid();
        var defect = await SeedDefectAsync(db, project, "D-DEF-001");
        var controller = MakeDefects(db, project);
        var uploaded = Assert.IsAssignableFrom<IReadOnlyList<DefectAttachmentDto>>(Assert.IsType<OkObjectResult>((await controller.UploadAttachments(defect.DefectId, [MakeImage("a.png", PngBytes)], CancellationToken.None)).Result).Value);
        var attachmentId = Assert.Single(uploaded).AttachmentId;

        Assert.IsType<NoContentResult>(await controller.DeleteAttachment(defect.DefectId, attachmentId, CancellationToken.None));

        Assert.False(await db.DefectAttachments.AnyAsync(x => x.DefectAttachmentId == attachmentId));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_tempRoot, "App_Data", "DefectAttachments", defect.DefectId.ToString("N"))));
        Assert.True(await db.DefectActivities.AnyAsync(x => x.DefectId == defect.DefectId && x.ActionType == "AttachmentDeleted"));
    }

    [Fact]
    public async Task Defects_by_test_case_only_returns_defects_in_member_projects()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var mine = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var own = await SeedDefectAsync(db, mine, "M-DEF-001");
        var foreign = await SeedDefectAsync(db, Guid.NewGuid(), "F-DEF-001");
        db.DefectTestCaseLinks.AddRange(new DefectTestCaseLink(own.DefectId, testCaseId, null), new DefectTestCaseLink(foreign.DefectId, testCaseId, null));
        await db.SaveChangesAsync();

        var rows = Assert.IsAssignableFrom<IReadOnlyList<DefectDto>>(Assert.IsType<OkObjectResult>((await MakeDefects(db, mine).ByTestCase(testCaseId, CancellationToken.None)).Result).Value);

        Assert.Equal(["M-DEF-001"], rows.Select(x => x.DefectCode));
    }

    [Fact]
    public void Audit_log_union_query_translates_to_a_single_sql_statement()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        // ต้องตรงกับ query ใน AuditLogsController.List — ToQueryString() ไม่เชื่อมต่อ DB จริง แต่จะ throw ถ้า EF
        // แปล Concat ของ 3 แหล่ง (store type ต่างกัน) เป็น SQL ไม่ได้
        var query = db.DefectActivities.AsNoTracking()
            .Select(x => new { Timestamp = x.CreatedAt, x.ActorUserId, Action = x.ActionType, Entity = "Defect", EntityId = x.DefectId.ToString(), Summary = (string?)x.Message, Source = 0, SourceId = x.DefectActivityId })
            .Concat(db.RegressionActivities.AsNoTracking()
                .Select(x => new { Timestamp = x.CreatedAt, x.ActorUserId, x.Action, Entity = "Regression", EntityId = x.ReleaseId.ToString(), Summary = x.Details, Source = 1, SourceId = x.RegressionActivityId }))
            .Concat(db.AuditLogs.AsNoTracking()
                .Select(x => new { Timestamp = x.CreatedAt, ActorUserId = x.UserId, x.Action, Entity = x.EntityType, x.EntityId, Summary = x.ChangeSummary, Source = 2, SourceId = x.AuditLogId }));
        var pattern = "%x%";
        var sql = query.Where(x => EF.Functions.Like(x.Action, pattern, "\\") || (x.Summary != null && EF.Functions.Like(x.Summary, pattern, "\\")))
            .OrderByDescending(x => x.Timestamp).ThenBy(x => x.Source).ThenByDescending(x => x.SourceId).Skip(0).Take(25).ToQueryString();

        Assert.Contains("UNION ALL", sql);
    }
}
