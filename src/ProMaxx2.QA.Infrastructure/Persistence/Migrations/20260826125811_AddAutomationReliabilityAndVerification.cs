using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationReliabilityAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassifiedFailureType",
                table: "AutomationExecutions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassifiedRecommendation",
                table: "AutomationExecutions",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "AutomationExecutions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryOfExecutionId",
                table: "AutomationExecutions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsQuarantined",
                table: "AutomationCases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaintenanceOpenedAt",
                table: "AutomationCases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MaintenanceOwnerUserId",
                table: "AutomationCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceReason",
                table: "AutomationCases",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineExpiresAt",
                table: "AutomationCases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuarantineOwnerUserId",
                table: "AutomationCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                table: "AutomationCases",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetrySafety",
                table: "AutomationActions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unsafe");

            migrationBuilder.CreateTable(
                name: "AutomationObjectVerifications",
                columns: table => new
                {
                    AutomationObjectVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActualControlType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ActualAutomationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationObjectVerifications", x => x.AutomationObjectVerificationId);
                    table.ForeignKey(
                        name: "FK_AutomationObjectVerifications_AutomationAgents_AssignedAgentId",
                        column: x => x.AssignedAgentId,
                        principalTable: "AutomationAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationObjectVerifications_AutomationObjects_AutomationObjectId",
                        column: x => x.AutomationObjectId,
                        principalTable: "AutomationObjects",
                        principalColumn: "AutomationObjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRetryPolicySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    BackoffSeconds = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRetryPolicySettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_ClassifiedFailureType",
                table: "AutomationExecutions",
                column: "ClassifiedFailureType");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_RetryOfExecutionId",
                table: "AutomationExecutions",
                column: "RetryOfExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjectVerifications_AssignedAgentId",
                table: "AutomationObjectVerifications",
                column: "AssignedAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjectVerifications_AutomationObjectId_RequestedAt",
                table: "AutomationObjectVerifications",
                columns: new[] { "AutomationObjectId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjectVerifications_Status",
                table: "AutomationObjectVerifications",
                column: "Status");

            migrationBuilder.InsertData(
                table: "AutomationRetryPolicySettings",
                columns: new[] { "Id", "MaxAttempts", "BackoffSeconds", "Enabled", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1, 2, 30, true, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationObjectVerifications");

            migrationBuilder.DropTable(
                name: "AutomationRetryPolicySettings");

            migrationBuilder.DropIndex(
                name: "IX_AutomationExecutions_ClassifiedFailureType",
                table: "AutomationExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AutomationExecutions_RetryOfExecutionId",
                table: "AutomationExecutions");

            migrationBuilder.DropColumn(
                name: "ClassifiedFailureType",
                table: "AutomationExecutions");

            migrationBuilder.DropColumn(
                name: "ClassifiedRecommendation",
                table: "AutomationExecutions");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "AutomationExecutions");

            migrationBuilder.DropColumn(
                name: "RetryOfExecutionId",
                table: "AutomationExecutions");

            migrationBuilder.DropColumn(
                name: "IsQuarantined",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "MaintenanceOpenedAt",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "MaintenanceOwnerUserId",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "MaintenanceReason",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "QuarantineExpiresAt",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "QuarantineOwnerUserId",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "QuarantineReason",
                table: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "RetrySafety",
                table: "AutomationActions");
        }
    }
}
