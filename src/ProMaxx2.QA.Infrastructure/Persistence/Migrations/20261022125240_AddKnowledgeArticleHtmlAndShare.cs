using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeArticleHtmlAndShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "KnowledgeArticles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                table: "KnowledgeArticles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Markdown");

            migrationBuilder.CreateTable(
                name: "KnowledgeArticleShareLinks",
                columns: table => new
                {
                    KnowledgeArticleShareLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    KnowledgeArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticleShareLinks", x => x.KnowledgeArticleShareLinkId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleShareLinks_Code",
                table: "KnowledgeArticleShareLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleShareLinks_KnowledgeArticleId",
                table: "KnowledgeArticleShareLinks",
                column: "KnowledgeArticleId",
                unique: true,
                filter: "[RevokedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeArticleShareLinks");

            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                table: "KnowledgeArticles");
        }
    }
}
