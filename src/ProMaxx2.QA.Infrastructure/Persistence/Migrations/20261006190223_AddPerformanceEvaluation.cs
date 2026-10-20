using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestExecutions_TesterUserId",
                table: "TestExecutions");

            migrationBuilder.CreateTable(
                name: "EmployeeProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Position = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupervisorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmploymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProbationEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_EmployeeProfiles_Users_SupervisorUserId",
                        column: x => x.SupervisorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Round = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SelfComment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SelfSubmittedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EvaluatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverallScore = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Strengths = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Improvements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NextGoals = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExtendToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MetricsSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    EmployeeComment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.EvaluationId);
                    table.ForeignKey(
                        name: "FK_Evaluations_Users_EvaluatorUserId",
                        column: x => x.EvaluatorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Evaluations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationTemplates",
                columns: table => new
                {
                    EvaluationTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationTemplates", x => x.EvaluationTemplateId);
                });

            migrationBuilder.CreateTable(
                name: "WorkLogEntries",
                columns: table => new
                {
                    WorkLogEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EffortHours = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferenceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLogEntries", x => x.WorkLogEntryId);
                    table.ForeignKey(
                        name: "FK_WorkLogEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLogEntries_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLogEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationScores",
                columns: table => new
                {
                    EvaluationScoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SelfScore = table.Column<int>(type: "int", nullable: true),
                    SelfComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManagerScore = table.Column<int>(type: "int", nullable: true),
                    ManagerComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationScores", x => x.EvaluationScoreId);
                    table.ForeignKey(
                        name: "FK_EvaluationScores_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "EvaluationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationTemplateCriteria",
                columns: table => new
                {
                    EvaluationTemplateCriterionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationTemplateCriteria", x => x.EvaluationTemplateCriterionId);
                    table.ForeignKey(
                        name: "FK_EvaluationTemplateCriteria_EvaluationTemplates_EvaluationTemplateId",
                        column: x => x.EvaluationTemplateId,
                        principalTable: "EvaluationTemplates",
                        principalColumn: "EvaluationTemplateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TesterUserId_CompletedAt",
                table: "TestExecutions",
                columns: new[] { "TesterUserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_CreatedBy_CreatedAt",
                table: "Defects",
                columns: new[] { "CreatedBy", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProfiles_EmploymentStatus",
                table: "EmployeeProfiles",
                column: "EmploymentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProfiles_SupervisorUserId",
                table: "EmployeeProfiles",
                column: "SupervisorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_EvaluatorUserId",
                table: "Evaluations",
                column: "EvaluatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_Status_DueDate",
                table: "Evaluations",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_UserId_Round_Year",
                table: "Evaluations",
                columns: new[] { "UserId", "Round", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationScores_EvaluationId",
                table: "EvaluationScores",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationTemplateCriteria_EvaluationTemplateId",
                table: "EvaluationTemplateCriteria",
                column: "EvaluationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationTemplates_Type_IsActive",
                table: "EvaluationTemplates",
                columns: new[] { "Type", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_CreatedBy",
                table: "WorkLogEntries",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_ProjectId",
                table: "WorkLogEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_UserId_WorkDate",
                table: "WorkLogEntries",
                columns: new[] { "UserId", "WorkDate" });

            // Database:ApplyMigrations=false ทำให้ DatabaseInitializer ไม่ seed permission ใหม่ — seed ที่นี่แบบ idempotent
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), c.Code, c.Code, 'PERFORMANCE'
                FROM (VALUES ('PERFORMANCE.VIEW'), ('PERFORMANCE.EDIT'), ('PERFORMANCE.CREATE'), ('PERFORMANCE.DELETE')) AS c(Code)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = c.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE ((r.RoleCode IN ('SYS_ADMIN','QA_LEAD') AND p.PermissionCode LIKE 'PERFORMANCE.%')
                    OR (r.RoleCode = 'QA_TESTER' AND p.PermissionCode = 'PERFORMANCE.VIEW'))
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeProfiles");

            migrationBuilder.DropTable(
                name: "EvaluationScores");

            migrationBuilder.DropTable(
                name: "EvaluationTemplateCriteria");

            migrationBuilder.DropTable(
                name: "WorkLogEntries");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "EvaluationTemplates");

            migrationBuilder.DropIndex(
                name: "IX_TestExecutions_TesterUserId_CompletedAt",
                table: "TestExecutions");

            migrationBuilder.DropIndex(
                name: "IX_Defects_CreatedBy_CreatedAt",
                table: "Defects");

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TesterUserId",
                table: "TestExecutions",
                column: "TesterUserId");
        }
    }
}
