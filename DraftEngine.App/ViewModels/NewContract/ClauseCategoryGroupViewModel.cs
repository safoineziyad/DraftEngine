using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// Groups clause options by category for the step 3 checklist.
/// </summary>
public class ClauseCategoryGroupViewModel : ObservableObject
{
    /// <summary>The clause category name shown as the group header.</summary>
    public string Category { get; }

    /// <summary>The clauses belonging to this category.</summary>
    public ObservableCollection<ClauseOptionViewModel> Clauses { get; } = new();

    /// <summary>Creates a clause category group.</summary>
    public ClauseCategoryGroupViewModel(string category)
    {
        Category = category;
    }
}
