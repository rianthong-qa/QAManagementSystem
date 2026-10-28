using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationBuildTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationBuildTriggerPolicies",
                columns: table => new
                {
                    AutomationBuildTriggerPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pack = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationBuildTriggerPolicies", x => x.AutomationBuildTriggerPolicyId);
                    table.ForeignKey(
                        name: "FK_AutomationBuildTriggerPolicies_AutomationSuites_AutomationSuiteId",
                        column: x => x.AutomationSuiteId,
                        principalTable: "AutomationSuites",
                        principalColumn: "AutomationSuiteId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationBuildTriggerPolicies_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationBuildTriggerRuns",
                columns: table => new
                {
                    AutomationBuildTriggerRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationBuildTriggerPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExecutionsCreated = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationBuildTriggerRuns", x => x.AutomationBuildTriggerRunId);
                    table.ForeignKey(
                        name: "FK_AutomationBuildTriggerRuns_AutomationBuildTriggerPolicies_AutomationBuildTriggerPolicyId",
                        column: x => x.AutomationBuildTriggerPolicyId,
                        principalTable: "AutomationBuildTriggerPolicies",
                        principalColumn: "AutomationBuildTriggerPolicyId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationBuildTriggerPolicies_AutomationSuiteId",
                table: "AutomationBuildTriggerPolicies",
                column: "AutomationSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationBuildTriggerPolicies_ProjectId_Pack_IsActive",
                table: "AutomationBuildTriggerPolicies",
                columns: new[] { "ProjectId", "Pack", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationBuildTriggerRuns_AutomationBuildTriggerPolicyId_FiredAtUtc",
                table: "AutomationBuildTriggerRuns",
                columns: new[] { "AutomationBuildTriggerPolicyId", "FiredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationBuildTriggerRuns");

            migrationBuilder.DropTable(
                name: "AutomationBuildTriggerPolicies");
        }
    }
}
