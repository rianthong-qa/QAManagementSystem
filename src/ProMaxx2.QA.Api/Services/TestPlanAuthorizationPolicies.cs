using Microsoft.AspNetCore.Authorization;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// Policy ของ Test Plan (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §4.5) — แยกจาก Program.cs ให้ test สร้างชุด policy เดียวกันได้
/// การบันทึกผลยังใช้ EXECUTION.RUN เดิม; Close/Cancel/Reopen/Freeze/Unfreeze ใช้ TESTPLAN.FREEZE
/// </summary>
public static class TestPlanAuthorizationPolicies
{
    public static AuthorizationBuilder AddTestPlanPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy("TestPlanView", p => p.RequireAssertion(c => c.User.IsInRole("SYS_ADMIN") || c.User.HasClaim("permission", "TESTPLAN.VIEW")))
        .AddPolicy("TestPlanEdit", p => p.RequireAssertion(c => c.User.IsInRole("SYS_ADMIN") || c.User.HasClaim("permission", "TESTPLAN.EDIT")))
        .AddPolicy("TestPlanAssign", p => p.RequireAssertion(c => c.User.IsInRole("SYS_ADMIN") || c.User.HasClaim("permission", "TESTPLAN.ASSIGN")))
        .AddPolicy("TestPlanFreeze", p => p.RequireAssertion(c => c.User.IsInRole("SYS_ADMIN") || c.User.HasClaim("permission", "TESTPLAN.FREEZE")));
}
