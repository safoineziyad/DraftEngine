using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DraftEngine.Data.Migrations
{
    public partial class SeedMissingClausesForAllTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Clauses",
                columns: new[] { "Id", "BodyTemplate", "Category", "ContractType", "IsDefaultSelected", "IsOptional", "SortOrder", "Title", "IsArchived", "IsSystem", "OwnerUserId", "Source" },
                values: new object[,]
                {
                    { 13, "The Receiving Party agrees to keep all {{ConfidentialInformation}} confidential and not disclose it to third parties except as permitted by this Agreement.", "Confidentiality", "Non-Disclosure Agreement (One-Way)", true, false, 1, "Confidentiality", false, true, null, "System" },
                    { 14, "Confidential Information means any non-public information disclosed by the Disclosing Party that is marked confidential or would reasonably be considered confidential.", "Definitions", "Non-Disclosure Agreement (One-Way)", true, false, 2, "Definition of Confidential Information", false, true, null, "System" },
                    { 15, "Confidential Information does not include information that is public, already known, independently developed, or rightfully received from third parties.", "Exclusions", "Non-Disclosure Agreement (One-Way)", true, false, 3, "Exclusions", false, true, null, "System" },
                    { 16, "The Receiving Party shall use {{ConfidentialInformation}} solely for the purpose of evaluating the relationship between {{Party1}} and {{Party2}} and shall take reasonable precautions to protect it.", "Obligations", "Non-Disclosure Agreement (One-Way)", true, false, 4, "Obligations of Receiving Party", false, true, null, "System" },
                    { 17, "This Agreement shall remain in effect for {{TermMonths}} months from the Effective Date ({{EffectiveDate}}). The obligations of confidentiality shall survive termination.", "Term", "Non-Disclosure Agreement (One-Way)", true, false, 5, "Term / Duration", false, true, null, "System" },
                    { 18, "Upon request or termination, the Receiving Party shall return or destroy all Confidential Information received from the Disclosing Party.", "Return of Materials", "Non-Disclosure Agreement (One-Way)", true, false, 6, "Return or Destruction of Information", false, true, null, "System" },
                    { 19, "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.", "Governing Law", "Non-Disclosure Agreement (One-Way)", true, false, 7, "Governing Law", false, true, null, "System" },
                    { 20, "The Disclosing Party acknowledges that breach may cause irreparable harm and monetary damages may be inadequate.", "Remedies", "Non-Disclosure Agreement (One-Way)", true, false, 8, "Remedies", false, true, null, "System" },
                    { 21, "This Agreement constitutes the entire agreement between the parties with respect to the subject matter hereof.", "Miscellaneous", "Non-Disclosure Agreement (One-Way)", true, false, 9, "Entire Agreement", false, true, null, "System" },
                    { 22, "If any provision is found invalid, the remainder shall remain in full force and effect.", "Miscellaneous", "Non-Disclosure Agreement (One-Way)", true, true, 10, "Severability", false, true, null, "System" },
                    { 23, "{{Party1}} engages {{Party2}} as an independent contractor to perform the services described for {{ClientName}} under the scope of {{Purpose}}.", "Engagement", "Independent Contractor Agreement", true, false, 1, "Engagement / Scope of Work", false, true, null, "System" },
                    { 24, "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall be responsible for taxes, insurance, and benefits.", "Status", "Independent Contractor Agreement", true, false, 2, "Independent Contractor Status", false, true, null, "System" },
                    { 25, "{{Party1}} shall pay {{Party2}} for services rendered as agreed. Payment terms shall be as specified in the engagement details.", "Compensation", "Independent Contractor Agreement", true, false, 3, "Fees and Payment", false, true, null, "System" },
                    { 26, "This Agreement shall commence on {{EffectiveDate}} and continue for {{TermMonths}} months unless terminated earlier by either party with written notice.", "Term", "Independent Contractor Agreement", true, false, 4, "Term and Termination", false, true, null, "System" },
                    { 27, "All work product created by {{Party2}} under this Agreement shall be assigned to {{Party1}} unless otherwise agreed in writing.", "Intellectual Property", "Independent Contractor Agreement", true, false, 5, "Intellectual Property Rights", false, true, null, "System" },
                    { 28, "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties.", "Confidentiality", "Independent Contractor Agreement", true, false, 6, "Confidentiality", false, true, null, "System" },
                    { 29, "Neither party shall be liable for indirect, incidental, or consequential damages arising out of this Agreement.", "Liability", "Independent Contractor Agreement", true, false, 7, "Limitation of Liability", false, true, null, "System" },
                    { 30, "{{Party2}} agrees to indemnify and hold harmless {{Party1}} from claims arising from {{Party2}}'s negligence or willful misconduct.", "Indemnification", "Independent Contractor Agreement", true, false, 8, "Indemnification", false, true, null, "System" },
                    { 31, "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.", "Governing Law", "Independent Contractor Agreement", true, false, 9, "Governing Law", false, true, null, "System" },
                    { 32, "This Agreement constitutes the entire agreement between the parties. If any provision is found invalid, the remainder shall remain in full force and effect.", "Miscellaneous", "Independent Contractor Agreement", true, false, 10, "Entire Agreement / Severability", false, true, null, "System" },
                    { 33, "{{Party2}} agrees to provide consulting services to {{Party1}} for {{ClientName}} in connection with {{Purpose}} as described herein.", "Services", "Consulting Agreement", true, false, 1, "Services", false, true, null, "System" },
                    { 34, "{{Party1}} shall pay {{Party2}} consulting fees as agreed. Reasonable expenses may be reimbursed with prior approval.", "Compensation", "Consulting Agreement", true, false, 2, "Fees and Expenses", false, true, null, "System" },
                    { 35, "This Agreement shall commence on {{EffectiveDate}} and continue for {{TermMonths}} months unless terminated earlier by either party with written notice.", "Term", "Consulting Agreement", true, false, 3, "Term and Termination", false, true, null, "System" },
                    { 36, "{{Party2}} is an independent contractor and not an employee of {{Party1}}. {{Party2}} shall have no authority to bind {{Party1}}.", "Status", "Consulting Agreement", true, false, 4, "Independent Contractor Relationship", false, true, null, "System" },
                    { 37, "{{Party2}} agrees to keep all {{ConfidentialInformation}} of {{Party1}} confidential and not disclose it to third parties except as required by law.", "Confidentiality", "Consulting Agreement", true, false, 5, "Confidentiality", false, true, null, "System" },
                    { 38, "All deliverables and work product created by {{Party2}} under this Agreement shall be owned by {{Party1}} upon full payment.", "Intellectual Property", "Consulting Agreement", true, false, 6, "Intellectual Property", false, true, null, "System" },
                    { 39, "During the term and for one year thereafter, {{Party2}} shall not solicit {{Party1}}'s employees or clients.", "Non-Solicitation", "Consulting Agreement", false, true, 7, "Non-Solicitation", false, true, null, "System" },
                    { 40, "Neither party shall be liable for indirect, incidental, or consequential damages arising out of this Agreement.", "Liability", "Consulting Agreement", true, false, 8, "Limitation of Liability", false, true, null, "System" },
                    { 41, "This Agreement shall be governed by and construed in accordance with the laws of {{GoverningLaw}}.", "Governing Law", "Consulting Agreement", true, false, 9, "Governing Law", false, true, null, "System" },
                    { 42, "This Agreement constitutes the entire agreement between the parties. Any amendments must be in writing and signed by both parties.", "Miscellaneous", "Consulting Agreement", true, false, 10, "Miscellaneous", false, true, null, "System" }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (int id = 13; id <= 42; id++)
            {
                migrationBuilder.DeleteData(table: "Clauses", keyColumn: "Id", keyValue: id);
            }
        }
    }
}
