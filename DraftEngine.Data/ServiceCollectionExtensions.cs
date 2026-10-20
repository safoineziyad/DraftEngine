using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.Data;

/// <summary>
/// DI registration for the DraftEngine data layer: the SQLite DbContext.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DraftEngineDbContext"/> (SQLite; environment variables such as
    /// <c>%APPDATA%</c> in the connection string are expanded here, since SQLite does not
    /// expand them itself). The DbContext-backed application services
    /// (contract, clause, field, chat) are registered separately by
    /// <c>AddContractServices</c> in DraftEngine.Core.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQLite connection string, possibly containing environment variables.</param>
    public static IServiceCollection AddDraftEngineData(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        var expandedConnectionString = Environment.ExpandEnvironmentVariables(connectionString);

        // Transient contexts: the page view models are singletons, so a scoped context would
        // become one app-lifetime context shared by everything (stale data, tracking clashes).
        // Options are singleton so resolving from the root provider never violates scoping.
        services.AddDbContext<DraftEngineDbContext>(
            options => options.UseSqlite(expandedConnectionString),
            contextLifetime: ServiceLifetime.Transient,
            optionsLifetime: ServiceLifetime.Singleton);

        return services;
    }
}
