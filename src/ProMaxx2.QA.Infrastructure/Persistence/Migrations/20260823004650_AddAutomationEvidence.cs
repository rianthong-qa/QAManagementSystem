using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationEvidences",
                columns: table => new
                {
                    AutomationEvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: true),
                    EvidenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CapturedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationEvidences", x => x.AutomationEvidenceId);
                    table.ForeignKey(
                        name: "FK_AutomationEvidences_AutomationExecutions_AutomationExecutionId",
                        column: x => x.AutomationExecutionId,
                        principalTable: "AutomationExecutions",
                        principalColumn: "AutomationExecutionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationEvidences_AutomationExecutionId_EvidenceType",
                table: "AutomationEvidences",
                columns: new[] { "AutomationExecutionId", "EvidenceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationEvidences");
        }
    }
}
