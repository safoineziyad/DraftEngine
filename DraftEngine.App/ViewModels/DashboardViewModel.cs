using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
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

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "Dashboard";

    /// <summary>The contracts of the current user, most recent first.</summary>
    public ObservableCollection<ContractRowViewModel> RecentContracts { get; } = new();

    /// <summary>Whether contract data is currently being loaded.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>The total number of contracts created by the user.</summary>
    [ObservableProperty] private int _contractCount;

    /// <summary>Estimated hours saved (contracts count &times; 0.75 h, rounded).</summary>
    [ObservableProperty] private int _hoursSavedEstimate;

    /// <summary>Whether the recent contracts list has at least one row.</summary>
    public bool HasContracts => RecentContracts.Count > 0;

    /// <summary>Whether the empty-state message should be shown.</summary>
    public bool ShowEmptyState => !HasContracts && !IsLoading;

    /// <summary>Creates the dashboard view model and loads the recent contracts.</summary>
    public DashboardViewModel(IContractService contractService, INavigationService navigation)
    {
        _contractService = contractService;
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += OnPageChanged;
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

        _navigation.Navigate("contract-detail");
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
        try
        {
            var contracts = await _contractService.GetAllAsync(CurrentUserId);

            RecentContracts.Clear();
            foreach (var contract in contracts)
            {
                RecentContracts.Add(new ContractRowViewModel(contract));
            }

            ContractCount = RecentContracts.Count;
            HoursSavedEstimate = (int)System.Math.Round(ContractCount * HoursSavedPerContract);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Failed to load contracts for the dashboard.");
            RecentContracts.Clear();
            ContractCount = 0;
            HoursSavedEstimate = 0;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasContracts));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
}
