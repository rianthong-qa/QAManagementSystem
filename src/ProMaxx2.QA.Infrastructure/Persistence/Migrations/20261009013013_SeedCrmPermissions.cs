using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedCrmPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CRM.VIEW / CRM.EDIT เคย seed เฉพาะใน DatabaseInitializer ซึ่งไม่ทำงานเมื่อ Database:ApplyMigrations=false
            // ฐานข้อมูลจริงจึงไม่มีสิทธิ์ CRM ให้กำหนดในหน้า User / Role — seed ที่นี่แบบ idempotent พร้อมค่าเริ่มต้นเดียวกับ DatabaseInitializer
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), c.Code, c.Code, 'CRM'
                FROM (VALUES ('CRM.VIEW'), ('CRM.EDIT')) AS c(Code)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = c.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE ((p.PermissionCode = 'CRM.VIEW' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','QA_TESTER','DEVELOPER','PRODUCT_OWNER','RELEASE_OWNER','VIEWER'))
                    OR (p.PermissionCode = 'CRM.EDIT' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','QA_TESTER','DEVELOPER')))
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
