using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 2B: Personal Access Token และเครื่องมือ MCP ให้ Claude ค้นคลังความรู้</summary>
public sealed class KnowledgeMcpTests
{
    // ---------- PersonalAccessToken ----------

    [Fact]
    public void Token_CreateHashesAndShowsPlainOnce()
    {
        var now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        var (token, plain) = PersonalAccessToken.Create(Guid.NewGuid(), "  Claude Code — โน้ตบุ๊ก ", 90, now);

        Assert.StartsWith("qak_", plain);
        Assert.True(plain.Length > 40);
        Assert.Equal(plain[..12], token.TokenPrefix);
        Assert.Equal(PersonalAccessToken.Hash(plain), token.TokenHash);
        Assert.DoesNotContain(plain, token.TokenHash);
        Assert.Equal(("Claude Code — โน้ตบุ๊ก", now.AddDays(90), "knowledge:read"), (token.Name, token.ExpiresAt, token.Scope));
        Assert.NotEqual(plain, PersonalAccessToken.Create(Guid.NewGuid(), "x", 1, now).PlainText);
    }

    [Theory]
    [InlineData(" ", 90)]
    [InlineData("ok", 0)]
    [InlineData("ok", 366)]
    public void Token_RejectsInvalidInput(string name, int days) =>
        Assert.Throws<ArgumentException>(() => PersonalAccessToken.Create(Guid.NewGuid(), name, days, DateTime.UtcNow));

    [Fact]
    public void Token_LifecycleAndUsageThrottle()
    {
        var now = DateTime.UtcNow;
        var (token, _) = PersonalAccessToken.Create(Guid.NewGuid(), "t", 1, now);
        Assert.True(token.IsActive(now));
        Assert.False(token.IsActive(now.AddDays(1)));
        Assert.True(token.MarkUsed(now, "1.1.1.1"));
        Assert.False(token.MarkUsed(now.AddSeconds(30), "1.1.1.1"));
        Assert.True(token.MarkUsed(now.AddSeconds(30), "2.2.2.2"));
        Assert.True(token.MarkUsed(now.AddMinutes(2), "2.2.2.2"));
        token.Revoke(null, now);
        Assert.False(token.IsActive(now));
        Assert.False(PersonalAccessToken.LooksLikeToken("eyJhbGciOiJIUzI1NiJ9.x.y"));
    }

    // ---------- PersonalAccessTokenService ----------

