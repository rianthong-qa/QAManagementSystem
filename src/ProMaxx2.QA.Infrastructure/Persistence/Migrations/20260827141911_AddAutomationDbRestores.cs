using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationDbRestores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationDbRestores",
                columns: table => new
                {
                    AutomationDbRestoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationDbSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChecksumVerified = table.Column<bool>(type: "bit", nullable: false),
                    AvailabilityVerified = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationDbRestores", x => x.AutomationDbRestoreId);
                    table.ForeignKey(
                        name: "FK_AutomationDbRestores_AutomationDbSnapshots_AutomationDbSnapshotId",
                        column: x => x.AutomationDbSnapshotId,
                        principalTable: "AutomationDbSnapshots",
                        principalColumn: "AutomationDbSnapshotId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationDbRestores_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDbRestores_AutomationDbSnapshotId",
                table: "AutomationDbRestores",
                column: "AutomationDbSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDbRestores_ProjectId_RequestedAt",
                table: "AutomationDbRestores",
                columns: new[] { "ProjectId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDbRestores_Status",
                table: "AutomationDbRestores",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationDbRestores");
        }
    }
}
