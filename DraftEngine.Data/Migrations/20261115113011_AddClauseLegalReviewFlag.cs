using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClauseLegalReviewFlag : Migration
    {
        private const string OldGoverningLaw =
            "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.";

        private const string NewGoverningLaw =
            "This Agreement is governed by the laws of the following jurisdiction: {{GoverningLaw}}.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsLegalReview",
                table: "Clauses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // The governing-law wording now takes a standalone jurisdiction noun phrase, which
            // stays grammatical in French and Arabic. These seeded clauses are marked for legal
            // review because the wording changed; user-authored clauses are untouched.
            foreach (var clauseId in new[] { 5, 19, 31, 41 })
            {
                migrationBuilder.UpdateData(
                    table: "Clauses",
                    keyColumn: "Id",
                    keyValue: clauseId,
                    columns: new[] { "BodyTemplate", "NeedsLegalReview" },
                    values: new object[] { NewGoverningLaw, true });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeedsLegalReview",
                table: "Clauses");

            foreach (var clauseId in new[] { 5, 19, 31, 41 })
            {
                migrationBuilder.UpdateData(
                    table: "Clauses",
                    keyColumn: "Id",
                    keyValue: clauseId,
                    column: "BodyTemplate",
                    value: OldGoverningLaw);
            }
        }
    }
}
