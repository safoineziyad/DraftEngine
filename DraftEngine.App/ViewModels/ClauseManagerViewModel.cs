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
using DraftEngine.Core.Models.ContractTypes;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the clause manager page: lists system and user clauses and allows
/// user clauses to be created, edited, duplicated and archived.
/// </summary>
public partial class ClauseManagerViewModel : ObservableObject, INavigationAware
{
    private const int CurrentUserId = 1;
    private const string AllFilter = "All";
    private readonly IClauseManagementService _clauseService;
    private readonly List<Clause> _allClauses = new();
    private int _loadVersion;

    public string PageTitle => "Clause Manager";
    public ObservableCollection<Clause> Clauses { get; } = new();
    public ObservableCollection<string> ContractTypes { get; } =
        new(ContractTypeRegistry.All.Select(type => type.DisplayName));
    public ObservableCollection<string> CategoryFilters { get; } = new() { AllFilter };
    public ObservableCollection<string> SourceFilters { get; } = new() { AllFilter, "System", "User" };

    [ObservableProperty]
    private string selectedContractType = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId).DisplayName;

    [ObservableProperty]
    private string selectedCategoryFilter = AllFilter;

    [ObservableProperty]
    private string selectedSourceFilter = AllFilter;

    [ObservableProperty]
    private Clause? selectedClause;

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? placeholderWarning;

    public bool CanSave => IsEditMode && SelectedClause is { Source: "User" };
    public bool CanEditSelected => SelectedClause is { Source: "User" } && !SelectedClause.IsArchived;
    public bool CanDuplicateSelected => SelectedClause is not null;
    public bool CanArchiveSelected => SelectedClause is { Source: "User", IsArchived: false };
    public bool HasError => ErrorMessage is not null;
    public bool HasNoClauses => Clauses.Count == 0;
    public bool HasPlaceholderWarning => PlaceholderWarning is not null;

    public ClauseManagerViewModel(IClauseManagementService clauseService)
    {
        _clauseService = clauseService ?? throw new ArgumentNullException(nameof(clauseService));
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();

    partial void OnSelectedContractTypeChanged(string value)
    {
        SelectedCategoryFilter = AllFilter;
        _ = LoadAsync();
    }

    partial void OnSelectedCategoryFilterChanged(string value) => RefreshVisibleClauses();

    partial void OnSelectedSourceFilterChanged(string value) => RefreshVisibleClauses();

    partial void OnSelectedClauseChanged(Clause? value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDuplicateSelected));
        OnPropertyChanged(nameof(CanArchiveSelected));
    }

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnPlaceholderWarningChanged(string? value) => OnPropertyChanged(nameof(HasPlaceholderWarning));

    [RelayCommand]
    private async Task LoadAsync()
    {
        var loadVersion = ++_loadVersion;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var clauses = await _clauseService.GetClausesAsync(SelectedContractType) ?? new List<Clause>();
            if (loadVersion != _loadVersion) return;
            _allClauses.Clear();
            _allClauses.AddRange(clauses.OrderBy(clause => clause.SortOrder));
            UpdateCategoryFilters();
            RefreshVisibleClauses();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load clauses.");
            ErrorMessage = "We couldn't load clauses. Please try again.";
        }
        finally
        {
            if (loadVersion == _loadVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private void NewClause()
    {
        SelectedClause = new Clause
        {
            ContractType = ContractTypeRegistry.GetByDisplayName(SelectedContractType).Id,
            Source = "User",
            OwnerUserId = CurrentUserId,
            IsSystem = false,
            IsUserDefined = true
        };
        PlaceholderWarning = null;
        IsEditMode = true;
    }

    [RelayCommand]
    private void EditClause(Clause? clause)
    {
        if (clause is not { Source: "User", IsArchived: false }) return;
        SelectedClause = clause.Clone();
        PlaceholderWarning = null;
        IsEditMode = true;
    }

    [RelayCommand]
    private async Task DuplicateClauseAsync(Clause? clause)
    {
        if (clause is null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await _clauseService.DuplicateClauseAsync(
                clause.Id, ContractTypeRegistry.GetByDisplayName(SelectedContractType).Id);
            SelectedClause = null;
            IsEditMode = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to duplicate clause {ClauseId}.", clause.Id);
            ErrorMessage = "We couldn't duplicate the clause. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ArchiveClauseAsync(Clause? clause)
    {
        if (clause is not { Source: "User", IsArchived: false }) return;

        var confirm = MessageBox.Show(
            $"Archive \"{clause.Title}\"?\n\nThis hides it from clause selection.",
            "Archive clause",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await _clauseService.ArchiveUserClauseAsync(clause.Id);
            SelectedClause = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to archive clause {ClauseId}.", clause.Id);
            ErrorMessage = "We couldn't archive the clause. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var clause = SelectedClause;
        if (clause is null || !CanSave) return;

        if (string.IsNullOrWhiteSpace(clause.Title) || string.IsNullOrWhiteSpace(clause.BodyTemplate))
        {
            MessageBox.Show("Please enter a clause title and body template.", "Clause Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (isValid, unknownKeys) = await _clauseService.ValidatePlaceholdersAsync(clause.BodyTemplate);
        if (!isValid)
        {
            PlaceholderWarning = $"Unknown placeholder(s): {string.Join(", ", unknownKeys)}. " +
                "These won't be resolved when drafting.";
            var proceed = MessageBox.Show(
                $"{PlaceholderWarning}\n\nContinue anyway?",
                "Unknown placeholders",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes) return;
        }
        else
        {
            PlaceholderWarning = null;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            if (clause.Id == 0)
                await _clauseService.CreateUserClauseAsync(
                    ContractTypeRegistry.GetByDisplayName(SelectedContractType).Id, clause);
            else
                await _clauseService.UpdateUserClauseAsync(clause.Id, clause);

            SelectedClause = null;
            IsEditMode = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save clause.");
            ErrorMessage = "We couldn't save the clause. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        SelectedClause = null;
        IsEditMode = false;
        PlaceholderWarning = null;
        ErrorMessage = null;
    }

    private void UpdateCategoryFilters()
    {
        var selected = SelectedCategoryFilter;
        CategoryFilters.Clear();
        CategoryFilters.Add(AllFilter);
        foreach (var category in _allClauses.Select(clause => clause.Category)
                     .Where(category => !string.IsNullOrWhiteSpace(category))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
        {
            CategoryFilters.Add(category);
        }

        SelectedCategoryFilter = CategoryFilters.Contains(selected) ? selected : AllFilter;
    }

    private void RefreshVisibleClauses()
    {
        var visible = _allClauses.Where(clause =>
            (SelectedCategoryFilter == AllFilter ||
             string.Equals(clause.Category, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedSourceFilter == AllFilter ||
             string.Equals(clause.Source, SelectedSourceFilter, StringComparison.OrdinalIgnoreCase)));
        Clauses.Clear();
        foreach (var clause in visible)
            Clauses.Add(clause);
        OnPropertyChanged(nameof(HasNoClauses));
    }
}

public static class ClauseExtensions
{
    /// <summary>Produces a shallow copy of a clause for editing without mutating the original.</summary>
    public static Clause Clone(this Clause source) => new()
    {
        Id = source.Id,
        ContractType = source.ContractType,
        Category = source.Category,
        Title = source.Title,
        BodyTemplate = source.BodyTemplate,
        IsOptional = source.IsOptional,
        IsDefaultSelected = source.IsDefaultSelected,
        SortOrder = source.SortOrder,
        Source = source.Source,
        OwnerUserId = source.OwnerUserId,
        IsArchived = source.IsArchived,
        IsSystem = source.IsSystem,
        IsUserDefined = source.IsUserDefined
    };
}
