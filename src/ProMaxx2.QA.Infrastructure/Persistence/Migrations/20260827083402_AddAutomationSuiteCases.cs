using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationSuiteCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationSuiteCases",
                columns: table => new
                {
                    AutomationSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSuiteCases", x => new { x.AutomationSuiteId, x.AutomationCaseId });
                    table.ForeignKey(
                        name: "FK_AutomationSuiteCases_AutomationCases_AutomationCaseId",
                        column: x => x.AutomationCaseId,
                        principalTable: "AutomationCases",
                        principalColumn: "AutomationCaseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationSuiteCases_AutomationSuites_AutomationSuiteId",
                        column: x => x.AutomationSuiteId,
                        principalTable: "AutomationSuites",
                        principalColumn: "AutomationSuiteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSuiteCases_AutomationCaseId",
                table: "AutomationSuiteCases",
                column: "AutomationCaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationSuiteCases");
        }
    }
}
