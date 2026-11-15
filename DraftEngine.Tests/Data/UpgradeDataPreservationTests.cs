using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Upgrade data preservation. Each fixture is built from a real migration target (the same
/// migration list the released app used), then filled with contracts and every dependent
/// row, snapshotted through the SQLite engine, and upgraded to the current schema. The test
/// fails if any user-owned row disappears.
/// </summary>
public sealed class UpgradeDataPreservationTests : IDisposable
{
    /// <summary>Last schema migration shipped in v0.2.2 (tag v0.2.2).</summary>
    private const string Version022Target = "20261021133317_AddContractVersionHistory";

    /// <summary>
    /// Pre-0.4 migration head. There is no v0.3.0 tag in this repository, so this schema is
    /// used as the 0.3.x-era stand-in for upgrade testing.
    /// </summary>
    private const string Version03EraTarget = "20261106211906_AlignClauseData";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DraftEngineUpgradeData", Guid.NewGuid().ToString("N"));

    public UpgradeDataPreservationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Version022Database_WithAllDependentRows_PreservesEveryRow()
    {
        var legacyPath = Path.Combine(_root, "legacy-022.db");
        await CreateDatabaseAtAsync(legacyPath, Version022Target);
        var seeded = SeedLegacyRows(legacyPath, contractCount: 3);
        Assert.Equal(3, seeded["Contracts"]);

        var before = ReadCounts(legacyPath);
        Assert.Equal(3, before["ContractFields"]);
        Assert.Equal(3, before["ChatMessages"]);
        Assert.Equal(3, before["GeneratedDocuments"]);
        Assert.Equal(3, before["ContractVersions"]);
        Assert.Equal(6, before["ContractClauses"]);

        var upgradePath = Path.Combine(_root, "upgrade-022.db");
        DatabaseService.CreateSnapshot(legacyPath, upgradePath);
        foreach (var table in before.Keys)
        {
            Assert.Equal(before[table], ReadCounts(upgradePath)[table]);
        }

        await UpgradeToLatestAsync(upgradePath);

        var after = ReadCounts(upgradePath);
        foreach (var table in UserDataTables)
        {
            Assert.Equal(before[table], after[table]);
        }

        // System clause seed grows by design; no pre-existing clause id may disappear.
        Assert.True(after["Clauses"] >= before["Clauses"]);
        Assert.Equal(
            ReadIds(legacyPath, "Clauses"),
            ReadIds(upgradePath, "Clauses").Take((int)before["Clauses"]));

        Assert.Equal(
            new[] { "Legacy Contract 1", "Legacy Contract 2", "Legacy Contract 3" },
            ReadStrings(upgradePath, "SELECT Title FROM Contracts ORDER BY Id"));
    }

    [Fact]
    public async Task Version03EraDatabase_WithDependentRows_PreservesEveryRow()
    {
        var legacyPath = Path.Combine(_root, "legacy-03.db");
        await CreateDatabaseAtAsync(legacyPath, Version03EraTarget);
        SeedLegacyRows(legacyPath, contractCount: 2);

        var before = ReadCounts(legacyPath);
        var upgradePath = Path.Combine(_root, "upgrade-03.db");
        DatabaseService.CreateSnapshot(legacyPath, upgradePath);

        await UpgradeToLatestAsync(upgradePath);

        var after = ReadCounts(upgradePath);
        foreach (var table in UserDataTables)
        {
            Assert.Equal(before[table], after[table]);
        }

        Assert.Equal(
            new[] { "Legacy Contract 1", "Legacy Contract 2" },
            ReadStrings(upgradePath, "SELECT Title FROM Contracts ORDER BY Id"));
    }

    private static readonly string[] UserDataTables =
    [
        "Users", "Contracts", "ContractFields", "ChatMessages",
        "GeneratedDocuments", "ContractVersions", "ContractClauses"
    ];

    private static async Task CreateDatabaseAtAsync(string path, string targetMigration)
    {
        using var context = CreateContext(path);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static async Task UpgradeToLatestAsync(string path)
    {
        using var context = CreateContext(path);
        await context.Database.MigrateAsync();
    }

    private static DraftEngineDbContext CreateContext(string path) =>
        new(new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString)
            .Options);

    /// <summary>Inserts contracts with a field, chat message, document, version and clauses each.</summary>
    private static Dictionary<string, long> SeedLegacyRows(string path, int contractCount)
    {
        using var connection = Open(path);
        for (var index = 1; index <= contractCount; index++)
        {
            Execute(connection, $"""
                INSERT INTO Contracts (
                    UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                    EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                    LastGeneratedAt, FinalFilePath, AssembledText)
                VALUES (
                    1, 'Legacy Contract {index}', 'MutualNda', 1, 'Acme {index}', 'client{index}@example.com',
                    '["Acme {index}","Beta"]', '2026-01-0{index}T00:00:00Z', 'California', {12 + index},
                    '2026-01-0{index}T00:00:00Z', '2026-01-0{index}T00:00:00Z', NULL, '', '1. Confidentiality');
                """);

            long contractId;
            using (var idCommand = connection.CreateCommand())
            {
                idCommand.CommandText = "SELECT last_insert_rowid()";
                contractId = Convert.ToInt64(
                    idCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }

            Execute(connection, $"INSERT INTO ContractFields (ContractId, Key, Value) VALUES ({contractId}, 'Party1', 'Acme {index}');");
            Execute(connection, $"INSERT INTO ChatMessages (ContractId, Role, Content, CreatedAt) VALUES ({contractId}, 0, 'message-{index}', '2026-01-0{index}T00:00:00Z');");
            Execute(connection, $"INSERT INTO GeneratedDocuments (ContractId, FilePath, FileType, GeneratedAt, Version) VALUES ({contractId}, 'doc-{index}.docx', 0, '2026-01-0{index}T00:00:00Z', 1);");
            Execute(connection, $"INSERT INTO ContractVersions (ContractId, VersionNumber, Text, Reason, CreatedAt) VALUES ({contractId}, 1, 'version-{index}', 'Initial', '2026-01-0{index}T00:00:00Z');");
            Execute(connection, $"INSERT INTO ContractClauses (ContractId, ClauseId, Selected, CustomBodyOverride) VALUES ({contractId}, 1, 1, '');");
            Execute(connection, $"INSERT INTO ContractClauses (ContractId, ClauseId, Selected, CustomBodyOverride) VALUES ({contractId}, 2, 1, '');");
        }

        return ReadCounts(path);
    }

    private static Dictionary<string, long> ReadCounts(string path)
    {
        using var connection = Open(path);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in new[]
                 {
                     "Users", "Contracts", "Clauses", "ContractClauses", "ContractFields",
                     "ChatMessages", "GeneratedDocuments", "ContractVersions"
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            counts[table] = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static List<long> ReadIds(string path, string table)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM {table} ORDER BY Id";
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static List<string> ReadStrings(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
