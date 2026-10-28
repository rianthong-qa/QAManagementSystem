using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectShareLinks",
                columns: table => new
                {
                    DefectShareLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    DefectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectShareLinks", x => x.DefectShareLinkId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectShareLinks_Code",
                table: "DefectShareLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefectShareLinks_DefectId",
                table: "DefectShareLinks",
                column: "DefectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefectShareLinks");
        }
    }
}
