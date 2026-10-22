using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 1 ของคลังความรู้: ข้อความความรู้จาก Defect/บทความ, การ sync/index และการค้นหา (Full-Text จำลองด้วย fake)</summary>
public sealed class KnowledgeIndexTests
{
    // ---------- KnowledgeSourceText / KnowledgeChunkPlanner ----------

    [Fact]
    public void DefectText_ContainsContextSectionsAndLatestComments()
    {
        var comments = Enumerable.Range(1, 25).Select(i => new DefectKnowledgeComment(new DateTime(2026, 9, 1).AddDays(i), $"คอมเมนต์ {i}")).ToList();
        var text = KnowledgeSourceText.ForDefect(new("PMX-DEF-0007", "พิมพ์ใบกำกับภาษีไม่ได้", "High", "Closed", new DateTime(2026, 9, 1),
            "PMX ProMaxx", "ขาย / ใบกำกับภาษี", "REL-01 (1.0)", "10.0.228", "กดพิมพ์แล้วค้าง", "1. เปิดบิล\n2. กดพิมพ์", "พิมพ์ได้", "ค้าง",
            "HD-123", [new("TC-2", "พิมพ์ซ้ำ"), new("TC-1", "พิมพ์ใบแรก")], comments));

        Assert.Equal("[Defect] PMX-DEF-0007: พิมพ์ใบกำกับภาษีไม่ได้", text.Header);
        Assert.Contains("โมดูล: ขาย / ใบกำกับภาษี", text.Body);
        Assert.Contains("ความรุนแรง: High | สถานะ: Closed | แจ้งเมื่อ: 2026-09-01", text.Body);
        Assert.Contains("CRM Ticket: HD-123", text.Body);
        Assert.Contains("ขั้นตอนการทำซ้ำ:\n1. เปิดบิล\n2. กดพิมพ์", text.Body);
        Assert.True(text.Body.IndexOf("TC-1", StringComparison.Ordinal) < text.Body.IndexOf("TC-2", StringComparison.Ordinal));
        // เก็บ 20 คอมเมนต์ล่าสุด เรียงเก่า → ใหม่
        Assert.DoesNotContain("คอมเมนต์ 5\n", text.Body + "\n");
        Assert.Contains("คอมเมนต์ 6", text.Body);
        Assert.True(text.Body.IndexOf("คอมเมนต์ 6", StringComparison.Ordinal) < text.Body.IndexOf("คอมเมนต์ 25", StringComparison.Ordinal));
    }

    [Fact]
    public void DefectText_SkipsEmptySections()
    {
        var text = KnowledgeSourceText.ForDefect(new("D-1", "t", "Low", "Open", DateTime.UtcNow, null, null, null, null, null, " ", null, null, null, [], []));
        Assert.DoesNotContain("ขั้นตอนการทำซ้ำ", text.Body);
        Assert.DoesNotContain("คอมเมนต์", text.Body);
        Assert.DoesNotContain("Project:", text.Body);
    }

    [Fact]
    public void ArticleText_GlobalWhenNoProject()
    {
        var text = KnowledgeSourceText.ForArticle(new("วิธีปิดวัน", "ปิดวัน, รายงาน", null, "# ขั้นตอน\nกดปุ่มปิดวัน"));
        Assert.Equal("[บทความ] วิธีปิดวัน", text.Header);
        Assert.Contains("Project: ความรู้กลาง (ทุก Project)", text.Body);
        Assert.Contains("Tag: ปิดวัน, รายงาน", text.Body);
        Assert.EndsWith("# ขั้นตอน\nกดปุ่มปิดวัน", text.Body);
    }

