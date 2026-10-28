using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "McpEnabled",
                table: "AiConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PersonalAccessTokens",
                columns: table => new
                {
                    PersonalAccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TokenPrefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalAccessTokens", x => x.PersonalAccessTokenId);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_TokenHash",
                table: "PersonalAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId_RevokedAt",
                table: "PersonalAccessTokens",
                columns: new[] { "UserId", "RevokedAt" });

            // สิทธิ์สร้าง Token เชื่อม Claude (MCP) — seed แบบ idempotent (DatabaseInitializer ไม่ทำงานเมื่อ ApplyMigrations=false)
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), 'KNOWLEDGE.MCP', 'KNOWLEDGE.MCP', 'KNOWLEDGE'
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = 'KNOWLEDGE.MCP')
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE p.PermissionCode = 'KNOWLEDGE.MCP' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','QA_TESTER')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.PermissionId = rp.PermissionId WHERE p.PermissionCode = 'KNOWLEDGE.MCP';
                DELETE FROM Permissions WHERE PermissionCode = 'KNOWLEDGE.MCP';
                """);

            migrationBuilder.DropTable(
                name: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "McpEnabled",
                table: "AiConfigurations");
        }
    }
}
