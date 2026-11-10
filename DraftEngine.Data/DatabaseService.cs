using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DraftEngine.Data;

/// <summary>
/// Database service for initializing and updating the database.
/// </summary>
public static class DatabaseService
{
    /// <summary>Number of pre-migration database backups kept on disk.</summary>
    private const int BackupFileCount = 3;

    /// <summary>
    /// Creates the database directory if needed (e.g. %APPDATA%\DraftEngine), takes a
    /// consistent snapshot of the database when pending migrations are about to be applied,
    /// and then applies those migrations. If a migration fails, the pre-migration snapshot is
    /// restored so the user is left exactly where they started. Failures are logged and
    /// rethrown with a friendly message instead of crashing with an opaque provider exception.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <exception cref="InvalidOperationException">Thrown when the database cannot be initialized.</exception>
    public static void EnsureDatabaseCreated(IServiceProvider services)
    {
        string? databasePath = null;
        string? backupPath = null;

        try
        {
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DraftEngineDbContext>();

            databasePath = ResolveDataDirectory(dbContext);
            // A consistent snapshot must exist before any schema change runs. A failed
            // backup aborts the migration rather than risking unbacked data loss.
            backupPath = BackupBeforePendingMigrations(dbContext, databasePath, services);
            dbContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            var restored = databasePath is not null &&
                backupPath is not null &&
                TryRestoreBackup(databasePath, backupPath, services);

            var friendlyMessage = restored
                ? "DraftEngine could not apply its database upgrade, so the pre-upgrade backup was restored. " +
                  "Your data is unchanged. Restart the application; if the problem persists, contact support."
                : "DraftEngine could not initialize its database (applying the schema failed). " +
                  "Check that the application data folder is writable, then restart the application.";

            Log(services, LogLevel.Error, friendlyMessage, ex);
            throw new InvalidOperationException(friendlyMessage, ex);
        }
    }

    /// <summary>
    /// Expands environment variables (e.g. %APPDATA%) in the SQLite connection string,
    /// creates the target directory when the file lives in it, and repoints the
    /// connection at the resolved path.
    /// </summary>
    /// <returns>
    /// The full path of the SQLite database file, or null when the provider is not SQLite
    /// (e.g. the InMemory provider used in tests) or the connection string carries no file path.
    /// </returns>
    private static string? ResolveDataDirectory(DraftEngineDbContext dbContext)
    {
        if (dbContext.Database.ProviderName is not { } provider ||
            !provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return null; // Non-SQLite provider (e.g. InMemory in tests): nothing to resolve.
        }

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(connectionString);
        var dataSource = ExtractDataSource(expanded);

        if (!string.IsNullOrWhiteSpace(dataSource) &&
            dataSource is not ":memory:" &&
            !dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(dataSource);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!string.Equals(expanded, connectionString, StringComparison.Ordinal))
            {
                // SQLite does not expand %VAR% itself, so point the connection at the resolved path.
                var connection = dbContext.Database.GetDbConnection();
                connection.ConnectionString = expanded;
            }

            return fullPath;
        }

        if (!string.Equals(expanded, connectionString, StringComparison.Ordinal))
        {
            // SQLite does not expand %VAR% itself, so point the connection at the resolved path.
            var connection = dbContext.Database.GetDbConnection();
            connection.ConnectionString = expanded;
        }