    [Fact]
    public void ChunkPlanner_PrefixesHeaderMasksAndRespectsLimit()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"ย่อหน้า {i} ติดต่อ somchai@shop.co.th โทร 081-234-5678 " + new string('ก', 300)));
        var chunks = KnowledgeChunkPlanner.Plan(new("[Defect] D-1: ทดสอบ", body), maxChars: 800, overlapChars: 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith("[Defect] D-1: ทดสอบ\n", c));
        Assert.All(chunks, c => Assert.True(c.Length <= 800, $"{c.Length}"));
        Assert.All(chunks, c => Assert.DoesNotContain("somchai@", c));
        Assert.Contains(chunks, c => c.Contains("[EMAIL]") && c.Contains("[PHONE]"));
    }

    [Fact]
    public void ChunkPlanner_HeaderOnlyWhenBodyEmpty() =>
        Assert.Equal(["[บทความ] t"], KnowledgeChunkPlanner.Plan(new("[บทความ] t", "  ")));

    // ---------- KnowledgeQuery / KnowledgeSnippet ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ก ", null)]
    [InlineData("  ปิดวัน   ไม่ได้ \n", "ปิดวัน ไม่ได้")]
    public void Query_Normalizes(string? input, string? expected) => Assert.Equal(expected, KnowledgeQuery.Normalize(input));

    [Fact]
    public void Query_TruncatesLongInput() => Assert.Equal(KnowledgeQuery.MaxLength, KnowledgeQuery.Normalize(new string('ข', 500))!.Length);

    [Fact]
    public void Snippet_CentersOnMatchAndDropsHeader()
    {
        var chunk = "[Defect] D-1: หัวเรื่อง\n" + new string('ก', 400) + " ปิดวันไม่ได้เพราะยอดไม่ตรง " + new string('ข', 400);
        var snippet = KnowledgeSnippet.Build(chunk, "ปิดวัน", 120);
        Assert.Contains("ปิดวันไม่ได้", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.DoesNotContain("[Defect]", snippet);
    }

    [Fact]
    public void Snippet_ShortBodyReturnedWhole() => Assert.Equal("เนื้อหาสั้น", KnowledgeSnippet.Build("[บทความ] x\nเนื้อหาสั้น", "ไม่เจอ"));

    // ---------- KnowledgeIndexService ----------

    [Fact]
    public async Task Index_CreatesDefectDocumentWithContextAndComments()
    {
        await using var db = CreateDb();
        var seed = await SeedDefectAsync(db);
        var indexer = Indexer(db);

        Assert.Equal(1, await indexer.SyncSourcesAsync(default));
        Assert.Equal(1, await indexer.IndexPendingAsync(50, default));

        var doc = await db.KnowledgeDocuments.SingleAsync();
        Assert.Equal(("Defect", "Project", seed.Project.ProjectId, "Indexed"), (doc.SourceType, doc.Scope, doc.ProjectId, doc.State));
        Assert.Equal($"#/defects?defectId={seed.Defect.DefectId}", doc.SourceUrl);
        var text = string.Join("\n", await db.KnowledgeChunks.Select(x => x.Text).ToListAsync());
        Assert.Contains("โมดูล: ขาย / ใบกำกับภาษี", text);
        Assert.Contains("Release: REL-01 (1.0)", text);
        Assert.Contains("Build: 10.0.228", text);
        Assert.Contains("TC-0001 พิมพ์ใบกำกับ", text);
        Assert.Contains("ลองเปลี่ยน printer แล้วหาย [PHONE]", text);
        Assert.DoesNotContain("Defect details updated", text); // activity ที่ไม่ใช่คอมเมนต์ไม่เข้าคลัง
        Assert.Equal(doc.ChunkCount, await db.KnowledgeChunks.CountAsync());
    }

    [Fact]
    public async Task Index_UnchangedSourceIsNotRebuilt_NewCommentMarksStale()
    {
        await using var db = CreateDb();
        var seed = await SeedDefectAsync(db);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        var firstChunkId = await db.KnowledgeChunks.MinAsync(x => x.KnowledgeChunkId);

        Assert.Equal(0, await indexer.SyncSourcesAsync(default));
        Assert.Equal(0, await indexer.IndexPendingAsync(50, default));

        // reindex ทั้งหมดแต่เนื้อหาเดิม → hash ถูกล้าง จึงสร้าง chunk ใหม่
        await indexer.ForceReindexAllAsync(default);
        await indexer.IndexPendingAsync(50, default);
        Assert.NotEqual(firstChunkId, await db.KnowledgeChunks.MinAsync(x => x.KnowledgeChunkId));

        await Task.Delay(5);
        db.DefectActivities.Add(new DefectActivity(seed.Defect.DefectId, "Comment", "สาเหตุคือ driver เก่า", null));
        await db.SaveChangesAsync();
        Assert.Equal(1, await indexer.SyncSourcesAsync(default));
        Assert.Equal("Stale", (await db.KnowledgeDocuments.SingleAsync()).State);
        await indexer.IndexPendingAsync(50, default);
        Assert.Contains(await db.KnowledgeChunks.Select(x => x.Text).ToListAsync(), t => t.Contains("สาเหตุคือ driver เก่า"));
    }

    [Fact]
    public async Task Index_DeletedDefectIsExcludedAndChunksRemoved()
    {
        await using var db = CreateDb();
        var seed = await SeedDefectAsync(db);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);

        var defect = await db.Defects.SingleAsync();
        defect.SoftDelete(null);
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);

        Assert.Equal("Excluded", (await db.KnowledgeDocuments.SingleAsync()).State);
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
        Assert.NotEqual(Guid.Empty, seed.Defect.DefectId);
    }

    [Fact]
    public async Task Index_ArticlesOnlyWhenPublished_ScopeFollowsProject()
    {
        await using var db = CreateDb();
        var project = new Project("PMX", "ProMaxx", null, null, null);
        db.Projects.Add(project);
        var draft = new KnowledgeArticle("ร่าง", "ยังไม่เสร็จ", null, null, "Draft", null);
        var global = new KnowledgeArticle("วิธีปิดวัน", "กดปุ่มปิดวันที่หน้ารายงาน", null, "ปิดวัน", "Published", null);
        var scoped = new KnowledgeArticle("ตั้งค่าเครื่องพิมพ์", "เลือก driver รุ่นล่าสุด", project.ProjectId, null, "Published", null);
        db.KnowledgeArticles.AddRange(draft, global, scoped);
        await db.SaveChangesAsync();
        var indexer = Indexer(db);

        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);

        var docs = await db.KnowledgeDocuments.ToDictionaryAsync(x => x.SourceKey);
        Assert.False(docs.ContainsKey(draft.KnowledgeArticleId.ToString()));
        Assert.Equal(("Global", (Guid?)null), (docs[global.KnowledgeArticleId.ToString()].Scope, docs[global.KnowledgeArticleId.ToString()].ProjectId));
        Assert.Equal(("Project", (Guid?)project.ProjectId), (docs[scoped.KnowledgeArticleId.ToString()].Scope, docs[scoped.KnowledgeArticleId.ToString()].ProjectId));

        // ย้ายบทความเป็นฉบับร่าง → ไม่ค้นเจออีก
        await Task.Delay(5);
        var tracked = await db.KnowledgeArticles.SingleAsync(x => x.KnowledgeArticleId == global.KnowledgeArticleId);
        tracked.Update(tracked.Title, tracked.BodyMarkdown, null, null, "Draft", null);
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        Assert.Equal("Excluded", (await db.KnowledgeDocuments.SingleAsync(x => x.SourceKey == global.KnowledgeArticleId.ToString())).State);
        Assert.DoesNotContain(await db.KnowledgeChunks.Select(x => x.Text).ToListAsync(), t => t.Contains("วิธีปิดวัน"));
    }

    [Fact]
    public async Task Index_ExcludesDocumentWhoseSourceIsGone()
    {
        await using var db = CreateDb();
        // CRM Ticket ที่ไม่มีข้อความที่ดึงไว้แล้ว (เช่น ถูกลบจาก mirror) — ไม่เข้าคลัง
        db.KnowledgeDocuments.Add(new KnowledgeDocument("CrmTicket", "HD-1", "Crm", null, "HD-1", null));
        await db.SaveChangesAsync();
        Assert.Equal(1, await Indexer(db).IndexPendingAsync(50, default));
        Assert.Equal("Excluded", (await db.KnowledgeDocuments.SingleAsync()).State);
        Assert.Empty(db.KnowledgeChunks);
    }

    [Fact]
    public async Task Status_CountsPerSource()
    {
        await using var db = CreateDb();
        await SeedDefectAsync(db);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        var before = await indexer.GetStatusAsync(true, default);
        Assert.Equal(1, before.Sources.Single(x => x.SourceType == "Defect").Pending);
        await indexer.IndexPendingAsync(50, default);
        var after = await indexer.GetStatusAsync(true, default);
        var defects = after.Sources.Single(x => x.SourceType == "Defect");
        Assert.Equal((1, 0), (defects.Indexed, defects.Pending));
        Assert.True(defects.Chunks >= 1);
        Assert.Equal(KnowledgeDocument.SourceTypes.Length, after.Sources.Count);
    }

    // ---------- KnowledgeSearchService ----------

    [Fact]
    public async Task Search_GroupsChunksPerDocumentAndBuildsSnippet()
    {
        await using var db = CreateDb();
        var seed = await SeedDefectAsync(db);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(50, default);
        var chunkIds = await db.KnowledgeChunks.Select(x => x.KnowledgeChunkId).ToListAsync();
        var fake = new FakeTextSearch(chunkIds.Select((id, i) => new KnowledgeChunkHit(id, 100 - i)).Append(new KnowledgeChunkHit(999_999, 500)).ToList());
        var service = new KnowledgeSearchService(db, fake, new ProjectAccessService(db), new AllowAllAuthorization(false));

        var results = await service.SearchAsync("printer", new([seed.Project.ProjectId], false), 10, default);

        var result = Assert.Single(results);
        Assert.Equal(("Defect", seed.Project.ProjectName, chunkIds.Count), (result.SourceType, result.ProjectName, result.MatchedChunks));
        Assert.Equal(100, result.Rank);
        Assert.DoesNotContain("[Defect]", result.Snippet);
    }

    [Fact]
    public async Task Search_ScopeUsesMembershipAndCrmPolicy()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var member = Guid.NewGuid();
        db.ProjectUsers.Add(new ProjectUser(member, userId, null));
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));

        var withCrm = await new KnowledgeSearchService(db, new FakeTextSearch([]), new ProjectAccessService(db), new AllowAllAuthorization(true)).ScopeForAsync(principal, userId, null, ["Defect"], default);
        Assert.Equal([member], withCrm.AllowedProjectIds);
        Assert.True(withCrm.IncludeCrm);
        Assert.Equal(["Defect"], withCrm.SourceTypes!);

        var noCrm = await new KnowledgeSearchService(db, new FakeTextSearch([]), new ProjectAccessService(db), new AllowAllAuthorization(false)).ScopeForAsync(principal, Guid.NewGuid(), null, null, default);
        Assert.Empty(noCrm.AllowedProjectIds);
        Assert.False(noCrm.IncludeCrm);
    }

    [Fact]
    public async Task Search_NoHitsReturnsEmpty()
    {
        await using var db = CreateDb();
        var service = new KnowledgeSearchService(db, new FakeTextSearch([]), new ProjectAccessService(db), new AllowAllAuthorization(false));
        Assert.Empty(await service.SearchAsync("อะไรก็ได้", new([], false), 10, default));
    }

    // ---------- helpers ----------

    private sealed record Seed(Project Project, Defect Defect);

    private static async Task<Seed> SeedDefectAsync(QaDbContext db)
    {
        var project = new Project("PMX", "ProMaxx", null, null, null);
        var root = new ProductModule(project.ProjectId, "SALE", "ขาย", null, null, null, null);
        var child = new ProductModule(project.ProjectId, "TAX", "ใบกำกับภาษี", root.ModuleId, null, null, null);
        var release = new Release(project.ProjectId, "REL-01", "1.0", null, null, null, null, null);
        var build = new Build(release.ReleaseId, "10.0.228", null, null, null, null, null, null, null);
        var defect = new Defect(project.ProjectId, release.ReleaseId, build.BuildId, child.ModuleId, "PMX-DEF-0001", "พิมพ์ใบกำกับภาษีไม่ได้", "High", "Open", null,
            "กดพิมพ์แล้วค้าง", "1. เปิดบิล\n2. กดพิมพ์", "พิมพ์ได้", "ค้าง", null);
        var testCase = new TestCase(project.ProjectId, child.ModuleId, "TC-0001", "พิมพ์ใบกำกับ", null, null, "P1", null, false, null, [new TestStepInput(1, "กดพิมพ์", null, "พิมพ์ได้")], null);
        db.AddRange(project, root, child, release, build, defect, testCase);
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, testCase.TestCaseId, null));
        db.DefectActivities.Add(new DefectActivity(defect.DefectId, "Updated", "Defect details updated", null));
        db.DefectActivities.Add(new DefectActivity(defect.DefectId, "Comment", "ลองเปลี่ยน printer แล้วหาย 081-234-5678", null));
        await db.SaveChangesAsync();
        return new(project, defect);
    }

    private static KnowledgeIndexService Indexer(QaDbContext db) => new(db, NullLogger<KnowledgeIndexService>.Instance);

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeTextSearch(IReadOnlyList<KnowledgeChunkHit> hits) : IKnowledgeTextSearch
    {
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) => Task.FromResult(hits);
    }

    private sealed class AllowAllAuthorization(bool succeed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
