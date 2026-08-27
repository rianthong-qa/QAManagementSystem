using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationSuiteRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RevisionNo",
                table: "AutomationSuites",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Any suite created before this migration should read as revision 1 (its creation), matching what the
            // domain constructor now sets for every new suite going forward.
            migrationBuilder.Sql("UPDATE AutomationSuites SET RevisionNo = 1 WHERE RevisionNo = 0;");

            migrationBuilder.CreateTable(
                name: "AutomationSuiteRevisions",
                columns: table => new
                {
                    AutomationSuiteRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSuiteRevisions", x => x.AutomationSuiteRevisionId);
                    table.ForeignKey(
                        name: "FK_AutomationSuiteRevisions_AutomationSuites_AutomationSuiteId",
                        column: x => x.AutomationSuiteId,
                        principalTable: "AutomationSuites",
                        principalColumn: "AutomationSuiteId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSuiteRevisions_AutomationSuiteId_RevisionNo",
                table: "AutomationSuiteRevisions",
                columns: new[] { "AutomationSuiteId", "RevisionNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationSuiteRevisions");

            migrationBuilder.DropColumn(
                name: "RevisionNo",
                table: "AutomationSuites");
        }
    }
}
