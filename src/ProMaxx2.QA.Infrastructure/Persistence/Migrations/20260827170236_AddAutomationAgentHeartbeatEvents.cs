using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationAgentHeartbeatEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationAgentHeartbeatEvents",
                columns: table => new
                {
                    AutomationAgentHeartbeatEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationAgentHeartbeatEvents", x => x.AutomationAgentHeartbeatEventId);
                    table.ForeignKey(
                        name: "FK_AutomationAgentHeartbeatEvents_AutomationAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AutomationAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationAgentHeartbeatEvents_AgentId_OccurredAt",
                table: "AutomationAgentHeartbeatEvents",
                columns: new[] { "AgentId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationAgentHeartbeatEvents");
        }
    }
}
