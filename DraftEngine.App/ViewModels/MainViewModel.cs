using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Shell view model: exposes the current page view model and the left-nav commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly AppOptions? _appOptions;
    private readonly UpdateFlowViewModel? _updateFlow;
    private readonly ILocalizationService? _localization;
    private readonly NavigationItemViewModel? _aboutItem;

    /// <summary>Segoe Fluent Icons glyphs with a Segoe MDL2 Assets fallback for older Windows.</summary>
    private const string IconHome = "\uE80F";
    private const string IconAdd = "\uE710";
    private const string IconList = "\uE8FD";
    private const string IconArchive = "\uE7B8";
    private const string IconAnalyze = "\uE9D2";
    private const string IconClauses = "\uE8F1";
    private const string IconSettings = "\uE713";
    private const string IconInfo = "\uE946";

    /// <summary>The view model of the page currently displayed in the content area.</summary>
    public object? CurrentViewModel => _navigation.CurrentViewModel;

    /// <summary>The key of the page currently shown (drives the sidebar active marker).</summary>
    public string CurrentPageKey => _navigation.CurrentKey;

    /// <summary>Workspace group: dashboard, contract creation and the contract lists.</summary>
    public IReadOnlyList<NavigationItemViewModel> WorkspaceItems { get; }

    /// <summary>Tools group: analysis and clause management.</summary>
    public IReadOnlyList<NavigationItemViewModel> ToolsItems { get; }

    /// <summary>Bottom group: settings and about, pinned below the scrollable list.</summary>
    public IReadOnlyList<NavigationItemViewModel> BottomItems { get; }

    /// <summary>Every navigation item in sidebar order (for tests and keyboard traversal).</summary>
    public IReadOnlyList<NavigationItemViewModel> AllItems { get; }

    /// <summary>Version label from the assembly version, localized ("Version 0.4.0…").</summary>
    public string VersionDisplay =>
        FormatLocalized("Common.Version", $"Version {AppVersion.Display}", AppVersion.Display);

    /// <summary>
    /// Commit label plus hash. Only Debug builds show it in the sidebar; the About page always
    /// shows the full build stamp, including the commit hash and UTC build date.
    /// </summary>
    public string CommitDisplay =>
#if DEBUG
        AppVersion.HasCommitHash
            ? $"{GetLocalized("Main.CommitLabel", "Commit")} {AppVersion.CommitHash}"
            : string.Empty;
#else
        string.Empty;
#endif

    /// <summary>True only in Debug builds that carry a real commit hash.</summary>
    public bool HasCommit =>
#if DEBUG
        AppVersion.HasCommitHash;
#else
        false;
#endif

    /// <summary>The signed update flow (banner, notes, download progress, install confirmation).</summary>
    public UpdateFlowViewModel? UpdateFlow => _updateFlow;

    /// <summary>True when the About sidebar entry shows an update indicator dot.</summary>
    public bool HasUpdateBadge => _updateFlow?.IsUpdateAvailable ?? false;

    /// <summary>The window title (from appsettings.json App:DisplayName).</summary>
    public string Title => _appOptions?.App.DisplayName ?? "DraftEngine";

    /// <summary>True when the About/Help page should appear in the sidebar.</summary>
    public bool AboutEnabled => _appOptions?.Features.AboutEnabled ?? false;

    /// <summary>Creates the main shell view model and navigates to the dashboard.</summary>
    public MainViewModel(
        INavigationService navigation,
        AppOptions? appOptions = null,
        UpdateFlowViewModel? updateFlow = null,
        ILocalizationService? localization = null)
    {
        _navigation = navigation;
        _appOptions = appOptions;
        _updateFlow = updateFlow;
        if (_updateFlow is not null)
        {
            _updateFlow.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(UpdateFlowViewModel.IsUpdateAvailable) or null)
                {
                    OnPropertyChanged(nameof(HasUpdateBadge));
                    if (_aboutItem is not null)
                    {
                        _aboutItem.HasUpdateBadge = _updateFlow.IsUpdateAvailable;
                    }
                }
            };
            _updateFlow.ShutdownRequested += () => Application.Current?.Shutdown();
        }
        _localization = localization ?? LocalizationSource.Current.Service;

        WorkspaceItems =
        [
            new NavigationItemViewModel("dashboard", "Nav.Dashboard", IconHome, NavigateDashboardCommand, _localization),
            new NavigationItemViewModel("new-contract", "Nav.NewContract", IconAdd, NavigateNewContractCommand, _localization),
            new NavigationItemViewModel("contracts", "Nav.Contracts", IconList, NavigateContractsCommand, _localization),
            new NavigationItemViewModel("archived-contracts", "Nav.Archived", IconArchive, NavigateArchivedContractsCommand, _localization),
        ];
        ToolsItems =
        [
            new NavigationItemViewModel("analyze", "Nav.Analyze", IconAnalyze, NavigateAnalyzeCommand, _localization),
            new NavigationItemViewModel("clause-management", "Nav.ClauseManager", IconClauses, NavigateClauseManagementCommand, _localization),
        ];
        var bottomItems = new List<NavigationItemViewModel>
        {
            new("settings", "Nav.Settings", IconSettings, NavigateSettingsCommand, _localization),
        };
        if (AboutEnabled)
        {
            _aboutItem = new NavigationItemViewModel("about", "Nav.About", IconInfo, NavigateAboutCommand, _localization);
            bottomItems.Add(_aboutItem);
        }

        BottomItems = bottomItems;
        AllItems = WorkspaceItems.Concat(ToolsItems).Concat(BottomItems).ToArray();

        _navigation.CurrentViewModelChanged += _ => OnCurrentViewModelChanged();
        _navigation.Navigate("dashboard");
        OnCurrentViewModelChanged();
        if (_localization is { } localizationService)
        {
            localizationService.LanguageChanged += OnLanguageChanged;
        }

        if (_updateFlow is not null)
        {
            // Never blocks startup: the flow waits, then checks at most once per 24 hours.
            _ = _updateFlow.CheckOnStartupAsync();
        }
    }

    private void OnCurrentViewModelChanged()
    {
        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(CurrentPageKey));
        foreach (var item in AllItems)
        {
            item.IsActive = string.Equals(item.Key, _navigation.CurrentKey, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(VersionDisplay));
        OnPropertyChanged(nameof(CommitDisplay));
        foreach (var item in AllItems)
        {
            item.RefreshLocalization();
        }
    }

    private static string GetLocalized(string key, string fallback) =>
        LocalizationSource.Current.Service?.GetString(key) ?? fallback;

    private static string FormatLocalized(string key, string fallback, object argument) =>
        LocalizationSource.Current.Service?.GetString(key, argument) ?? fallback;



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
