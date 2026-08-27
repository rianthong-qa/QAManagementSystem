using Microsoft.AspNetCore.Authorization;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// Registers the Automation module's authorization policies. Extracted out of Program.cs so tests can build the
/// exact same policy set in-memory (via <c>AddAuthorizationBuilder().AddAutomationPolicies()</c> on a bare
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollection"/>) without needing a hosted server —
/// there is a single source of truth for what each policy requires.
/// </summary>
public static class AutomationAuthorizationPolicies
{
    public static AuthorizationBuilder AddAutomationPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy("AutomationView", p => p.RequireClaim("permission", "AUTOMATION.VIEW"))
        .AddPolicy("AutomationEdit", p => p.RequireClaim("permission", "AUTOMATION.EDIT"))
        .AddPolicy("AutomationValidate", p => p.RequireClaim("permission", "AUTOMATION.VALIDATE"))
        .AddPolicy("AutomationApprove", p => p.RequireClaim("permission", "AUTOMATION.APPROVE"))
        .AddPolicy("AutomationExecute", p => p.RequireAssertion(c => c.User.IsInRole("SYS_ADMIN") || c.User.HasClaim("permission", "AUTOMATION.EXECUTE") || c.User.HasClaim("permission", "EXECUTION.RUN")))
        .AddPolicy("AutomationManage", p => p.RequireClaim("permission", "AUTOMATION.MANAGE"))
        .AddPolicy("AutomationGenerateAi", p => p.RequireClaim("permission", "AUTOMATION.GENERATEAI"))
        .AddPolicy("AutomationEvidence", p => p.RequireClaim("permission", "AUTOMATION.VIEWEVIDENCE"));
}
