using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.Tests.Data;

/// <summary>
/// A migration that throws must not leave a half-upgraded database. The pre-migration
/// snapshot is restored, the original data survives, and the error is clear.
/// </summary>
public sealed class DatabaseUpgradeFailureTests : IDisposable
{
    private const string BeforeFailureTarget = "20261106211906_AlignClauseData";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DraftEngineUpgradeFailure", Guid.NewGuid().ToString("N"));

    public DatabaseUpgradeFailureTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FailingMigration_RestoresSnapshot_AndReportsIt()
    {
        var databasePath = Path.Combine(_root, "draftengine.db");
        await CreateDatabaseAtAsync(databasePath, BeforeFailureTarget);
        SeedContract(databasePath);

        // The next migration creates ContractAnalyses; a pre-existing table with the same
        // name makes it fail deterministically.
        Execute(databasePath, "CREATE TABLE ContractAnalyses(Id INTEGER PRIMARY KEY);");

        using var provider = BuildProvider(databasePath);

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseService.EnsureDatabaseCreated(provider));
        Assert.Contains("restored", exception.Message, StringComparison.OrdinalIgnoreCase);

        // Data is back to the pre-upgrade state.
        Assert.Equal(1L, Convert.ToInt64(Scalar(databasePath, "SELECT COUNT(*) FROM Contracts")));
        Assert.Equal("Upgrade Failure Contract", Convert.ToString(Scalar(databasePath, "SELECT Title FROM Contracts LIMIT 1")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(databasePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ContractAnalyses'")));
        Assert.Single(Directory.GetFiles(_root, "draftengine.db.backup-*"));
    }

    private static async Task CreateDatabaseAtAsync(string path, string targetMigration)
    {
        using var context = CreateContext(path);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static ServiceProvider BuildProvider(string path)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DraftEngineDbContext>(options => options.UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString));
        return services.BuildServiceProvider();
    }

    private static DraftEngineDbContext CreateContext(string path) =>
        new(new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString)
            .Options);

    private static void SeedContract(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contracts (
                UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                LastGeneratedAt, FinalFilePath, AssembledText)
            VALUES (
                1, 'Upgrade Failure Contract', 'MutualNda', 1, 'Acme', '', '[]',
                NULL, 'California', 12, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                NULL, '', '1. Confidentiality');
            """;
        command.ExecuteNonQuery();
    }

    private static void Execute(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
