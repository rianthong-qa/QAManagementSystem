using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "PersonalAccessTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PersonalAccessTokens",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshExpiresAt",
                table: "PersonalAccessTokens",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshTokenHash",
                table: "PersonalAccessTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resource",
                table: "PersonalAccessTokens",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OAuthClients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RedirectUris = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClients", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizations",
                columns: table => new
                {
                    OAuthAuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CodeChallenge = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CodeExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizations", x => x.OAuthAuthorizationId);
                    table.ForeignKey(
                        name: "FK_OAuthAuthorizations_OAuthClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "OAuthClients",
                        principalColumn: "ClientId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_RefreshTokenHash",
                table: "PersonalAccessTokens",
                column: "RefreshTokenHash",
                unique: true,
                filter: "[RefreshTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizations_ClientId",
                table: "OAuthAuthorizations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizations_CodeHash",
                table: "OAuthAuthorizations",
                column: "CodeHash",
                unique: true,
                filter: "[CodeHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizations_CreatedAt",
                table: "OAuthAuthorizations",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuthAuthorizations");

            migrationBuilder.DropTable(
                name: "OAuthClients");

            migrationBuilder.DropIndex(
                name: "IX_PersonalAccessTokens_RefreshTokenHash",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "RefreshExpiresAt",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "RefreshTokenHash",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "Resource",
                table: "PersonalAccessTokens");
        }
    }
}
