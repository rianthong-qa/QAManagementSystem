using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationDataSeedScriptType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutomationDataSeedScripts_ProjectId_IsActive",
                table: "AutomationDataSeedScripts");

            // Every row that already exists was, by definition, a Seed script — "Cleanup" as a ScriptType did not
            // exist before this migration.
            migrationBuilder.AddColumn<string>(
                name: "ScriptType",
                table: "AutomationDataSeedScripts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Seed");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedScripts_ProjectId_ScriptType_IsActive",
                table: "AutomationDataSeedScripts",
                columns: new[] { "ProjectId", "ScriptType", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AutomationDataSeedScripts_ProjectId_ScriptType_IsActive",
                table: "AutomationDataSeedScripts");

            migrationBuilder.DropColumn(
                name: "ScriptType",
                table: "AutomationDataSeedScripts");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDataSeedScripts_ProjectId_IsActive",
                table: "AutomationDataSeedScripts",
                columns: new[] { "ProjectId", "IsActive" });
        }
    }
}
