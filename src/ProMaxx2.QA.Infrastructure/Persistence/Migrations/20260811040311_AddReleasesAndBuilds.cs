using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasesAndBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReleaseType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlannedReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReleaseOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.ReleaseId);
                    table.ForeignKey(
                        name: "FK_Releases_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Releases_Users_ReleaseOwnerUserId",
                        column: x => x.ReleaseOwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Builds",
                columns: table => new
                {
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApplicationVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PackageVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CommitReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuildDate = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    ChangeNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KnownIssues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsReleaseCandidate = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Builds", x => x.BuildId);
                    table.ForeignKey(
                        name: "FK_Builds_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "ReleaseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Builds_ReleaseId_BuildNumber",
                table: "Builds",
                columns: new[] { "ReleaseId", "BuildNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ProjectId_ReleaseCode",
                table: "Releases",
                columns: new[] { "ProjectId", "ReleaseCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ReleaseOwnerUserId",
                table: "Releases",
                column: "ReleaseOwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Builds");

            migrationBuilder.DropTable(
                name: "Releases");
        }
    }
}
