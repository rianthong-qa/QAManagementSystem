using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyReportNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyReportNotes",
                columns: table => new
                {
                    WeeklyReportNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Health = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Headline = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    KeyRisk = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Ask = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextWeekPlan = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MemberNotesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyReportNotes", x => x.WeeklyReportNoteId);
                    table.ForeignKey(
                        name: "FK_WeeklyReportNotes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyReportNotes_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyReportNotes_CreatedBy",
                table: "WeeklyReportNotes",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyReportNotes_UpdatedBy",
                table: "WeeklyReportNotes",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyReportNotes_WeekStart",
                table: "WeeklyReportNotes",
                column: "WeekStart",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyReportNotes");
        }
    }
}
