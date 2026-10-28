using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

// ลิงก์อ่านอย่างเดียวของบทความความรู้ (ดู KnowledgeArticleShareService) — AllowAnonymous เพราะผู้อ่านอาจไม่มีบัญชี QA Hub
// สิทธิ์มาจาก short code สุ่มที่ผูกกับบทความเดียว; คืนเฉพาะเนื้อหาที่ต้องแสดง (ไม่มี UserId / สถานะ index / ข้อมูลภายในอื่น)
[ApiController, Route("api/v1/shared/articles"), AllowAnonymous, EnableRateLimiting("share")]
public sealed class SharedArticlesController(QaDbContext db, KnowledgeArticleShareService shares) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<ActionResult<SharedArticleDto>> Get(string code, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var a = await shares.ResolveAsync(code, ct);
        if (a is null) return NotFound();
        var project = a.ProjectId is { } projectId ? await db.Projects.AsNoTracking().Where(x => x.ProjectId == projectId).Select(x => x.ProjectName).FirstOrDefaultAsync(ct) : null;
        var authorId = a.UpdatedBy ?? a.CreatedBy;
        var author = authorId is { } id ? await db.Users.AsNoTracking().Where(x => x.UserId == id).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;
        return Ok(new SharedArticleDto(a.Title, project, a.Tags, a.ContentFormat, a.ContentFormat == "Html" ? null : a.BodyMarkdown, a.BodyHtml, author, a.UpdatedAt ?? a.CreatedAt));
    }
}

public sealed record SharedArticleDto(string Title, string? ProjectName, string? Tags, string ContentFormat, string? BodyMarkdown, string? BodyHtml, string? AuthorName, DateTime UpdatedAt);
