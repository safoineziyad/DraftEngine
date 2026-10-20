using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Shell view model: exposes the current page view model and the left-nav commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AppOptions? _appOptions;

    /// <summary>The view model of the page currently displayed in the content area.</summary>
    public object? CurrentViewModel => _navigation.CurrentViewModel;

    /// <summary>The window title (from appsettings.json App:DisplayName).</summary>
    public string Title => _appOptions?.App.DisplayName ?? "DraftEngine";

    /// <summary>Creates the main shell view model and navigates to the dashboard.</summary>
    public MainViewModel(INavigationService navigation, AppOptions? appOptions = null)
    {
        _navigation = navigation;
        _appOptions = appOptions;
        _navigation.CurrentViewModelChanged += _ => OnPropertyChanged(nameof(CurrentViewModel));
        _navigation.Navigate("dashboard");
    }

    /// <summary>Navigates to the dashboard page.</summary>
    [RelayCommand]
    private void NavigateDashboard() => _navigation.Navigate("dashboard");

    /// <summary>Navigates to the new contract page.</summary>
    [RelayCommand]
    private void NavigateNewContract() => _navigation.Navigate("new-contract");

    /// <summary>Navigates to the contracts list page.</summary>
    [RelayCommand]
    private void NavigateContracts() => _navigation.Navigate("contracts");

    /// <summary>Navigates to the contract detail page.</summary>
    [RelayCommand]
    private void NavigateContractDetail() => _navigation.Navigate("contract-detail");

    /// <summary>Navigates to the settings page.</summary>
    [RelayCommand]
    private void NavigateSettings() => _navigation.Navigate("settings");
}
