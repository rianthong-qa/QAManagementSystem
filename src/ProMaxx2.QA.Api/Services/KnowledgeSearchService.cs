using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// Full-Text ภาษาไทย (FREETEXTTABLE LANGUAGE 1054) + กรองสิทธิ์ใน SQL เดียวกัน
/// ไม่เจอผลจาก Full-Text (เช่น คำสั้น/รหัสบางส่วน) → ค้นแบบ LIKE เป็นทางสำรอง
/// </summary>
public sealed class SqlKnowledgeTextSearch(QaDbContext db) : IKnowledgeTextSearch
{
    public async Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct)
    {
        var projects = JsonSerializer.Serialize(scope.AllowedProjectIds);
        var sources = JsonSerializer.Serialize(scope.SourceTypes ?? []);
        var anySource = scope.SourceTypes is null || scope.SourceTypes.Count == 0 ? 1 : 0;
        var crm = scope.IncludeCrm ? 1 : 0;
        var projectFilter = scope.ProjectId;
        var hits = await db.Database.SqlQuery<KnowledgeChunkHit>($"""
            SELECT TOP ({take}) c.KnowledgeChunkId, ft.[RANK] AS [Rank]
            FROM FREETEXTTABLE(dbo.KnowledgeChunks, [Text], {query}, LANGUAGE 1054) ft
            JOIN dbo.KnowledgeChunks c ON c.KnowledgeChunkId = ft.[KEY]
            JOIN dbo.KnowledgeDocuments d ON d.KnowledgeDocumentId = c.KnowledgeDocumentId
            WHERE d.State = 'Indexed'
              AND ((d.Scope = 'Project' AND d.ProjectId IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON({projects})))
                   OR d.Scope = 'Global' OR (d.Scope = 'Crm' AND {crm} = 1))
              AND ({projectFilter} IS NULL OR d.ProjectId = {projectFilter} OR d.Scope <> 'Project')
              AND ({anySource} = 1 OR d.SourceType IN (SELECT [value] FROM OPENJSON({sources})))
            ORDER BY ft.[RANK] DESC
            """).ToListAsync(ct);
        if (hits.Count > 0) return hits;

        var pattern = $"%{query.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%";
        return await db.Database.SqlQuery<KnowledgeChunkHit>($"""
            SELECT TOP ({take}) c.KnowledgeChunkId, 1 AS [Rank]
            FROM dbo.KnowledgeChunks c
            JOIN dbo.KnowledgeDocuments d ON d.KnowledgeDocumentId = c.KnowledgeDocumentId
            WHERE c.[Text] LIKE {pattern}
              AND d.State = 'Indexed'
              AND ((d.Scope = 'Project' AND d.ProjectId IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON({projects})))
                   OR d.Scope = 'Global' OR (d.Scope = 'Crm' AND {crm} = 1))
              AND ({projectFilter} IS NULL OR d.ProjectId = {projectFilter} OR d.Scope <> 'Project')
              AND ({anySource} = 1 OR d.SourceType IN (SELECT [value] FROM OPENJSON({sources})))
            ORDER BY d.IndexedAt DESC
            """).ToListAsync(ct);
    }
}

public sealed record KnowledgeSearchResultDto(Guid KnowledgeDocumentId, string SourceType, string SourceKey, string Title, Guid? ProjectId, string? ProjectName,
    string? SourceUrl, string Snippet, int Rank, int MatchedChunks, DateTime? IndexedAt);

