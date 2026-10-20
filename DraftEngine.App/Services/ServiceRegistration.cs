using DraftEngine.AI.Services;
using DraftEngine.App.Configuration;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using DraftEngine.Documents.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace DraftEngine.App.Services;

/// <summary>
/// Registers all DraftEngine application services in the dependency injection container.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Adds the DraftEngine DbContext (SQLite, path from <see cref="AppOptions.Data"/>),
    /// page view models, navigation, the AI drafting service (real OpenAI client when a key
    /// is configured, friendly stand-in otherwise) and document building services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Application options loaded from appsettings.json + environment.</param>
    public static IServiceCollection AddDraftEngine(this IServiceCollection services, AppOptions options)
    {
        services.AddSingleton(options);

        // SQLite + the DbContext (connection string from appsettings.json; environment
        // variables such as %APPDATA% are expanded inside AddDraftEngineData).
        services.AddDraftEngineData(options.Data.ConnectionString);

        // DbContext-backed application services (contract, clause, field, chat, export).
        services.AddContractServices();

        // Page view models (singletons: navigation caches them).
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<NewContractViewModel>();
        services.AddSingleton<ContractsListViewModel>();
        services.AddSingleton<ContractDetailViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        // Navigation.
        services.AddSingleton<INavigationService, NavigationService>();

        // New Contract wizard support (dialog helper).
        services.AddContractWizard();

        // AI drafting: the real OpenAI client when a key is configured, otherwise a
        // stand-in that throws the typed "key missing" exception so the UI can guide
        // the user to Settings. Non-AI features keep working either way.
        // The key saved in Settings (encrypted) wins over appsettings/environment, and is
        // re-read on every call so changes apply without a restart.
        services.AddAppSettings();
        services.AddSingleton<IContractAiService, SettingsBackedAiService>();

        // Document building (the composite delegates to the specialized builders).
        services.AddSingleton<IDocxBuilder, DocxBuilder>();
        services.AddSingleton<IPdfBuilder, PdfBuilder>();
        services.AddSingleton<IDocumentBuilder, DocumentBuilder>();

        return services;
    }
}
