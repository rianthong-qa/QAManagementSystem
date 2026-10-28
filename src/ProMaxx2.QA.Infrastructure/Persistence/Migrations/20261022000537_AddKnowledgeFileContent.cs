using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeFileContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "KnowledgeFiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImportPath",
                table: "KnowledgeFiles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "KnowledgeFiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "KnowledgeFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_ContentHash",
                table: "KnowledgeFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_ImportPath",
                table: "KnowledgeFiles",
                column: "ImportPath",
                filter: "[ImportPath] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_ContentHash",
                table: "KnowledgeFiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_ImportPath",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "ImportPath",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "KnowledgeFiles");
        }
    }
}
