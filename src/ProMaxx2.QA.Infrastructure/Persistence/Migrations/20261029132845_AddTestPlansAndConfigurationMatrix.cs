using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestPlansAndConfigurationMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestPlans",
                columns: table => new
                {
                    TestPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlanType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LifecycleStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Active"),
                    IsFrozen = table.Column<bool>(type: "bit", nullable: false),
                    FrozenAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    FrozenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CopiedFromTestPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlans", x => x.TestPlanId);
                    table.ForeignKey(
                        name: "FK_TestPlans_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlans_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "ReleaseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlans_TestPlans_CopiedFromTestPlanId",
                        column: x => x.CopiedFromTestPlanId,
                        principalTable: "TestPlans",
                        principalColumn: "TestPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlans_Users_FrozenByUserId",
                        column: x => x.FrozenByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlans_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestPlanRuns",
                columns: table => new
                {
                    TestPlanRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TestPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RunName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConfigurationKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConfigurationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlanRuns", x => x.TestPlanRunId);
                    table.ForeignKey(
                        name: "FK_TestPlanRuns_TestCycles_TestCycleId",
                        column: x => x.TestCycleId,
                        principalTable: "TestCycles",
                        principalColumn: "TestCycleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlanRuns_TestPlans_TestPlanId",
                        column: x => x.TestPlanId,
                        principalTable: "TestPlans",
                        principalColumn: "TestPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlanRuns_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestPlanRunAssignments",
                columns: table => new
                {
                    TestPlanRunAssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TestPlanRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlanRunAssignments", x => x.TestPlanRunAssignmentId);
                    table.ForeignKey(
                        name: "FK_TestPlanRunAssignments_TestPlanRuns_TestPlanRunId",
                        column: x => x.TestPlanRunId,
                        principalTable: "TestPlanRuns",
                        principalColumn: "TestPlanRunId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestPlanRunAssignments_Users_AssignedBy",
                        column: x => x.AssignedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestPlanRunAssignments_Users_TesterUserId",
                        column: x => x.TesterUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRunAssignments_AssignedBy",
                table: "TestPlanRunAssignments",
                column: "AssignedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRunAssignments_TesterUserId",
                table: "TestPlanRunAssignments",
                column: "TesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRunAssignments_TestPlanRunId_TesterUserId_Role",
                table: "TestPlanRunAssignments",
                columns: new[] { "TestPlanRunId", "TesterUserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRuns_OwnerUserId",
                table: "TestPlanRuns",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRuns_TestCycleId",
                table: "TestPlanRuns",
                column: "TestCycleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRuns_TestPlanId_ConfigurationKey",
                table: "TestPlanRuns",
                columns: new[] { "TestPlanId", "ConfigurationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRuns_TestPlanId_RunCode",
                table: "TestPlanRuns",
                columns: new[] { "TestPlanId", "RunCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_CopiedFromTestPlanId",
                table: "TestPlans",
                column: "CopiedFromTestPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_FrozenByUserId",
                table: "TestPlans",
                column: "FrozenByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_OwnerUserId",
                table: "TestPlans",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ProjectId_IsDeleted_CreatedAt",
                table: "TestPlans",
                columns: new[] { "ProjectId", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ProjectId_PlanCode",
                table: "TestPlans",
                columns: new[] { "ProjectId", "PlanCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ProjectId_ReleaseId_LifecycleStatus",
                table: "TestPlans",
                columns: new[] { "ProjectId", "ReleaseId", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ReleaseId",
                table: "TestPlans",
                column: "ReleaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestPlanRunAssignments");

            migrationBuilder.DropTable(
                name: "TestPlanRuns");

            migrationBuilder.DropTable(
                name: "TestPlans");
        }
    }
}
