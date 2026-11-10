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
            File.Copy(legacyDatabasePath, copiedDatabasePath);

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
            Assert.Equal(File.ReadAllBytes(copiedDatabasePath), File.ReadAllBytes(backupPath));

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
}
