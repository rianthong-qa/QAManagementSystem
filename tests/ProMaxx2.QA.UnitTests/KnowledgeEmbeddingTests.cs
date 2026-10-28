using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 5: embedding + ค้นตามความหมาย (cosine ในแอป) + รวมผลกับ Full-Text ด้วย RRF</summary>
public sealed class KnowledgeEmbeddingTests
{
    private static readonly EmbeddingRuntime Local = new("Local", "bge-m3", "http://localhost:11434/v1", "");
    private static readonly EmbeddingRuntime OpenAi = new("OpenAI", "text-embedding-3-small", "https://api.openai.com/v1", "sk-test");

    // ---------- math / ranking / parsing ----------

    [Fact]
    public void VectorMath_NormalizeAndDot()
    {
        var a = VectorMath.Normalize([3, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal(0.6f, a[0], 4);
        Assert.Equal(1f, VectorMath.Dot(a, a), 4);
        Assert.Equal(0f, VectorMath.Dot(a, VectorMath.Normalize([0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0])), 4);
        Assert.Equal(0f, VectorMath.Dot(a, [1f, 0f]));
        Assert.All(VectorMath.Normalize([0, 0]), x => Assert.Equal(0f, x));
    }

    [Fact]
    public void HybridRanker_ChunkFoundByBothWins()
    {
        var text = new[] { new KnowledgeChunkHit(1, 900), new KnowledgeChunkHit(2, 500), new KnowledgeChunkHit(3, 100) };
        var vector = new[] { new KnowledgeChunkHit(4, 820), new KnowledgeChunkHit(3, 700) };

        var fused = HybridRanker.Fuse(text, vector, 10);
        Assert.Equal(3, fused[0].KnowledgeChunkId);
        Assert.Equal([3, 1, 4, 2], fused.Select(x => x.KnowledgeChunkId));
        Assert.Equal([1, 2], HybridRanker.Fuse(text, [], 2).Select(x => x.KnowledgeChunkId));
        Assert.Equal([4, 3], HybridRanker.Fuse([], vector, 5).Select(x => x.KnowledgeChunkId));
    }

    [Fact]
    public void Client_ParsesByIndexAndRejectsBadResponses()
    {
        var vectors = OpenAiCompatibleEmbeddingClient.Parse("""{"data":[{"index":1,"embedding":[0.5,0.5]},{"index":0,"embedding":[1,0]}]}""", 2, "Local");
        Assert.Equal([1f, 0f], vectors[0]);
        Assert.Throws<EmbeddingException>(() => OpenAiCompatibleEmbeddingClient.Parse("""{"data":[{"embedding":[1]}]}""", 2, "Local"));
        Assert.Throws<EmbeddingException>(() => OpenAiCompatibleEmbeddingClient.Parse("<html>error</html>", 1, "Local"));
    }

    // ---------- embed pending ----------

    [Fact]
    public async Task Embed_IndexedChunksOnceAndAgainWhenModelChanges()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var client = new KeywordClient();
        var settings = new FakeSettings(Local, allowCrm: false);
        var service = Service(db, settings, client);

        var first = await service.EmbedPendingAsync(100, default);
        Assert.Equal((4, false), (first.Count, first.Failed)); // ไม่รวมเอกสาร Stale
        Assert.All(await db.KnowledgeChunks.Where(x => x.KnowledgeDocumentId != seed.StaleDoc).ToListAsync(), c => Assert.Equal("Local:bge-m3", c.EmbeddingModel));
        var stored = (await db.KnowledgeChunks.FirstAsync(x => x.Embedding != null)).GetEmbedding();
        Assert.Equal(1f, VectorMath.Dot(stored, stored), 3);
        Assert.Equal(0, (await service.EmbedPendingAsync(100, default)).Count);

        settings.Runtime = Local with { Model = "nomic-embed-text" };
        Assert.Equal(4, (await service.EmbedPendingAsync(100, default)).Count);
        Assert.Equal(2, client.Calls); // batch เดียวต่อรอบ (ค่าเริ่มต้น 32)
    }

    [Fact]
    public async Task Embed_ExternalProviderSkipsCrmAndExcludedUnlessAllowed()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var excluded = await db.KnowledgeDocuments.SingleAsync(x => x.KnowledgeDocumentId == seed.GlobalDoc);
        excluded.SetExcludeFromAi(true);
        await db.SaveChangesAsync();
        var client = new KeywordClient();
        var settings = new FakeSettings(OpenAi, allowCrm: false);

        Assert.Equal(2, (await Service(db, settings, client).EmbedPendingAsync(100, default)).Count);
        Assert.DoesNotContain(client.Texts, t => t.Contains("[CRM]") || t.Contains("ปิดวัน"));

        settings.AllowCrm = true;
        Assert.Equal(1, (await Service(db, settings, client).EmbedPendingAsync(100, default)).Count);
        Assert.Contains(client.Texts, t => t.Contains("[CRM]"));
        var status = await Service(db, settings, client).GetStatusAsync(default);
        Assert.Equal((true, true, 3, 3), (status.Enabled, status.IsExternal, status.Eligible, status.Embedded));
    }

