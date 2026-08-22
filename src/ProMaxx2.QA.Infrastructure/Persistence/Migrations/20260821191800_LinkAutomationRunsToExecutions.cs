using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAutomationRunsToExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TestCycleId",
                table: "AutomationRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TestExecutionId",
                table: "AutomationRunCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_TestCycleId",
                table: "AutomationRuns",
                column: "TestCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunCases_TestExecutionId",
                table: "AutomationRunCases",
                column: "TestExecutionId",
                unique: true,
                filter: "[TestExecutionId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRunCases_TestExecutions_TestExecutionId",
                table: "AutomationRunCases",
                column: "TestExecutionId",
                principalTable: "TestExecutions",
                principalColumn: "TestExecutionId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRuns_TestCycles_TestCycleId",
                table: "AutomationRuns",
                column: "TestCycleId",
                principalTable: "TestCycles",
                principalColumn: "TestCycleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRunCases_TestExecutions_TestExecutionId",
                table: "AutomationRunCases");

            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRuns_TestCycles_TestCycleId",
                table: "AutomationRuns");

            migrationBuilder.DropIndex(
                name: "IX_AutomationRuns_TestCycleId",
                table: "AutomationRuns");

            migrationBuilder.DropIndex(
                name: "IX_AutomationRunCases_TestExecutionId",
                table: "AutomationRunCases");

            migrationBuilder.DropColumn(
                name: "TestCycleId",
                table: "AutomationRuns");

            migrationBuilder.DropColumn(
                name: "TestExecutionId",
                table: "AutomationRunCases");
        }
    }
}
