using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_SoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Contracts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Contracts",
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

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Clauses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Clauses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Clauses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Clauses",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsUserDefined",
                table: "Clauses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 28,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 29,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 30,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 31,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 32,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 33,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 34,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 35,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 36,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 37,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 38,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 39,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 40,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 41,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 42,
                columns: new[] { "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[] { false, true, null, "System" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_IsDeleted",
                table: "Contracts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_OwnerUserId",
                table: "Clauses",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clauses_Users_OwnerUserId",
                table: "Clauses",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clauses_Users_OwnerUserId",
                table: "Clauses");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_IsDeleted",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Clauses_OwnerUserId",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Clauses");
        }
    }
}
