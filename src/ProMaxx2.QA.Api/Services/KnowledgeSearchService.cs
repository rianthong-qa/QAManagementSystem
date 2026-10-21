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

public sealed class KnowledgeSearchService(QaDbContext db, IKnowledgeTextSearch textSearch, ProjectAccessService projectAccess, IAuthorizationService authorization)
{
    public const int MaxResults = 30;

    /// <summary>ขอบเขตสิทธิ์ของผู้ใช้ — Project ที่เป็นสมาชิก (ไม่ยกเว้น SYS_ADMIN ตามกติกา ProjectAccessFilter) และ CRM ตาม policy CrmView</summary>
    public async Task<KnowledgeSearchScope> ScopeForAsync(ClaimsPrincipal user, Guid userId, Guid? projectId, IReadOnlyCollection<string>? sourceTypes, CancellationToken ct)
    {
        var allowed = await projectAccess.GetAllowedProjectIdsAsync(userId, ct);
        var crm = (await authorization.AuthorizeAsync(user, "CrmView")).Succeeded;
        return new(allowed, crm, projectId, sourceTypes);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResultDto>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxResults);
        var hits = await textSearch.SearchAsync(query, scope, take * 4, ct);
        if (hits.Count == 0) return [];
        var chunkIds = hits.Select(x => x.KnowledgeChunkId).ToList();
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(x => chunkIds.Contains(x.KnowledgeChunkId))
            .Select(x => new { x.KnowledgeChunkId, x.KnowledgeDocumentId, x.Text }).ToDictionaryAsync(x => x.KnowledgeChunkId, ct);
        var docIds = chunks.Values.Select(x => x.KnowledgeDocumentId).Distinct().ToList();
        var docs = await db.KnowledgeDocuments.AsNoTracking().Where(x => docIds.Contains(x.KnowledgeDocumentId)).ToDictionaryAsync(x => x.KnowledgeDocumentId, ct);
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
}
