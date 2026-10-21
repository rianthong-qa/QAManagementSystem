using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record CreatePersonalAccessTokenRequest(string Name, int LifetimeDays = PersonalAccessToken.DefaultLifetimeDays);
public sealed record KnowledgeMcpInfoDto(bool Enabled, bool CanCreateToken, int DefaultLifetimeDays, int MaxLifetimeDays, int MaxActiveTokens);

/// <summary>จัดการ Personal Access Token สำหรับเชื่อม Claude ผ่าน MCP (ตัว MCP server อยู่ที่ /mcp — ดู Program.cs) — ใช้ JWT ของหน้าเว็บ</summary>
[ApiController, Route("api/v1/knowledge/mcp"), Authorize(Policy = "KnowledgeView")]
public sealed class KnowledgeMcpController(PersonalAccessTokenService tokens, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("info")]
    public async Task<KnowledgeMcpInfoDto> Info(CancellationToken ct) =>
        new(await tokens.IsMcpEnabledAsync(ct), (await authorization.AuthorizeAsync(User, "KnowledgeMcpManage")).Succeeded,
            PersonalAccessToken.DefaultLifetimeDays, PersonalAccessToken.MaxLifetimeDays, PersonalAccessToken.MaxActivePerUser);

    [HttpGet("tokens"), Authorize(Policy = "KnowledgeMcpManage")]
    public async Task<ActionResult<IReadOnlyList<PersonalAccessTokenDto>>> MyTokens(CancellationToken ct) =>
        UserId() is { } userId ? Ok(await tokens.ListAsync(userId, ct)) : Unauthorized();

    [HttpPost("tokens"), Authorize(Policy = "KnowledgeMcpManage")]
    public async Task<ActionResult<CreatedPersonalAccessTokenDto>> Create(CreatePersonalAccessTokenRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        try { return Ok(await tokens.CreateAsync(userId, request.Name, request.LifetimeDays, ClientIp(), ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Detail = ex.Message }); }
    }

    [HttpDelete("tokens/{id:guid}"), Authorize(Policy = "KnowledgeMcpManage")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return await tokens.RevokeAsync(userId, id, asAdmin: false, ClientIp(), ct) ? NoContent() : NotFound();
    }

    /// <summary>SYS_ADMIN: token ของทุกคน (ยกเลิกแทนได้ เช่น พนักงานลาออก/token หลุด)</summary>
    [HttpGet("tokens/all"), Authorize(Policy = "SysAdminOnly")]
    public Task<IReadOnlyList<PersonalAccessTokenDto>> AllTokens(CancellationToken ct) => tokens.ListAsync(null, ct);

    [HttpDelete("tokens/all/{id:guid}"), Authorize(Policy = "SysAdminOnly")]
    public async Task<IActionResult> RevokeAny(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return await tokens.RevokeAsync(userId, id, asAdmin: true, ClientIp(), ct) ? NoContent() : NotFound();
    }

    private string? ClientIp() => Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
