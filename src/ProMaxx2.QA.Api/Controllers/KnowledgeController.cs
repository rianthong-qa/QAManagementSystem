using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record KnowledgeArticleDto(Guid KnowledgeArticleId, string Title, string? BodyMarkdown, string Excerpt, Guid? ProjectId, string? ProjectName, string? Tags, string Status,
    DateTime CreatedAt, string? CreatedByName, DateTime? UpdatedAt, string? UpdatedByName, string? IndexState, DateTime? IndexedAt);
public sealed record SaveKnowledgeArticleRequest(string Title, string BodyMarkdown, Guid? ProjectId, string? Tags, string Status = "Draft");

/// <summary>
/// คลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md): ค้นหา, บทความ, สถานะการ index
/// ไม่ใช้ [RequireProjectAccess] เพราะผู้ใช้ที่ไม่มี Project ยังต้องเห็นความรู้กลาง — ตรวจสิทธิ์ Project เองด้วย ProjectAccessService (ไม่ยกเว้น "allowed ว่าง = เห็นหมด")
/// </summary>
[ApiController, Route("api/v1/knowledge"), Authorize(Policy = "KnowledgeView")]
public sealed class KnowledgeController(QaDbContext db, KnowledgeSearchService search, KnowledgeIndexService indexer, KnowledgeIndexTrigger trigger,
    ProjectAccessService projectAccess, IAuthorizationService authorization, IConfiguration configuration, KnowledgeFileService files) : ControllerBase
{
    private const string AuditEntityType = "KnowledgeArticle";

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<KnowledgeSearchResultDto>>> Search([FromQuery] string? q, [FromQuery] Guid? projectId, [FromQuery] string? sources, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (UserId() is not { } userId) return Unauthorized();
        var query = KnowledgeQuery.Normalize(q);
        if (query is null) return BadRequest(new ProblemDetails { Detail = $"กรุณาพิมพ์คำค้นอย่างน้อย {KnowledgeQuery.MinLength} ตัวอักษร" });
        var sourceTypes = string.IsNullOrWhiteSpace(sources) ? null
            : sources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => KnowledgeDocument.SourceTypes.Contains(x)).ToArray();
        var scope = await search.ScopeForAsync(User, userId, projectId, sourceTypes, ct);
        if (projectId.HasValue && !scope.AllowedProjectIds.Contains(projectId.Value)) return Forbid();
        return Ok(await search.SearchAsync(query, scope, take, ct));
    }

    [HttpGet("articles")]
    public async Task<ActionResult<IReadOnlyList<KnowledgeArticleDto>>> ListArticles([FromQuery] Guid? projectId, [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        var allowed = await projectAccess.GetAllowedProjectIdsAsync(userId, ct);
        var canEdit = await CanEditAsync();
        var q = db.KnowledgeArticles.AsNoTracking().Where(x => !x.IsDeleted && (x.ProjectId == null || allowed.Contains(x.ProjectId.Value)));
        // ฉบับร่างเห็นเฉพาะผู้มีสิทธิ์แก้ไขคลังความรู้
        if (!canEdit) q = q.Where(x => x.Status == "Published");
        else if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        if (projectId.HasValue) q = q.Where(x => x.ProjectId == projectId || x.ProjectId == null);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim(); q = q.Where(x => x.Title.Contains(s) || (x.Tags != null && x.Tags.Contains(s))); }
        var rows = await q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(500).ToListAsync(ct);
        return Ok(await MapAsync(rows, includeBody: false, ct));
    }

    [HttpGet("articles/{id:guid}")]
    public async Task<ActionResult<KnowledgeArticleDto>> GetArticle(Guid id, CancellationToken ct)
    {
        var article = await FindVisibleArticleAsync(id, ct);
        if (article is null) return NotFound();
        return Ok((await MapAsync([article], includeBody: true, ct))[0]);
    }

    [HttpPost("articles"), Authorize(Policy = "KnowledgeEdit")]
    public async Task<ActionResult<KnowledgeArticleDto>> CreateArticle(SaveKnowledgeArticleRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        if (await ValidateProjectAsync(userId, request.ProjectId, ct) is { } problem) return problem;
        KnowledgeArticle article;
        try { article = new KnowledgeArticle(request.Title, request.BodyMarkdown, request.ProjectId, request.Tags, request.Status, userId); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
        db.KnowledgeArticles.Add(article);
        db.AuditLogs.Add(new AuditLog(userId, "Created", AuditEntityType, article.KnowledgeArticleId.ToString(), $"สร้างบทความความรู้ \"{article.Title}\" ({article.Status})", null, Snapshot(article), ClientIp()));
        await db.SaveChangesAsync(ct);
        trigger.Signal();
        return Ok((await MapAsync([article], includeBody: true, ct))[0]);
    }

    [HttpPut("articles/{id:guid}"), Authorize(Policy = "KnowledgeEdit")]
    public async Task<ActionResult<KnowledgeArticleDto>> UpdateArticle(Guid id, SaveKnowledgeArticleRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        var visible = await FindVisibleArticleAsync(id, ct);
        if (visible is null) return NotFound();
        if (await ValidateProjectAsync(userId, request.ProjectId, ct) is { } problem) return problem;
        var article = await db.KnowledgeArticles.SingleAsync(x => x.KnowledgeArticleId == id, ct);
        var before = Snapshot(article);
        try { article.Update(request.Title, request.BodyMarkdown, request.ProjectId, request.Tags, request.Status, userId); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
        db.AuditLogs.Add(new AuditLog(userId, "Updated", AuditEntityType, id.ToString(), $"แก้ไขบทความความรู้ \"{article.Title}\" ({article.Status})", before, Snapshot(article), ClientIp()));
        await db.SaveChangesAsync(ct);
        trigger.Signal();
        return Ok((await MapAsync([article], includeBody: true, ct))[0]);
    }

    [HttpDelete("articles/{id:guid}"), Authorize(Policy = "KnowledgeEdit")]
    public async Task<IActionResult> DeleteArticle(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        if (await FindVisibleArticleAsync(id, ct) is null) return NotFound();
        var article = await db.KnowledgeArticles.SingleAsync(x => x.KnowledgeArticleId == id, ct);
        article.Delete(userId);
        db.AuditLogs.Add(new AuditLog(userId, "Deleted", AuditEntityType, id.ToString(), $"ลบบทความความรู้ \"{article.Title}\"", Snapshot(article), null, ClientIp()));
        await db.SaveChangesAsync(ct);
        trigger.Signal();
        return NoContent();
    }

    // ---------- เอกสาร (Phase 3) ----------

    [HttpGet("files")]
    public async Task<ActionResult<IReadOnlyList<KnowledgeFileDto>>> ListFiles([FromQuery] Guid? projectId, [FromQuery] string? search, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return Ok(await files.ListAsync(await projectAccess.GetAllowedProjectIdsAsync(userId, ct), projectId, search, ct));
    }

    /// <summary>multipart: files (1–10), projectId (ว่าง = ความรู้กลาง), description</summary>
    [HttpPost("files"), Authorize(Policy = "KnowledgeEdit"), RequestSizeLimit(60_000_000), RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeFileUploadResult>>> UploadFiles([FromForm] Guid? projectId, [FromForm] string? description, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        if (await ValidateProjectAsync(userId, projectId, ct) is { } problem) return problem;
        var incoming = new List<IncomingKnowledgeFile>();
        foreach (var file in Request.Form.Files)
        {
            if (file.Length > KnowledgeFileService.MaxFileBytes) return BadRequest(new ProblemDetails { Detail = $"ไฟล์ {file.FileName} ใหญ่เกิน 20 MB" });
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            incoming.Add(new(file.FileName, memory.ToArray()));
        }
        try { return Ok(await files.UploadAsync(incoming, projectId, description, userId, ClientIp(), ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    [HttpGet("files/{id:guid}/download")]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        var file = await files.OpenAsync(id, await projectAccess.GetAllowedProjectIdsAsync(userId, ct), ct);
        if (file is null) return NotFound();
        return PhysicalFile(file.Value.Path, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("files/{id:guid}"), Authorize(Policy = "KnowledgeEdit")]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return await files.DeleteAsync(id, await projectAccess.GetAllowedProjectIdsAsync(userId, ct), userId, ClientIp(), ct) ? NoContent() : NotFound();
    }

    /// <summary>นำเข้าเอกสาร .md/.txt จากโฟลเดอร์ระบบ (config Knowledge:SystemDocumentFolders, ค่าเริ่มต้น Document/ProMaxx2_MD) เป็นความรู้กลาง</summary>
    [HttpPost("files/import-system"), Authorize(Policy = "SysAdminOnly")]
    public async Task<ActionResult<KnowledgeImportResult>> ImportSystemDocuments(CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return Ok(await files.ImportSystemDocumentsAsync(userId, ClientIp(), ct));
    }

    [HttpGet("status"), Authorize(Policy = "SysAdminOnly")]
    public Task<KnowledgeStatusDto> Status(CancellationToken ct) => indexer.GetStatusAsync(configuration.GetValue("Knowledge:IndexerEnabled", true), ct);

    [HttpPost("reindex"), Authorize(Policy = "SysAdminOnly")]
    public async Task<ActionResult<object>> Reindex(CancellationToken ct)
    {
        var count = await indexer.ForceReindexAllAsync(ct);
        db.AuditLogs.Add(new AuditLog(UserId(), "Updated", "KnowledgeIndex", "all", $"สั่งสร้างดัชนีคลังความรู้ใหม่ {count} เอกสาร", null, null, ClientIp()));
        await db.SaveChangesAsync(ct);
        trigger.Signal();
        return Ok(new { documents = count });
    }

    private async Task<KnowledgeArticle?> FindVisibleArticleAsync(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return null;
        var article = await db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(x => x.KnowledgeArticleId == id && !x.IsDeleted, ct);
        if (article is null) return null;
        if (article.ProjectId.HasValue && !await projectAccess.HasAccessAsync(userId, article.ProjectId.Value, ct)) return null;
        if (article.Status != "Published" && !await CanEditAsync()) return null;
        return article;
    }

    private async Task<ActionResult?> ValidateProjectAsync(Guid userId, Guid? projectId, CancellationToken ct)
    {
        if (!projectId.HasValue) return null;
        if (!await db.Projects.AnyAsync(x => x.ProjectId == projectId && x.IsActive, ct)) return BadRequest(new ProblemDetails { Detail = "ไม่พบ Project ที่เลือก หรือ Project ถูกปิดใช้งาน" });
        return await projectAccess.HasAccessAsync(userId, projectId.Value, ct) ? null : Forbid();
    }

    private async Task<IReadOnlyList<KnowledgeArticleDto>> MapAsync(IReadOnlyList<KnowledgeArticle> articles, bool includeBody, CancellationToken ct)
    {
        var projectIds = articles.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, x => x.ProjectName, ct);
        var userIds = articles.SelectMany(x => new[] { x.CreatedBy, x.UpdatedBy }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var keys = articles.Select(x => x.KnowledgeArticleId.ToString()).ToList();
        var docs = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.SourceType == "Article" && keys.Contains(x.SourceKey))
            .ToDictionaryAsync(x => x.SourceKey, x => new { x.State, x.IndexedAt }, ct);
        return articles.Select(a =>
        {
            var doc = docs.GetValueOrDefault(a.KnowledgeArticleId.ToString());
            var plain = TextChunker.Normalize(a.BodyMarkdown).Replace('\n', ' ');
            return new KnowledgeArticleDto(a.KnowledgeArticleId, a.Title, includeBody ? a.BodyMarkdown : null, plain.Length > 220 ? plain[..220] + "…" : plain,
                a.ProjectId, a.ProjectId.HasValue ? projects.GetValueOrDefault(a.ProjectId.Value) : null, a.Tags, a.Status,
                a.CreatedAt, a.CreatedBy.HasValue ? users.GetValueOrDefault(a.CreatedBy.Value) : null,
                a.UpdatedAt, a.UpdatedBy.HasValue ? users.GetValueOrDefault(a.UpdatedBy.Value) : null,
                doc?.State, doc?.IndexedAt);
        }).ToList();
    }

    private async Task<bool> CanEditAsync() => (await authorization.AuthorizeAsync(User, "KnowledgeEdit")).Succeeded;
    private static string Snapshot(KnowledgeArticle a) => JsonSerializer.Serialize(new { a.Title, a.ProjectId, a.Tags, a.Status, BodyLength = a.BodyMarkdown.Length });
    private string? ClientIp() => Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
