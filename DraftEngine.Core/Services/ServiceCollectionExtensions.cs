using DraftEngine.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DraftEngine.Core.Services;

/// <summary>
/// Dependency injection registration for the DraftEngine contract services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFieldService"/>, <see cref="IClauseService"/>,
    /// <see cref="IContractService"/>, <see cref="IChatService"/> and
    /// <see cref="IDocumentExportService"/>.
    /// Call this once at startup, after the application DbContext has been registered with
    /// AddDbContext (the context is resolved lazily, so call order does not matter).
    /// The AI service is optional: when <see cref="IContractAiService"/> is not registered,
    /// generation stays template-only and chat edits report a friendly message.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddContractServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IFieldService>(provider =>
            new FieldService(ResolveDatabase(provider), provider.GetService<ILogger<FieldService>>()));

        services.AddScoped<IClauseService>(provider =>
            new ClauseService(ResolveDatabase(provider)));

        services.AddScoped<IContractService>(provider => new ContractService(
            ResolveDatabase(provider),
            provider.GetRequiredService<IFieldService>(),
            provider.GetService<IContractAiService>(),
            provider.GetService<ILogger<ContractService>>()));

        services.AddScoped<IChatService>(provider =>
            new ChatService(ResolveDatabase(provider)));

        services.AddScoped<IDocumentExportService>(provider =>
            new DocumentExportService(ResolveDatabase(provider)));

        return services;
    }

    /// <summary>
    /// Resolves the application DbContext. DraftEngine.Core cannot reference DraftEngine.Data,
    /// so the concrete type is obtained at runtime from the registered DbContextOptions
    /// (AddDbContext registers both the concrete context and the non-generic options).
    /// </summary>
    private static DbContext ResolveDatabase(IServiceProvider provider)
    {
        if (provider.GetService<DbContext>() is { } direct)
        {
            return direct;
        }

        var options = provider.GetRequiredService<DbContextOptions>();
        return (DbContext)provider.GetRequiredService(options.ContextType);
    }
}
