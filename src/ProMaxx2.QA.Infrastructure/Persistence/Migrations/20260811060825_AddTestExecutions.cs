using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestExecutions",
                columns: table => new
                {
                    TestExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TestCycleCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionNo = table.Column<int>(type: "int", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActualResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestExecutions", x => x.TestExecutionId);
                    table.ForeignKey(
                        name: "FK_TestExecutions_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "BuildId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestExecutions_TestCycleCases_TestCycleCaseId",
                        column: x => x.TestCycleCaseId,
                        principalTable: "TestCycleCases",
                        principalColumn: "TestCycleCaseId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestExecutions_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "TestEnvironmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestExecutions_Users_TesterUserId",
                        column: x => x.TesterUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestStepResults",
                columns: table => new
                {
                    TestStepResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TestExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActualResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestStepResults", x => x.TestStepResultId);
                    table.ForeignKey(
                        name: "FK_TestStepResults_TestExecutions_TestExecutionId",
                        column: x => x.TestExecutionId,
                        principalTable: "TestExecutions",
                        principalColumn: "TestExecutionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_BuildId",
                table: "TestExecutions",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_EnvironmentId",
                table: "TestExecutions",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TestCycleCaseId_ExecutionNo",
                table: "TestExecutions",
                columns: new[] { "TestCycleCaseId", "ExecutionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TesterUserId",
                table: "TestExecutions",
                column: "TesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestStepResults_TestExecutionId",
                table: "TestStepResults",
                column: "TestExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestStepResults");

            migrationBuilder.DropTable(
                name: "TestExecutions");
        }
    }
}
