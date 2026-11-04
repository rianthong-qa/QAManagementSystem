using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeArticleAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeArticleAssets",
                columns: table => new
                {
                    KnowledgeArticleAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnowledgeArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2(0)", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticleAssets", x => x.KnowledgeArticleAssetId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleAssets_KnowledgeArticleId",
                table: "KnowledgeArticleAssets",
                column: "KnowledgeArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticleAssets_UploadedAt",
                table: "KnowledgeArticleAssets",
                column: "UploadedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeArticleAssets");
        }
    }
}
