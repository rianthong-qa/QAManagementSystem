using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// สร้าง/อัปเดตคลังความรู้จากแหล่งข้อมูล (AI_KNOWLEDGE_CHAT_PLAN.md Phase 1: Defect + บทความ)
/// 1) <see cref="SyncSourcesAsync"/> เทียบแหล่งข้อมูลกับ KnowledgeDocuments — สร้างเอกสารใหม่, mark Stale เมื่อแหล่งเปลี่ยนหลัง IndexedAt, Exclude เมื่อถูกลบ
///    (ตรวจจากเวลาแก้ไข/กิจกรรมล่าสุด แทนการ hook ทุกจุดที่แก้ Defect — Defect ถูกแก้จากหลายช่องทาง: หน้า Defect, Execution, Automation, CRM sync)
/// 2) <see cref="IndexPendingAsync"/> สร้าง chunk ของเอกสาร Stale ทีละ batch — ContentHash เดิม = ไม่เขียน chunk ใหม่
/// </summary>
public sealed class KnowledgeIndexService(QaDbContext db, ILogger<KnowledgeIndexService> logger)
{
    /// <summary>เปลี่ยนเมื่อรูปแบบข้อความ/การแบ่ง chunk เปลี่ยน — hash จะต่างจากเดิมและทุกเอกสารถูกสร้างใหม่</summary>
    public const string IndexVersion = "k1";

    /// <summary>แหล่งที่ index ได้ใน Phase นี้ — File (Phase 3) / CrmTicket (Phase 4) ยังไม่ถูกดึงมาประมวลผล</summary>
    public static readonly string[] SupportedSourceTypes = ["Defect", "Article"];

