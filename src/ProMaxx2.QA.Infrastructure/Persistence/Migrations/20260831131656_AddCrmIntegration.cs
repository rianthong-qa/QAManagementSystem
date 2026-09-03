using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CrmLastKnownAssignto",
                table: "Defects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrmLastKnownStatus",
                table: "Defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CrmLastSyncedAt",
                table: "Defects",
                type: "datetime2(0)",
                precision: 0,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrmSyncStatus",
                table: "Defects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "CrmTicketId",
                table: "Defects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmConfigurations",
                columns: table => new
                {
                    CrmConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TokenUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedClientSecret = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ClientSecretHint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecipientId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmConfigurations", x => x.CrmConfigurationId);
                });

            migrationBuilder.CreateTable(
                name: "CrmProjectMappings",
                columns: table => new
                {
                    CrmProjectMappingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrmProductId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CrmVersionId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmProjectMappings", x => x.CrmProjectMappingId);
                    table.ForeignKey(
                        name: "FK_CrmProjectMappings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmProjectMappings_ProjectId",
                table: "CrmProjectMappings",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmConfigurations");

            migrationBuilder.DropTable(
                name: "CrmProjectMappings");

            migrationBuilder.DropColumn(
                name: "CrmLastKnownAssignto",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "CrmLastKnownStatus",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "CrmLastSyncedAt",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "CrmSyncStatus",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "CrmTicketId",
                table: "Defects");
        }
    }
}
