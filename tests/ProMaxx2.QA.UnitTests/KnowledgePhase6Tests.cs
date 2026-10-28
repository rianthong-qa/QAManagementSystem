using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 6: ผลการปิด Defect (Root Cause / Resolution), เตือน Defect ซ้ำ, Test Case เข้าคลังความรู้</summary>
public sealed class KnowledgePhase6Tests
{
    // ---------- Defect resolution ----------

    [Fact]
    public void Resolution_ValidatesTypeRootCauseAndDuplicate()
    {
        var defect = NewDefect(Guid.NewGuid(), "PMX-DEF-0001", "พิมพ์ไม่ได้");
        Assert.Throws<ArgumentException>(() => defect.SetResolution("Nope", null, null, null, null, null));
        Assert.Throws<ArgumentException>(() => defect.SetResolution("Fixed", " ", null, null, null, null));
        Assert.Throws<ArgumentException>(() => defect.SetResolution("Duplicate", null, null, null, null, null));
        Assert.Throws<ArgumentException>(() => defect.SetResolution("Duplicate", null, null, null, defect.DefectId, null));

        defect.SetResolution("fixed", " CodeLogic ", "  ", new string('x', 2500), Guid.NewGuid(), null);
        Assert.Equal(("Fixed", "CodeLogic", null, 2000, null), (defect.ResolutionType, defect.RootCauseCategory, defect.RootCauseNote, defect.ResolutionNote!.Length, defect.DuplicateOfDefectId));
        var original = Guid.NewGuid();
        defect.SetResolution("Duplicate", null, null, null, original, null);
        Assert.Equal(original, defect.DuplicateOfDefectId);
    }

    [Fact]
    public void DefectText_IncludesResolutionOnlyWhenPresent()
    {
        DefectKnowledgeInput Input(string? type = null) => new("PMX-DEF-0001", "พิมพ์ไม่ได้", "High", "Resolved", new DateTime(2026, 10, 1), "PMX", null, null, null,
            "กดพิมพ์แล้วค้าง", null, null, null, null, [], [], type, type is null ? null : "ตรรกะในโค้ด", type is null ? null : "คำนวณภาษีผิด", type is null ? null : "แก้สูตร", type == "Duplicate" ? "PMX-DEF-0002" : null);
        var plain = KnowledgeSourceText.ForDefect(Input());
        var legacy = KnowledgeSourceText.ForDefect(new("PMX-DEF-0001", "พิมพ์ไม่ได้", "High", "Resolved", new DateTime(2026, 10, 1), "PMX", null, null, null, "กดพิมพ์แล้วค้าง", null, null, null, null, [], []));
        Assert.Equal(legacy.Full, plain.Full); // Defect ที่ยังไม่ระบุผล hash ไม่เปลี่ยน — ไม่ต้อง index ใหม่ทั้งคลัง
        var fixedText = KnowledgeSourceText.ForDefect(Input("Fixed")).Body;
        Assert.Contains("ผลการปิด: แก้ไขแล้ว", fixedText);
        Assert.Contains("หมวดสาเหตุ: ตรรกะในโค้ด", fixedText);
        Assert.Contains("สาเหตุ:\nคำนวณภาษีผิด", fixedText);
        Assert.Contains("วิธีแก้:\nแก้สูตร", fixedText);
        Assert.Contains("ผลการปิด: ซ้ำกับ Defect อื่น (ซ้ำกับ PMX-DEF-0002)", KnowledgeSourceText.ForDefect(Input("Duplicate")).Body);
    }

