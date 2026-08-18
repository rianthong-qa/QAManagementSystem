using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestoreMasterSettingsAndAddDashboardShortShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardShares",
                columns: table => new
                {
                    DashboardShareId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardShares", x => x.DashboardShareId);
                });

            migrationBuilder.CreateTable(
                name: "MasterOptions",
                columns: table => new
                {
                    MasterOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterOptions", x => x.MasterOptionId);
                });

            migrationBuilder.Sql(@"
INSERT INTO MasterOptions (MasterOptionId,Category,Value,DisplayName,SortOrder,IsActive) VALUES
(NEWID(),'ReleaseType','Major','Major',10,1),(NEWID(),'ReleaseType','Minor','Minor',20,1),(NEWID(),'ReleaseType','Patch','Patch',30,1),(NEWID(),'ReleaseType','Hotfix','Hotfix',40,1),
(NEWID(),'TestCasePriority','P0','P0',10,1),(NEWID(),'TestCasePriority','P1','P1',20,1),(NEWID(),'TestCasePriority','P2','P2',30,1),(NEWID(),'TestCasePriority','P3','P3',40,1),
(NEWID(),'TestCaseType','Functional','Functional',10,1),(NEWID(),'TestCaseType','Integration','Integration',20,1),(NEWID(),'TestCaseType','Regression','Regression',30,1),(NEWID(),'TestCaseType','Smoke','Smoke',40,1),(NEWID(),'TestCaseType','Performance','Performance',50,1),(NEWID(),'TestCaseType','Security','Security',60,1),
(NEWID(),'TestSuiteType','Smoke','Smoke',10,1),(NEWID(),'TestSuiteType','Regression','Regression',20,1),(NEWID(),'TestSuiteType','Functional','Functional',30,1),
(NEWID(),'TestSuiteRiskTier','P0','P0',10,1),(NEWID(),'TestSuiteRiskTier','P1','P1',20,1),(NEWID(),'TestSuiteRiskTier','P2','P2',30,1),(NEWID(),'TestSuiteRiskTier','P3','P3',40,1),
(NEWID(),'TestCycleType','Smoke','Smoke',10,1),(NEWID(),'TestCycleType','Regression','Regression',20,1),(NEWID(),'TestCycleType','UAT','UAT',30,1),(NEWID(),'TestCycleType','Hotfix','Hotfix',40,1);");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_Code",
                table: "DashboardShares",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterOptions_Category_Value",
                table: "MasterOptions",
                columns: new[] { "Category", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardShares");

            migrationBuilder.DropTable(
                name: "MasterOptions");
        }
    }
}
