using DraftEngine.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DraftEngine.App.Services;

/// <summary>
/// Simple string-key based navigation service. Maps page keys to view model types
/// and resolves them from the DI container (view models are registered as singletons).
/// </summary>
public class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> PageRegistry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardViewModel),
        ["new-contract"] = typeof(NewContractViewModel),
        ["contracts"] = typeof(ContractsListViewModel),
        ["contract-detail"] = typeof(ContractDetailViewModel),
        ["settings"] = typeof(SettingsViewModel),
    };

    private readonly IServiceProvider _services;

    /// <inheritdoc />
    public event Action<object?>? CurrentViewModelChanged;

    /// <inheritdoc />
    public object? CurrentViewModel { get; private set; }

    /// <inheritdoc />
    public string CurrentKey { get; private set; } = string.Empty;

    /// <summary>Creates the navigation service.</summary>
    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public bool Navigate(string key)
    {
        if (!PageRegistry.TryGetValue(key, out var viewModelType))
        {
            Log.Warning("Navigation failed: unknown page key '{Key}'.", key);
            return false;
        }

        var viewModel = _services.GetRequiredService(viewModelType);
        CurrentViewModel = viewModel;
        CurrentKey = key;
        CurrentViewModelChanged?.Invoke(viewModel);
        return true;
    }
}
