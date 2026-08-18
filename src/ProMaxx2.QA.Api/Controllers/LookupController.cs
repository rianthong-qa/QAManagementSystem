using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Identity;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record UserLookupDto(Guid UserId, string DisplayName);

[ApiController, Route("api/v1/lookups"), Authorize]
public sealed class LookupController(AdministrationService service) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IReadOnlyList<UserLookupDto>> Users(CancellationToken ct) =>
        (await service.UsersAsync(ct)).Where(x => x.IsActive).Select(x => new UserLookupDto(x.UserId, x.DisplayName)).ToList();
}
