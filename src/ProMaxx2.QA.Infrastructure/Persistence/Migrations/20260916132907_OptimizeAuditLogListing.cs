using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAuditLogListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RegressionActivities_CreatedAt_RegressionActivityId",
                table: "RegressionActivities",
                columns: new[] { "CreatedAt", "RegressionActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DefectActivities_CreatedAt_DefectActivityId",
                table: "DefectActivities",
                columns: new[] { "CreatedAt", "DefectActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt_AuditLogId",
                table: "AuditLogs",
                columns: new[] { "CreatedAt", "AuditLogId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_CreatedAt_AuditLogId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "CreatedAt", "AuditLogId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegressionActivities_CreatedAt_RegressionActivityId",
                table: "RegressionActivities");

            migrationBuilder.DropIndex(
                name: "IX_DefectActivities_CreatedAt_DefectActivityId",
                table: "DefectActivities");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CreatedAt_AuditLogId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_CreatedAt_AuditLogId",
                table: "AuditLogs");
        }
    }
}
