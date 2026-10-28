using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestEnvironments",
                columns: table => new
                {
                    TestEnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestEnvironments", x => x.TestEnvironmentId);
                    table.ForeignKey(
                        name: "FK_TestEnvironments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestCycles",
                columns: table => new
                {
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestSuiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CycleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CycleName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CycleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCycles", x => x.TestCycleId);
                    table.ForeignKey(
                        name: "FK_TestCycles_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "BuildId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycles_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycles_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "ReleaseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycles_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "TestEnvironmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycles_TestSuites_TestSuiteId",
                        column: x => x.TestSuiteId,
                        principalTable: "TestSuites",
                        principalColumn: "TestSuiteId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycles_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestCycleCases",
                columns: table => new
                {
                    TestCycleCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCaseRevisionNo = table.Column<int>(type: "int", nullable: false),
                    AssignedTesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExecutionOrder = table.Column<int>(type: "int", nullable: false),
                    CurrentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCycleCases", x => x.TestCycleCaseId);
                    table.ForeignKey(
                        name: "FK_TestCycleCases_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "TestCaseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestCycleCases_TestCycles_TestCycleId",
                        column: x => x.TestCycleId,
                        principalTable: "TestCycles",
                        principalColumn: "TestCycleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestCycleCases_Users_AssignedTesterUserId",
                        column: x => x.AssignedTesterUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCycleCases_AssignedTesterUserId",
                table: "TestCycleCases",
                column: "AssignedTesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycleCases_TestCaseId",
                table: "TestCycleCases",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycleCases_TestCycleId_TestCaseId",
                table: "TestCycleCases",
                columns: new[] { "TestCycleId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_BuildId",
                table: "TestCycles",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_EnvironmentId",
                table: "TestCycles",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_OwnerUserId",
                table: "TestCycles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_ProjectId_CycleCode",
                table: "TestCycles",
                columns: new[] { "ProjectId", "CycleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_ReleaseId",
                table: "TestCycles",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCycles_TestSuiteId",
                table: "TestCycles",
                column: "TestSuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TestEnvironments_ProjectId",
                table: "TestEnvironments",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestCycleCases");

            migrationBuilder.DropTable(
                name: "TestCycles");

            migrationBuilder.DropTable(
                name: "TestEnvironments");
        }
    }
}
