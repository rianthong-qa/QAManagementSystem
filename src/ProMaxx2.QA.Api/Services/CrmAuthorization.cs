using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProMaxx2.QA.Application.Identity;

namespace ProMaxx2.QA.Api.Services;

public sealed class CrmViewRequirement : IAuthorizationRequirement;

/// <summary>
/// Resolves CRM authorization from the current database profile as well as the JWT identity.
/// This keeps newly seeded permissions effective for an existing token without weakening the
/// requirement: the user must still have CRM.VIEW or SYS_ADMIN in the current profile.
/// </summary>
public sealed class CrmViewAuthorizationHandler(IIdentityRepository identity) : AuthorizationHandler<CrmViewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CrmViewRequirement requirement)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId)) return;

        var profile = await identity.GetProfileAsync(userId, CancellationToken.None);
        if (profile is null) return;

        if (profile.Roles.Any(x => string.Equals(x, "SYS_ADMIN", StringComparison.OrdinalIgnoreCase)) ||
            profile.Permissions.Any(x => string.Equals(x, "CRM.VIEW", StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }
    }
}
