using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>Embedding ที่ใช้งานอยู่ — ModelKey ("Provider:Model") เก็บคู่กับเวกเตอร์ เปลี่ยน provider/model = สร้างเวกเตอร์ใหม่ทั้งหมด</summary>
public sealed record EmbeddingRuntime(string Provider, string Model, string BaseUrl, string ApiKey)
{
    public string ModelKey => $"{Provider}:{Model}";
    /// <summary>Provider นอกองค์กร (ข้อความถูกส่งออก) — ใช้ตัดเอกสาร ExcludeFromAi และ CRM ที่ไม่ได้อนุญาต</summary>
    public bool IsExternal => Provider != "Local";
}

public sealed record KnowledgeEmbeddingSettings(EmbeddingRuntime? Runtime, bool AllowCrmToExternalAi, string? Error);

public interface IKnowledgeEmbeddingSettings
{
    Task<KnowledgeEmbeddingSettings> GetEmbeddingSettingsAsync(CancellationToken ct);
}

public sealed class EmbeddingException(string message, Exception? inner = null) : Exception(message, inner);

public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(EmbeddingRuntime runtime, IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>POST {BaseUrl}/embeddings แบบ OpenAI — ใช้ได้ทั้ง OpenAI และ AI Local (Ollama / LM Studio / vLLM ที่มี endpoint แบบ OpenAI)</summary>
public sealed class OpenAiCompatibleEmbeddingClient(IHttpClientFactory clients) : IEmbeddingClient
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(EmbeddingRuntime runtime, IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return [];
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{runtime.BaseUrl.TrimEnd('/')}/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model = runtime.Model, input = texts }), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(runtime.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        string body;
        try
        {
            using var response = await client.SendAsync(request, ct);
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new EmbeddingException($"Embedding {runtime.Provider} ตอบกลับไม่สำเร็จ ({(int)response.StatusCode}): {Cut(body, 300)}");
        }
        catch (HttpRequestException ex) { throw new EmbeddingException($"เชื่อมต่อ Embedding {runtime.Provider} ({runtime.BaseUrl}) ไม่ได้: {ex.Message}", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new EmbeddingException($"Embedding {runtime.Provider} ตอบช้าเกินกำหนด", ex); }
        return Parse(body, texts.Count, runtime.Provider);
    }

    public static IReadOnlyList<float[]> Parse(string body, int expected, string provider)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var items = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select((x, i) => (Index: x.TryGetProperty("index", out var index) ? index.GetInt32() : i, Vector: x.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()))
                .OrderBy(x => x.Index).Select(x => x.Vector).ToList();
            if (items.Count != expected || items.Any(x => x.Length == 0)) throw new EmbeddingException($"Embedding {provider} ส่งเวกเตอร์กลับมา {items.Count} รายการ (ขอ {expected})");
            return items;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new EmbeddingException($"Embedding {provider} ตอบกลับในรูปแบบที่ไม่รองรับ: {Cut(body, 200)}", ex);
        }
    }

    private static string Cut(string value, int max) => value.Length > max ? value[..max] + "…" : value;
}

