using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DraftEngine.App.Tests;

public sealed class UpgradeMigrationTests
{
    private const string Version022LastSchemaMigration = "20261021133317_AddContractVersionHistory";
    private const string ContractTitle = "Upgrade Smoke Contract";

    [Fact]
    public async Task Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "DraftEngineUpgradeTests", Guid.NewGuid().ToString("N"));
        var legacyDirectory = Path.Combine(root, "legacy");
        var copiedDirectory = Path.Combine(root, "copied");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(copiedDirectory);

        try
        {
            var legacyDatabasePath = Path.Combine(legacyDirectory, "draftengine.db");
            var connectionString = $"Data Source={legacyDatabasePath}";
            var legacyOptions = new DbContextOptionsBuilder<DraftEngineDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var legacyDatabase = new DraftEngineDbContext(legacyOptions))
            {
                await legacyDatabase.GetService<IMigrator>().MigrateAsync(Version022LastSchemaMigration);
            }

            await using (var legacyConnection = new SqliteConnection(connectionString))
            {
                await legacyConnection.OpenAsync();
                await using (var seedCommand = legacyConnection.CreateCommand())
                {
                    seedCommand.CommandText = """
                        INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                        VALUES ('20261022110520_SeedDefaultUser', '8.0.0');
                        """;
                    await seedCommand.ExecuteNonQueryAsync();
                }

                await using var command = legacyConnection.CreateCommand();
                command.CommandText = """
                    INSERT INTO Contracts (
                        UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                        EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                        LastGeneratedAt, FinalFilePath, AssembledText)
                    VALUES (
                        1, $title, 'MutualNda', 0, 'Upgrade Test Client', '', '[]',
                        NULL, 'California', 12, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                        NULL, '', '1. Upgrade preservation test');
                    """;
                command.Parameters.AddWithValue("$title", ContractTitle);
                await command.ExecuteNonQueryAsync();
            }

            var copiedDatabasePath = Path.Combine(copiedDirectory, "draftengine.db");
            // EF Core leaves SQLite in WAL mode: committed rows can live in the -wal
            // sidecar. A plain File.Copy of only the .db file yields a stale (even
            // structurally incomplete) database, so snapshot through the engine.
            CopyDatabase(legacyDatabasePath, copiedDatabasePath);

            var appAssembly = typeof(App).Assembly.Location;
            Assert.True(File.Exists(appAssembly), $"Expected application assembly at '{appAssembly}'.");
            Assert.True(
                File.Exists(Path.Combine(Path.GetDirectoryName(appAssembly)!, "appsettings.json")),
                "The application settings file must be available beside the built application assembly.");

            var reportPath = Path.Combine(root, "upgrade-report.json");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appAssembly)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(appAssembly);
            startInfo.ArgumentList.Add("--verify-upgrade");
            startInfo.Environment["DRAFTENGINE_DATA_DIR"] = copiedDirectory;
            startInfo.Environment["DRAFTENGINE_EXPECTED_CONTRACT_TITLE"] = ContractTitle;
            startInfo.Environment["DRAFTENGINE_UPGRADE_REPORT"] = reportPath;
            startInfo.Environment["Features__AutoUpdateEnabled"] = "false";
            startInfo.Environment["Features__OnboardingEnabled"] = "false";
            startInfo.Environment["Features__LicensingEnabled"] = "false";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The application upgrade verifier could not be started.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync();
                throw new TimeoutException("The application upgrade verifier exceeded 60 seconds.");
            }

            Assert.True(File.Exists(reportPath), "The application did not write an upgrade verification report.");
            Assert.True(
                process.ExitCode == 0,
                $"The application upgrade verifier exited with code {process.ExitCode}: " +
                await File.ReadAllTextAsync(reportPath));
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.Equal("passed", report.RootElement.GetProperty("Status").GetString());
            Assert.True(report.RootElement.GetProperty("ContractSurvived").GetBoolean());
            Assert.Equal(0, report.RootElement.GetProperty("PendingMigrations").GetInt32());
            Assert.True(report.RootElement.GetProperty("PreMigrationBackupCount").GetInt32() > 0);

            var backupPath = Assert.Single(Directory.GetFiles(copiedDirectory, "draftengine.db.backup-*"));
            // Byte equality is not a valid invariant for a live SQLite database: opening the
            // copied file can checkpoint WAL and update the header change counter. The real
            // guarantee is that the snapshot contains every row of the pre-migration data.
            AssertDatabasesHaveSameData(copiedDatabasePath, backupPath);

            await using (var upgradedDatabase = new DraftEngineDbContext(
                new DbContextOptionsBuilder<DraftEngineDbContext>().UseSqlite(
                    $"Data Source={copiedDatabasePath}").Options))
            {
                Assert.True(await upgradedDatabase.Contracts.AnyAsync(contract => contract.Title == ContractTitle));
                Assert.Empty(await upgradedDatabase.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Asserts two SQLite databases contain exactly the same rows in every user table.
    /// This is stronger for data safety than a byte comparison (which a header counter can
    /// break) and still fails if any row is missing, added or altered.
    /// </summary>
    private static void AssertDatabasesHaveSameData(string firstPath, string secondPath)
    {
        string[] tables =
        [
            "Users", "Contracts", "Clauses", "ContractClauses", "ContractFields",
            "ChatMessages", "GeneratedDocuments", "ContractVersions"
        ];

        using var first = Open(firstPath);
        using var second = Open(secondPath);
        // The clause library is seeded reference data: migrations may append clauses, so it
        // must retain every pre-upgrade row rather than stay the same size.
        var growsByDesign = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Clauses" };

        foreach (var table in tables)
        {
            // Compare by column name, not ordinal: SQLite migrations rebuild tables, which
            // reorders columns even when every value is preserved. Match rows by Id so
            // appended reference rows cannot hide a lost row.
            var expected = ReadRows(second, table);
            var actual = ReadRows(first, table);
            if (!growsByDesign.Contains(table))
            {
                Assert.True(expected.Count == actual.Count,
                    $"Table '{table}' row count changed from {expected.Count} to {actual.Count}.");
            }
            else
            {
                Assert.True(actual.Count >= expected.Count,
                    $"Table '{table}' lost rows: {expected.Count} before, {actual.Count} after.");
            }

            var actualById = actual.ToDictionary(row => row["Id"]);
            foreach (var expectedRow in expected)
            {
                Assert.True(actualById.TryGetValue(expectedRow["Id"], out var upgradedRow),
                    $"Table '{table}' lost the row with Id {expectedRow["Id"]}.");
                foreach (var (column, value) in expectedRow)
                {
                    Assert.True(upgradedRow.TryGetValue(column, out var upgradedValue),
                        $"Table '{table}' lost the column '{column}'.");
                    Assert.True(value == upgradedValue,
                        $"Table '{table}' row Id {expectedRow["Id"]} column '{column}' changed from '{value}' to '{upgradedValue}'.");
                }
            }
        }
    }

    private static SqliteConnection Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static List<Dictionary<string, string>> ReadRows(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, string>>();
        while (reader.Read())
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[reader.GetName(i)] = reader.IsDBNull(i)
                    ? "<null>"
                    : reader.GetValue(i).ToString() ?? string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }

    /// <summary>Copies a SQLite database consistently, including any WAL contents.</summary>
    private static void CopyDatabase(string sourcePath, string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false };
        var destinationBuilder = new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false };

        using var source = new SqliteConnection(sourceBuilder.ConnectionString);
        source.Open();
        using var destination = new SqliteConnection(destinationBuilder.ConnectionString);
        destination.Open();
        source.BackupDatabase(destination);
    }
}
