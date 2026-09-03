using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthenticationService authentication, IIdentityRepository repository, CrmConfigurationService crmConfiguration) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await authentication.LoginAsync(request, cancellationToken)); }
        catch (InvalidCredentialsException ex) { return Unauthorized(new ProblemDetails { Title="เข้าสู่ระบบไม่สำเร็จ", Detail=ex.Message, Status=StatusCodes.Status401Unauthorized }); }
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticatedUser>> Me(CancellationToken cancellationToken)
    {
        var subject=User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if(!Guid.TryParse(subject,out var userId)) return Unauthorized();
        var profile=await repository.GetProfileAsync(userId,cancellationToken);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    // CRM login เป็น self-service ต่อ user (CRM แยกงานตามคนที่ login จริง — ไม่ใช่ Service Account กลางอีกต่อไป)
    // ทุกคนจัดการบัญชี CRM ของตัวเองเท่านั้น ไม่ต้องมี Policy="AdminUser" เหมือน /master-settings/*
    [HttpGet("me/crm")]
    [Authorize]
    public async Task<ActionResult<CrmConfigurationView>> MyCrmConfiguration(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        return Ok(await crmConfiguration.GetViewAsync(userId.Value, ct));
    }

    [HttpPut("me/crm")]
    [Authorize]
    public async Task<ActionResult<CrmConfigurationView>> SaveMyCrmConfiguration(SaveMyCrmConfigurationRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        try { return Ok(await crmConfiguration.SaveAsync(userId.Value, request.MerchantId, request.Username, request.Password, request.IsEnabled, request.ClearPassword, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    private Guid? CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(subject, out var id) ? id : null;
    }
}

public sealed record SaveMyCrmConfigurationRequest(string MerchantId, string Username, string? Password, bool IsEnabled = true, bool ClearPassword = false);