/// <summary>
/// ค้นคลังความรู้: Full-Text ภาษาไทย + (ถ้าตั้งค่า Embedding) ค้นตามความหมาย รวมผลด้วย Reciprocal Rank Fusion (Phase 5)
/// ไม่มี vectorSearch / ไม่ได้ตั้งค่า / embedding ล่ม = ค้นด้วยคำอย่างเดียวเหมือนเดิม
/// </summary>
public sealed class KnowledgeSearchService(QaDbContext db, IKnowledgeTextSearch textSearch, ProjectAccessService projectAccess, IAuthorizationService authorization, IKnowledgeVectorSearch? vectorSearch = null)
{
    /// <summary>ผลค้นด้วยคำ + ตามความหมาย (เรียงตามลำดับ — DbContext เดียวกัน ห้ามรันขนาน)</summary>
    private async Task<IReadOnlyList<KnowledgeChunkHit>> HybridHitsAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct)
    {
        var textHits = await textSearch.SearchAsync(query, scope, take, ct);
        if (vectorSearch is null) return textHits;
        var vectorHits = await vectorSearch.SearchAsync(query, scope, take, ct);
        return HybridRanker.Fuse(textHits, vectorHits, take);
    }

    public const int MaxResults = 30;

    /// <summary>ขอบเขตสิทธิ์ของผู้ใช้ — Project ที่เป็นสมาชิก (ไม่ยกเว้น SYS_ADMIN ตามกติกา ProjectAccessFilter) และ CRM ตาม policy CrmView</summary>
    public async Task<KnowledgeSearchScope> ScopeForAsync(ClaimsPrincipal user, Guid userId, Guid? projectId, IReadOnlyCollection<string>? sourceTypes, CancellationToken ct)
    {
        var allowed = await projectAccess.GetAllowedProjectIdsAsync(userId, ct);
        var crm = (await authorization.AuthorizeAsync(user, "CrmView")).Succeeded;
        return new(allowed, crm, projectId, sourceTypes);
    }

    /// <param name="forAi">true = ผลลัพธ์จะถูกส่งให้ AI ภายนอก (เช่น Claude ผ่าน MCP) — ตัดเอกสารที่ติด ExcludeFromAi</param>
    public async Task<IReadOnlyList<KnowledgeSearchResultDto>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct, bool forAi = false)
    {
        take = Math.Clamp(take, 1, MaxResults);
        var hits = await HybridHitsAsync(query, scope, take * 4, ct);
        if (hits.Count == 0) return [];
        var chunkIds = hits.Select(x => x.KnowledgeChunkId).ToList();
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(x => chunkIds.Contains(x.KnowledgeChunkId))
            .Select(x => new { x.KnowledgeChunkId, x.KnowledgeDocumentId, x.Text }).ToDictionaryAsync(x => x.KnowledgeChunkId, ct);
        var docIds = chunks.Values.Select(x => x.KnowledgeDocumentId).Distinct().ToList();
        var docs = await db.KnowledgeDocuments.AsNoTracking().Where(x => docIds.Contains(x.KnowledgeDocumentId) && (!forAi || !x.ExcludeFromAi)).ToDictionaryAsync(x => x.KnowledgeDocumentId, ct);
        var projectIds = docs.Values.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => x.ProjectName, ct);

        return hits.Where(h => chunks.ContainsKey(h.KnowledgeChunkId))
            .GroupBy(h => chunks[h.KnowledgeChunkId].KnowledgeDocumentId)
            .Where(g => docs.ContainsKey(g.Key))
            .Select(g =>
            {
                var best = g.OrderByDescending(x => x.Rank).First();
                var doc = docs[g.Key];
                return new KnowledgeSearchResultDto(doc.KnowledgeDocumentId, doc.SourceType, doc.SourceKey, doc.Title, doc.ProjectId,
                    doc.ProjectId.HasValue ? projects.GetValueOrDefault(doc.ProjectId.Value) : null, doc.SourceUrl,
                    KnowledgeSnippet.Build(chunks[best.KnowledgeChunkId].Text, query), best.Rank, g.Count(), doc.IndexedAt);
            })
            .OrderByDescending(x => x.Rank).ThenByDescending(x => x.MatchedChunks)
            .Take(take).ToList();
    }

    public const int MaxContextDocuments = 6;
    public const int MaxChunksPerDocument = 2;
    public const int MaxContextChars = 7000;

    /// <summary>
    /// บริบทสำหรับผู้ช่วย AI (RAG): เลข [n] ต่อ 1 เอกสาร (รวมได้ไม่เกิน 2 ช่วงที่ตรงที่สุดของเอกสารนั้น) เรียงตามความตรง
    /// ไม่ส่งเอกสารที่ติด ExcludeFromAi และจำกัดความยาวรวม ~7,000 ตัวอักษร
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeContextChunk>> RetrieveContextAsync(string query, KnowledgeSearchScope scope, CancellationToken ct)
    {
        var hits = await HybridHitsAsync(query, scope, 40, ct);
        if (hits.Count == 0) return [];
        var chunkIds = hits.Select(x => x.KnowledgeChunkId).ToList();
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(x => chunkIds.Contains(x.KnowledgeChunkId))
            .Select(x => new { x.KnowledgeChunkId, x.KnowledgeDocumentId, x.ChunkNo, x.Text }).ToDictionaryAsync(x => x.KnowledgeChunkId, ct);
        var docIds = chunks.Values.Select(x => x.KnowledgeDocumentId).Distinct().ToList();
        var docs = await db.KnowledgeDocuments.AsNoTracking().Where(x => docIds.Contains(x.KnowledgeDocumentId) && !x.ExcludeFromAi && x.State == "Indexed")
            .ToDictionaryAsync(x => x.KnowledgeDocumentId, ct);
        var projectIds = docs.Values.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => x.ProjectName, ct);

        var result = new List<KnowledgeContextChunk>();
        var total = 0;
        var groups = hits.Where(h => chunks.TryGetValue(h.KnowledgeChunkId, out var c) && docs.ContainsKey(c.KnowledgeDocumentId))
            .GroupBy(h => chunks[h.KnowledgeChunkId].KnowledgeDocumentId)
            .OrderByDescending(g => g.Max(x => x.Rank));
        foreach (var group in groups)
        {
            if (result.Count >= MaxContextDocuments || total >= MaxContextChars) break;
            var doc = docs[group.Key];
            // ช่วงที่ตรงที่สุด แล้วเรียงตามลำดับในเอกสารเพื่อให้อ่านต่อเนื่อง; ช่วงที่สองตัด header ซ้ำออก
            var picked = group.OrderByDescending(x => x.Rank).Take(MaxChunksPerDocument).Select(x => chunks[x.KnowledgeChunkId]).OrderBy(x => x.ChunkNo).ToList();
            var text = string.Join("\n…\n", picked.Select((c, i) => i == 0 ? c.Text : StripHeader(c.Text)));
            if (total > 0 && total + text.Length > MaxContextChars) text = picked[0].Text;
            if (total > 0 && total + text.Length > MaxContextChars) break;
            total += text.Length;
            result.Add(new KnowledgeContextChunk(result.Count + 1, doc.KnowledgeDocumentId, doc.SourceType, doc.SourceKey, doc.Title,
                doc.ProjectId.HasValue ? projects.GetValueOrDefault(doc.ProjectId.Value) : null, doc.SourceUrl, text));
        }
        return result;
    }

    private static string StripHeader(string chunkText)
    {
        var newline = chunkText.IndexOf('\n');
        return newline >= 0 ? chunkText[(newline + 1)..] : chunkText;
    }
}
