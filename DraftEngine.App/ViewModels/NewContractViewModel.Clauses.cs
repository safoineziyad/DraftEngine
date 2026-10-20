using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using DraftEngine.App.ViewModels.NewContract;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Step 3 (clause selection) logic for the New Contract wizard.
/// </summary>
public partial class NewContractViewModel
{
    /// <summary>Loads the clause library for the selected contract type, grouped by category.</summary>
    private async Task LoadClausesAsync()
    {
        IsLoadingClauses = true;
        try
        {
            var clauses = await _clauseService.GetClausesByContractTypeAsync(
                SelectedContractType?.Name ?? MutualNdaType);

            ClauseGroups.Clear();
            var values = GetFieldValues();

            foreach (var group in clauses
                .GroupBy(clause => clause.Category)
                .OrderBy(g => g.Min(c => c.SortOrder)))
            {
                var groupViewModel = new ClauseCategoryGroupViewModel(group.Key);
                foreach (var clause in group.OrderBy(c => c.SortOrder))
                {
                    var option = new ClauseOptionViewModel(
                        clause.Id,
                        clause.Title,
                        clause.BodyTemplate,
                        clause.IsOptional,
                        clause.IsDefaultSelected,
                        _fieldService.ReplacePlaceholders(clause.BodyTemplate, values));
                    option.PropertyChanged += OnClauseOptionChanged;
                    groupViewModel.Clauses.Add(option);
                }

                ClauseGroups.Add(groupViewModel);
            }

            _clausesLoaded = true;
            TotalClauseCount = AllClauseOptions().Count();
            UpdateSelectedClauseCount();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load clauses for the new contract wizard.");
            _dialogs.ShowError(
                "The clause library could not be loaded.\n\n" +
                "Please go back and try again, or restart DraftEngine.");
        }
        finally
        {
            IsLoadingClauses = false;
        }
    }

    /// <summary>Rebuilds the clause previews with the current intake values (used when returning to step 3).</summary>
    private void RefreshClausePreviews()
    {
        var values = GetFieldValues();
        foreach (var option in AllClauseOptions())
        {
            option.UpdatePreview(_fieldService.ReplacePlaceholders(option.BodyTemplate, values));
        }
    }

    private IEnumerable<ClauseOptionViewModel> AllClauseOptions() =>
        ClauseGroups.SelectMany(group => group.Clauses);

    private void OnClauseOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClauseOptionViewModel.IsSelected))
        {
            UpdateSelectedClauseCount();
        }
    }

    private void UpdateSelectedClauseCount()
    {
        SelectedClauseCount = AllClauseOptions().Count(option => option.IsSelected);
    }
}
