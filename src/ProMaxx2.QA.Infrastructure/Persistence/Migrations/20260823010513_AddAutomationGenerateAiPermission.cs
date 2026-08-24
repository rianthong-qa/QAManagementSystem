using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationGenerateAiPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), 'AUTOMATION.GENERATEAI', 'Automation Generate AI', 'Automation'
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = 'AUTOMATION.GENERATEAI')
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE p.PermissionCode = 'AUTOMATION.GENERATEAI'
                  AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}