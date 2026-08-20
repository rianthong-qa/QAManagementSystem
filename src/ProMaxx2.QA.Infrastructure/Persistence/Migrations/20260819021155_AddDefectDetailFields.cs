using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualResult",
                table: "Defects",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId",
                table: "Defects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Defects",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedResult",
                table: "Defects",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepsToReproduce",
                table: "Defects",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_AssigneeUserId",
                table: "Defects",
                column: "AssigneeUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_Users_AssigneeUserId",
                table: "Defects",
                column: "AssigneeUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Defects_Users_AssigneeUserId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_AssigneeUserId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ActualResult",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "ExpectedResult",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "StepsToReproduce",
                table: "Defects");
        }
    }
}
