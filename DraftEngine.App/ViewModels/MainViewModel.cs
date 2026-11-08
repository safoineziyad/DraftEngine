using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Updates;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Shell view model: exposes the current page view model and the left-nav commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AppOptions? _appOptions;
    private readonly IUpdateService? _updateService;
    private UpdateInfo? _availableUpdate;

    /// <summary>The view model of the page currently displayed in the content area.</summary>
    public object? CurrentViewModel => _navigation.CurrentViewModel;

    /// <summary>Whether the update banner is currently visible.</summary>
    public bool IsUpdateBannerVisible => IsUpdateAvailable && !IsUpdateDismissed;

    /// <summary>Banner text including the version found on GitHub.</summary>
    public string UpdateBannerText => _availableUpdate is null
        ? string.Empty
        : FormatLocalized("Update.Available", $"A new version ({_availableUpdate.Version}) is available!", _availableUpdate.Version);

    /// <summary>Update button text reflecting download progress.</summary>
    public string UpdateButtonText => GetLocalized(
        IsDownloadingUpdate ? "Update.Downloading" : "Update.Now",
        IsDownloadingUpdate ? "Downloading..." : "Update Now");

    /// <summary>Whether the update can currently be started.</summary>
    public bool CanDownloadUpdate => IsUpdateBannerVisible && !IsDownloadingUpdate;

    /// <summary>Download/install error shown in the update banner.</summary>
    [ObservableProperty]
    private string updateErrorMessage = string.Empty;

    [ObservableProperty]
    private bool isUpdateAvailable;

    [ObservableProperty]
    private bool isUpdateDismissed;

    [ObservableProperty]
    private bool isDownloadingUpdate;

    /// <summary>The window title (from appsettings.json App:DisplayName).</summary>
    public string Title => _appOptions?.App.DisplayName ?? "DraftEngine";

    /// <summary>True when the About/Help page should appear in the sidebar.</summary>
    public bool AboutEnabled => _appOptions?.Features.AboutEnabled ?? false;

    /// <summary>Creates the main shell view model and navigates to the dashboard.</summary>
    public MainViewModel(
        INavigationService navigation,
        AppOptions? appOptions = null,
        IUpdateService? updateService = null)
    {
        _navigation = navigation;
        _appOptions = appOptions;
        _updateService = updateService;
        _navigation.CurrentViewModelChanged += _ => OnPropertyChanged(nameof(CurrentViewModel));
        _navigation.Navigate("dashboard");
        if (LocalizationSource.Current.Service is { } localizationService)
        {
            localizationService.LanguageChanged += OnLanguageChanged;
        }

        if (_appOptions?.Features.AutoUpdateEnabled == true && _updateService is not null)
        {
            _ = CheckForUpdatesAsync();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    private static string GetLocalized(string key, string fallback) =>
        LocalizationSource.Current.Service?.GetString(key) ?? fallback;

    private static string FormatLocalized(string key, string fallback, object argument) =>
        LocalizationSource.Current.Service?.GetString(key, argument) ?? fallback;

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(UpdateBannerText));
        DownloadAndInstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdateDismissedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUpdateBannerVisible));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        DownloadAndInstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        DownloadAndInstallUpdateCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _availableUpdate = await _updateService!.CheckForUpdateAsync();
            IsUpdateAvailable = _availableUpdate is not null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitHub release update check failed.");
        }
    }

    /// <summary>Downloads and launches the silent installer, then closes the app.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_updateService is null || _availableUpdate is null)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateErrorMessage = string.Empty;
        try
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "DraftEngineUpdate.exe");
            await _updateService.DownloadInstallerAsync(_availableUpdate, installerPath);

            var installer = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true,
            });
            if (installer is null)
            {
                throw new InvalidOperationException("The update installer could not be started.");
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The downloaded GitHub update could not be installed.");
            UpdateErrorMessage = FormatLocalized(
                "Update.InstallFailed",
                $"The update could not be installed: {ex.Message}",
                ex.Message);
            IsDownloadingUpdate = false;
        }
    }

    /// <summary>Hides the update banner until the next application session.</summary>
    [RelayCommand]
    private void DismissUpdate() => IsUpdateDismissed = true;

    /// <summary>Navigates to the dashboard page.</summary>
    [RelayCommand]
    private void NavigateDashboard() => _navigation.Navigate("dashboard");

    /// <summary>Navigates to the new contract page.</summary>
    [RelayCommand]
    private void NavigateNewContract() => _navigation.Navigate("new-contract");

    /// <summary>Navigates to the contracts list page.</summary>
    [RelayCommand]
    private void NavigateContracts() => _navigation.Navigate("contracts");

    /// <summary>Navigates to the settings page.</summary>
    [RelayCommand]
    private void NavigateSettings() => _navigation.Navigate("settings");

    /// <summary>Navigates to the clause management page.</summary>
    [RelayCommand]
    private void NavigateClauseManagement() => _navigation.Navigate("clause-management");

    /// <summary>Navigates to archived contracts.</summary>
    [RelayCommand]
    private void NavigateArchivedContracts() => _navigation.Navigate("archived-contracts");

    /// <summary>Navigates to the Analyze page.</summary>
    [RelayCommand]
    private void NavigateAnalyze() => _navigation.Navigate("analyze");

    /// <summary>Navigates to the About &amp; Help page.</summary>
    [RelayCommand]
    private void NavigateAbout() => _navigation.Navigate("about");
}
