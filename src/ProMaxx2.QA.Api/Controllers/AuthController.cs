using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Identity;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthenticationService authentication, IIdentityRepository repository) : ControllerBase
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
}
