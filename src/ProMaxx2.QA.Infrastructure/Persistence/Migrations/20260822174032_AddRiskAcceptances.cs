using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskAcceptances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiskAcceptances",
                columns: table => new
                {
                    RiskAcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RiskCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Issue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Impact = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Probability = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Workaround = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TargetFix = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    QaRecommendation = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAcceptances", x => x.RiskAcceptanceId);
                    table.ForeignKey(
                        name: "FK_RiskAcceptances_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskAcceptances_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "ReleaseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_ProjectId_RiskCode",
                table: "RiskAcceptances",
                columns: new[] { "ProjectId", "RiskCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_ReleaseId",
                table: "RiskAcceptances",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_Status",
                table: "RiskAcceptances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskAcceptances");
        }
    }
}
