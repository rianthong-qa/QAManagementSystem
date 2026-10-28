using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectBuildLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Defects_ReleaseId",
                table: "Defects");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Defects",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FixedInBuildId",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRetestAt",
                table: "Defects",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRetestResult",
                table: "Defects",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegressionOfDefectId",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                table: "Defects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Defects",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedInBuildId",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuildId",
                table: "DefectActivities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromValue",
                table: "DefectActivities",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToValue",
                table: "DefectActivities",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FixedInBuildId",
                table: "Defects",
                column: "FixedInBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_RegressionOfDefectId",
                table: "Defects",
                column: "RegressionOfDefectId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_ReleaseId_Status",
                table: "Defects",
                columns: new[] { "ReleaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_VerifiedInBuildId",
                table: "Defects",
                column: "VerifiedInBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectActivities_BuildId",
                table: "DefectActivities",
                column: "BuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_DefectActivities_Builds_BuildId",
                table: "DefectActivities",
                column: "BuildId",
                principalTable: "Builds",
                principalColumn: "BuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Builds_FixedInBuildId",
                table: "Defects",
                column: "FixedInBuildId",
                principalTable: "Builds",
                principalColumn: "BuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Builds_VerifiedInBuildId",
                table: "Defects",
                column: "VerifiedInBuildId",
                principalTable: "Builds",
                principalColumn: "BuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Defects_RegressionOfDefectId",
                table: "Defects",
                column: "RegressionOfDefectId",
                principalTable: "Defects",
                principalColumn: "DefectId");
            // เติมเวลาที่ Resolved/Closed ของข้อมูลเดิมจาก activity การเปลี่ยนสถานะล่าสุด (ไม่มี = UpdatedAt/CreatedAt)
            // Fix Build / Verified Build ของข้อมูลเดิมปล่อยว่าง — หน้าจอแสดง "ไม่ระบุ Build" (DEFECT_BUILD_LIFECYCLE.md)
            // ห่อด้วย EXEC: สคริปต์ idempotent ไม่มี GO คั่น — ไม่ห่อแล้ว SQL Server compile ก่อนคอลัมน์ใหม่มีอยู่จริง (เหมือน backfill ManagerScore)
            migrationBuilder.Sql(@"EXEC(N'
UPDATE d SET ResolvedAt = COALESCE(
    (SELECT MAX(a.CreatedAt) FROM DefectActivities a WHERE a.DefectId = d.DefectId
        AND (a.Message LIKE N''% to Resolved%'' OR a.Message LIKE N''%Status=Resolved%'')),
    d.UpdatedAt, d.CreatedAt)
FROM Defects d WHERE d.Status IN (N''Resolved'', N''Closed'') AND d.ResolvedAt IS NULL;
UPDATE d SET ClosedAt = COALESCE(
    (SELECT MAX(a.CreatedAt) FROM DefectActivities a WHERE a.DefectId = d.DefectId
        AND (a.Message LIKE N''% to Closed%'' OR a.Message LIKE N''%Status=Closed%'' OR a.Message LIKE N''% to Rejected%'' OR a.Message LIKE N''%Status=Rejected%'')),
    d.UpdatedAt, d.CreatedAt)
FROM Defects d WHERE d.Status IN (N''Closed'', N''Rejected'') AND d.ClosedAt IS NULL;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DefectActivities_Builds_BuildId",
                table: "DefectActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Builds_FixedInBuildId",
                table: "Defects");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Builds_VerifiedInBuildId",
                table: "Defects");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Defects_RegressionOfDefectId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_FixedInBuildId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_RegressionOfDefectId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_ReleaseId_Status",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_VerifiedInBuildId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_DefectActivities_BuildId",
                table: "DefectActivities");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "FixedInBuildId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "LastRetestAt",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "LastRetestResult",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "RegressionOfDefectId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ReopenCount",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "VerifiedInBuildId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "BuildId",
                table: "DefectActivities");

            migrationBuilder.DropColumn(
                name: "FromValue",
                table: "DefectActivities");

            migrationBuilder.DropColumn(
                name: "ToValue",
                table: "DefectActivities");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_ReleaseId",
                table: "Defects",
                column: "ReleaseId");
        }
    }
}
