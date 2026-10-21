using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationExternalReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExternalWeight",
                table: "EvaluationTemplates",
                type: "int",
                nullable: false,
                defaultValue: 30); // น้ำหนักแผนกอื่นเริ่มต้น 30% (หัวหน้าตรง 70%)

            migrationBuilder.AddColumn<int>(
                name: "ExternalReviewCount",
                table: "Evaluations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ExternalScore",
                table: "Evaluations",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalWeight",
                table: "Evaluations",
                type: "int",
                nullable: false,
                defaultValue: 30); // น้ำหนักแผนกอื่นเริ่มต้น 30% (หัวหน้าตรง 70%)

            migrationBuilder.AddColumn<decimal>(
                name: "ManagerScore",
                table: "Evaluations",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            // รอบที่ปิดก่อนมีผู้ประเมินแผนกอื่น: ผลรวมมาจากหัวหน้าตรงอย่างเดียว
            // ห่อด้วย EXEC: สคริปต์ idempotent ไม่มี GO คั่น — ถ้าไม่ห่อ SQL Server compile ทั้งก้อนก่อนคอลัมน์ ManagerScore มีอยู่จริงแล้ว error
            migrationBuilder.Sql("EXEC(N'UPDATE [Evaluations] SET [ManagerScore] = [OverallScore] WHERE [OverallScore] IS NOT NULL')");

            migrationBuilder.CreateTable(
                name: "EvaluationExternalReviews",
                columns: table => new
                {
                    EvaluationExternalReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReviewerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReviewerDepartment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    WeightedScore = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationExternalReviews", x => x.EvaluationExternalReviewId);
                    table.ForeignKey(
                        name: "FK_EvaluationExternalReviews_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "EvaluationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationExternalReviews_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationExternalReviews_Users_RevokedBy",
                        column: x => x.RevokedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationExternalScores",
                columns: table => new
                {
                    EvaluationExternalScoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationExternalReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationScoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationExternalScores", x => x.EvaluationExternalScoreId);
                    table.ForeignKey(
                        name: "FK_EvaluationExternalScores_EvaluationExternalReviews_EvaluationExternalReviewId",
                        column: x => x.EvaluationExternalReviewId,
                        principalTable: "EvaluationExternalReviews",
                        principalColumn: "EvaluationExternalReviewId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationExternalScores_EvaluationScores_EvaluationScoreId",
                        column: x => x.EvaluationScoreId,
                        principalTable: "EvaluationScores",
                        principalColumn: "EvaluationScoreId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalReviews_Code",
                table: "EvaluationExternalReviews",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalReviews_CreatedBy",
                table: "EvaluationExternalReviews",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalReviews_EvaluationId_CreatedAt",
                table: "EvaluationExternalReviews",
                columns: new[] { "EvaluationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalReviews_RevokedBy",
                table: "EvaluationExternalReviews",
                column: "RevokedBy");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalScores_EvaluationExternalReviewId_EvaluationScoreId",
                table: "EvaluationExternalScores",
                columns: new[] { "EvaluationExternalReviewId", "EvaluationScoreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationExternalScores_EvaluationScoreId",
                table: "EvaluationExternalScores",
                column: "EvaluationScoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationExternalScores");

            migrationBuilder.DropTable(
                name: "EvaluationExternalReviews");

            migrationBuilder.DropColumn(
                name: "ExternalWeight",
                table: "EvaluationTemplates");

            migrationBuilder.DropColumn(
                name: "ExternalReviewCount",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "ExternalScore",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "ExternalWeight",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "ManagerScore",
                table: "Evaluations");
        }
    }
}
