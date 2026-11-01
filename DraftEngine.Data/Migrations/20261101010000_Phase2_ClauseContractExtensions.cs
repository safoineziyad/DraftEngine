using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    public partial class Phase2_ClauseContractExtensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUserDefined",
                table: "Clauses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Contracts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Contracts",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsUserDefined", table: "Clauses");
            migrationBuilder.DropColumn(name: "IsArchived", table: "Contracts");
            migrationBuilder.DropColumn(name: "ArchivedAt", table: "Contracts");
        }
    }
}