/// <summary>สถานะล่าสุดของการสร้าง embedding (singleton — แสดงในหน้าสถานะคลัง)</summary>
public sealed class KnowledgeEmbeddingState
{
    public string? LastError { get; private set; }
    public DateTime? LastErrorAt { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public void Fail(string error) { LastError = error.Length > 500 ? error[..500] : error; LastErrorAt = DateTime.UtcNow; }
    public void Succeed() { LastError = null; LastSuccessAt = DateTime.UtcNow; }
}

public sealed record EmbedResult(int Count, bool Failed);
public sealed record KnowledgeEmbeddingStatusDto(bool Enabled, string? Provider, string? Model, bool IsExternal, int Eligible, int Embedded, string? Error, DateTime? LastErrorAt, DateTime? LastSuccessAt);
public sealed record EmbeddingTestResult(string Provider, string Model, int Dimensions, long ElapsedMs);

/// <summary>
/// Phase 5: ค้นตามความหมาย — สร้าง embedding ของ chunk ที่ index แล้ว (worker) และค้นด้วย cosine ในหน่วยความจำของแอป (SQL Server 2022 ไม่มีชนิด VECTOR)
/// เวกเตอร์เก็บใน KnowledgeChunks.Embedding (float32, normalize แล้ว) + EmbeddingModel = "Provider:Model"; โหลดเข้าหน่วยความจำและ cache จนกว่าชุดเวกเตอร์จะเปลี่ยน
/// สิทธิ์กรองจาก KnowledgeDocuments ทุกครั้งที่ค้น (ไม่ cache สิทธิ์); Provider ภายนอกไม่ส่งเอกสาร ExcludeFromAi และ CRM ที่ไม่ได้อนุญาต
/// </summary>
public sealed class KnowledgeEmbeddingService(QaDbContext db, IKnowledgeEmbeddingSettings settings, IEmbeddingClient client, IMemoryCache cache,
    KnowledgeEmbeddingState state, IConfiguration configuration, ILogger<KnowledgeEmbeddingService> logger) : IKnowledgeVectorSearch
{
    public const int MaxInputChars = 6000;
    public const string TestText = "ทดสอบการค้นหาความรู้ภาษาไทย: พิมพ์ใบกำกับภาษีไม่ได้";

    public int BatchSize => Math.Clamp(configuration.GetValue("Knowledge:EmbeddingBatchSize", 32), 1, 256);
    /// <summary>similarity ต่ำสุดที่ถือว่าเกี่ยวข้อง — ต่ำไป = ดึงเอกสารไม่เกี่ยวมาให้ AI, สูงไป = ไม่ช่วยอะไร (ขึ้นกับ model)</summary>
    public double MinScore => Math.Clamp(configuration.GetValue("Knowledge:VectorMinScore", 0.35), 0, 1);

    /// <summary>สร้าง embedding ให้ chunk ที่ยังไม่มี (หรือสร้างด้วย model อื่น) ไม่เกิน maxChunks — Failed = provider ใช้งานไม่ได้ (หยุดรอบนี้)</summary>
    public async Task<EmbedResult> EmbedPendingAsync(int maxChunks, CancellationToken ct)
    {
        var current = await settings.GetEmbeddingSettingsAsync(ct);
        if (current.Runtime is not { } runtime) return new(0, false);
        var done = 0;
        while (done < maxChunks)
        {
            var batch = await Pending(runtime, current.AllowCrmToExternalAi).OrderBy(x => x.KnowledgeChunkId).Take(Math.Min(BatchSize, maxChunks - done)).ToListAsync(ct);
            if (batch.Count == 0) break;
            IReadOnlyList<float[]> vectors;
            try { vectors = await client.EmbedAsync(runtime, batch.Select(x => x.Text.Length > MaxInputChars ? x.Text[..MaxInputChars] : x.Text).ToList(), ct); }
            catch (EmbeddingException ex)
            {
                logger.LogWarning("Knowledge embedding: หยุดรอบนี้ — {Reason}", ex.Message);
                state.Fail(ex.Message);
                return new(done, true);
            }
            for (var i = 0; i < batch.Count; i++) batch[i].SetEmbedding(VectorMath.Normalize(vectors[i]), runtime.ModelKey);
            await db.SaveChangesAsync(ct);
            done += batch.Count;
        }
        if (done > 0) state.Succeed();
        return new(done, false);
    }

    private IQueryable<Domain.Knowledge.KnowledgeChunk> Pending(EmbeddingRuntime runtime, bool allowCrm)
    {
        var key = runtime.ModelKey;
        return from c in Eligible(runtime, allowCrm)
               where c.EmbeddingModel == null || c.EmbeddingModel != key
               select c;
    }

    /// <summary>chunk ของเอกสารที่ index แล้วและส่งให้ provider นี้ได้</summary>
    private IQueryable<Domain.Knowledge.KnowledgeChunk> Eligible(EmbeddingRuntime runtime, bool allowCrm)
    {
        var external = runtime.IsExternal;
        return from c in db.KnowledgeChunks
               join d in db.KnowledgeDocuments on c.KnowledgeDocumentId equals d.KnowledgeDocumentId
               where d.State == "Indexed" && (!external || (!d.ExcludeFromAi && (d.SourceType != "CrmTicket" || allowCrm)))
               select c;
    }

    public async Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct)
    {
        var current = await settings.GetEmbeddingSettingsAsync(ct);
        if (current.Runtime is not { } runtime || string.IsNullOrWhiteSpace(query)) return [];
        var index = await LoadIndexAsync(runtime.ModelKey, ct);
        if (index.Entries.Count == 0) return [];

        float[] queryVector;
        var cacheKey = $"kn-qvec:{runtime.ModelKey}:{query}";
        if (cache.TryGetValue(cacheKey, out float[]? cached) && cached is not null) queryVector = cached;
        else
        {
            try { queryVector = VectorMath.Normalize((await client.EmbedAsync(runtime, [query], ct))[0]); }
            catch (EmbeddingException ex)
            {
                // ค้นตามความหมายไม่ได้ → ใช้ผลค้นด้วยคำอย่างเดียว (ไม่ทำให้การค้นหาทั้งหมดล้ม)
                logger.LogWarning("Knowledge embedding: ค้นตามความหมายไม่ได้ — {Reason}", ex.Message);
                state.Fail(ex.Message);
                return [];
            }
            cache.Set(cacheKey, queryVector, TimeSpan.FromMinutes(30));
        }

        var allowedDocs = (await AllowedDocuments(scope).ToListAsync(ct)).ToHashSet();
        var minScore = (float)MinScore;
        var best = new List<KnowledgeChunkHit>();
        foreach (var entry in index.Entries)
        {
            if (!allowedDocs.Contains(entry.KnowledgeDocumentId)) continue;
            var similarity = VectorMath.Dot(queryVector, entry.Vector);
            if (similarity >= minScore) best.Add(new KnowledgeChunkHit(entry.KnowledgeChunkId, (int)Math.Round(similarity * 1000)));
        }
        return best.OrderByDescending(x => x.Rank).ThenBy(x => x.KnowledgeChunkId).Take(take).ToList();
    }

