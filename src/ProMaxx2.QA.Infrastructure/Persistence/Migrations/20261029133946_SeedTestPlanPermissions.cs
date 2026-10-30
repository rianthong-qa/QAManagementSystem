using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedTestPlanPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DatabaseInitializer ไม่ทำงานเมื่อ Database:ApplyMigrations=false — seed สิทธิ์ Test Plan ที่นี่แบบ idempotent
            // ค่าเริ่มต้นตาม TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §4.5: ASSIGN/EXPORT ให้ Role ที่มี EXECUTION.ASSIGN/REPORT.EXPORT อยู่แล้ว
            // (อ่านจากฐานข้อมูลจริง เพื่อเคารพสิทธิ์ที่ผู้ดูแลปรับไว้)
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), c.Code, c.Code, 'TESTPLAN'
                FROM (VALUES ('TESTPLAN.VIEW'), ('TESTPLAN.EDIT'), ('TESTPLAN.ASSIGN'), ('TESTPLAN.FREEZE'), ('TESTPLAN.EXPORT')) AS c(Code)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = c.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE ((p.PermissionCode = 'TESTPLAN.VIEW' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','QA_TESTER','RELEASE_OWNER','VIEWER'))
                    OR (p.PermissionCode = 'TESTPLAN.EDIT' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD'))
                    OR (p.PermissionCode = 'TESTPLAN.FREEZE' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','RELEASE_OWNER'))
                    OR (p.PermissionCode = 'TESTPLAN.ASSIGN' AND (r.RoleCode = 'SYS_ADMIN' OR EXISTS (
                        SELECT 1 FROM RolePermissions rp JOIN Permissions ep ON ep.PermissionId = rp.PermissionId
                        WHERE rp.RoleId = r.RoleId AND ep.PermissionCode = 'EXECUTION.ASSIGN')))
                    OR (p.PermissionCode = 'TESTPLAN.EXPORT' AND (r.RoleCode = 'SYS_ADMIN' OR EXISTS (
                        SELECT 1 FROM RolePermissions rp JOIN Permissions ep ON ep.PermissionId = rp.PermissionId
                        WHERE rp.RoleId = r.RoleId AND ep.PermissionCode = 'REPORT.EXPORT'))))
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.PermissionId = rp.PermissionId WHERE p.PermissionCode LIKE 'TESTPLAN.%';
                DELETE FROM Permissions WHERE PermissionCode LIKE 'TESTPLAN.%';
                """);
        }
    }
}
