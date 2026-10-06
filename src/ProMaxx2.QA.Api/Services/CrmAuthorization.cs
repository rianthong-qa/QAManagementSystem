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
        if (await CrmProfilePermission.HasAsync(identity, context.User, "CRM.VIEW")) context.Succeed(requirement);
    }
}

public sealed class CrmEditRequirement : IAuthorizationRequirement;

/// <summary>
/// CRM.EDIT แบบเดียวกับ CRM.VIEW: อ่านจากโปรไฟล์ปัจจุบันในฐานข้อมูล และนับ SYS_ADMIN — ตรงกับเงื่อนไขที่หน้าเว็บใช้แสดงปุ่ม
/// (`can("CRM.EDIT")`); เดิม policy เช็กแค่ claim ใน JWT จึงตอบ 403 กับ SYS_ADMIN และผู้ที่เพิ่งได้สิทธิ์หลัง login
/// </summary>
public sealed class CrmEditAuthorizationHandler(IIdentityRepository identity) : AuthorizationHandler<CrmEditRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CrmEditRequirement requirement)
    {
        if (await CrmProfilePermission.HasAsync(identity, context.User, "CRM.EDIT")) context.Succeed(requirement);
    }
}

internal static class CrmProfilePermission
{
    public static async Task<bool> HasAsync(IIdentityRepository identity, ClaimsPrincipal user, string permission)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId)) return false;

        var profile = await identity.GetProfileAsync(userId, CancellationToken.None);
        if (profile is null) return false;

        return profile.Roles.Any(x => string.Equals(x, "SYS_ADMIN", StringComparison.OrdinalIgnoreCase)) ||
               profile.Permissions.Any(x => string.Equals(x, permission, StringComparison.OrdinalIgnoreCase));
    }
}