    [Fact]
    public async Task Controller_ResolutionValidatesMasterOptionAndDuplicateProject()
    {
        await using var db = CreateDb();
        var (project, user) = await SeedProjectAsync(db);
        var other = new Project("OTH", "Other", null, null, null);
        var defect = NewDefect(project.ProjectId, "PMX-DEF-0001", "พิมพ์ใบกำกับไม่ได้");
        var original = NewDefect(project.ProjectId, "PMX-DEF-0002", "พิมพ์ใบกำกับค้าง");
        var foreign = NewDefect(other.ProjectId, "OTH-DEF-0001", "อื่น");
        db.AddRange(other, defect, original, foreign);
        db.MasterOptions.AddRange(new MasterOption("DefectRootCause", "CodeLogic", "ตรรกะในโค้ด", 10), Inactive("Legacy"));
        await db.SaveChangesAsync();
        var controller = Controller(db, user, project.ProjectId);

        Assert.IsType<BadRequestObjectResult>((await controller.SaveResolution(defect.DefectId, new("Fixed", "Unknown"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.SaveResolution(defect.DefectId, new("Fixed", "Legacy"), default)).Result);
        Assert.Equal("แก้ไขแล้ว (Fixed) ต้องเลือกหมวดสาเหตุ", ((BadRequestObjectResult)(await controller.SaveResolution(defect.DefectId, new("Fixed"), default)).Result!).Value);
        Assert.IsType<BadRequestObjectResult>((await controller.SaveResolution(defect.DefectId, new("Duplicate", DuplicateOfDefectId: foreign.DefectId), default)).Result);

        var ok = (OkObjectResult)(await controller.ChangeStatus(defect.DefectId, new("Resolved", new("Duplicate", RootCauseNote: "เรื่องเดียวกัน", DuplicateOfDefectId: original.DefectId)), default)).Result!;
        var dto = (DefectDto)ok.Value!;
        Assert.Equal(("Resolved", "Duplicate", "PMX-DEF-0002"), (dto.Status, dto.ResolutionType, dto.DuplicateOfDefectCode));
        Assert.Contains(await db.DefectActivities.Select(x => x.ActionType).ToListAsync(), x => x == "Resolution");

        // Resolved โดยไม่ส่งผลการปิด (Bulk/CRM) ยังทำได้ แต่ปิดตรงจาก Open ต้องระบุผลที่ไม่ใช่ Fixed (DEFECT_BUILD_LIFECYCLE.md)
        Assert.IsType<BadRequestObjectResult>((await controller.ChangeStatus(original.DefectId, new("Closed"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.ChangeStatus(original.DefectId, new("Resolved"), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.ChangeStatus(original.DefectId, new("Closed"), default)).Result);
    }

    [Fact]
    public async Task Controller_SimilarDefectsFromSameProjectOnly()
    {
        await using var db = CreateDb();
        var (project, user) = await SeedProjectAsync(db);
        var a = NewDefect(project.ProjectId, "PMX-DEF-0001", "พิมพ์ใบกำกับภาษีแล้วค้าง");
        var b = NewDefect(project.ProjectId, "PMX-DEF-0002", "ปิดวันยอดไม่ตรง");
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        var indexer = new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        var search = new CapturingSearch(await db.KnowledgeChunks.Select(x => new KnowledgeChunkHit(x.KnowledgeChunkId, 100)).ToListAsync());
        var controller = Controller(db, user, project.ProjectId, search);

        var rows = (IReadOnlyList<SimilarDefectDto>)((OkObjectResult)(await controller.Similar(project.ProjectId, "พิมพ์ใบกำกับภาษีไม่ออก", a.DefectId, default)).Result!).Value!;
        Assert.Equal(["PMX-DEF-0002"], rows.Select(x => x.DefectCode)); // ตัด Defect ที่กำลังแก้ออก
        Assert.Equal((project.ProjectId, false), (search.LastScope!.ProjectId!.Value, search.LastScope.IncludeCrm));
        Assert.Equal(["Defect"], search.LastScope.SourceTypes!);
        Assert.Empty((IReadOnlyList<SimilarDefectDto>)((OkObjectResult)(await controller.Similar(project.ProjectId, " ", null, default)).Result!).Value!);
        Assert.IsType<ForbidResult>((await Controller(db, user, Guid.NewGuid(), search).Similar(project.ProjectId, "พิมพ์", null, default)).Result);
    }

    [Fact]
    public async Task Index_DefectResolutionUsesRootCauseDisplayName()
    {
        await using var db = CreateDb();
        var (project, _) = await SeedProjectAsync(db);
        var defect = NewDefect(project.ProjectId, "PMX-DEF-0001", "ยอด VAT เกิน");
        db.Add(defect);
        db.MasterOptions.Add(new MasterOption("DefectRootCause", "CodeLogic", "ตรรกะในโค้ด (Code Logic)", 10));
        await db.SaveChangesAsync();
        var indexer = new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        Assert.DoesNotContain("ผลการปิด", (await db.KnowledgeChunks.SingleAsync()).Text);

        var tracked = await db.Defects.SingleAsync();
        tracked.SetResolution("Fixed", "CodeLogic", "คำนวณ VAT ก่อนหักส่วนลด", "แก้ลำดับการคำนวณ", null, null);
        typeof(Defect).GetProperty(nameof(Defect.UpdatedAt))!.SetValue(tracked, DateTime.UtcNow.AddMinutes(1)); // หลัง IndexedAt แน่นอน
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        var text = (await db.KnowledgeChunks.SingleAsync()).Text;
        Assert.Contains("หมวดสาเหตุ: ตรรกะในโค้ด (Code Logic)", text);
        Assert.Contains("วิธีแก้:\nแก้ลำดับการคำนวณ", text);
    }

    // ---------- Test Case source ----------

    [Fact]
    public async Task Index_TestCaseCurrentRevisionStepsAndLinkedDefects()
    {
        await using var db = CreateDb();
        var (project, _) = await SeedProjectAsync(db);
        var module = new ProductModule(project.ProjectId, "SALE", "ขาย", null, null, null, null);
        db.Add(module);
        var testCase = new TestCase(project.ProjectId, module.ModuleId, "PMX-TC-001", "พิมพ์ใบกำกับภาษีเต็มรูป", "ตรวจการพิมพ์", "มีบิลขาย", "P1", "Functional", false, null,
            [new TestStepInput(1, "เปิดบิล", "บิล 001", "แสดงบิล"), new TestStepInput(2, "กดพิมพ์", null, "ได้ใบกำกับ")], null);
        db.Add(testCase);
        await db.SaveChangesAsync();
        var indexer = new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);

        var doc = await db.KnowledgeDocuments.SingleAsync(x => x.SourceType == "TestCase");
        Assert.Equal(("Project", (Guid?)project.ProjectId, "PMX-TC-001: พิมพ์ใบกำกับภาษีเต็มรูป", $"#/test-cases?testCaseId={testCase.TestCaseId}"), (doc.Scope, doc.ProjectId, doc.Title, doc.SourceUrl));
        var text = (await db.KnowledgeChunks.SingleAsync(x => x.KnowledgeDocumentId == doc.KnowledgeDocumentId)).Text;
        Assert.StartsWith("[Test Case] PMX-TC-001: พิมพ์ใบกำกับภาษีเต็มรูป", text);
        Assert.Contains("โมดูล: ขาย", text);
        Assert.Contains("1. เปิดบิล (ข้อมูล: บิล 001) → ผลที่คาดหวัง: แสดงบิล", text);

        // แก้ขั้นตอน (Revision ใหม่) + ลิงก์ Defect → index ใหม่ ใช้เฉพาะขั้นตอนฉบับล่าสุด
        var tracked = await db.TestCases.Include(x => x.Steps).SingleAsync();
        tracked.CreateRevision(tracked.Title, null, null, [new TestStepInput(1, "กดพิมพ์ด่วน", null, "ได้ใบกำกับทันที")], "ปรับขั้นตอน", null);
        typeof(TestCase).GetProperty(nameof(TestCase.UpdatedAt))!.SetValue(tracked, DateTime.UtcNow.AddMinutes(1));
        var defect = NewDefect(project.ProjectId, "PMX-DEF-0009", "พิมพ์แล้วค้าง");
        db.Add(defect);
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, tracked.TestCaseId, null));
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        text = (await db.KnowledgeChunks.SingleAsync(x => x.KnowledgeDocumentId == doc.KnowledgeDocumentId)).Text;
        Assert.Contains("1. กดพิมพ์ด่วน", text);
        Assert.DoesNotContain("เปิดบิล", text);
        Assert.Contains("- PMX-DEF-0009 พิมพ์แล้วค้าง (Open)", text);

        tracked.SoftDelete(null);
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        Assert.Equal("Excluded", (await db.KnowledgeDocuments.SingleAsync(x => x.SourceType == "TestCase")).State);
    }

    [Fact]
    public async Task Mcp_GetTestCaseRespectsProjectAndSourceFilter()
    {
        await using var db = CreateDb();
        var (project, user) = await SeedProjectAsync(db);
        var module = new ProductModule(project.ProjectId, "SALE", "ขาย", null, null, null, null);
        var testCase = new TestCase(project.ProjectId, module.ModuleId, "PMX-TC-001", "ส่งอีเมลใบเสร็จ", null, null, "P2", null, false, null,
            [new TestStepInput(1, "ส่งไปที่ shop@demo.co.th", null, "ได้รับอีเมล")], null);
        var hidden = new Project("SEC", "Secret", null, null, null);
        var hiddenCase = new TestCase(hidden.ProjectId, module.ModuleId, "SEC-TC-001", "ลับ", null, null, "P2", null, false, null, [new TestStepInput(1, "x", null, "y")], null);
        db.AddRange(module, testCase, hidden, hiddenCase);
        await db.SaveChangesAsync();
        var search = new CapturingSearch([]);
        var tools = new KnowledgeMcpTools(Accessor(user), db, new KnowledgeSearchService(db, search, new ProjectAccessService(db), new Allow()),
            new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance), new ConfigurationBuilder().Build());

        var text = await tools.GetTestCase("pmx-tc-001");
        Assert.StartsWith("[Test Case] PMX-TC-001: ส่งอีเมลใบเสร็จ", text);
        Assert.Contains("[EMAIL]", text);
        Assert.Contains($"#/test-cases?testCaseId={testCase.TestCaseId}", text);
        await Assert.ThrowsAsync<McpException>(() => tools.GetTestCase("SEC-TC-001"));
        await tools.SearchKnowledge("ใบเสร็จ", source: "testcase");
        Assert.Equal(["TestCase"], search.LastScope!.SourceTypes!);
    }

    // ---------- helpers ----------

    private static Defect NewDefect(Guid projectId, string code, string title) =>
        new(projectId, null, null, null, code, title, "High", "Open", null, null, null, null, null, null);

    private static MasterOption Inactive(string value) { var option = new MasterOption("DefectRootCause", value, value, 99); option.Update(value, value, 99, false); return option; }

    private static async Task<(Project Project, Guid UserId)> SeedProjectAsync(QaDbContext db)
    {
        var userId = Guid.NewGuid();
        var project = new Project("PMX", "ProMaxx", null, null, null);
        db.AddRange(project, new ProjectUser(project.ProjectId, userId, null));
        await db.SaveChangesAsync();
        return (project, userId);
    }

    private static DefectsController Controller(QaDbContext db, Guid userId, Guid allowedProject, IKnowledgeTextSearch? search = null) =>
        new(db, new ProjectAccessContext { CurrentUserId = userId, AllowedProjectIds = [allowedProject] }, new DefectActivityService(db), null!, null!, NullLogger<DefectsController>.Instance,
            new KnowledgeSearchService(db, search ?? new CapturingSearch([]), new ProjectAccessService(db), new Allow()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } },
        };

    private static ClaimsPrincipal Principal(Guid userId) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
    private static IHttpContextAccessor Accessor(Guid userId) => new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(userId) } };
    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CapturingSearch(IReadOnlyList<KnowledgeChunkHit> hits) : IKnowledgeTextSearch
    {
        public KnowledgeSearchScope? LastScope { get; private set; }
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) { LastScope = scope; return Task.FromResult(hits); }
    }

    private sealed class Allow : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Success());
    }
}
