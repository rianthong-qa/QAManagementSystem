using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerUserCrmConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CRM login moves from one shared Service Account row to one row per QA Hub user — the old shared
            // row has no valid UserId to backfill (confirmed with the user: everyone re-enters their own CRM
            // login fresh instead of migrating the old shared credentials to any one specific user).
            migrationBuilder.Sql("DELETE FROM CrmConfigurations;");

            migrationBuilder.DropColumn(
                name: "PollIntervalMinutes",
                table: "CrmConfigurations");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "CrmConfigurations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "CrmSyncSettings",
                columns: table => new
                {
                    CrmSyncSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PollIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmSyncSettings", x => x.CrmSyncSettingsId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmConfigurations_UserId",
                table: "CrmConfigurations",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmSyncSettings");

            migrationBuilder.DropIndex(
                name: "IX_CrmConfigurations_UserId",
                table: "CrmConfigurations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "CrmConfigurations");

            migrationBuilder.AddColumn<int>(
                name: "PollIntervalMinutes",
                table: "CrmConfigurations",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }
    }
}