    [Fact]
    public async Task Embed_ProviderDownStopsAndRecordsError()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var state = new KnowledgeEmbeddingState();
        var service = Service(db, new FakeSettings(Local), new KeywordClient { Fail = true }, state);

        Assert.Equal((0, true), ((await service.EmbedPendingAsync(100, default)).Count, (await service.EmbedPendingAsync(100, default)).Failed));
        Assert.Contains("เชื่อมต่อไม่ได้", state.LastError);
        Assert.Equal((false, 0), ((await Service(db, new FakeSettings(null), new KeywordClient()).EmbedPendingAsync(10, default)).Failed, 0));
        Assert.False((await Service(db, new FakeSettings(null), new KeywordClient()).GetStatusAsync(default)).Enabled);
    }

    // ---------- semantic search ----------

    [Fact]
    public async Task Search_RespectsScopeAndMinScore()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var client = new KeywordClient();
        var service = Service(db, new FakeSettings(Local), client);
        await service.EmbedPendingAsync(100, default);

        // ถามคนละคำแต่ความหมายเดียวกัน (คำว่า "ใบกำกับ" ไม่อยู่ในเอกสาร แต่ fake model ให้เวกเตอร์ใกล้ "ภาษี")
        var member = new KnowledgeSearchScope([seed.ProjectId], IncludeCrm: true);
        var hits = await service.SearchAsync("ออกใบกำกับไม่ได้", member, 10, default);
        var docs = await DocsOf(db, hits);
        Assert.Equal(seed.ProjectDoc, docs[0]);
        Assert.Contains(seed.CrmDoc, docs);
        Assert.True(hits[0].Rank > 900);

        var outsider = new KnowledgeSearchScope([], IncludeCrm: false);
        var outsiderDocs = await DocsOf(db, await service.SearchAsync("ออกใบกำกับไม่ได้", outsider, 10, default));
        Assert.DoesNotContain(seed.ProjectDoc, outsiderDocs);
        Assert.DoesNotContain(seed.CrmDoc, outsiderDocs);
        Assert.Empty(await service.SearchAsync("ระบบเครือข่ายช้า", member, 10, default)); // ไม่ถึง MinScore
        Assert.Equal([seed.CrmDoc], await DocsOf(db, await service.SearchAsync("ใบกำกับ", member with { SourceTypes = ["CrmTicket"] }, 10, default)));

        // ถามซ้ำใช้เวกเตอร์คำถามจาก cache
        var calls = client.Calls;
        await service.SearchAsync("ออกใบกำกับไม่ได้", member, 10, default);
        Assert.Equal(calls, client.Calls);
    }

    [Fact]
    public async Task Search_HybridFindsSemanticMatchWhenFullTextMisses()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var embedding = Service(db, new FakeSettings(Local), new KeywordClient());
        await embedding.EmbedPendingAsync(100, default);
        var search = new KnowledgeSearchService(db, new NoTextSearch(), new ProjectAccessService(db), new Allow(), embedding);

        var results = await search.SearchAsync("ออกใบกำกับไม่ได้", new KnowledgeSearchScope([seed.ProjectId], false), 5, default);
        Assert.Equal(seed.ProjectDoc, results[0].KnowledgeDocumentId);
        var context = await search.RetrieveContextAsync("ออกใบกำกับไม่ได้", new KnowledgeSearchScope([seed.ProjectId], false), default);
        Assert.Equal(seed.ProjectDoc, context[0].KnowledgeDocumentId);

        // embedding ล่มตอนค้น → ค้นด้วยคำอย่างเดียว (ไม่ error)
        var down = new KnowledgeSearchService(db, new NoTextSearch(), new ProjectAccessService(db), new Allow(), Service(db, new FakeSettings(Local), new KeywordClient { Fail = true }));
        Assert.Empty(await down.SearchAsync("คำถามใหม่", new KnowledgeSearchScope([seed.ProjectId], false), 5, default));
    }

    [Fact]
    public async Task Settings_RuntimeFromSavedConfiguration()
    {
        await using var db = CreateDb();
        var service = new SharedAiConfigurationService(db, new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), new NoHttp());
        Assert.Null((await service.GetEmbeddingSettingsAsync(default)).Runtime);
        db.AiConfigurations.Add(new AiConfiguration("Local", "qwen3", "http://localhost:11434/v1", "", null, true));
        await db.SaveChangesAsync();
        Assert.Null((await service.GetEmbeddingSettingsAsync(default)).Runtime);

        await service.SaveKnowledgeAsync("OpenAI", "text-embedding-3-small", null, "sk-abcdef1234", false, true, false, default);
        var settings = await service.GetEmbeddingSettingsAsync(default);
        Assert.Equal(("OpenAI", "text-embedding-3-small", "https://api.openai.com/v1", "sk-abcdef1234", true), (settings.Runtime!.Provider, settings.Runtime.Model, settings.Runtime.BaseUrl, settings.Runtime.ApiKey, settings.AllowCrmToExternalAi));
        Assert.True(settings.Runtime.IsExternal);

        await service.SaveKnowledgeAsync("Local", "bge-m3", "http://gpu-box:11434/v1/", null, false, false, false, default);
        Assert.Equal(("Local:bge-m3", "http://gpu-box:11434/v1", false), ((await service.GetEmbeddingSettingsAsync(default)).Runtime!.ModelKey, (await service.GetEmbeddingSettingsAsync(default)).Runtime!.BaseUrl, (await service.GetEmbeddingSettingsAsync(default)).Runtime!.IsExternal));
    }

    // ---------- helpers ----------

    private sealed record Seed(Guid ProjectId, Guid ProjectDoc, Guid GlobalDoc, Guid CrmDoc, Guid StaleDoc);

    private static async Task<Seed> SeedAsync(QaDbContext db)
    {
        var projectId = Guid.NewGuid();
        Guid Add(string type, string key, string scope, Guid? project, string text, bool indexed = true)
        {
            var doc = new KnowledgeDocument(type, key, scope, project, key, null);
            if (indexed) doc.MarkIndexed("h-" + key, 1, DateTime.UtcNow);
            db.KnowledgeDocuments.Add(doc);
            db.KnowledgeChunks.Add(new KnowledgeChunk(doc.KnowledgeDocumentId, 0, text));
            return doc.KnowledgeDocumentId;
        }
        var projectDoc = Add("Defect", "D-1", "Project", projectId, "[Defect] พิมพ์ภาษีแล้วระบบค้าง");
        var globalDoc = Add("Article", "A-1", "Global", null, "[บทความ] วิธีปิดวันให้ยอดตรง");
        var crmDoc = Add("CrmTicket", "HD-1", "Crm", null, "[CRM] HD-1: ลูกค้าแจ้งเรื่องภาษี");
        Add("Article", "A-2", "Global", null, "[บทความ] พิมพ์เอกสาร", indexed: false);
        var stale = db.KnowledgeDocuments.Local.Single(x => x.SourceKey == "A-2").KnowledgeDocumentId;
        Add("File", "F-1", "Global", null, "[เอกสาร] คู่มือสต็อกสินค้า");
        await db.SaveChangesAsync();
        return new(projectId, projectDoc, globalDoc, crmDoc, stale);
    }

    private static async Task<List<Guid>> DocsOf(QaDbContext db, IReadOnlyList<KnowledgeChunkHit> hits)
    {
        var ids = hits.Select(x => x.KnowledgeChunkId).ToList();
        var map = await db.KnowledgeChunks.Where(x => ids.Contains(x.KnowledgeChunkId)).ToDictionaryAsync(x => x.KnowledgeChunkId, x => x.KnowledgeDocumentId);
        return hits.Select(x => map[x.KnowledgeChunkId]).ToList();
    }

    private KnowledgeEmbeddingService Service(QaDbContext db, IKnowledgeEmbeddingSettings settings, IEmbeddingClient client, KnowledgeEmbeddingState? state = null) =>
        new(db, settings, client, cache, state ?? new KnowledgeEmbeddingState(), new ConfigurationBuilder().Build(), NullLogger<KnowledgeEmbeddingService>.Instance);

    // cache ต่อ test (xUnit สร้าง instance ใหม่ทุก test) — ฐานข้อมูลในหน่วยความจำแต่ละชุดใช้รหัสช่วงซ้ำกันได้ จึงห้ามแชร์ดัชนีเวกเตอร์
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeSettings(EmbeddingRuntime? runtime, bool allowCrm = false) : IKnowledgeEmbeddingSettings
    {
        public EmbeddingRuntime? Runtime { get; set; } = runtime;
        public bool AllowCrm { get; set; } = allowCrm;
        public Task<KnowledgeEmbeddingSettings> GetEmbeddingSettingsAsync(CancellationToken ct) => Task.FromResult(new KnowledgeEmbeddingSettings(Runtime, AllowCrm, null));
    }

    /// <summary>model จำลอง: แต่ละมิติ = กลุ่มคำ ("ภาษี/ใบกำกับ", "พิมพ์", "ปิดวัน/ยอด", "สต็อก") + มิติพื้นฐาน — คำพ้องได้เวกเตอร์ใกล้กัน</summary>
    private sealed class KeywordClient : IEmbeddingClient
    {
        private static readonly string[][] Groups = [["ภาษี", "ใบกำกับ"], ["พิมพ์", "ออก"], ["ปิดวัน", "ยอด"], ["สต็อก"]];
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public List<string> Texts { get; } = [];

        public Task<IReadOnlyList<float[]>> EmbedAsync(EmbeddingRuntime runtime, IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new EmbeddingException("Embedding Local เชื่อมต่อไม่ได้");
            Texts.AddRange(texts);
            IReadOnlyList<float[]> vectors = texts.Select(t => Groups.Select(g => g.Any(t.Contains) ? 1f : 0f).Append(0.15f).ToArray()).ToList();
            return Task.FromResult(vectors);
        }
    }

    private sealed class NoTextSearch : IKnowledgeTextSearch
    {
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<KnowledgeChunkHit>>([]);
    }

    private sealed class Allow : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("ไม่ควรเรียก HTTP");
    }
}