    /// <summary>กติกาสิทธิ์เดียวกับ SqlKnowledgeTextSearch (Project ที่เป็นสมาชิก / ความรู้กลาง / CRM เมื่อมีสิทธิ์)</summary>
    private IQueryable<Guid> AllowedDocuments(KnowledgeSearchScope scope)
    {
        var projects = scope.AllowedProjectIds.ToList();
        var crm = scope.IncludeCrm;
        var projectId = scope.ProjectId;
        var sources = scope.SourceTypes?.ToList() ?? [];
        var anySource = sources.Count == 0;
        return db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.State == "Indexed"
                && ((d.Scope == "Project" && d.ProjectId != null && projects.Contains(d.ProjectId.Value)) || d.Scope == "Global" || (d.Scope == "Crm" && crm))
                && (projectId == null || d.ProjectId == projectId || d.Scope != "Project")
                && (anySource || sources.Contains(d.SourceType)))
            .Select(d => d.KnowledgeDocumentId);
    }

    private sealed record VectorEntry(int KnowledgeChunkId, Guid KnowledgeDocumentId, float[] Vector);
    private sealed record VectorIndex(string Stamp, IReadOnlyList<VectorEntry> Entries);

    /// <summary>เวกเตอร์ทั้งหมดของ model นี้ในหน่วยความจำ — โหลดใหม่เมื่อจำนวน/ช่วงล่าสุดเปลี่ยน (เช็กด้วย query เล็ก ๆ ทุกครั้งที่ค้น)</summary>
    private async Task<VectorIndex> LoadIndexAsync(string modelKey, CancellationToken ct)
    {
        var stats = await db.KnowledgeChunks.AsNoTracking().Where(x => x.EmbeddingModel == modelKey)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), MaxId = g.Max(x => x.KnowledgeChunkId) }).FirstOrDefaultAsync(ct);
        var stamp = $"{stats?.Count ?? 0}:{stats?.MaxId ?? 0}";
        var cacheKey = $"kn-vindex:{modelKey}";
        if (cache.TryGetValue(cacheKey, out VectorIndex? cached) && cached?.Stamp == stamp) return cached;
        var started = Stopwatch.StartNew();
        var rows = await db.KnowledgeChunks.AsNoTracking().Where(x => x.EmbeddingModel == modelKey && x.Embedding != null)
            .Select(x => new { x.KnowledgeChunkId, x.KnowledgeDocumentId, x.Embedding }).ToListAsync(ct);
        var entries = rows.Select(x =>
        {
            var vector = new float[x.Embedding!.Length / sizeof(float)];
            Buffer.BlockCopy(x.Embedding, 0, vector, 0, vector.Length * sizeof(float));
            return new VectorEntry(x.KnowledgeChunkId, x.KnowledgeDocumentId, vector);
        }).ToList();
        var index = new VectorIndex(stamp, entries);
        cache.Set(cacheKey, index, TimeSpan.FromHours(6));
        if (entries.Count > 0) logger.LogInformation("Knowledge embedding: โหลดเวกเตอร์ {Count} ช่วง ({Model}) ใน {Ms} ms", entries.Count, modelKey, started.ElapsedMilliseconds);
        return index;
    }

    public async Task<KnowledgeEmbeddingStatusDto> GetStatusAsync(CancellationToken ct)
    {
        var current = await settings.GetEmbeddingSettingsAsync(ct);
        if (current.Runtime is not { } runtime) return new(false, null, null, false, 0, 0, current.Error, state.LastErrorAt, state.LastSuccessAt);
        var key = runtime.ModelKey;
        var eligible = Eligible(runtime, current.AllowCrmToExternalAi);
        var total = await eligible.CountAsync(ct);
        var embedded = await eligible.CountAsync(c => c.EmbeddingModel == key, ct);
        return new(true, runtime.Provider, runtime.Model, runtime.IsExternal, total, embedded, state.LastError, state.LastErrorAt, state.LastSuccessAt);
    }

    /// <summary>ทดสอบการตั้งค่าที่บันทึกไว้ (Setting Center) — ส่งข้อความทดสอบ 1 ข้อความ</summary>
    public async Task<EmbeddingTestResult> TestAsync(CancellationToken ct)
    {
        var current = await settings.GetEmbeddingSettingsAsync(ct);
        var runtime = current.Runtime ?? throw new EmbeddingException(current.Error ?? "ยังไม่ได้เลือก Embedding Provider — บันทึกการตั้งค่าคลังความรู้ก่อนทดสอบ");
        var started = Stopwatch.StartNew();
        var vector = (await client.EmbedAsync(runtime, [TestText], ct))[0];
        return new(runtime.Provider, runtime.Model, vector.Length, started.ElapsedMilliseconds);
    }
}
