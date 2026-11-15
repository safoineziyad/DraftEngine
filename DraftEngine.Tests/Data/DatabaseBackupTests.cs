using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the pre-migration database backup performed by
/// <see cref="DatabaseService"/>: a consistent SQLite snapshot must be taken just before
/// pending migrations are applied (including committed WAL contents), and old backups
/// must be pruned.
/// </summary>
public class DatabaseBackupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DraftEngineDbContext> _options;
    private readonly DraftEngineDbContext _dbContext;
    private readonly string _tempDirectory;

    public DatabaseBackupTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new DraftEngineDbContext(_options);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "DraftEngineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort; leftover files in %TEMP% are harmless.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BackupBeforePendingMigrations_WithPendingMigrations_CreatesSnapshotOfEveryRow()
    {
        // The in-memory context has no migration history table, so every migration is
        // pending; the snapshot must capture the real on-disk database file.
        var databasePath = CreateRealDatabaseFile();

        var backupPath = DatabaseService.BackupBeforePendingMigrations(
            _dbContext, databasePath, new EmptyServiceProvider());

        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(ReadValues(databasePath), ReadValues(backupPath!));
    }

    [Fact]
    public void BackupBeforePendingMigrations_NoPendingMigrations_CreatesNoBackup()
    {
        // Apply the migrations first so the history table marks the schema as current.
        _dbContext.Database.Migrate();
        var databasePath = CreateRealDatabaseFile();

        var backupPath = DatabaseService.BackupBeforePendingMigrations(
            _dbContext, databasePath, new EmptyServiceProvider());

        Assert.Null(backupPath);
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.backup-*"));
    }

    [Fact]
    public void BackupBeforePendingMigrations_MissingFile_IsNoOp()
    {
        var databasePath = Path.Combine(_tempDirectory, "draftengine.db");

        var backupPath = DatabaseService.BackupBeforePendingMigrations(
            _dbContext, databasePath, new EmptyServiceProvider());

        Assert.Null(backupPath);
        Assert.False(File.Exists(databasePath));
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.backup-*"));
    }

    [Fact]
    public void CreateSnapshot_FromWalDatabase_IncludesCommittedRows()
    {
        // Regression for the upgrade data loss: EF leaves SQLite in WAL mode, so a plain
        // File.Copy of only the .db file loses rows that are still in the -wal sidecar.
        var databasePath = Path.Combine(_tempDirectory, "wal.db");
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        using var live = new SqliteConnection(builder.ConnectionString);
        live.Open();
        Execute(live, "PRAGMA journal_mode=WAL;");
        Execute(live, "CREATE TABLE Data(Id INTEGER PRIMARY KEY, Value TEXT);");
        Execute(live, "INSERT INTO Data(Value) VALUES ('committed-in-wal');");

        Assert.Equal("wal", ExecuteScalar(live, "PRAGMA journal_mode;"));
        Assert.True(new FileInfo(databasePath + "-wal").Length > 0, "expected a non-empty WAL sidecar");

        var snapshotPath = Path.Combine(_tempDirectory, "wal-snapshot.db");
        DatabaseService.CreateSnapshot(databasePath, snapshotPath);

        Assert.Equal(new[] { "committed-in-wal" }, ReadValues(snapshotPath));
    }

    [Fact]
    public void WalDatabase_BackupAndRestore_PreservesEveryTable()
    {
        var databasePath = Path.Combine(_tempDirectory, "wal-full.db");
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        using (var context = new DraftEngineDbContext(
                   new DbContextOptionsBuilder<DraftEngineDbContext>()
                       .UseSqlite(builder.ConnectionString)
                       .Options))
        {
            context.Database.Migrate();
        }

        using (var live = new SqliteConnection(builder.ConnectionString))
        {
            live.Open();
            Execute(live, "PRAGMA journal_mode=WAL;");
            Execute(live, "PRAGMA wal_autocheckpoint=0;");
            Execute(live, """
                INSERT INTO Contracts (
                    UserId, Title, ContractType, Status, ClientName, ClientEmail, PartiesJson,
                    EffectiveDate, GoverningLaw, TermMonths, CreatedAt, UpdatedAt,
                    LastGeneratedAt, FinalFilePath, AssembledText)
                VALUES (
                    1, 'WAL backup contract', 'MutualNda', 1, 'Acme', '', '[]',
                    NULL, 'California', 12, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                    NULL, '', '1. Confidentiality');
                INSERT INTO ContractFields (ContractId, Key, Value) VALUES (1, 'client', 'Acme');
                INSERT INTO ContractClauses (ContractId, ClauseId, Selected, CustomBodyOverride)
                VALUES (1, 1, 1, '');
                INSERT INTO ChatMessages (ContractId, Role, Content, CreatedAt)
                VALUES (1, 0, 'hello', '2026-01-01T00:00:00Z');
                INSERT INTO ContractVersions (ContractId, VersionNumber, Text, Reason, CreatedAt)
                VALUES (1, 1, 'v1', 'Generated', '2026-01-01T00:00:00Z');
                INSERT INTO GeneratedDocuments (ContractId, FilePath, FileType, GeneratedAt, Version)
                VALUES (1, 'contract.docx', 0, '2026-01-01T00:00:00Z', 1);
                """);

            Assert.Equal("wal", ExecuteScalar(live, "PRAGMA journal_mode;")?.ToString());
            Assert.True(new FileInfo(databasePath + "-wal").Length > 0,
                "expected committed rows to still live in the WAL sidecar");
        }

        var snapshotPath = databasePath + ".snapshot";
        DatabaseService.CreateSnapshot(databasePath, snapshotPath);
        var expected = CountAllTables(databasePath);

        // Damage the live database so only a real restore can bring the rows back.
        using (var damage = new SqliteConnection(builder.ConnectionString))
        {
            damage.Open();
            Execute(damage, "PRAGMA foreign_keys=OFF;");
            Execute(damage, "DELETE FROM ContractFields;");
            Execute(damage, "DELETE FROM ContractClauses;");
            Execute(damage, "DELETE FROM ChatMessages;");
            Execute(damage, "DELETE FROM ContractVersions;");
            Execute(damage, "DELETE FROM GeneratedDocuments;");
            Execute(damage, "DELETE FROM Contracts;");
        }

        DatabaseService.RestoreSnapshot(databasePath, snapshotPath);

        var restored = CountAllTables(databasePath);
        Assert.Equal(expected.Keys.OrderBy(name => name), restored.Keys.OrderBy(name => name));
        foreach (var (table, count) in expected)
        {
            Assert.Equal(count, restored[table]);
        }

        Assert.True(expected["Contracts"] > 0, "the fixture must contain a contract before restoring");
    }

    [Fact]
    public void PruneBackups_KeepsOnlyTheNewestBackups()
    {
        var databasePath = CreateRealDatabaseFile();

        // Five fake backups with clearly ordered timestamps.
        for (var i = 0; i < 5; i++)
        {
            var backup = databasePath + ".backup-2026010" + (i + 1) + "-000000";
            File.WriteAllText(backup, $"backup {i}");
            File.SetLastWriteTimeUtc(backup, new DateTime(2026, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
        }

        DatabaseService.PruneBackups(databasePath);

        var remaining = Directory.GetFiles(_tempDirectory, "*.backup-*");
        Assert.Equal(3, remaining.Length);

        // The three newest survive; the two oldest are gone.
        var names = remaining.Select(Path.GetFileName).ToList();
        Assert.Contains("draftengine.db.backup-20260105-000000", names);
        Assert.Contains("draftengine.db.backup-20260104-000000", names);
        Assert.Contains("draftengine.db.backup-20260103-000000", names);
        Assert.DoesNotContain("draftengine.db.backup-20260101-000000", names);
        Assert.DoesNotContain("draftengine.db.backup-20260102-000000", names);
    }

    /// <summary>Creates a real SQLite database file with a single row.</summary>
    private string CreateRealDatabaseFile()
    {
        var path = Path.Combine(_tempDirectory, "draftengine.db");
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        Execute(connection, "CREATE TABLE Data(Id INTEGER PRIMARY KEY, Value TEXT);");
        Execute(connection, "INSERT INTO Data(Value) VALUES ('backup-me');");
        return path;
    }

    /// <summary>Row counts for every user table in the database.</summary>
    private static Dictionary<string, long> CountAllTables(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();

        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            counts[table] = Convert.ToInt64(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static List<string> ReadValues(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Data ORDER BY Id";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>Minimal service provider: DatabaseService only resolves loggers from it, optionally.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
