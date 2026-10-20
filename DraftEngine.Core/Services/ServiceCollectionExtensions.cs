using DraftEngine.Core.Interfaces;
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
    /// <see cref="IDocumentExportService"/>. Call this once at startup, after
    /// <c>IDatabaseContextFactory</c> has been registered (DraftEngine.Data's
    /// AddDraftEngineData does that). The services are stateless and open one
    /// short-lived DbContext per operation, so no context is ever shared or pinned
    /// for the application's lifetime.
    /// The AI service is optional: when <see cref="IContractAiService"/> is not registered,
    /// generation stays template-only and chat edits report a friendly message.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddContractServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IFieldService>(provider =>
            new FieldService(
                provider.GetRequiredService<IDatabaseContextFactory>(),
                provider.GetService<ILogger<FieldService>>()));

        services.AddTransient<IClauseService>(provider =>
            new ClauseService(provider.GetRequiredService<IDatabaseContextFactory>()));

        services.AddTransient<IContractService>(provider => new ContractService(
            provider.GetRequiredService<IDatabaseContextFactory>(),
            provider.GetRequiredService<IFieldService>(),
            provider.GetService<IContractAiService>(),
            provider.GetService<ILogger<ContractService>>()));

        services.AddTransient<IChatService>(provider =>
            new ChatService(provider.GetRequiredService<IDatabaseContextFactory>()));

        services.AddTransient<IDocumentExportService>(provider =>
            new DocumentExportService(provider.GetRequiredService<IDatabaseContextFactory>()));

        return services;
    }
}
