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
/// View model for the clause manager page: lists user-owned clauses and lets the user
/// create, edit, duplicate and archive them. Clauses are scoped to a contract type.
/// </summary>
public partial class ClauseManagerViewModel : ObservableObject
{
    private const int CurrentUserId = 1;
    private const string DefaultContractType = "Mutual NDA";

    private readonly IClauseManagementService _clauseService;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "Clause Manager";

    /// <summary>User-owned clauses for the selected contract type.</summary>
    public ObservableCollection<Clause> Clauses { get; } = new();

    /// <summary>The clause currently being edited (null when creating a new clause).</summary>
    [ObservableProperty]
    private Clause? selectedClause;

    /// <summary>True when the edit form is in edit/save mode rather than new-clause mode.</summary>
    [ObservableProperty]
    private bool isEditMode;

    partial void OnSelectedClauseChanged(Clause? value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>True when a clause is selected/being edited and can be saved.</summary>
    public bool CanSave => SelectedClause is not null;

    /// <summary>True while clauses are being loaded or saved.</summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>Friendly error message shown in the error banner; null when there is no error.</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Clauses filtered to non-archived user clauses.</summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>True when there are no clauses to display.</summary>
    public bool HasNoClauses => Clauses.Count == 0;

    /// <summary>Creates the clause manager view model.</summary>
    public ClauseManagerViewModel(IClauseManagementService clauseService)
    {
        _clauseService = clauseService ?? throw new ArgumentNullException(nameof(clauseService));
        _ = LoadAsync();
    }

    /// <summary>Loads the user's clauses for the default contract type.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var clauses = await _clauseService.GetUserClausesAsync(DefaultContractType) ?? new List<Clause>();
            Clauses.Clear();
            foreach (var clause in clauses.OrderBy(c => c.SortOrder))
                Clauses.Add(clause);
            OnPropertyChanged(nameof(HasNoClauses));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load clauses.");
            ErrorMessage = "We couldn't load your clauses. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Prepares the form for creating a new clause.</summary>
    [RelayCommand]
    private void NewClause()
    {
        SelectedClause = new Clause { ContractType = DefaultContractType, Source = "User", OwnerUserId = CurrentUserId };
        IsEditMode = true;
    }

    /// <summary>Opens the selected clause for editing.</summary>
    [RelayCommand]
    private void EditClause(Clause? clause)
    {
        if (clause is null) return;
        SelectedClause = clause.Clone();
        IsEditMode = true;
    }

    /// <summary>Duplicates the selected clause.</summary>
    [RelayCommand]
    private async Task DuplicateClauseAsync(Clause? clause)
    {
        if (clause is null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await _clauseService.DuplicateClauseAsync(clause.Id, DefaultContractType);
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

    /// <summary>Archives the selected clause after user confirmation.</summary>
    [RelayCommand]
    private async Task ArchiveClauseAsync(Clause? clause)
    {
        if (clause is null) return;

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

    /// <summary>Validates placeholders and saves the edited/new clause.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var clause = SelectedClause;
        if (clause is null) return;

        if (string.IsNullOrWhiteSpace(clause.Title))
        {
            MessageBox.Show("Please enter a clause title.", "Clause Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(clause.BodyTemplate))
        {
            MessageBox.Show("Please enter the clause body template.", "Clause Manager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate placeholders before saving; warn but do not block on unknown keys.
        var (isValid, unknownKeys) = await _clauseService.ValidatePlaceholdersAsync(clause.BodyTemplate);
        if (!isValid)
        {
            var warning = $"Unknown placeholder(s): {string.Join(", ", unknownKeys)}.\n\n" +
                "These won't be resolved when drafting. Continue anyway?";
            var proceed = MessageBox.Show(warning, "Unknown placeholders",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes) return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            if (clause.Id == 0)
            {
                await _clauseService.CreateUserClauseAsync(DefaultContractType, clause);
            }
            else
            {
                await _clauseService.UpdateUserClauseAsync(clause.Id, clause);
            }
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

    /// <summary>Cancels editing and clears the form.</summary>
    [RelayCommand]
    private void CancelEdit()
    {
        SelectedClause = null;
        IsEditMode = false;
        ErrorMessage = null;
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
        IsSystem = source.IsSystem
    };
}
