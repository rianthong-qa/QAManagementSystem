using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationDataSeedScriptApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "AutomationDataSeedScripts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "AutomationDataSeedScripts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "AutomationDataSeedScripts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "AutomationDataSeedScripts",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "AutomationDataSeedScripts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "AutomationDataSeedScripts");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "AutomationDataSeedScripts");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "AutomationDataSeedScripts");
        }
    }
}
