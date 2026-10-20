using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceEvidenceAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationNotifications",
                columns: table => new
                {
                    EvaluationNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationNotifications", x => x.EvaluationNotificationId);
                    table.ForeignKey(
                        name: "FK_EvaluationNotifications_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "EvaluationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkLogAttachments",
                columns: table => new
                {
                    WorkLogAttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkLogEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLogAttachments", x => x.WorkLogAttachmentId);
                    table.ForeignKey(
                        name: "FK_WorkLogAttachments_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLogAttachments_WorkLogEntries_WorkLogEntryId",
                        column: x => x.WorkLogEntryId,
                        principalTable: "WorkLogEntries",
                        principalColumn: "WorkLogEntryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationNotifications_EvaluationId_Kind_DueDate_RecipientUserId",
                table: "EvaluationNotifications",
                columns: new[] { "EvaluationId", "Kind", "DueDate", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationNotifications_RecipientUserId",
                table: "EvaluationNotifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogAttachments_UploadedBy",
                table: "WorkLogAttachments",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogAttachments_WorkLogEntryId",
                table: "WorkLogAttachments",
                column: "WorkLogEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationNotifications");

            migrationBuilder.DropTable(
                name: "WorkLogAttachments");
        }
    }
}
