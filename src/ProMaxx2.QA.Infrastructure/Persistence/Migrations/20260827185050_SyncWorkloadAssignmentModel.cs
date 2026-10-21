using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncWorkloadAssignmentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestCycleCaseAssignments",
                columns: table => new
                {
                    TestCycleCaseAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCycleCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCycleCaseAssignments", x => x.TestCycleCaseAssignmentId);
                    table.ForeignKey(
                        name: "FK_TestCycleCaseAssignments_TestCycleCases_TestCycleCaseId",
                        column: x => x.TestCycleCaseId,
                        principalTable: "TestCycleCases",
                        principalColumn: "TestCycleCaseId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestCycleCaseAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCycleCaseAssignments_AssignedByUserId",
                table: "TestCycleCaseAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycleCaseAssignments_TestCycleCaseId",
                table: "TestCycleCaseAssignments",
                column: "TestCycleCaseId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestCycleCaseAssignments");
        }
    }
}
