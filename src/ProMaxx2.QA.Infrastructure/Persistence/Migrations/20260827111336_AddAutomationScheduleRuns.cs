using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationScheduleRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRunAtUtc",
                table: "AutomationSuiteSchedules",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AutomationScheduleRuns",
                columns: table => new
                {
                    AutomationScheduleRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExecutionsCreated = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationScheduleRuns", x => x.AutomationScheduleRunId);
                    table.ForeignKey(
                        name: "FK_AutomationScheduleRuns_AutomationSuiteSchedules_AutomationScheduleId",
                        column: x => x.AutomationScheduleId,
                        principalTable: "AutomationSuiteSchedules",
                        principalColumn: "AutomationScheduleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationScheduleRuns_AutomationScheduleId_FiredAtUtc",
                table: "AutomationScheduleRuns",
                columns: new[] { "AutomationScheduleId", "FiredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationScheduleRuns");

            migrationBuilder.DropColumn(
                name: "LastRunAtUtc",
                table: "AutomationSuiteSchedules");
        }
    }
}
