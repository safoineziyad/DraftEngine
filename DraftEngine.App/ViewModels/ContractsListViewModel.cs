using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the contracts list page: loads the user's contracts and offers
/// search, type filtering, sorting, opening, and confirmed deletion.
/// </summary>
public partial class ContractsListViewModel : ObservableObject
{
    // Authentication is not wired yet; the app currently runs as a single local user.
    private const int CurrentUserId = 1;
    private const string AllTypesFilter = "All";
    private const string SortByUpdated = "Recently updated";
    private const string SortByCreated = "Date created";

    private readonly IContractService _contractService;
    private readonly INavigationService _navigation;
    private List<Contract> _allContracts = new();

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "Contracts";

    /// <summary>The contracts currently visible (after search/filter/sort).</summary>
    public ObservableCollection<Contract> Contracts { get; } = new();

    /// <summary>Contract type filter options: "All" plus every distinct type loaded.</summary>
    public ObservableCollection<string> TypeFilters { get; } = new() { AllTypesFilter };

    /// <summary>Sort options for the list.</summary>
    public IReadOnlyList<string> SortOptions { get; } = new[] { SortByUpdated, SortByCreated };

    /// <summary>Search text filtering contracts by title or client name (case-insensitive).</summary>
    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>Selected contract type filter (<see cref="AllTypesFilter"/> for everything).</summary>
    [ObservableProperty]
    private string selectedTypeFilter = AllTypesFilter;

    /// <summary>Selected sort mode.</summary>
    [ObservableProperty]
    private string selectedSortMode = SortByUpdated;

    /// <summary>True while contracts are being fetched or deleted.</summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>Friendly error message shown in the error banner; null when there is no error.</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>True when no contracts exist at all (empty state rather than filter results).</summary>
    [ObservableProperty]
    private bool isEmpty;

    /// <summary>True when filters/search hide every contract.</summary>
    [ObservableProperty]
    private bool isFilteredEmpty;

    /// <summary>True when a friendly error is displayed.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>Creates the view model and loads the contract list.</summary>
    public ContractsListViewModel(IContractService contractService, INavigationService navigation)
    {
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

        // View models are singletons: refresh the list every time this page becomes visible.
        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        _ = LoadAsync();
    }

    /// <summary>Reloads the contracts list.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _allContracts = await _contractService.GetAllAsync(CurrentUserId) ?? new List<Contract>();
            RebuildTypeFilters();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            // Never show raw exceptions; log the detail and give the user a way forward.
            // Previously loaded rows stay visible so the page doesn't go blank.
            Log.Error(ex, "Failed to load the contracts list.");
            ErrorMessage = "We couldn't load your contracts. Please try again.";
            UpdateEmptyStates();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Navigates to the new contract page.</summary>
    [RelayCommand]
    private void NewContract() => _navigation.Navigate("new-contract");

    /// <summary>Opens the selected contract in the contract detail page.</summary>
    [RelayCommand]
    private void OpenContract(Contract? contract)
    {
        if (contract is null) return;
        _navigation.Navigate("contract-detail");
    }

    /// <summary>Deletes the selected contract after an explicit confirmation from the user.</summary>
    [RelayCommand]
    private async Task DeleteAsync(Contract? contract)
    {
        if (contract is null) return;

        var confirm = MessageBox.Show(
            $"Delete \"{contract.Title}\"?\n\nThis cannot be undone.",
            "Delete contract",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _contractService.DeleteAsync(contract.Id);
            _allContracts.Remove(contract);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete contract {ContractId}.", contract.Id);
            ErrorMessage = "We couldn't delete the contract. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedSortModeChanged(string value) => ApplyFilters();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void OnCurrentViewModelChanged(object? viewModel)
    {
        if (ReferenceEquals(viewModel, this))
            _ = LoadAsync();
    }

    private void RebuildTypeFilters()
    {
        var selected = SelectedTypeFilter;
        var types = _allContracts
            .Select(c => c.ContractType?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TypeFilters.Clear();
        TypeFilters.Add(AllTypesFilter);
        foreach (var type in types)
            TypeFilters.Add(type);

        SelectedTypeFilter = types.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : AllTypesFilter;
    }

    private void ApplyFilters()
    {
        IEnumerable<Contract> query = _allContracts;

        var search = SearchText?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            query = query.Where(c =>
                c.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.ClientName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrEmpty(SelectedTypeFilter) && SelectedTypeFilter != AllTypesFilter)
        {
            query = query.Where(c =>
                string.Equals(c.ContractType?.Trim(), SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedSortMode == SortByCreated
            ? query.OrderByDescending(c => c.CreatedAt)
            : query.OrderByDescending(c => c.UpdatedAt);

        Contracts.Clear();
        foreach (var contract in query)
            Contracts.Add(contract);

        UpdateEmptyStates();
    }

    private void UpdateEmptyStates()
    {
        IsEmpty = Contracts.Count == 0 && _allContracts.Count == 0;
        IsFilteredEmpty = Contracts.Count == 0 && _allContracts.Count > 0;
    }
}
