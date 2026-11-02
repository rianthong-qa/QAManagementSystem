using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "WorkLogEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "WorkLogEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WorkLogEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TestCycles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TestCycles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TestCases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "TestCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "RiskAcceptances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "RiskAcceptances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Requirements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Requirements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "KnowledgeFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "KnowledgeFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "KnowledgeArticles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "KnowledgeArticles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Defects",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TestCycles");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "TestCycles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "RiskAcceptances");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "RiskAcceptances");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Defects");
        }
    }
}
