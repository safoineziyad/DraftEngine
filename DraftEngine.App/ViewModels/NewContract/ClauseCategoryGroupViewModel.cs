using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// Groups clause options by category for the step 3 checklist.
/// </summary>
public class ClauseCategoryGroupViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    /// <summary>The clause category name shown as the group header.</summary>
    public string Category { get; }

    /// <summary>The clauses belonging to this category.</summary>
    public ObservableCollection<ClauseOptionViewModel> Clauses { get; } = new();

    /// <summary>Localized clause count, with distinct singular and plural resources.</summary>
    public string ClauseCountText => _localization.GetString(
        Clauses.Count == 1 ? "Wizard.ClauseCount.One" : "Wizard.ClauseCount.Other",
        Clauses.Count);

    /// <summary>Creates a clause category group.</summary>
    public ClauseCategoryGroupViewModel(string category, ILocalizationService? localization = null)
    {
        Category = category;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
        Clauses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ClauseCountText));
    }

    public void RefreshLocalization() =>
        OnPropertyChanged(nameof(ClauseCountText));
}