        return null;
    }

    /// <summary>
    /// Takes a consistent SQLite snapshot of the database file to a timestamped
    /// <c>.backup-</c> sibling just before pending migrations are applied, then prunes old
    /// backups down to <see cref="BackupFileCount"/>.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="File.Copy(string,string)"/> is unsafe: EF Core leaves SQLite in
    /// WAL mode, so committed rows can live in the <c>-wal</c> sidecar and a file copy would
    /// produce a stale or even structurally incomplete database. The SQLite online backup
    /// API reads the main file and WAL through the engine and writes a standalone snapshot.
    /// </remarks>
    /// <returns>The backup path, or null when no backup was needed.</returns>
    internal static string? BackupBeforePendingMigrations(
        DraftEngineDbContext dbContext,
        string? databasePath,
        IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return null; // Fresh database (or non-file provider): nothing to lose.
        }

        var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count == 0)
        {
            return null; // Schema already current: no need to churn backup files.
        }

        var backupPath = databasePath + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");

        try
        {
            CreateSnapshot(databasePath, backupPath);
            PruneBackups(databasePath);

            Log(services, LogLevel.Information,
                $"Backed up the database to '{backupPath}' before applying " +
                $"{pendingMigrations.Count} pending migration(s).");

            return backupPath;
        }
        catch (Exception ex)
        {
            // Never apply a schema change without a usable snapshot: abort and let the
            // caller report a clear error instead of risking the only copy of user data.
            Log(services, LogLevel.Error,
                "The pre-migration database backup could not be created; the upgrade was aborted.", ex);
            throw new InvalidOperationException(
                "DraftEngine could not back up the database before upgrading it, so the upgrade was aborted. " +
                "Your data is unchanged. Check that the application data folder is writable, then restart.",
                ex);
        }
    }

    /// <summary>Writes a consistent, standalone SQLite snapshot of <paramref name="sourcePath"/>.</summary>
    internal static void CreateSnapshot(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Pooling = false
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Pooling = false
        };

        using var source = new SqliteConnection(sourceBuilder.ConnectionString);
        source.Open();
        using var destination = new SqliteConnection(destinationBuilder.ConnectionString);
        destination.Open();
        source.BackupDatabase(destination);
    }

    /// <summary>
    /// Restores a pre-migration snapshot over the live database after a failed migration.
    /// Returns false when the restore itself fails (the original error is still reported).
    /// </summary>
    private static bool TryRestoreBackup(string databasePath, string backupPath, IServiceProvider services)
    {
        try
        {
            // Release every pooled handle before replacing the files.
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath + "-wal");
            DeleteIfExists(databasePath + "-shm");
            File.Copy(backupPath, databasePath, overwrite: true);

            Log(services, LogLevel.Warning,
                $"The database upgrade failed; restored the pre-upgrade snapshot '{backupPath}'.");
            return true;
        }
        catch (Exception ex)
        {
            Log(services, LogLevel.Error,
                $"The database upgrade failed and the snapshot '{backupPath}' could not be restored.", ex);
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort; the snapshot copy below still overwrites the database file.
        }
    }

    /// <summary>Keeps only the <see cref="BackupFileCount"/> newest backups for a database file.</summary>
    internal static void PruneBackups(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var prefix = Path.GetFileName(databasePath) + ".backup-";
        var backups = new DirectoryInfo(directory)
            .GetFiles(prefix + "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var stale in backups.Skip(BackupFileCount))
        {
            try
            {
                stale.Delete();
            }
            catch (Exception)
            {
                // A locked or read-only backup must never fail startup; it just stays around.
            }
        }
    }

    /// <summary>
    /// Extracts the Data Source / Filename value from a SQLite connection string.
    /// </summary>
    private static string? ExtractDataSource(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
            {
                return segment[(separatorIndex + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }

    /// <summary>
    /// Logs through the application's ILoggerFactory when available, otherwise to the console.
    /// </summary>
    private static void Log(IServiceProvider services, LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var loggerFactory = services.GetService<ILoggerFactory>();
            if (loggerFactory is not null)
            {
                var logger = loggerFactory.CreateLogger("DraftEngine.Data.DatabaseService");
                if (exception is not null)
                {
                    logger.Log(level, exception, message);
                }
                else
                {
                    logger.Log(level, message);
                }

                return;
            }
        }
        catch (Exception)
        {
            // Logging must never be the reason initialization fails; fall back to console.
        }

        Console.Error.WriteLine($"[{level}] DraftEngine.Data.DatabaseService: {message}");
        if (exception is not null)
        {
            Console.Error.WriteLine(exception);
        }
    }
}
