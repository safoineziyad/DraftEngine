using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignClauseData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The historical Phase4_SoftDelete migration adds this column and then rebuilds the
            // table without it on SQLite, so repaired databases need the column back before the
            // model can query clauses. Databases that still have it would fail here; the Phase4
            // rebuild means every migrated database is missing it.
            migrationBuilder.Sql("ALTER TABLE Clauses ADD COLUMN IsUserDefined INTEGER NOT NULL DEFAULT 0;");

            // Task3 introduced canonical registry ids in the model; existing databases still
            // hold the legacy display names, so migrate the rows before anything queries them.
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'MutualNda' WHERE ContractType = 'Mutual NDA';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'OneWayNda' WHERE ContractType = 'Non-Disclosure Agreement (One-Way)';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'IndependentContractorAgreement' WHERE ContractType = 'Independent Contractor Agreement';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'ConsultingAgreement' WHERE ContractType = 'Consulting Agreement';");
            migrationBuilder.Sql("UPDATE Clauses SET IsUserDefined = 1 WHERE Source = 'User';");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 23,
                column: "BodyTemplate",
                value: "{{ClientName}} engages {{ContractorName}} as an independent contractor to perform the services described under the scope of {{Purpose}}.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 24,
                column: "BodyTemplate",
                value: "{{ContractorName}} is an independent contractor and not an employee of {{ClientName}}. {{ContractorName}} shall be responsible for taxes, insurance, and benefits.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 25,
                column: "BodyTemplate",
                value: "{{ClientName}} shall pay {{ContractorName}} for services rendered as agreed. Payment terms shall be as specified in the engagement details.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 27,
                column: "BodyTemplate",
                value: "All work product created by {{ContractorName}} under this Agreement shall be assigned to {{ClientName}} unless otherwise agreed in writing.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 28,
                column: "BodyTemplate",
                value: "{{ContractorName}} agrees to keep all {{ConfidentialInformation}} of {{ClientName}} confidential and not disclose it to third parties.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 30,
                column: "BodyTemplate",
                value: "{{ContractorName}} agrees to indemnify and hold harmless {{ClientName}} from claims arising from {{ContractorName}}'s negligence or willful misconduct.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 33,
                column: "BodyTemplate",
                value: "{{ConsultantName}} agrees to provide consulting services to {{ClientName}} in connection with {{Purpose}} as described herein.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 34,
                column: "BodyTemplate",
                value: "{{ClientName}} shall pay {{ConsultantName}} consulting fees as agreed. Reasonable expenses may be reimbursed with prior approval.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 36,
                column: "BodyTemplate",
                value: "{{ConsultantName}} is an independent contractor and not an employee of {{ClientName}}. {{ConsultantName}} shall have no authority to bind {{ClientName}}.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 37,
                column: "BodyTemplate",
                value: "{{ConsultantName}} agrees to keep all {{ConfidentialInformation}} of {{ClientName}} confidential and not disclose it to third parties except as required by law.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 38,
                column: "BodyTemplate",
                value: "All deliverables and work product created by {{ConsultantName}} under this Agreement shall be owned by {{ClientName}} upon full payment.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 39,
                column: "BodyTemplate",
                value: "During the term and for one year thereafter, {{ConsultantName}} shall not solicit {{ClientName}}'s employees or clients.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'Mutual NDA' WHERE ContractType = 'MutualNda';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'Non-Disclosure Agreement (One-Way)' WHERE ContractType = 'OneWayNda';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'Independent Contractor Agreement' WHERE ContractType = 'IndependentContractorAgreement';");
            migrationBuilder.Sql("UPDATE Clauses SET ContractType = 'Consulting Agreement' WHERE ContractType = 'ConsultingAgreement';");
            migrationBuilder.Sql("UPDATE Clauses SET IsUserDefined = 0;");
            migrationBuilder.Sql("ALTER TABLE Clauses DROP COLUMN IsUserDefined;");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 23,
                column: "BodyTemplate",
                value: "{{Party1}} engages {{Party2}} as an independent contractor to perform the services described for {{ClientName}} under the scope of {{Purpose}}.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 24,
                column: "BodyTemplate",
                value: "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall be responsible for taxes, insurance, and benefits.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 25,
                column: "BodyTemplate",
                value: "{{Party1}} shall pay {{Party2}} for services rendered as agreed. Payment terms shall be as specified in the engagement details.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 27,
                column: "BodyTemplate",
                value: "All work product created by {{Party2}} under this Agreement shall be assigned to {{Party1}} unless otherwise agreed in writing.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 28,
                column: "BodyTemplate",
                value: "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 30,
                column: "BodyTemplate",
                value: "{{Party2}} agrees to indemnify and hold harmless {{Party1}} from claims arising from {{Party2}}'s negligence or willful misconduct.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 33,
                column: "BodyTemplate",
                value: "{{Party2}} agrees to provide consulting services to {{Party1}} for {{ClientName}} in connection with {{Purpose}} as described herein.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 34,
                column: "BodyTemplate",
                value: "{{Party1}} shall pay {{Party2}} consulting fees as agreed. Reasonable expenses may be reimbursed with prior approval.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 36,
                column: "BodyTemplate",
                value: "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall have no authority to bind {{Party1}}.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 37,
                column: "BodyTemplate",
                value: "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties except as required by law.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 38,
                column: "BodyTemplate",
                value: "All deliverables and work product created by {{Party2}} under this Agreement shall be owned by {{Party1}} upon full payment.");

            migrationBuilder.UpdateData(
                table: "Clauses",
                keyColumn: "Id",
                keyValue: 39,
                column: "BodyTemplate",
                value: "During the term and for one year thereafter, {{Party2}} shall not solicit {{Party1}}'s employees or clients.");
        }
    }
}
