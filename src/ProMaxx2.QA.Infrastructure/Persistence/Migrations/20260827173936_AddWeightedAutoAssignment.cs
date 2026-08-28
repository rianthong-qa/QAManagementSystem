using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightedAutoAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlgorithmVersion",
                table: "TestCycleCases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "AssignmentVersion",
                table: "TestCycleCases",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "CaseWeight",
                table: "TestCycleCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedMinutesSnapshot",
                table: "TestCycleCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillLevelSnapshot",
                table: "TestCycleCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ComplexityWeight",
                table: "TestCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedMinutes",
                table: "TestCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCritical",
                table: "TestCases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RequiredSkillLevel",
                table: "TestCases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewerRequired",
                table: "TestCases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AssignmentHistories",
                columns: table => new
                {
                    AssignmentHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCycleCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuggestedTesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalTesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentHistories", x => x.AssignmentHistoryId);
                    table.ForeignKey(
                        name: "FK_AssignmentHistories_TestCycleCases_TestCycleCaseId",
                        column: x => x.TestCycleCaseId,
                        principalTable: "TestCycleCases",
                        principalColumn: "TestCycleCaseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentPreviews",
                columns: table => new
                {
                    AssignmentPreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentPreviews", x => x.AssignmentPreviewId);
                    table.ForeignKey(
                        name: "FK_AssignmentPreviews_TestCycles_TestCycleId",
                        column: x => x.TestCycleId,
                        principalTable: "TestCycles",
                        principalColumn: "TestCycleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaAvailabilities",
                columns: table => new
                {
                    QaAvailabilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CapacityMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaAvailabilities", x => x.QaAvailabilityId);
                    table.ForeignKey(
                        name: "FK_QaAvailabilities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSkillMatrixEntries",
                columns: table => new
                {
                    QaSkillMatrixEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkillCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSkillMatrixEntries", x => x.QaSkillMatrixEntryId);
                    table.ForeignKey(
                        name: "FK_QaSkillMatrixEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentHistories_TestCycleCaseId",
                table: "AssignmentHistories",
                column: "TestCycleCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentPreviews_TestCycleId",
                table: "AssignmentPreviews",
                column: "TestCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_QaAvailabilities_UserId_Date",
                table: "QaAvailabilities",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSkillMatrixEntries_UserId_SkillCode",
                table: "QaSkillMatrixEntries",
                columns: new[] { "UserId", "SkillCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentHistories");

            migrationBuilder.DropTable(
                name: "AssignmentPreviews");

            migrationBuilder.DropTable(
                name: "QaAvailabilities");

            migrationBuilder.DropTable(
                name: "QaSkillMatrixEntries");

            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "TestCycleCases");

            migrationBuilder.DropColumn(
                name: "AssignmentVersion",
                table: "TestCycleCases");

            migrationBuilder.DropColumn(
                name: "CaseWeight",
                table: "TestCycleCases");

            migrationBuilder.DropColumn(
                name: "EstimatedMinutesSnapshot",
                table: "TestCycleCases");

            migrationBuilder.DropColumn(
                name: "RequiredSkillLevelSnapshot",
                table: "TestCycleCases");

            migrationBuilder.DropColumn(
                name: "ComplexityWeight",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "EstimatedMinutes",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IsCritical",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "RequiredSkillLevel",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewerRequired",
                table: "TestCases");
        }
    }
}
