using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly string[] RoleCodes = ["SYS_ADMIN","QA_LEAD","QA_TESTER","DEVELOPER","PRODUCT_OWNER","RELEASE_OWNER","VIEWER"];
    private static readonly string[] PermissionCodes = ["PROJECT.VIEW","PROJECT.EDIT","REQUIREMENT.VIEW","REQUIREMENT.EDIT","TESTCASE.VIEW","TESTCASE.EDIT","REGRESSION.VIEW","REGRESSION.MANAGE","EXECUTION.RUN","EXECUTION.ASSIGN","DEFECT.VIEW","DEFECT.CREATE","DEFECT.EDIT","DEFECT.RESOLVE","RISK.APPROVE","RELEASE.SIGNOFF","REPORT.EXPORT","ADMIN.USER","ADMIN.PERMISSION","AUTOMATION.VIEW","AUTOMATION.EDIT","AUTOMATION.VALIDATE","AUTOMATION.APPROVE","AUTOMATION.EXECUTE","AUTOMATION.MANAGE","AUTOMATION.VIEWEVIDENCE","AUTOMATION.GENERATEAI"];
    private static readonly string[] WorkloadPermissionCodes = ["QA.WORKLOAD.VIEW", "QA.MYWORK.VIEW", "QA.MYWORK.EXECUTE", "QA.ASSIGN.CREATE", "QA.ASSIGN.REASSIGN", "QA.ASSIGN.AUTO"];
    private static readonly string[] MatrixPermissionCodes = ["DASHBOARD.CREATE","DASHBOARD.DELETE","DASHBOARD.EDIT","DASHBOARD.VIEW","MYWORK.CREATE","MYWORK.DELETE","MYWORK.EDIT","MYWORK.VIEW","PROJECT.CREATE","PROJECT.DELETE","PROJECT.EDIT","PROJECT.VIEW","REQUIREMENT.CREATE","REQUIREMENT.DELETE","REQUIREMENT.EDIT","REQUIREMENT.VIEW","RTM.CREATE","RTM.DELETE","RTM.EDIT","RTM.VIEW","TESTCASE.CREATE","TESTCASE.DELETE","TESTCASE.EDIT","TESTCASE.VIEW","TESTSUITE.CREATE","TESTSUITE.DELETE","TESTSUITE.EDIT","TESTSUITE.VIEW","TESTCYCLE.CREATE","TESTCYCLE.DELETE","TESTCYCLE.EDIT","TESTCYCLE.VIEW","EXECUTION.CREATE","EXECUTION.DELETE","EXECUTION.EDIT","EXECUTION.VIEW","DEFECT.CREATE","DEFECT.DELETE","DEFECT.EDIT","DEFECT.VIEW","REGRESSION.CREATE","REGRESSION.DELETE","REGRESSION.EDIT","REGRESSION.VIEW","AUTOMATION.CREATE","AUTOMATION.DELETE","AUTOMATION.EDIT","AUTOMATION.VIEW","WORKLOAD.CREATE","WORKLOAD.DELETE","WORKLOAD.EDIT","WORKLOAD.VIEW","REPORT.CREATE","REPORT.DELETE","REPORT.EDIT","REPORT.VIEW","RISK.CREATE","RISK.DELETE","RISK.EDIT","RISK.VIEW","RELEASE.CREATE","RELEASE.DELETE","RELEASE.EDIT","RELEASE.VIEW","ADMIN.CREATE","ADMIN.DELETE","ADMIN.EDIT","ADMIN.VIEW","SETTING.CREATE","SETTING.DELETE","SETTING.EDIT","SETTING.VIEW","MONITOR.CREATE","MONITOR.DELETE","MONITOR.EDIT","MONITOR.VIEW","AUDIT.CREATE","AUDIT.DELETE","AUDIT.EDIT","AUDIT.VIEW"];
    private static readonly IReadOnlyDictionary<string,string[]> DefaultRolePermissions = new Dictionary<string,string[]>
    {
        ["SYS_ADMIN"] = PermissionCodes,
        ["QA_LEAD"] = ["PROJECT.VIEW","PROJECT.EDIT","REQUIREMENT.VIEW","REQUIREMENT.EDIT","TESTCASE.VIEW","TESTCASE.EDIT","REGRESSION.VIEW","REGRESSION.MANAGE","EXECUTION.RUN","EXECUTION.ASSIGN","DEFECT.VIEW","DEFECT.CREATE","DEFECT.EDIT","RISK.APPROVE","RELEASE.SIGNOFF","REPORT.EXPORT","AUTOMATION.VIEW","AUTOMATION.EDIT","AUTOMATION.VALIDATE","AUTOMATION.APPROVE","AUTOMATION.EXECUTE","AUTOMATION.MANAGE","AUTOMATION.VIEWEVIDENCE","AUTOMATION.GENERATEAI"],
        ["QA_TESTER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","TESTCASE.EDIT","REGRESSION.VIEW","EXECUTION.RUN","DEFECT.VIEW","DEFECT.CREATE","DEFECT.EDIT","AUTOMATION.VIEW","AUTOMATION.EDIT","AUTOMATION.EXECUTE"],
        ["DEVELOPER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","DEFECT.VIEW","DEFECT.EDIT","DEFECT.RESOLVE"],
        ["PRODUCT_OWNER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","REQUIREMENT.EDIT","DEFECT.VIEW","RISK.APPROVE","REPORT.EXPORT","AUTOMATION.VIEW"],
        ["RELEASE_OWNER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","REGRESSION.VIEW","DEFECT.VIEW","RISK.APPROVE","RELEASE.SIGNOFF","REPORT.EXPORT","AUTOMATION.VIEW"],
        ["VIEWER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","REGRESSION.VIEW","DEFECT.VIEW"]
    };

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, string? adminPassword, CancellationToken cancellationToken = default)
    {
        await using var scope=services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<QaDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        if (!await db.Roles.AnyAsync(cancellationToken)) { db.Roles.AddRange(RoleCodes.Select(x=>new Role(x,x.Replace('_',' ')))); await db.SaveChangesAsync(cancellationToken); }
        var allPermissionCodes = PermissionCodes.Concat(WorkloadPermissionCodes).Concat(MatrixPermissionCodes).Distinct().ToArray();
        if (!await db.Permissions.AnyAsync(cancellationToken)) { db.Permissions.AddRange(allPermissionCodes.Select(x=>new Permission(x,x,x.Split('.')[0]))); await db.SaveChangesAsync(cancellationToken); }
        else { var existing=await db.Permissions.Select(x=>x.PermissionCode).ToListAsync(cancellationToken); var missing=allPermissionCodes.Where(x=>!existing.Contains(x)).ToList(); if(missing.Count>0){db.Permissions.AddRange(missing.Select(x=>new Permission(x,x,x.Split('.')[0])));await db.SaveChangesAsync(cancellationToken);} }
        foreach(var mapping in DefaultRolePermissions)
        {
            var role=await db.Roles.SingleAsync(x=>x.RoleCode==mapping.Key,cancellationToken);
            var assigned=await db.RolePermissions.Where(x=>x.RoleId==role.RoleId).Select(x=>x.PermissionId).ToListAsync(cancellationToken);
            var missing=await db.Permissions.Where(x=>mapping.Value.Contains(x.PermissionCode)&&!assigned.Contains(x.PermissionId)).Select(x=>x.PermissionId).ToListAsync(cancellationToken);
            db.RolePermissions.AddRange(missing.Select(x=>new RolePermission(role.RoleId,x)));
        }
        var workloadRolePermissions = new Dictionary<string, string[]> { ["SYS_ADMIN"] = WorkloadPermissionCodes, ["QA_LEAD"] = WorkloadPermissionCodes, ["QA_TESTER"] = ["QA.MYWORK.VIEW", "QA.MYWORK.EXECUTE"] };
        foreach (var mapping in workloadRolePermissions) { var role = await db.Roles.SingleAsync(x => x.RoleCode == mapping.Key, cancellationToken); var assigned = await db.RolePermissions.Where(x => x.RoleId == role.RoleId).Select(x => x.PermissionId).ToListAsync(cancellationToken); var missing = await db.Permissions.Where(x => mapping.Value.Contains(x.PermissionCode) && !assigned.Contains(x.PermissionId)).Select(x => x.PermissionId).ToListAsync(cancellationToken); db.RolePermissions.AddRange(missing.Select(x => new RolePermission(role.RoleId, x))); }
        var adminRole=await db.Roles.SingleAsync(x=>x.RoleCode=="SYS_ADMIN",cancellationToken);
        if (!string.IsNullOrWhiteSpace(adminPassword) && !await db.Users.AnyAsync(x=>x.Username=="admin",cancellationToken))
        {
            var password=scope.ServiceProvider.GetRequiredService<IPasswordService>(); var admin=new User("admin","System Administrator",null,password.Hash(adminPassword)); db.Users.Add(admin); db.UserRoles.Add(new UserRole(admin.UserId,adminRole.RoleId));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
