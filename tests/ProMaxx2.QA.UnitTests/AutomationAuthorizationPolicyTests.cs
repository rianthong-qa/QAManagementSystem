using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Covers AUT-TEST-008 (Automation permission tests): View/Edit/Validate/Approve/Execute/Manage/Evidence/GenerateAI.
/// Builds the exact same policy set the API registers — via the shared
/// <see cref="AutomationAuthorizationPolicies.AddAutomationPolicies"/> extension (extracted out of Program.cs for
/// this purpose) — on a bare <see cref="ServiceCollection"/> with <c>AddAuthorizationCore</c>, so this runs the real
/// <see cref="IAuthorizationService"/> evaluation without needing a hosted server (no WebApplicationFactory/
/// Mvc.Testing). Controller-level wiring (which endpoint uses which policy) and the separate
/// <c>[RequireProjectAccess]</c> filter are not covered here — see the AUT-TEST-007 note for that boundary.
/// </summary>
public sealed class AutomationAuthorizationPolicyTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => { }); // registers IAuthorizationService/IAuthorizationPolicyProvider
        services.AddAuthorizationBuilder().AddAutomationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "TestAuth"));

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity()); // no authentication type => unauthenticated

    public static IEnumerable<object[]> SimpleClaimPolicies =>
    [
        ["AutomationView", "AUTOMATION.VIEW"],
        ["AutomationEdit", "AUTOMATION.EDIT"],
        ["AutomationValidate", "AUTOMATION.VALIDATE"],
        ["AutomationApprove", "AUTOMATION.APPROVE"],
        ["AutomationManage", "AUTOMATION.MANAGE"],
        ["AutomationGenerateAi", "AUTOMATION.GENERATEAI"],
        ["AutomationEvidence", "AUTOMATION.VIEWEVIDENCE"],
    ];

    [Theory]
    [MemberData(nameof(SimpleClaimPolicies))]
    public async Task Policy_succeeds_with_the_exact_required_permission_claim(string policy, string requiredClaim)
    {
        var auth = BuildAuthorizationService();
        var principal = PrincipalWith(("permission", requiredClaim));

        var result = await auth.AuthorizeAsync(principal, null, policy);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(SimpleClaimPolicies))]
    public async Task Policy_fails_with_no_claims_at_all(string policy, string _)
    {
        var auth = BuildAuthorizationService();

        var result = await auth.AuthorizeAsync(Anonymous, null, policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(SimpleClaimPolicies))]
    public async Task Policy_fails_with_an_unrelated_automation_permission_no_cross_privilege_escalation(string policy, string requiredClaim)
    {
        var auth = BuildAuthorizationService();
        // A user holding some other Automation permission (but not this one) must not pass.
        var otherClaim = requiredClaim == "AUTOMATION.VIEW" ? "AUTOMATION.MANAGE" : "AUTOMATION.VIEW";
        var principal = PrincipalWith(("permission", otherClaim));

        var result = await auth.AuthorizeAsync(principal, null, policy);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("AUTOMATION.EXECUTE")]
    [InlineData("EXECUTION.RUN")]
    public async Task Automation_execute_succeeds_via_either_of_its_two_permission_claims(string claimValue)
    {
        var auth = BuildAuthorizationService();
        var principal = PrincipalWith(("permission", claimValue));

        var result = await auth.AuthorizeAsync(principal, null, "AutomationExecute");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Automation_execute_succeeds_for_sys_admin_role_even_without_the_permission_claim()
    {
        var auth = BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SYS_ADMIN")], "TestAuth"));

        var result = await auth.AuthorizeAsync(principal, null, "AutomationExecute");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Automation_execute_fails_with_an_unrelated_permission_and_no_sys_admin_role()
    {
        var auth = BuildAuthorizationService();
        var principal = PrincipalWith(("permission", "AUTOMATION.MANAGE"));

        var result = await auth.AuthorizeAsync(principal, null, "AutomationExecute");

        Assert.False(result.Succeeded);
    }
}
