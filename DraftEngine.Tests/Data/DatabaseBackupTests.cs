using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the pre-migration database backup performed by
/// <see cref="DatabaseService"/> (audit Finding #4): a copy of the SQLite file must be
/// taken just before pending migrations are applied, and old backups must be pruned.
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
    public void BackupBeforePendingMigrations_WithPendingMigrations_CreatesBackupFile()
    {
        // The in-memory context has no migration history table, so every migration is
        // pending; the "database file" is a dummy on disk whose copy we can assert.
        var databasePath = CreateDatabaseFile();

        DatabaseService.BackupBeforePendingMigrations(_dbContext, databasePath, new EmptyServiceProvider());

        var backups = Directory.GetFiles(_tempDirectory, "*.backup-*");
        var backup = Assert.Single(backups);
        Assert.Equal(File.ReadAllBytes(databasePath), File.ReadAllBytes(backup));
    }

    [Fact]
    public void BackupBeforePendingMigrations_NoPendingMigrations_CreatesNoBackup()
    {
        // Apply the migrations first so the history table marks the schema as current.
        _dbContext.Database.Migrate();
        var databasePath = CreateDatabaseFile();

        DatabaseService.BackupBeforePendingMigrations(_dbContext, databasePath, new EmptyServiceProvider());

        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.backup-*"));
    }

    [Fact]
    public void BackupBeforePendingMigrations_MissingFile_IsNoOp()
    {
        var databasePath = Path.Combine(_tempDirectory, "draftengine.db");

        DatabaseService.BackupBeforePendingMigrations(_dbContext, databasePath, new EmptyServiceProvider());

        Assert.False(File.Exists(databasePath));
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.backup-*"));
    }

    [Fact]
    public void PruneBackups_KeepsOnlyTheNewestBackups()
    {
        var databasePath = CreateDatabaseFile();

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

    /// <summary>Creates a small dummy file standing in for the SQLite database.</summary>
    private string CreateDatabaseFile()
    {
        var path = Path.Combine(_tempDirectory, "draftengine.db");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    /// <summary>Minimal service provider: DatabaseService only resolves loggers from it, optionally.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
