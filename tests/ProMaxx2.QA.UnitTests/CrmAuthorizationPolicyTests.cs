using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Identity;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmAuthorizationPolicyTests
{
    [Theory]
    [InlineData("SYS_ADMIN", "")]
    [InlineData("QA_LEAD", "CRM.VIEW")]
    public async Task Policy_uses_current_profile_for_crm_access(string role, string permission)
    {
        var userId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPolicy("CrmView", policy => policy.AddRequirements(new CrmViewRequirement())));
        services.AddScoped<IAuthorizationHandler, CrmViewAuthorizationHandler>();
        services.AddScoped<IIdentityRepository>(_ => new StubIdentityRepository(new AuthenticatedUser(userId, "qa", "QA", null, [role], string.IsNullOrWhiteSpace(permission) ? [] : [permission], [])));
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth"));

        var result = await auth.AuthorizeAsync(principal, null, "CrmView");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Policy_rejects_profile_without_crm_access_even_when_token_has_unrelated_claim()
    {
        var userId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPolicy("CrmView", policy => policy.AddRequirements(new CrmViewRequirement())));
        services.AddScoped<IAuthorizationHandler, CrmViewAuthorizationHandler>();
        services.AddScoped<IIdentityRepository>(_ => new StubIdentityRepository(new AuthenticatedUser(userId, "qa", "QA", null, ["QA_TESTER"], ["PROJECT.VIEW"], [])));
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("permission", "CRM.VIEW")], "TestAuth"));

        var result = await auth.AuthorizeAsync(principal, null, "CrmView");

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("SYS_ADMIN", "", true)]
    [InlineData("QA_LEAD", "CRM.EDIT", true)]
    [InlineData("QA_LEAD", "CRM.VIEW", false)]
    public async Task Edit_policy_uses_current_profile_and_allows_sys_admin(string role, string permission, bool expected)
    {
        var userId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPolicy("CrmEdit", policy => policy.AddRequirements(new CrmEditRequirement())));
        services.AddScoped<IAuthorizationHandler, CrmEditAuthorizationHandler>();
        services.AddScoped<IIdentityRepository>(_ => new StubIdentityRepository(new AuthenticatedUser(userId, "qa", "QA", null, [role], string.IsNullOrWhiteSpace(permission) ? [] : [permission], [])));
        var provider = services.BuildServiceProvider();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        // token ไม่มี claim CRM.EDIT (เช่น SYS_ADMIN หรือได้สิทธิ์หลัง login) — ต้องตัดสินจากโปรไฟล์ปัจจุบัน
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth"));

        var result = await auth.AuthorizeAsync(principal, null, "CrmEdit");

        Assert.Equal(expected, result.Succeeded);
    }

    private sealed class StubIdentityRepository(AuthenticatedUser profile) : IIdentityRepository
    {
        public Task<ProMaxx2.QA.Domain.Identity.User?> FindByUsernameAsync(string username, CancellationToken cancellationToken) => Task.FromResult<ProMaxx2.QA.Domain.Identity.User?>(null);
        public Task<AuthenticatedUser?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(userId == profile.UserId ? profile : null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
