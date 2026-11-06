using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class Task3_WizardDraftAndCanonicalContractTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WizardDrafts",
                columns: table => new
                {
                    DraftKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EncryptedPayload = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WizardDrafts", x => x.DraftKey);
                });

            migrationBuilder.Sql(
                """
                UPDATE Contracts
                SET ContractType = CASE lower(trim(ContractType))
                    WHEN 'mutual nda' THEN 'MutualNda'
                    WHEN 'non-disclosure agreement (one-way)' THEN 'OneWayNda'
                    WHEN 'one-way nda' THEN 'OneWayNda'
                    WHEN 'independent contractor agreement' THEN 'IndependentContractorAgreement'
                    WHEN 'consulting agreement' THEN 'ConsultingAgreement'
                    ELSE ContractType
                END
                WHERE lower(trim(ContractType)) IN (
                    'mutual nda',
                    'non-disclosure agreement (one-way)',
                    'one-way nda',
                    'independent contractor agreement',
                    'consulting agreement'
                );

                UPDATE Clauses
                SET ContractType = CASE lower(trim(ContractType))
                    WHEN 'mutual nda' THEN 'MutualNda'
                    WHEN 'non-disclosure agreement (one-way)' THEN 'OneWayNda'
                    WHEN 'one-way nda' THEN 'OneWayNda'
                    WHEN 'independent contractor agreement' THEN 'IndependentContractorAgreement'
                    WHEN 'consulting agreement' THEN 'ConsultingAgreement'
                    ELSE ContractType
                END
                WHERE lower(trim(ContractType)) IN (
                    'mutual nda',
                    'non-disclosure agreement (one-way)',
                    'one-way nda',
                    'independent contractor agreement',
                    'consulting agreement'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Contracts
                SET ContractType = CASE ContractType
                    WHEN 'MutualNda' THEN 'Mutual NDA'
                    WHEN 'OneWayNda' THEN 'Non-Disclosure Agreement (One-Way)'
                    WHEN 'IndependentContractorAgreement' THEN 'Independent Contractor Agreement'
                    WHEN 'ConsultingAgreement' THEN 'Consulting Agreement'
                    ELSE ContractType
                END
                WHERE ContractType IN (
                    'MutualNda',
                    'OneWayNda',
                    'IndependentContractorAgreement',
                    'ConsultingAgreement'
                );

                UPDATE Clauses
                SET ContractType = CASE ContractType
                    WHEN 'MutualNda' THEN 'Mutual NDA'
                    WHEN 'OneWayNda' THEN 'Non-Disclosure Agreement (One-Way)'
                    WHEN 'IndependentContractorAgreement' THEN 'Independent Contractor Agreement'
                    WHEN 'ConsultingAgreement' THEN 'Consulting Agreement'
                    ELSE ContractType
                END
                WHERE ContractType IN (
                    'MutualNda',
                    'OneWayNda',
                    'IndependentContractorAgreement',
                    'ConsultingAgreement'
                );
                """);

            migrationBuilder.DropTable(
                name: "WizardDrafts");
        }
    }
}
