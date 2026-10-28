using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationScheduleNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationScheduleNotifications",
                columns: table => new
                {
                    AutomationScheduleNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationScheduleNotifications", x => x.AutomationScheduleNotificationId);
                    table.ForeignKey(
                        name: "FK_AutomationScheduleNotifications_AutomationSuiteSchedules_AutomationScheduleId",
                        column: x => x.AutomationScheduleId,
                        principalTable: "AutomationSuiteSchedules",
                        principalColumn: "AutomationScheduleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationScheduleNotifications_AutomationExecutionId_EventType",
                table: "AutomationScheduleNotifications",
                columns: new[] { "AutomationExecutionId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationScheduleNotifications_AutomationScheduleId",
                table: "AutomationScheduleNotifications",
                column: "AutomationScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationScheduleNotifications_ProjectId_IsRead_CreatedAtUtc",
                table: "AutomationScheduleNotifications",
                columns: new[] { "ProjectId", "IsRead", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationScheduleNotifications");
        }
    }
}
