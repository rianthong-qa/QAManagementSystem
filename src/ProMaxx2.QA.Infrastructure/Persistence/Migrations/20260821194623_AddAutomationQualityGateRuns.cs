using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationQualityGateRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationQualityGateRuns",
                columns: table => new
                {
                    AutomationQualityGateRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaselineBuild = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentBuild = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NewMissingCount = table.Column<int>(type: "int", nullable: false),
                    NewDuplicateCount = table.Column<int>(type: "int", nullable: false),
                    RemovedCount = table.Column<int>(type: "int", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    Messages = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationQualityGateRuns", x => x.AutomationQualityGateRunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationQualityGateRuns_ProjectId_BuildId_TargetApp_CompletedAt",
                table: "AutomationQualityGateRuns",
                columns: new[] { "ProjectId", "BuildId", "TargetApp", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationQualityGateRuns");
        }
    }
}
