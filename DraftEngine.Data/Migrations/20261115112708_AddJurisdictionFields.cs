using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DraftEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJurisdictionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoverningLawCountry",
                table: "Contracts",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GoverningLawSubdivision",
                table: "Contracts",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VenueCity",
                table: "Contracts",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VenueCountry",
                table: "Contracts",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            // Map the legacy free-text governing law onto the structured columns without
            // touching the original text. Values outside these certain lists stay free text
            // (empty country/subdivision), so nothing is lost or guessed.
            migrationBuilder.Sql("""
                UPDATE Contracts SET GoverningLawCountry = 'US', GoverningLawSubdivision = GoverningLaw
                WHERE GoverningLaw IN (
                    'Alabama','Alaska','Arizona','Arkansas','California','Colorado','Connecticut',
                    'Delaware','District of Columbia','Florida','Georgia','Hawaii','Idaho','Illinois',
                    'Indiana','Iowa','Kansas','Kentucky','Louisiana','Maine','Maryland','Massachusetts',
                    'Michigan','Minnesota','Mississippi','Missouri','Montana','Nebraska','Nevada',
                    'New Hampshire','New Jersey','New Mexico','New York','North Carolina','North Dakota',
                    'Ohio','Oklahoma','Oregon','Pennsylvania','Rhode Island','South Carolina',
                    'South Dakota','Tennessee','Texas','Utah','Vermont','Virginia','Washington',
                    'West Virginia','Wisconsin','Wyoming');

                UPDATE Contracts SET GoverningLawCountry = 'CA', GoverningLawSubdivision = GoverningLaw
                WHERE GoverningLaw IN (
                    'Alberta','British Columbia','Manitoba','New Brunswick','Newfoundland and Labrador',
                    'Northwest Territories','Nova Scotia','Nunavut','Ontario','Prince Edward Island',
                    'Quebec','Saskatchewan','Yukon');

                UPDATE Contracts SET GoverningLawCountry = 'AU', GoverningLawSubdivision = GoverningLaw
                WHERE GoverningLaw IN (
                    'Australian Capital Territory','New South Wales','Northern Territory','Queensland',
                    'South Australia','Tasmania','Victoria','Western Australia');

                UPDATE Contracts SET GoverningLawCountry = 'GB', GoverningLawSubdivision = GoverningLaw
                WHERE GoverningLaw IN ('England','Scotland','Wales','Northern Ireland');

                UPDATE Contracts SET GoverningLawCountry = 'GB', GoverningLawSubdivision = 'England and Wales'
                WHERE GoverningLaw = 'England and Wales';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoverningLawCountry",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "GoverningLawSubdivision",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "VenueCity",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "VenueCountry",
                table: "Contracts");
        }
    }
}
