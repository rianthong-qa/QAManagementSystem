using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// OAuth 2.1 ของ MCP server (claude.ai Team / Claude Desktop / Claude Code แบบ Login) — ดู OAuthService
/// endpoint มาตรฐานเป็น anonymous (Claude เรียกจาก cloud ของ Anthropic, ช่วง IP 160.79.104.0/21) และจำกัดอัตราด้วย policy "oauth";
/// การอนุญาตทำในหน้าเว็บ QA Hub (?oauth=) ซึ่งเรียก api/v1/oauth/requests ด้วย JWT ของผู้ใช้
/// </summary>
public sealed class OAuthController(OAuthService oauth) : ControllerBase
{
    // ---------- discovery ----------

    [HttpGet("/.well-known/oauth-protected-resource"), HttpGet("/.well-known/oauth-protected-resource/mcp"), AllowAnonymous, EnableRateLimiting("oauth")]
    public IActionResult ProtectedResource() => Json(oauth.ProtectedResourceMetadata(), cache: true);

    [HttpGet("/.well-known/oauth-authorization-server"), AllowAnonymous, EnableRateLimiting("oauth")]
    public IActionResult AuthorizationServer() => Json(oauth.AuthorizationServerMetadata(), cache: true);

    // ---------- RFC 7591 / 6749 ----------

    [HttpPost("/oauth/register"), AllowAnonymous, EnableRateLimiting("oauth")]
    public async Task<IActionResult> Register(CancellationToken ct)
    {
        JsonElement body;
        try { body = await JsonSerializer.DeserializeAsync<JsonElement>(Request.Body, cancellationToken: ct); }
        catch (JsonException) { return OAuthError(new OAuthException("invalid_client_metadata", "Body must be JSON.")); }
        try { return StatusCode(StatusCodes.Status201Created, await oauth.RegisterAsync(body, ClientIp(), ct)); }
        catch (OAuthException ex) { return OAuthError(ex); }
    }

    [HttpGet("/oauth/authorize"), AllowAnonymous, EnableRateLimiting("oauth")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var result = await oauth.AuthorizeAsync(Request.Query, ct);
        if (result.RedirectUrl is not null) return Redirect(result.RedirectUrl);
        return new ContentResult
        {
            StatusCode = StatusCodes.Status400BadRequest, ContentType = "text/html; charset=utf-8",
            Content = $"<!doctype html><html lang=\"th\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>เชื่อมต่อไม่สำเร็จ</title></head>" +
                      $"<body style=\"font-family:Tahoma,'Noto Sans Thai',sans-serif;max-width:560px;margin:10vh auto;padding:0 16px;color:#1f2937\"><h1 style=\"font-size:20px\">เชื่อมต่อ ProMaxx2 QA Hub ไม่สำเร็จ</h1>" +
                      $"<p>{WebUtility.HtmlEncode(result.ErrorPage)}</p></body></html>",
        };
    }

    [HttpPost("/oauth/token"), AllowAnonymous, EnableRateLimiting("oauth")]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        if (!Request.HasFormContentType) return OAuthError(new OAuthException("invalid_request", "Content-Type must be application/x-www-form-urlencoded."));
        try { return Json(await oauth.TokenAsync(await Request.ReadFormAsync(ct), ClientIp(), ct), cache: false); }
        catch (OAuthException ex) { return OAuthError(ex); }
    }

    // ---------- หน้ายืนยันในเว็บ (JWT) ----------

    [HttpGet("/api/v1/oauth/requests/{id}"), Authorize(Policy = "KnowledgeView")]
    public async Task<ActionResult<OAuthConsentDto>> Consent(string id, CancellationToken ct) =>
        await oauth.GetConsentAsync(id, User, ct) is { } consent ? Ok(consent) : NotFound(new ProblemDetails { Detail = "คำขอเชื่อมต่อนี้หมดอายุหรือถูกใช้ไปแล้ว — กลับไปกด Connect ใน Claude อีกครั้ง" });

    [HttpPost("/api/v1/oauth/requests/{id}/approve"), Authorize(Policy = "KnowledgeView")]
    public Task<IActionResult> Approve(string id, CancellationToken ct) => DecideAsync(id, true, ct);

    [HttpPost("/api/v1/oauth/requests/{id}/deny"), Authorize(Policy = "KnowledgeView")]
    public Task<IActionResult> Deny(string id, CancellationToken ct) => DecideAsync(id, false, ct);

    private async Task<IActionResult> DecideAsync(string id, bool approve, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId)) return Unauthorized();
        try { return Ok(await oauth.DecideAsync(id, User, userId, approve, ClientIp(), ct)); }
        catch (OAuthException ex) { return StatusCode(ex.StatusCode, new ProblemDetails { Detail = ex.Message }); }
        catch (InvalidOperationException ex) { return NotFound(new ProblemDetails { Detail = ex.Message }); }
    }

    private ObjectResult OAuthError(OAuthException ex) =>
        StatusCode(ex.StatusCode, new Dictionary<string, string> { ["error"] = ex.Error, ["error_description"] = ex.Message });

    private ObjectResult Json(object value, bool cache)
    {
        if (cache) Response.Headers.CacheControl = "public, max-age=300";
        return Ok(value);
    }

    private string? ClientIp() => Services.ClientIp.Resolve(HttpContext);
}
