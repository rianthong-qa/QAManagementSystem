using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationConcurrencyAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutomationVersions_AutomationCaseId_VersionNo",
                table: "AutomationVersions");

            migrationBuilder.DropIndex(
                name: "IX_AutomationStepResults_AutomationExecutionId_StepNo",
                table: "AutomationStepResults");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AutomationJobs",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AutomationExecutions",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_AutomationWebhookDeliveries_Project_Request_Created",
                table: "AutomationWebhookDeliveries",
                columns: new[] { "ProjectId", "RequestId" },
                unique: true,
                filter: "[Status] = 'Created'");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationVersions_AutomationCaseId_VersionNo",
                table: "AutomationVersions",
                columns: new[] { "AutomationCaseId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationStepResults_AutomationExecutionId_StepNo",
                table: "AutomationStepResults",
                columns: new[] { "AutomationExecutionId", "StepNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AutomationWebhookDeliveries_Project_Request_Created",
                table: "AutomationWebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_AutomationVersions_AutomationCaseId_VersionNo",
                table: "AutomationVersions");

            migrationBuilder.DropIndex(
                name: "IX_AutomationStepResults_AutomationExecutionId_StepNo",
                table: "AutomationStepResults");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AutomationJobs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AutomationExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationVersions_AutomationCaseId_VersionNo",
                table: "AutomationVersions",
                columns: new[] { "AutomationCaseId", "VersionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationStepResults_AutomationExecutionId_StepNo",
                table: "AutomationStepResults",
                columns: new[] { "AutomationExecutionId", "StepNo" });
        }
    }
}
