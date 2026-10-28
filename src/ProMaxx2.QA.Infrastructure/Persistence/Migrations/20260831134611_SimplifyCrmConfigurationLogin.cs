using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCrmConfigurationLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "CrmConfigurations");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "CrmConfigurations");

            migrationBuilder.DropColumn(
                name: "TokenUrl",
                table: "CrmConfigurations");

            migrationBuilder.RenameColumn(
                name: "RecipientId",
                table: "CrmConfigurations",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "EncryptedClientSecret",
                table: "CrmConfigurations",
                newName: "EncryptedPassword");

            migrationBuilder.RenameColumn(
                name: "ClientSecretHint",
                table: "CrmConfigurations",
                newName: "PasswordHint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Username",
                table: "CrmConfigurations",
                newName: "RecipientId");

            migrationBuilder.RenameColumn(
                name: "PasswordHint",
                table: "CrmConfigurations",
                newName: "ClientSecretHint");

            migrationBuilder.RenameColumn(
                name: "EncryptedPassword",
                table: "CrmConfigurations",
                newName: "EncryptedClientSecret");

            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "CrmConfigurations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "CrmConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TokenUrl",
                table: "CrmConfigurations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
