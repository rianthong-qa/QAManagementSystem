using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationWebhookTokens",
                columns: table => new
                {
                    AutomationWebhookTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationWebhookTokens", x => x.AutomationWebhookTokenId);
                    table.ForeignKey(
                        name: "FK_AutomationWebhookTokens_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationWebhookDeliveries",
                columns: table => new
                {
                    AutomationWebhookDeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationWebhookTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationWebhookDeliveries", x => x.AutomationWebhookDeliveryId);
                    table.ForeignKey(
                        name: "FK_AutomationWebhookDeliveries_AutomationWebhookTokens_AutomationWebhookTokenId",
                        column: x => x.AutomationWebhookTokenId,
                        principalTable: "AutomationWebhookTokens",
                        principalColumn: "AutomationWebhookTokenId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationWebhookDeliveries_AutomationWebhookTokenId",
                table: "AutomationWebhookDeliveries",
                column: "AutomationWebhookTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationWebhookDeliveries_ProjectId_RequestId",
                table: "AutomationWebhookDeliveries",
                columns: new[] { "ProjectId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationWebhookTokens_ProjectId_IsActive",
                table: "AutomationWebhookTokens",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationWebhookTokens_TokenHash",
                table: "AutomationWebhookTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "AutomationWebhookTokens");
        }
    }
}
