using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureSessionAndUniqueAutomationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationCaptureSessions",
                columns: table => new
                {
                    CaptureSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceMachine = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ApplicationVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationCaptureSessions", x => x.CaptureSessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjects_ProjectId_ApplicationCode_AutomationId",
                table: "AutomationObjects",
                columns: new[] { "ProjectId", "ApplicationCode", "AutomationId" },
                unique: true,
                filter: "[AutomationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationCaptureSessions_UserId_Status_ExpiresAt",
                table: "AutomationCaptureSessions",
                columns: new[] { "UserId", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationCaptureSessions");

            migrationBuilder.DropIndex(
                name: "IX_AutomationObjects_ProjectId_ApplicationCode_AutomationId",
                table: "AutomationObjects");
        }
    }
}
