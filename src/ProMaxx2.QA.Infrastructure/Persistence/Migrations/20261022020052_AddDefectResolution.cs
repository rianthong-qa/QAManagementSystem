using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfDefectId",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "Defects",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionType",
                table: "Defects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootCauseCategory",
                table: "Defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootCauseNote",
                table: "Defects",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_DuplicateOfDefectId",
                table: "Defects",
                column: "DuplicateOfDefectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Defects_DuplicateOfDefectId",
                table: "Defects",
                column: "DuplicateOfDefectId",
                principalTable: "Defects",
                principalColumn: "DefectId");

            // หมวดสาเหตุตั้งต้น (DEFECT_KNOWLEDGE_PLAN.md §4.1) — แก้/เพิ่มได้ใน Setting Center → Defect; ไม่ใส่ซ้ำถ้ามีอยู่แล้ว
            migrationBuilder.Sql(@"
INSERT INTO MasterOptions (MasterOptionId,Category,Value,DisplayName,SortOrder,IsActive)
SELECT NEWID(),'DefectRootCause',v.Value,v.DisplayName,v.SortOrder,1
FROM (VALUES
 ('CodeLogic',N'Code Logic',10),
 ('RequirementUnclear',N'Requirement ไม่ชัด',20),
 ('DataMigration',N'Data/Migration',30),
 ('ConfigEnvironment',N'Config/Environment',40),
 ('UiUx',N'UI/UX',50),
 ('IntegrationCrm',N'Integration/CRM',60),
 ('Performance',N'Performance',70),
 ('Regression',N'Regression',80),
 ('TestCaseData',N'Test Case/Test Data ผิด',90)
) v(Value,DisplayName,SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM MasterOptions m WHERE m.Category='DefectRootCause' AND m.Value=v.Value);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM MasterOptions WHERE Category='DefectRootCause';");

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Defects_DuplicateOfDefectId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_DuplicateOfDefectId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "DuplicateOfDefectId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ResolutionType",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "RootCauseCategory",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "RootCauseNote",
                table: "Defects");
        }
    }
}
