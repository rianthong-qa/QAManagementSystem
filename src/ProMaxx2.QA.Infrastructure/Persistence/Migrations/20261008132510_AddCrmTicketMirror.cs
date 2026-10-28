using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmTicketMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmMirrorRuns",
                columns: table => new
                {
                    CrmMirrorRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RangeFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RangeTo = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountUsername = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TicketCount = table.Column<int>(type: "int", nullable: false),
                    NewTickets = table.Column<int>(type: "int", nullable: false),
                    ChangedTickets = table.Column<int>(type: "int", nullable: false),
                    NewAnswers = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DiagnosticsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmMirrorRuns", x => x.CrmMirrorRunId);
                });

            migrationBuilder.CreateTable(
                name: "CrmTicketEvents",
                columns: table => new
                {
                    CrmTicketEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FromAssigneeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToAssigneeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FromDeveloperCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToDeveloperCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OwnerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AnswerNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ActorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmTicketEvents", x => x.CrmTicketEventId);
                });

            migrationBuilder.CreateTable(
                name: "CrmTickets",
                columns: table => new
                {
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    JobType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ServiceType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Product = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssigneeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OwnerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeveloperCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReplyAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmTickets", x => x.JobNo);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmMirrorRuns_StartedAt",
                table: "CrmMirrorRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTicketEvents_JobNo_AnswerNo",
                table: "CrmTicketEvents",
                columns: new[] { "JobNo", "AnswerNo" },
                unique: true,
                filter: "[AnswerNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTicketEvents_JobNo_OccurredAt",
                table: "CrmTicketEvents",
                columns: new[] { "JobNo", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmTicketEvents_OccurredAt",
                table: "CrmTicketEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTickets_AssigneeCode",
                table: "CrmTickets",
                column: "AssigneeCode");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTickets_ContactDate",
                table: "CrmTickets",
                column: "ContactDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmMirrorRuns");

            migrationBuilder.DropTable(
                name: "CrmTicketEvents");

            migrationBuilder.DropTable(
                name: "CrmTickets");
        }
    }
}
