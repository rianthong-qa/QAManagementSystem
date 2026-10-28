using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationDataSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationDataSeedScripts",
                columns: table => new
                {
                    AutomationDataSeedScriptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DbKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SqlScript = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationDataSeedScripts", x => x.AutomationDataSeedScriptId);
                    table.ForeignKey(
                        name: "FK_AutomationDataSeedScripts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationDataSeedRuns",
                columns: table => new
                {
                    AutomationDataSeedRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationDataSeedScriptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowsAffected = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationDataSeedRuns", x => x.AutomationDataSeedRunId);
                    table.ForeignKey(
                        name: "FK_AutomationDataSeedRuns_AutomationDataSeedScripts_AutomationDataSeedScriptId",
                        column: x => x.AutomationDataSeedScriptId,
                        principalTable: "AutomationDataSeedScripts",
                        principalColumn: "AutomationDataSeedScriptId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationDataSeedRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedRuns_AutomationDataSeedScriptId",
                table: "AutomationDataSeedRuns",
                column: "AutomationDataSeedScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedRuns_ProjectId_RequestedAt",
                table: "AutomationDataSeedRuns",
                columns: new[] { "ProjectId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedRuns_Status",
                table: "AutomationDataSeedRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedScripts_ProjectId_IsActive",
                table: "AutomationDataSeedScripts",
                columns: new[] { "ProjectId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationDataSeedRuns");

            migrationBuilder.DropTable(
                name: "AutomationDataSeedScripts");
        }
    }
}
