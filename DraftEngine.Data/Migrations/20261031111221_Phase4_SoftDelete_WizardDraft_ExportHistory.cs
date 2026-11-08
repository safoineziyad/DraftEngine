using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_SoftDelete_WizardDraft_ExportHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: clause data seeding consolidated to avoid duplicates.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op.
        }
    }
}
