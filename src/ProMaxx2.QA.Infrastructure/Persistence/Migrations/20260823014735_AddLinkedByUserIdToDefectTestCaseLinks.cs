using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedByUserIdToDefectTestCaseLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid?>(
                name: "LinkedByUserId",
                table: "DefectTestCaseLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefectTestCaseLinks_LinkedByUserId",
                table: "DefectTestCaseLinks",
                column: "LinkedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DefectTestCaseLinks_LinkedByUserId",
                table: "DefectTestCaseLinks");

            migrationBuilder.DropColumn(
                name: "LinkedByUserId",
                table: "DefectTestCaseLinks");
        }
    }
}
