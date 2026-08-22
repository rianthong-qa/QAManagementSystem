using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationRunnerAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "AutomationQueueJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "AutomationQueueJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutomationRunnerAgents",
                columns: table => new
                {
                    AutomationRunnerAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Capabilities = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRunnerAgents", x => x.AutomationRunnerAgentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunnerAgents_ProjectId_LastHeartbeatAt",
                table: "AutomationRunnerAgents",
                columns: new[] { "ProjectId", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunnerAgents_ProjectId_RunnerName",
                table: "AutomationRunnerAgents",
                columns: new[] { "ProjectId", "RunnerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRunnerAgents");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "AutomationQueueJobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "AutomationQueueJobs");
        }
    }
}
