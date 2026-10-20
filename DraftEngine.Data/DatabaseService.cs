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
    /// <summary>
    /// Creates the database directory if needed (e.g. %APPDATA%\DraftEngine) and applies
    /// any pending migrations. Failures are logged and rethrown with a friendly message
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

            ResolveDataDirectory(dbContext);
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
    private static void ResolveDataDirectory(DraftEngineDbContext dbContext)
    {
        if (dbContext.Database.ProviderName is not { } provider ||
            !provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return; // Non-SQLite provider (e.g. InMemory in tests): nothing to resolve.
        }

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var expanded = Environment.ExpandEnvironmentVariables(connectionString);
        var dataSource = ExtractDataSource(expanded);

        if (!string.IsNullOrWhiteSpace(dataSource) &&
            dataSource is not ":memory:" &&
            !dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        if (!string.Equals(expanded, connectionString, StringComparison.Ordinal))
        {
            // SQLite does not expand %VAR% itself, so point the connection at the resolved path.
            var connection = dbContext.Database.GetDbConnection();
            connection.ConnectionString = expanded;
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
