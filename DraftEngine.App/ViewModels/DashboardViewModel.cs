using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Interfaces;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the dashboard page: recent contracts, quick stats and the
/// prominent "New Contract" action.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    /// <summary>
    /// The local user id used until authentication is available (single-user desktop app).
    /// </summary>
    private const int CurrentUserId = 1;

    /// <summary>
    /// Average time saved per drafted contract, used for the "hours saved" estimate (0.75 h).
    /// </summary>
    private const double HoursSavedPerContract = 0.75;

    private readonly IContractService _contractService;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private bool _hasLoadError;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => _localization.GetString("Nav.Dashboard");

    /// <summary>The contracts of the current user, most recent first.</summary>
    public ObservableCollection<ContractRowViewModel> RecentContracts { get; } = new();

    /// <summary>Whether contract data is currently being loaded.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>The total number of contracts created by the user.</summary>
    [ObservableProperty] private int _contractCount;

    /// <summary>Estimated hours saved (contracts count &times; 0.75 h, rounded).</summary>
    [ObservableProperty] private int _hoursSavedEstimate;

    /// <summary>Friendly error message shown in the error banner; null when there is no error.</summary>
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Whether the recent contracts list has at least one row.</summary>
    public bool HasContracts => RecentContracts.Count > 0;

    /// <summary>Whether a friendly load error is currently displayed.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Whether the empty-state message should be shown. Never true while an
    /// error is displayed, so a failed load can't masquerade as "no contracts yet".
    /// </summary>
    public bool ShowEmptyState => !HasContracts && !IsLoading && !HasError;

    /// <summary>Creates the dashboard view model and loads the recent contracts.</summary>
    public DashboardViewModel(
        IContractService contractService,
        INavigationService navigation,
        ILocalizationService? localization = null)
    {
        _contractService = contractService;
        _navigation = navigation;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
        _navigation.CurrentViewModelChanged += OnPageChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Navigates to the new contract wizard.</summary>
    [RelayCommand]
    private void NewContract() => _navigation.Navigate("new-contract");

    /// <summary>Opens the detail page for the clicked contract.</summary>
    [RelayCommand]
    private void OpenContract(ContractRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _navigation.Navigate("contract-detail", row.Contract.Id);
    }

    // The page view models are singletons, so refresh the data each time the
    // dashboard becomes the visible page.
    private void OnPageChanged(object? viewModel)
    {
        if (ReferenceEquals(viewModel, this))
        {
            _ = LoadContractsAsync();
        }
    }

    /// <summary>Loads the user's contracts and refreshes the stats.</summary>
    private async Task LoadContractsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        _hasLoadError = false;
        try
        {
            var contracts = await _contractService.GetAllAsync(CurrentUserId);

            RecentContracts.Clear();
            foreach (var contract in contracts)
            {
                RecentContracts.Add(new ContractRowViewModel(contract, _localization));
            }

            ContractCount = RecentContracts.Count;
            HoursSavedEstimate = (int)System.Math.Round(ContractCount * HoursSavedPerContract);
        }
        catch (System.Exception ex)
        {
            // Never show raw exceptions; log the detail and surface a friendly banner
            // so a database failure isn't mistaken for an empty dashboard.
            Log.Error(ex, "Failed to load contracts for the dashboard.");
            RecentContracts.Clear();
            ContractCount = 0;
            HoursSavedEstimate = 0;
            _hasLoadError = true;
            ErrorMessage = _localization.GetString("Dashboard.ErrorLoad");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasContracts));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));

        // Contract rows derive their status, title fallback and date text from the
        // active language, so rebuild them from the same contracts.
        var contracts = RecentContracts.Select(row => row.Contract).ToList();
        RecentContracts.Clear();
        foreach (var contract in contracts)
        {
            RecentContracts.Add(new ContractRowViewModel(contract, _localization));
        }

        OnPropertyChanged(nameof(HasContracts));
        OnPropertyChanged(nameof(ShowEmptyState));

        if (_hasLoadError)
        {
            ErrorMessage = _localization.GetString("Dashboard.ErrorLoad");
        }
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
