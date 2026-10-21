using DraftEngine.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DraftEngine.Data;

/// <summary>
/// DI registration for the DraftEngine data layer: the SQLite DbContext and its factory.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DraftEngineDbContext"/> and its <see cref="IDbContextFactory{TContext}"/>
    /// (SQLite; environment variables such as <c>%APPDATA%</c> in the connection string are expanded
    /// here, since SQLite does not expand them itself), plus <see cref="IDatabaseContextFactory"/>,
    /// the abstraction the application services use to open one short-lived context per operation.
    /// The DbContext-backed application services (contract, clause, field, chat) are registered
    /// separately by <c>AddContractServices</c> in DraftEngine.Core.
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

        // Encrypts the sensitive text columns (document, intake answers, chat, versions) at
        // rest with DPAPI — audit Finding #3. Registered as a singleton so the same
        // interceptor instance covers every context handed out by the factory.
        services.AddSingleton<SensitiveDataInterceptor>();

        // Factory pattern (recommended by EF Core for desktop apps): no context is ever
        // shared across operations or pinned for the application's lifetime.
        services.AddDbContextFactory<DraftEngineDbContext>((serviceProvider, options) =>
            options
                .UseSqlite(expandedConnectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<SensitiveDataInterceptor>()));

        // Bridge to the Core-layer abstraction consumed by the application services.
        services.AddSingleton<IDatabaseContextFactory, DatabaseContextFactory>();

        return services;
    }
}
