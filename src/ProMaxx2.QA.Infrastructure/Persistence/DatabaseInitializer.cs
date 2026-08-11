using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly string[] RoleCodes = ["SYS_ADMIN","QA_LEAD","QA_TESTER","DEVELOPER","PRODUCT_OWNER","RELEASE_OWNER","VIEWER"];
    private static readonly string[] PermissionCodes = ["PROJECT.VIEW","PROJECT.EDIT","REQUIREMENT.VIEW","REQUIREMENT.EDIT","TESTCASE.VIEW","TESTCASE.EDIT","EXECUTION.RUN","EXECUTION.ASSIGN","DEFECT.CREATE","DEFECT.EDIT","DEFECT.RESOLVE","RISK.APPROVE","RELEASE.SIGNOFF","REPORT.EXPORT","ADMIN.USER","ADMIN.PERMISSION"];
    private static readonly IReadOnlyDictionary<string,string[]> DefaultRolePermissions = new Dictionary<string,string[]>
    {
        ["SYS_ADMIN"] = PermissionCodes,
        ["QA_LEAD"] = ["PROJECT.VIEW","PROJECT.EDIT","REQUIREMENT.VIEW","REQUIREMENT.EDIT","TESTCASE.VIEW","TESTCASE.EDIT","EXECUTION.RUN","EXECUTION.ASSIGN","DEFECT.CREATE","DEFECT.EDIT","REPORT.EXPORT"],
        ["QA_TESTER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","TESTCASE.EDIT","EXECUTION.RUN","DEFECT.CREATE","DEFECT.EDIT"],
        ["DEVELOPER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","DEFECT.EDIT","DEFECT.RESOLVE"],
        ["PRODUCT_OWNER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","REQUIREMENT.EDIT","REPORT.EXPORT"],
        ["RELEASE_OWNER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW","RELEASE.SIGNOFF","REPORT.EXPORT"],
        ["VIEWER"] = ["PROJECT.VIEW","REQUIREMENT.VIEW","TESTCASE.VIEW"]
    };

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, string? adminPassword, CancellationToken cancellationToken = default)
    {
        await using var scope=services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<QaDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        if (!await db.Roles.AnyAsync(cancellationToken)) { db.Roles.AddRange(RoleCodes.Select(x=>new Role(x,x.Replace('_',' ')))); await db.SaveChangesAsync(cancellationToken); }
        if (!await db.Permissions.AnyAsync(cancellationToken)) { db.Permissions.AddRange(PermissionCodes.Select(x=>new Permission(x,x,x.Split('.')[0]))); await db.SaveChangesAsync(cancellationToken); }
        foreach(var mapping in DefaultRolePermissions)
        {
            var role=await db.Roles.SingleAsync(x=>x.RoleCode==mapping.Key,cancellationToken);
            var assigned=await db.RolePermissions.Where(x=>x.RoleId==role.RoleId).Select(x=>x.PermissionId).ToListAsync(cancellationToken);
            var missing=await db.Permissions.Where(x=>mapping.Value.Contains(x.PermissionCode)&&!assigned.Contains(x.PermissionId)).Select(x=>x.PermissionId).ToListAsync(cancellationToken);
            db.RolePermissions.AddRange(missing.Select(x=>new RolePermission(role.RoleId,x)));
        }
        var adminRole=await db.Roles.SingleAsync(x=>x.RoleCode=="SYS_ADMIN",cancellationToken);
        if (!string.IsNullOrWhiteSpace(adminPassword) && !await db.Users.AnyAsync(x=>x.Username=="admin",cancellationToken))
        {
            var password=scope.ServiceProvider.GetRequiredService<IPasswordService>(); var admin=new User("admin","System Administrator",null,password.Hash(adminPassword)); db.Users.Add(admin); db.UserRoles.Add(new UserRole(admin.UserId,adminRole.RoleId));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
