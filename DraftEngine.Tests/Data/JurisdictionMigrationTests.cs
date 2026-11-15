using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DraftEngine.Tests.Data;

/// <summary>
/// The jurisdiction migration must map legacy free-text governing-law values onto the new
/// structured columns without changing or losing the original text.
/// </summary>
public sealed class JurisdictionMigrationTests : IDisposable
{
    private const string BeforeJurisdiction = "20261110212129_RestrictUserCascade";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "DraftEngineJurisdictionMigration", Guid.NewGuid().ToString("N"));

    private readonly string _databasePath;

    public JurisdictionMigrationTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "draftengine.db");
    }

    [Fact]
    public async Task LegacyFreeText_IsMapped_AndUnchanged()
    {
        await CreateDatabaseAtAsync(BeforeJurisdiction);
        SeedLegacyContract("California contract", "California");
        SeedLegacyContract("England contract", "England and Wales");
        SeedLegacyContract("Custom contract", "Grand Duchy of Freedonia");

        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync();
        }

        await using var verify = CreateContext();
        var california = await verify.Contracts.SingleAsync(contract => contract.Title == "California contract");
        Assert.Equal("US", california.GoverningLawCountry);
        Assert.Equal("California", california.GoverningLawSubdivision);
        Assert.Equal("California", california.GoverningLaw);

        var england = await verify.Contracts.SingleAsync(contract => contract.Title == "England contract");
        Assert.Equal("GB", england.GoverningLawCountry);
        Assert.Equal("England and Wales", england.GoverningLawSubdivision);
        Assert.Equal("England and Wales", england.GoverningLaw);

        var custom = await verify.Contracts.SingleAsync(contract => contract.Title == "Custom contract");
        Assert.Equal(string.Empty, custom.GoverningLawCountry);
        Assert.Equal(string.Empty, custom.GoverningLawSubdivision);
        Assert.Equal("Grand Duchy of Freedonia", custom.GoverningLaw);

        Assert.Equal(3, await verify.Contracts.CountAsync());
    }

    [Fact]
    public async Task GoverningLawClauses_AreReworded_AndFlaggedForLegalReview()
    {
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var verify = CreateContext();
        var clauses = await verify.Clauses
            .Where(clause => clause.Title == "Governing Law")
            .ToListAsync();

        Assert.Equal(4, clauses.Count);
        Assert.All(clauses, clause =>
        {
            Assert.Equal(
                "This Agreement is governed by the laws of the following jurisdiction: {{GoverningLaw}}.",
                clause.BodyTemplate);
            Assert.True(clause.NeedsLegalReview);
        });
    }

    private async Task CreateDatabaseAtAsync(string target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private DraftEngineDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Pooling = false
            }.ConnectionString)
            .Options);

    private void SeedLegacyContract(string title, string governingLaw)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contracts (
                UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                LastGeneratedAt, FinalFilePath, AssembledText, IsDeleted, IsArchived)
            VALUES (
                1, $title, 'MutualNda', 0, 'Acme', '', '[]',
                NULL, $law, 12, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                NULL, '', '', 0, 0);
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$law", governingLaw);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
