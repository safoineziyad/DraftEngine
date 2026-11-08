using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>A selectable filter or sort entry with a stable id and localized display text.</summary>
public sealed record SelectOption(string Id, string DisplayName);

/// <summary>
/// View model for the contracts list page: loads the user's contracts and offers
/// search, type filtering, sorting, opening, and confirmed deletion.
/// </summary>
public partial class ContractsListViewModel : ObservableObject
{
    // Authentication is not wired yet; the app currently runs as a single local user.
    private const int CurrentUserId = 1;

    // Stable ids keep filtering/sorting correct when the display text changes language.
    private const string AllTypesId = "all";
    private const string SortByUpdatedId = "updated";
    private const string SortByCreatedId = "created";

    private readonly IContractService _contractService;
    private readonly IContractEditService _contractEditService;
    private readonly INavigationService _navigation;
    private readonly IWizardDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private List<Contract> _allContracts = new();
    private string? _errorKey;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => _localization.GetString("Nav.Contracts");

    /// <summary>The contracts currently visible (after search/filter/sort).</summary>
    public ObservableCollection<ContractRowViewModel> Contracts { get; } = new();

    /// <summary>Contract type filter options: localized "All" plus every distinct type loaded.</summary>
    public ObservableCollection<SelectOption> TypeFilters { get; } = new();

    /// <summary>Sort options for the list.</summary>
    public ObservableCollection<SelectOption> SortOptions { get; } = new();

    /// <summary>Search text filtering contracts by title or client name (case-insensitive).</summary>
    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>Selected contract type filter id (<see cref="AllTypesId"/> for everything).</summary>
    [ObservableProperty]
    private string selectedTypeFilterId = AllTypesId;

    /// <summary>Selected sort mode id.</summary>
    [ObservableProperty]
    private string selectedSortModeId = SortByUpdatedId;

    /// <summary>True when any filter is active (search or type filter not "all").</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) || SelectedTypeFilterId != AllTypesId;

    /// <summary>Resets all filters and sorting to defaults.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedTypeFilterId = AllTypesId;
        SelectedSortModeId = SortByUpdatedId;
    }

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
    public ContractsListViewModel(
        IContractService contractService,
        IContractEditService contractEditService,
        INavigationService navigation,
        IWizardDialogService dialogs,
        ILocalizationService? localization = null)
    {
        _contractService = contractService ?? throw new ArgumentNullException(nameof(contractService));
        _contractEditService = contractEditService ?? throw new ArgumentNullException(nameof(contractEditService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();

        RebuildSortOptions();
        RebuildTypeFilters();
        _localization.LanguageChanged += OnLanguageChanged;

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
        ClearError();
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
            SetError("Contracts.ErrorLoad");
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
        _navigation.Navigate("contract-detail", contract.Id);
    }

    /// <summary>Creates a draft copy of the selected contract and opens it.</summary>
    [RelayCommand]
    private async Task DuplicateAsync(Contract? contract)
    {
        if (contract is null || IsLoading) return;

        IsLoading = true;
        ClearError();
        try
        {
            var copy = await _contractEditService.DuplicateContractAsync(
                contract.Id,
                _localization.GetString("Contracts.DuplicateTitle", contract.Title));
            if (copy is null)
            {
                SetError("Contracts.ErrorDuplicateMissing");
                return;
            }

            _navigation.Navigate("contract-detail", copy.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to duplicate contract {ContractId}.", contract.Id);
            SetError("Contracts.ErrorDuplicate");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Deletes the selected contract after an explicit confirmation from the user.</summary>
    [RelayCommand]
    private async Task DeleteAsync(Contract? contract)
    {
        if (contract is null) return;

        var confirm = _dialogs.Confirm(
            _localization.GetString("Contracts.DeleteConfirm", contract.Title),
            _localization.GetString("Contracts.DeleteConfirmTitle"));
        if (!confirm) return;

        IsLoading = true;
        ClearError();
        try
        {
            await _contractService.DeleteAsync(contract.Id);
            _allContracts.Remove(contract);

            // The deleted contract may have been the only one of its type: rebuild the
            // dropdown (it resets the selection to "All" when the type is gone) instead of
            // leaving an option that can only ever filter to an empty list.
            RebuildTypeFilters();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete contract {ContractId}.", contract.Id);
            SetError("Contracts.ErrorDelete");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnSelectedTypeFilterIdChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnSelectedSortModeIdChanged(string value) => ApplyFilters();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void OnCurrentViewModelChanged(object? viewModel)
    {
        if (ReferenceEquals(viewModel, this))
            _ = LoadAsync();
    }

    private void RebuildTypeFilters()
    {
        var selectedId = SelectedTypeFilterId;
        var types = _allContracts
            .Select(c => c.ContractType?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Select(ContractTypeRegistry.GetByDisplayName)
            .GroupBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TypeFilters.Clear();
        TypeFilters.Add(new SelectOption(AllTypesId, _localization.GetString("Contracts.AllTypes")));
        foreach (var type in types)
            TypeFilters.Add(new SelectOption(type.Id, type.DisplayName));

        SelectedTypeFilterId = types.Any(type =>
            string.Equals(type.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ? types.First(type => string.Equals(type.Id, selectedId, StringComparison.OrdinalIgnoreCase)).Id
            : AllTypesId;
    }

    private void RebuildSortOptions()
    {
        var selectedId = SelectedSortModeId;

        SortOptions.Clear();
        SortOptions.Add(new SelectOption(SortByUpdatedId, _localization.GetString("Contracts.SortUpdated")));
        SortOptions.Add(new SelectOption(SortByCreatedId, _localization.GetString("Contracts.SortCreated")));

        SelectedSortModeId = SortOptions.Any(option =>
            string.Equals(option.Id, selectedId, StringComparison.Ordinal))
            ? selectedId
            : SortByUpdatedId;
    }

    private void ApplyFilters()
    {
        IEnumerable<Contract> query = _allContracts;

        var search = SearchText?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            query = query.Where(c =>
                c.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.ClientName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.GoverningLaw?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.AssembledText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrEmpty(SelectedTypeFilterId) && SelectedTypeFilterId != AllTypesId)
        {
            var selectedTypeId = SelectedTypeFilterId;
            query = query.Where(c =>
                string.Equals(
                    ContractTypeRegistry.GetByDisplayName(c.ContractType ?? string.Empty).Id,
                    selectedTypeId,
                    StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedSortModeId == SortByCreatedId
            ? query.OrderByDescending(c => c.CreatedAt)
            : query.OrderByDescending(c => c.UpdatedAt);

        Contracts.Clear();
        foreach (var contract in query)
            Contracts.Add(new ContractRowViewModel(contract, _localization));

        UpdateEmptyStates();
    }

    private void UpdateEmptyStates()
    {
        IsEmpty = Contracts.Count == 0 && _allContracts.Count == 0;
        IsFilteredEmpty = Contracts.Count == 0 && _allContracts.Count > 0;
    }

    private void SetError(string key)
    {
        _errorKey = key;
        ErrorMessage = _localization.GetString(key);
    }

    private void ClearError()
    {
        _errorKey = null;
        ErrorMessage = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));

        // Filters, sort labels and row values are all language-dependent and must be
        // rebuilt while keeping the user's current selection (stored as stable ids).
        RebuildSortOptions();
        RebuildTypeFilters();
        ApplyFilters();

        if (_errorKey is not null)
            ErrorMessage = _localization.GetString(_errorKey);
    }
}