    [Fact]
    public async Task Service_CreateRequiresMcpEnabledAndLimitsActiveTokens()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db, "QA_TESTER", ["KNOWLEDGE.VIEW", "KNOWLEDGE.MCP"]);
        var service = TokenService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(user.UserId, "t", 90, null, default));

        await EnableMcpAsync(db);
        service.ForgetMcpEnabled(); // Setting Center ล้าง cache สวิตช์ MCP หลังบันทึก
        for (var i = 0; i < PersonalAccessToken.MaxActivePerUser; i++) await service.CreateAsync(user.UserId, $"t{i}", 90, null, default);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(user.UserId, "เกิน", 90, null, default));
        Assert.Equal(PersonalAccessToken.MaxActivePerUser, await db.AuditLogs.CountAsync(x => x.EntityType == "PersonalAccessToken"));
    }

    [Fact]
    public async Task Service_AuthenticatesWithCurrentPermissionsAndRejectsRevokedExpiredInactive()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db, "QA_TESTER", ["KNOWLEDGE.VIEW", "KNOWLEDGE.MCP"]);
        await EnableMcpAsync(db);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = TokenService(db, cache);
        var created = await service.CreateAsync(user.UserId, "Claude", 90, null, default);

        var principal = await service.AuthenticateAsync(created.Token, "10.0.0.5", "PersonalAccessToken", default);
        Assert.NotNull(principal);
        Assert.Equal(user.UserId.ToString(), principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.HasClaim("permission", "KNOWLEDGE.MCP"));
        Assert.True(principal.IsInRole("QA_TESTER"));
        Assert.Equal(("knowledge:read", created.Item.TokenPrefix), (principal.FindFirstValue("scope"), principal.FindFirstValue("token_prefix")));
        Assert.NotNull((await db.PersonalAccessTokens.SingleAsync()).LastUsedAt);

        Assert.Null(await service.AuthenticateAsync(created.Token + "x", null, "PersonalAccessToken", default));
        Assert.Null(await service.AuthenticateAsync("Bearer-not-a-pat", null, "PersonalAccessToken", default));

        // ยกเลิกแล้วใช้ไม่ได้ทันที (ล้าง cache)
        Assert.True(await service.RevokeAsync(user.UserId, created.Item.PersonalAccessTokenId, false, null, default));
        Assert.Null(await service.AuthenticateAsync(created.Token, null, "PersonalAccessToken", default));

        // ผู้ใช้ถูกปิดบัญชี
        var second = await service.CreateAsync(user.UserId, "Claude 2", 90, null, default);
        (await db.Users.SingleAsync()).Deactivate();
        await db.SaveChangesAsync();
        Assert.Null(await TokenService(db).AuthenticateAsync(second.Token, null, "PersonalAccessToken", default));
    }

    [Fact]
    public async Task Service_RevokeOnlyOwnUnlessAdmin()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db, "QA_TESTER", ["KNOWLEDGE.MCP"]);
        await EnableMcpAsync(db);
        var service = TokenService(db);
        var created = await service.CreateAsync(owner.UserId, "t", 30, null, default);
        var other = Guid.NewGuid();
        Assert.False(await service.RevokeAsync(other, created.Item.PersonalAccessTokenId, false, null, default));
        Assert.True(await service.RevokeAsync(other, created.Item.PersonalAccessTokenId, true, null, default));
        Assert.Contains(await db.AuditLogs.Select(x => x.ChangeSummary).ToListAsync(), x => x!.Contains("ของผู้ใช้อื่น"));
        Assert.False((await service.ListAsync(owner.UserId, default)).Single().IsActive);
    }

    // ---------- KnowledgeMcpTools ----------

    [Fact]
    public async Task Tools_SearchReturnsStatusLinksAndAudits()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);
        var tools = Tools(db, seed.UserId, seed.Hits);

        var text = await tools.SearchKnowledge("พิมพ์ใบกำกับภาษี", cancellationToken: default);

        Assert.Contains("PMX-DEF-0001", text);
        Assert.Contains("สถานะ Open · ความรุนแรง High", text);
        Assert.Contains($"https://promaxx2.qahub.store/#/defects?defectId={seed.DefectId}", text);
        Assert.Contains("get_defect(\"PMX-DEF-0001\")", text);
        Assert.DoesNotContain("081-234-5678", text);
        Assert.Contains(await db.AuditLogs.Select(x => x.ChangeSummary).ToListAsync(), x => x!.Contains("search_knowledge") && x.Contains("token qak_test"));
    }

    [Fact]
    public async Task Tools_GetDefectMasksAndRespectsProjectMembership()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);

        var text = await Tools(db, seed.UserId, seed.Hits).GetDefect("pmx-def-0001");
        Assert.StartsWith("[Defect] PMX-DEF-0001", text);
        Assert.Contains("[PHONE]", text);
        Assert.DoesNotContain("081-234-5678", text);
        Assert.Contains("ลิงก์: https://promaxx2.qahub.store/#/defects?defectId=", text);
        Assert.StartsWith("[Defect]", await Tools(db, seed.UserId, seed.Hits).GetDefect(seed.DefectId.ToString()));

        var outsider = Guid.NewGuid();
        await Assert.ThrowsAsync<McpException>(() => Tools(db, outsider, seed.Hits).GetDefect("PMX-DEF-0001"));

        var doc = await db.KnowledgeDocuments.SingleAsync(x => x.SourceType == "Defect");
        doc.SetExcludeFromAi(true);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<McpException>(() => Tools(db, seed.UserId, seed.Hits).GetDefect("PMX-DEF-0001"));
    }

    [Fact]
    public async Task Tools_SearchSkipsExcludedDocumentsAndCrm()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);
        var doc = await db.KnowledgeDocuments.SingleAsync(x => x.SourceType == "Defect");
        doc.SetExcludeFromAi(true);
        await db.SaveChangesAsync();
        var search = new CapturingSearch(seed.Hits);
        var tools = new KnowledgeMcpTools(Accessor(seed.UserId), db, new KnowledgeSearchService(db, search, new ProjectAccessService(db), new Allow(true)), Indexer(db), Config());

        Assert.StartsWith("ไม่พบความรู้", await tools.SearchKnowledge("พิมพ์"));
        Assert.False(search.LastScope!.IncludeCrm);
    }

    [Fact]
    public async Task Tools_ProjectFilterAndListProjects()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);
        var search = new CapturingSearch(seed.Hits);
        var tools = new KnowledgeMcpTools(Accessor(seed.UserId), db, new KnowledgeSearchService(db, search, new ProjectAccessService(db), new Allow(false)), Indexer(db), Config());

        Assert.Contains("- PMX: ProMaxx", await tools.ListProjects(default));
        await tools.SearchKnowledge("พิมพ์", projectCode: "pmx");
        Assert.Equal(seed.ProjectId, search.LastScope!.ProjectId);
        await Assert.ThrowsAsync<McpException>(() => tools.SearchKnowledge("พิมพ์", projectCode: "NOPE"));
        await Assert.ThrowsAsync<McpException>(() => tools.SearchKnowledge("พิมพ์", source: "crm"));
        await Assert.ThrowsAsync<McpException>(() => tools.SearchKnowledge(" "));
        Assert.Contains("ยังไม่ได้เป็นสมาชิก", await Tools(db, Guid.NewGuid(), seed.Hits).ListProjects(default));
    }

    [Fact]
    public async Task Tools_GetArticlePublishedOnly()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);
        var published = new KnowledgeArticle("วิธีแก้เครื่องพิมพ์", "ติดต่อ it@shop.co.th แล้วเปลี่ยน driver", null, "พิมพ์", "Published", null);
        var draft = new KnowledgeArticle("ร่าง", "ยังไม่เสร็จ", null, null, "Draft", null);
        db.KnowledgeArticles.AddRange(published, draft);
        await db.SaveChangesAsync();
        var tools = Tools(db, seed.UserId, seed.Hits);

        var text = await tools.GetArticle(published.KnowledgeArticleId.ToString());
        Assert.StartsWith("[บทความ] วิธีแก้เครื่องพิมพ์", text);
        Assert.Contains("[EMAIL]", text);
        await Assert.ThrowsAsync<McpException>(() => tools.GetArticle(draft.KnowledgeArticleId.ToString()));
        await Assert.ThrowsAsync<McpException>(() => tools.GetArticle("not-a-guid"));
    }

    [Fact]
    public async Task Tools_FindSimilarDefectsLimitsToDefects()
    {
        await using var db = CreateDb();
        var seed = await SeedKnowledgeAsync(db);
        var search = new CapturingSearch(seed.Hits);
        var tools = new KnowledgeMcpTools(Accessor(seed.UserId), db, new KnowledgeSearchService(db, search, new ProjectAccessService(db), new Allow(false)), Indexer(db), Config());

        var text = await tools.FindSimilarDefects("กดพิมพ์ใบกำกับภาษีแล้วค้าง");
        Assert.Contains("Defect ที่อาจซ้ำ", text);
        Assert.Contains("สถานะ Open", text);
        Assert.Equal(["Defect"], search.LastScope!.SourceTypes!);
    }

    // ---------- helpers ----------

    private sealed record KnowledgeSeed(Guid UserId, Guid ProjectId, Guid DefectId, IReadOnlyList<KnowledgeChunkHit> Hits);

    private static async Task<KnowledgeSeed> SeedKnowledgeAsync(QaDbContext db)
    {
        var userId = Guid.NewGuid();
        var project = new Project("PMX", "ProMaxx", null, null, null);
        var defect = new Defect(project.ProjectId, null, null, null, "PMX-DEF-0001", "พิมพ์ใบกำกับภาษีไม่ได้", "High", "Open", null, "กดพิมพ์แล้วค้าง โทร 081-234-5678", null, null, null, null);
        db.AddRange(project, defect, new ProjectUser(project.ProjectId, userId, null));
        await db.SaveChangesAsync();
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(10, default);
        var hits = await db.KnowledgeChunks.Select(x => new KnowledgeChunkHit(x.KnowledgeChunkId, 100)).ToListAsync();
        return new(userId, project.ProjectId, defect.DefectId, hits);
    }

    private static async Task<User> SeedUserAsync(QaDbContext db, string roleCode, string[] permissions)
    {
        var user = new User("qa1", "QA One", null, "hash");
        var role = new Role(roleCode, roleCode);
        db.AddRange(user, role);
        foreach (var code in permissions)
        {
            var permission = new Permission(code, code, code.Split('.')[0]);
            db.Add(permission);
            db.Add(new RolePermission(role.RoleId, permission.PermissionId));
        }
        db.Add(new UserRole(user.UserId, role.RoleId));
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task EnableMcpAsync(QaDbContext db)
    {
        var config = new AiConfiguration("Local", "qwen3", "http://localhost:11434/v1", "", null, true);
        config.UpdateKnowledge("None", null, null, "", null, false, true);
        db.AiConfigurations.Add(config);
        await db.SaveChangesAsync();
    }

    private static PersonalAccessTokenService TokenService(QaDbContext db, IMemoryCache? cache = null) =>
        new(db, new IdentityRepository(db), cache ?? new MemoryCache(new MemoryCacheOptions()), Config());

    private static KnowledgeMcpTools Tools(QaDbContext db, Guid userId, IReadOnlyList<KnowledgeChunkHit> hits) =>
        new(Accessor(userId), db, new KnowledgeSearchService(db, new CapturingSearch(hits), new ProjectAccessService(db), new Allow(false)), Indexer(db), Config());

    private static IHttpContextAccessor Accessor(Guid userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("token_prefix", "qak_test")], "PersonalAccessToken")),
        },
    };

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicWebBaseUrl"] = "https://promaxx2.qahub.store/" }).Build();
    private static KnowledgeIndexService Indexer(QaDbContext db) => new(db, NullLogger<KnowledgeIndexService>.Instance);
    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CapturingSearch(IReadOnlyList<KnowledgeChunkHit> hits) : IKnowledgeTextSearch
    {
        public KnowledgeSearchScope? LastScope { get; private set; }
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) { LastScope = scope; return Task.FromResult(hits); }
    }

    private sealed class Allow(bool succeed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
