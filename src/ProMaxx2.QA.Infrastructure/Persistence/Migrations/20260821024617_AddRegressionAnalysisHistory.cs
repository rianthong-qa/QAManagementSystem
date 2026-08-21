using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegressionAnalysisHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegressionAnalyses",
                columns: table => new
                {
                    RegressionAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImpactedModules = table.Column<int>(type: "int", nullable: false),
                    RecommendedCases = table.Column<int>(type: "int", nullable: false),
                    MinimumPriority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ChangeNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnalyzedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnalyzedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegressionAnalyses", x => x.RegressionAnalysisId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegressionAnalyses_ReleaseId_AnalyzedAt",
                table: "RegressionAnalyses",
                columns: new[] { "ReleaseId", "AnalyzedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegressionAnalyses");

        }
    }
}
