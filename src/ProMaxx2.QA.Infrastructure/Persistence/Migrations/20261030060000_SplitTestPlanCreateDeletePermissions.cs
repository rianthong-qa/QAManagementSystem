using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitTestPlanCreateDeletePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // แยกสร้าง/Clone (TESTPLAN.CREATE) และลบแผน (TESTPLAN.DELETE) ออกจาก TESTPLAN.EDIT — ให้ทุก Role ที่มี EDIT อยู่ตอนนี้
            // ได้ทั้งสองสิทธิ์ (อ่านจากฐานข้อมูลจริง เคารพสิทธิ์ที่ผู้ดูแลปรับไว้) ผู้ใช้เดิมจึงทำได้เท่าเดิม; idempotent
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), c.Code, c.Code, 'TESTPLAN'
                FROM (VALUES ('TESTPLAN.CREATE'), ('TESTPLAN.DELETE')) AS c(Code)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = c.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE p.PermissionCode IN ('TESTPLAN.CREATE', 'TESTPLAN.DELETE')
                  AND (r.RoleCode = 'SYS_ADMIN' OR EXISTS (
                      SELECT 1 FROM RolePermissions rp JOIN Permissions ep ON ep.PermissionId = rp.PermissionId
                      WHERE rp.RoleId = r.RoleId AND ep.PermissionCode = 'TESTPLAN.EDIT'))
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.PermissionId = rp.PermissionId WHERE p.PermissionCode IN ('TESTPLAN.CREATE', 'TESTPLAN.DELETE');
                DELETE FROM Permissions WHERE PermissionCode IN ('TESTPLAN.CREATE', 'TESTPLAN.DELETE');
                """);
        }
    }
}
