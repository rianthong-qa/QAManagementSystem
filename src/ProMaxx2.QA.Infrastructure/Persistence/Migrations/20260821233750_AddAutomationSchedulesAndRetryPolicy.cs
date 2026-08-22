using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationSchedulesAndRetryPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorType",
                table: "AutomationQueueJobs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "AutomationQueueJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Pack",
                table: "AutomationQueueJobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceScheduleId",
                table: "AutomationQueueJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutomationSchedules",
                columns: table => new
                {
                    AutomationScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Pack = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RunAtUtc = table.Column<TimeOnly>(type: "time", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastQueuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSchedules", x => x.AutomationScheduleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSchedules_ProjectId_IsActive_NextRunAt",
                table: "AutomationSchedules",
                columns: new[] { "ProjectId", "IsActive", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationSchedules");

            migrationBuilder.DropColumn(
                name: "ErrorType",
                table: "AutomationQueueJobs");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "AutomationQueueJobs");

            migrationBuilder.DropColumn(
                name: "Pack",
                table: "AutomationQueueJobs");

            migrationBuilder.DropColumn(
                name: "SourceScheduleId",
                table: "AutomationQueueJobs");
        }
    }
}