    public async Task<int> SyncSourcesAsync(CancellationToken ct)
    {
        var changed = await SyncDefectsAsync(ct) + await SyncArticlesAsync(ct);
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    private async Task<int> SyncDefectsAsync(CancellationToken ct)
    {
        var docs = await db.KnowledgeDocuments.Where(x => x.SourceType == "Defect").ToDictionaryAsync(x => x.SourceKey, ct);
        var defects = await db.Defects.AsNoTracking()
            .Select(x => new { x.DefectId, x.ProjectId, x.DefectCode, x.Title, x.IsDeleted, Changed = x.UpdatedAt ?? x.CreatedAt })
            .ToListAsync(ct);
        // คอมเมนต์/ลิงก์ Test Case/CRM ไม่เปลี่ยน Defect.UpdatedAt แต่บันทึก DefectActivity เสมอ
        var lastActivity = await db.DefectActivities.AsNoTracking().GroupBy(x => x.DefectId)
            .Select(g => new { DefectId = g.Key, At = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.DefectId, x => x.At, ct);
        var changed = 0;
        foreach (var d in defects)
        {
            var key = d.DefectId.ToString();
            var title = $"{d.DefectCode}: {d.Title}";
            var at = lastActivity.TryGetValue(d.DefectId, out var activityAt) && activityAt > d.Changed ? activityAt : d.Changed;
            changed += Reconcile(docs.GetValueOrDefault(key), !d.IsDeleted, at,
                () => new KnowledgeDocument("Defect", key, "Project", d.ProjectId, title, DefectUrl(d.DefectId)),
                doc => doc.ProjectId != d.ProjectId || doc.Title != Cut(title, 300),
                doc => doc.UpdateSource("Project", d.ProjectId, title, DefectUrl(d.DefectId)));
        }
        // Defect ที่ถูกลบออกจากฐานข้อมูลจริง (ไม่ใช่ soft delete)
        var existing = defects.Select(x => x.DefectId.ToString()).ToHashSet();
        foreach (var orphan in docs.Values.Where(x => x.State != "Excluded" && !existing.Contains(x.SourceKey))) { orphan.Exclude(); changed++; }
        return changed;
    }

    private async Task<int> SyncArticlesAsync(CancellationToken ct)
    {
        var docs = await db.KnowledgeDocuments.Where(x => x.SourceType == "Article").ToDictionaryAsync(x => x.SourceKey, ct);
        var articles = await db.KnowledgeArticles.AsNoTracking()
            .Select(x => new { x.KnowledgeArticleId, x.ProjectId, x.Title, x.Status, x.IsDeleted, Changed = x.UpdatedAt ?? x.CreatedAt })
            .ToListAsync(ct);
        var changed = 0;
        foreach (var a in articles)
        {
            var key = a.KnowledgeArticleId.ToString();
            var scope = a.ProjectId.HasValue ? "Project" : "Global";
            changed += Reconcile(docs.GetValueOrDefault(key), !a.IsDeleted && a.Status == "Published", a.Changed,
                () => new KnowledgeDocument("Article", key, scope, a.ProjectId, a.Title, ArticleUrl(a.KnowledgeArticleId)),
                doc => doc.ProjectId != a.ProjectId || doc.Scope != scope || doc.Title != Cut(a.Title, 300),
                doc => doc.UpdateSource(scope, a.ProjectId, a.Title, ArticleUrl(a.KnowledgeArticleId)));
        }
        var existing = articles.Select(x => x.KnowledgeArticleId.ToString()).ToHashSet();
        foreach (var orphan in docs.Values.Where(x => x.State != "Excluded" && !existing.Contains(x.SourceKey))) { orphan.Exclude(); changed++; }
        return changed;
    }

    /// <summary>คืน 1 เมื่อเอกสารถูกสร้าง/เปลี่ยนสถานะ</summary>
    private int Reconcile(KnowledgeDocument? doc, bool active, DateTime sourceChangedAt, Func<KnowledgeDocument> create, Func<KnowledgeDocument, bool> sourceInfoChanged, Action<KnowledgeDocument> updateSource)
    {
        if (!active)
        {
            if (doc is null || doc.State == "Excluded") return 0;
            doc.Exclude();
            return 1;
        }
        if (doc is null) { db.KnowledgeDocuments.Add(create()); return 1; }
        if (doc.State == "Excluded") { doc.Restore(); updateSource(doc); return 1; }
        if (sourceInfoChanged(doc)) { updateSource(doc); return 1; }
        if (doc.State != "Stale" && (doc.IndexedAt is null || sourceChangedAt > doc.IndexedAt)) { doc.MarkStale(); return 1; }
        return 0;
    }

    /// <summary>สร้าง chunk ให้เอกสาร Stale (และลบ chunk ของเอกสาร Excluded) — คืนจำนวนเอกสารที่ประมวลผล</summary>
    public async Task<int> IndexPendingAsync(int batchSize, CancellationToken ct)
    {
        // ลบ chunk ของเอกสารที่ถูก Exclude ก่อน (ให้ค้นไม่เจอทันที)
        var excludedIds = await db.KnowledgeDocuments.Where(x => x.State == "Excluded").Select(x => x.KnowledgeDocumentId).ToListAsync(ct);
        if (excludedIds.Count > 0)
        {
            var orphanChunks = await db.KnowledgeChunks.Where(x => excludedIds.Contains(x.KnowledgeDocumentId)).ToListAsync(ct);
            if (orphanChunks.Count > 0) { db.KnowledgeChunks.RemoveRange(orphanChunks); await db.SaveChangesAsync(ct); }
        }

        var pending = await db.KnowledgeDocuments.Where(x => x.State == "Stale" && SupportedSourceTypes.Contains(x.SourceType)).OrderBy(x => x.CreatedAt).Take(batchSize).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var readAt = DateTime.UtcNow;
        var texts = new Dictionary<Guid, KnowledgeText?>();
        foreach (var (id, text) in await BuildDefectTextsAsync(pending.Where(x => x.SourceType == "Defect").ToList(), ct)) texts[id] = text;
        foreach (var (id, text) in await BuildArticleTextsAsync(pending.Where(x => x.SourceType == "Article").ToList(), ct)) texts[id] = text;

        var pendingIds = pending.Select(x => x.KnowledgeDocumentId).ToList();
        var oldChunks = (await db.KnowledgeChunks.Where(x => pendingIds.Contains(x.KnowledgeDocumentId)).ToListAsync(ct)).ToLookup(x => x.KnowledgeDocumentId);
        foreach (var doc in pending)
        {
            try
            {
                var text = texts.GetValueOrDefault(doc.KnowledgeDocumentId);
                if (text is null) { doc.Exclude(); db.KnowledgeChunks.RemoveRange(oldChunks[doc.KnowledgeDocumentId]); continue; }
                var hash = KnowledgeHash.Compute($"{IndexVersion}\n{text.Full}");
                var existing = oldChunks[doc.KnowledgeDocumentId].ToList();
                if (hash == doc.ContentHash && existing.Count > 0) { doc.MarkIndexed(hash, existing.Count, readAt); continue; }
                var pieces = KnowledgeChunkPlanner.Plan(text);
                db.KnowledgeChunks.RemoveRange(existing);
                for (var i = 0; i < pieces.Count; i++) db.KnowledgeChunks.Add(new KnowledgeChunk(doc.KnowledgeDocumentId, i, pieces[i]));
                doc.MarkIndexed(hash, pieces.Count, readAt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Index knowledge document {DocumentId} ({SourceType} {SourceKey}) failed", doc.KnowledgeDocumentId, doc.SourceType, doc.SourceKey);
                doc.MarkFailed(ex.Message, readAt);
            }
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }

    /// <summary>สร้าง chunk ใหม่ทุกเอกสาร (SYS_ADMIN สั่งจากหน้าสถานะคลัง) — คืนจำนวนเอกสาร</summary>
    public async Task<int> ForceReindexAllAsync(CancellationToken ct)
    {
        var docs = await db.KnowledgeDocuments.Where(x => x.State != "Excluded").ToListAsync(ct);
        foreach (var doc in docs) doc.ForceReindex();
        await db.SaveChangesAsync(ct);
        return docs.Count;
    }

    private async Task<Dictionary<Guid, KnowledgeText?>> BuildDefectTextsAsync(List<KnowledgeDocument> docs, CancellationToken ct)
    {
        var result = new Dictionary<Guid, KnowledgeText?>();
        if (docs.Count == 0) return result;
        var idByDoc = docs.ToDictionary(x => x.KnowledgeDocumentId, x => Guid.TryParse(x.SourceKey, out var id) ? id : Guid.Empty);
        var defectIds = idByDoc.Values.Where(x => x != Guid.Empty).Distinct().ToList();
        var defects = await db.Defects.AsNoTracking().Where(x => defectIds.Contains(x.DefectId) && !x.IsDeleted).ToDictionaryAsync(x => x.DefectId, ct);
        var projectIds = defects.Values.Select(x => x.ProjectId).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => $"{x.ProjectCode} {x.ProjectName}", ct);
        var modules = await db.Modules.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .Select(x => new { x.ModuleId, x.ParentModuleId, x.ModuleCode, x.ModuleName }).ToDictionaryAsync(x => x.ModuleId, ct);
        var releaseIds = defects.Values.Where(x => x.ReleaseId.HasValue).Select(x => x.ReleaseId!.Value).Distinct().ToList();
        var releases = await db.Releases.AsNoTracking().Where(x => releaseIds.Contains(x.ReleaseId)).ToDictionaryAsync(x => x.ReleaseId, x => $"{x.ReleaseCode} ({x.Version})", ct);
        var buildIds = defects.Values.Where(x => x.BuildId.HasValue).Select(x => x.BuildId!.Value).Distinct().ToList();
        var builds = await db.Builds.AsNoTracking().Where(x => buildIds.Contains(x.BuildId)).ToDictionaryAsync(x => x.BuildId, x => x.BuildNumber, ct);
        var comments = (await db.DefectActivities.AsNoTracking()
            .Where(x => defectIds.Contains(x.DefectId) && (x.ActionType == "Comment" || x.ActionType == "CrmComment"))
            .Select(x => new { x.DefectId, x.CreatedAt, x.Message }).ToListAsync(ct)).ToLookup(x => x.DefectId);
        var testCases = (await db.DefectTestCaseLinks.AsNoTracking().Where(x => defectIds.Contains(x.DefectId))
            .Join(db.TestCases.Where(t => !t.IsDeleted), l => l.TestCaseId, t => t.TestCaseId, (l, t) => new { l.DefectId, t.TestCaseCode, t.Title })
            .ToListAsync(ct)).ToLookup(x => x.DefectId);

        string? ModulePath(Guid? moduleId)
        {
            if (!moduleId.HasValue) return null;
            var names = new List<string>();
            var current = moduleId;
            for (var depth = 0; current.HasValue && depth < 10 && modules.TryGetValue(current.Value, out var m); depth++)
            {
                names.Insert(0, m.ModuleName);
                current = m.ParentModuleId;
            }
            return names.Count == 0 ? null : string.Join(" / ", names);
        }

        foreach (var (docId, defectId) in idByDoc)
        {
            if (!defects.TryGetValue(defectId, out var d)) { result[docId] = null; continue; }
            result[docId] = KnowledgeSourceText.ForDefect(new(
                d.DefectCode, d.Title, d.Severity, d.Status, d.CreatedAt,
                projects.GetValueOrDefault(d.ProjectId), ModulePath(d.ModuleId),
                d.ReleaseId.HasValue ? releases.GetValueOrDefault(d.ReleaseId.Value) : null,
                d.BuildId.HasValue ? builds.GetValueOrDefault(d.BuildId.Value) : null,
                d.Description, d.StepsToReproduce, d.ExpectedResult, d.ActualResult, d.CrmTicketId,
                testCases[defectId].Select(x => new DefectKnowledgeTestCase(x.TestCaseCode, x.Title)).ToList(),
                comments[defectId].Select(x => new DefectKnowledgeComment(x.CreatedAt, x.Message)).ToList()));
        }
        return result;
    }

    private async Task<Dictionary<Guid, KnowledgeText?>> BuildArticleTextsAsync(List<KnowledgeDocument> docs, CancellationToken ct)
    {
        var result = new Dictionary<Guid, KnowledgeText?>();
        if (docs.Count == 0) return result;
        var idByDoc = docs.ToDictionary(x => x.KnowledgeDocumentId, x => Guid.TryParse(x.SourceKey, out var id) ? id : Guid.Empty);
        var ids = idByDoc.Values.Distinct().ToList();
        var articles = await db.KnowledgeArticles.AsNoTracking().Where(x => ids.Contains(x.KnowledgeArticleId) && !x.IsDeleted && x.Status == "Published").ToDictionaryAsync(x => x.KnowledgeArticleId, ct);
        var projectIds = articles.Values.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => $"{x.ProjectCode} {x.ProjectName}", ct);
        foreach (var (docId, articleId) in idByDoc)
        {
            result[docId] = articles.TryGetValue(articleId, out var a)
                ? KnowledgeSourceText.ForArticle(new(a.Title, a.Tags, a.ProjectId.HasValue ? projects.GetValueOrDefault(a.ProjectId.Value) : null, a.BodyMarkdown))
                : null;
        }
        return result;
    }

    public async Task<KnowledgeStatusDto> GetStatusAsync(bool indexerEnabled, CancellationToken ct)
    {
        var groups = await db.KnowledgeDocuments.AsNoTracking().GroupBy(x => new { x.SourceType, x.State })
            .Select(g => new { g.Key.SourceType, g.Key.State, Count = g.Count(), Chunks = g.Sum(x => x.ChunkCount), Last = g.Max(x => x.IndexedAt) }).ToListAsync(ct);
        var sources = KnowledgeDocument.SourceTypes.Select(type =>
        {
            var rows = groups.Where(x => x.SourceType == type).ToList();
            int Count(string state) => rows.Where(x => x.State == state).Sum(x => x.Count);
            return new KnowledgeSourceStatusDto(type, Count("Indexed"), Count("Stale"), Count("Failed"), Count("Excluded"),
                rows.Where(x => x.State == "Indexed").Sum(x => x.Chunks), rows.Max(x => x.Last));
        }).ToList();
        var failures = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.State == "Failed").OrderByDescending(x => x.IndexedAt).Take(20)
            .Select(x => new KnowledgeFailureDto(x.KnowledgeDocumentId, x.SourceType, x.Title, x.Error, x.IndexedAt)).ToListAsync(ct);
        return new(indexerEnabled, sources, failures);
    }

    public static string DefectUrl(Guid defectId) => $"#/defects?defectId={defectId}";
    public static string ArticleUrl(Guid articleId) => $"#/knowledge?articleId={articleId}";

    private static string Cut(string value, int max) { var text = value.Trim(); return text.Length > max ? text[..max] : text; }
}

public sealed record KnowledgeSourceStatusDto(string SourceType, int Indexed, int Pending, int Failed, int Excluded, int Chunks, DateTime? LastIndexedAt);
public sealed record KnowledgeFailureDto(Guid KnowledgeDocumentId, string SourceType, string Title, string? Error, DateTime? AttemptedAt);
public sealed record KnowledgeStatusDto(bool IndexerEnabled, IReadOnlyList<KnowledgeSourceStatusDto> Sources, IReadOnlyList<KnowledgeFailureDto> Failures);

/// <summary>ปลุก KnowledgeIndexWorker ทันทีเมื่อมีการแก้บทความ/สั่ง reindex แทนการรอรอบถัดไป</summary>
public sealed class KnowledgeIndexTrigger
{
    private readonly SemaphoreSlim signal = new(0, 1);
    public void Signal() { if (signal.CurrentCount == 0) { try { signal.Release(); } catch (SemaphoreFullException) { } } }
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => signal.WaitAsync(timeout, ct);
}

/// <summary>
/// ทุก 5 นาที (Knowledge:IndexIntervalMinutes): sync แหล่งข้อมูล → index เอกสาร Stale ทีละ batch จนหมดหรือครบเวลา 2 นาทีต่อรอบ
/// ปิดได้ด้วย Knowledge:IndexerEnabled=false; รอบล้มเหลวไม่หยุด worker (รูปแบบเดียวกับ CrmSyncWorker)
/// </summary>
public sealed class KnowledgeIndexWorker(IServiceScopeFactory scopeFactory, KnowledgeIndexTrigger trigger, IConfiguration configuration, ILogger<KnowledgeIndexWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickBudget = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // รอ API พร้อมก่อน ไม่แย่ง DB ตอนเริ่มระบบ
        try { await trigger.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Knowledge:IndexIntervalMinutes", 5), 1, 60));
            if (configuration.GetValue("Knowledge:IndexerEnabled", true))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var indexer = scope.ServiceProvider.GetRequiredService<KnowledgeIndexService>();
                    await indexer.SyncSourcesAsync(stoppingToken);
                    var batch = Math.Clamp(configuration.GetValue("Knowledge:IndexBatchSize", 200), 10, 1000);
                    var started = DateTime.UtcNow;
                    int processed, total = 0;
                    do { processed = await indexer.IndexPendingAsync(batch, stoppingToken); total += processed; }
                    while (processed > 0 && DateTime.UtcNow - started < TickBudget);
                    if (total > 0) logger.LogInformation("KnowledgeIndexWorker indexed {Count} documents", total);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "KnowledgeIndexWorker tick failed — will retry in {Interval}.", interval); }
            }
            try { await trigger.WaitAsync(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
