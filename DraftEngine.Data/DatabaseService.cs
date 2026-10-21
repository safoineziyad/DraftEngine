using System;
using System.IO;
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
    /// Creates the database directory if needed (e.g. %APPDATA%\DraftEngine), backs the
    /// database file up when pending migrations are about to be applied, and then applies
    /// those migrations. Failures are logged and rethrown with a friendly message
    /// instead of crashing with an opaque provider exception.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <exception cref="InvalidOperationException">Thrown when the database cannot be initialized.</exception>
    public static void EnsureDatabaseCreated(IServiceProvider services)
    {
        try
        {
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DraftEngineDbContext>();

            var databasePath = ResolveDataDirectory(dbContext);
            BackupBeforePendingMigrations(dbContext, databasePath, services);
            dbContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            const string friendlyMessage =
                "DraftEngine could not initialize its database (applying the schema failed). " +
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
    /// Copies the SQLite file to a timestamped <c>.backup-</c> sibling just before pending
    /// migrations are applied, then prunes old backups down to <see cref="BackupFileCount"/>.
    /// This guarantees a failed or buggy migration never destroys the only copy of the
    /// user's contracts. A backup failure is logged and never blocks startup: the migration
    /// itself may still succeed, and refusing to start would be a worse outcome.
    /// </summary>
    internal static void BackupBeforePendingMigrations(
        DraftEngineDbContext dbContext,
        string? databasePath,
        IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return; // Fresh database (or non-file provider): nothing to lose.
        }

        try
        {
            var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();
            if (pendingMigrations.Count == 0)
            {
                return; // Schema already current: no need to churn backup files.
            }

            var backupPath = databasePath + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(databasePath, backupPath, overwrite: false);
            PruneBackups(databasePath);

            Log(services, LogLevel.Information,
                $"Backed up the database to '{backupPath}' before applying " +
                $"{pendingMigrations.Count} pending migration(s).");
        }
        catch (Exception ex)
        {
            Log(services, LogLevel.Warning,
                "The database backup could not be created; continuing with the migration.", ex);
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
