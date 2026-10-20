using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    BodyTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    IsOptional = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefaultSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clauses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenAiApiKeyEncrypted = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SubscriptionTier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SubscriptionExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PartiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GoverningLaw = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TermMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastGeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinalFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AssembledText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractClauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClauseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Selected = table.Column<bool>(type: "INTEGER", nullable: false),
                    CustomBodyOverride = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractClauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractClauses_Clauses_ClauseId",
                        column: x => x.ClauseId,
                        principalTable: "Clauses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractClauses_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractFields_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileType = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedDocuments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Clauses",
                columns: new[] { "Id", "BodyTemplate", "Category", "ContractType", "IsDefaultSelected", "IsOptional", "SortOrder", "Title" },
                values: new object[,]
                {
                    { 1, "The parties agree to keep all {{ConfidentialInformation}} confidential and not disclose it to third parties except as permitted by this Agreement.", "Confidentiality", "Mutual NDA", true, false, 1, "Confidentiality" },
                    { 2, "Confidential Information means any non-public information disclosed by {{Party1}} or {{Party2}} that is marked confidential or would reasonably be considered confidential.", "Definitions", "Mutual NDA", true, false, 2, "Definition of Confidential Information" },
                    { 3, "Confidential Information does not include information that is public, already known, independently developed, or rightfully received from third parties.", "Exclusions", "Mutual NDA", true, false, 3, "Exclusions" },
                    { 4, "This Agreement shall remain in effect for {{TermMonths}} months from the Effective Date ({{EffectiveDate}}).", "Term", "Mutual NDA", true, false, 4, "Term" },
                    { 5, "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.", "Governing Law", "Mutual NDA", true, false, 5, "Governing Law" },
                    { 6, "This Agreement constitutes the entire agreement between the parties with respect to the subject matter hereof.", "Miscellaneous", "Mutual NDA", true, false, 6, "Entire Agreement" },
                    { 7, "Nothing in this Agreement grants any license or rights to the other party's intellectual property.", "Intellectual Property", "Mutual NDA", true, false, 7, "No License" },
                    { 8, "Upon termination, each party shall return or destroy all Confidential Information received from the other party.", "Return of Materials", "Mutual NDA", true, false, 8, "Return of Materials" },
                    { 9, "Each party acknowledges that breach may cause irreparable harm and monetary damages may be inadequate.", "Remedies", "Mutual NDA", true, false, 9, "Remedies" },
                    { 10, "Neither party may assign this Agreement without the prior written consent of the other party.", "Assignment", "Mutual NDA", true, true, 10, "Assignment" },
                    { 11, "If any provision is found invalid, the remainder shall remain in full force and effect.", "Miscellaneous", "Mutual NDA", true, true, 11, "Severability" },
                    { 12, "This Agreement may be executed in counterparts, each of which shall be deemed an original.", "Miscellaneous", "Mutual NDA", true, true, 12, "Counterparts" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ContractId",
                table: "ChatMessages",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_ContractType",
                table: "Clauses",
                column: "ContractType");

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_SortOrder",
                table: "Clauses",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_ContractClauses_ClauseId",
                table: "ContractClauses",
                column: "ClauseId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractClauses_ContractId",
                table: "ContractClauses",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractFields_ContractId",
                table: "ContractFields",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ContractType",
                table: "Contracts",
                column: "ContractType");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CreatedAt",
                table: "Contracts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_Title",
                table: "Contracts",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_UserId",
                table: "Contracts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_ContractId",
                table: "GeneratedDocuments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ContractClauses");

            migrationBuilder.DropTable(
                name: "ContractFields");

            migrationBuilder.DropTable(
                name: "GeneratedDocuments");

            migrationBuilder.DropTable(
                name: "Clauses");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
