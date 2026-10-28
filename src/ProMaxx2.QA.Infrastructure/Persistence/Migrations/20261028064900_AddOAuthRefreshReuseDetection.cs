using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthRefreshReuseDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousRefreshTokenHash",
                table: "PersonalAccessTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_PreviousRefreshTokenHash",
                table: "PersonalAccessTokens",
                column: "PreviousRefreshTokenHash",
                filter: "[PreviousRefreshTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PersonalAccessTokens_PreviousRefreshTokenHash",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "PreviousRefreshTokenHash",
                table: "PersonalAccessTokens");
        }
    }
}
