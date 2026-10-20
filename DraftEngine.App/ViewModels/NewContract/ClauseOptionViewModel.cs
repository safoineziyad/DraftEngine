using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// View model for one clause row on step 3 of the wizard: a checkbox plus an
/// expandable preview of the clause body with intake placeholders substituted.
/// </summary>
public partial class ClauseOptionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _previewText;

    /// <summary>The clause template id (sent to the contract service on generation).</summary>
    public int Id { get; }

    /// <summary>The clause title shown next to the checkbox.</summary>
    public string Title { get; }

    /// <summary>Whether the clause is optional (informational badge on the row).</summary>
    public bool IsOptional { get; }

    /// <summary>The raw clause template with <c>{{Placeholder}}</c> tokens (used to rebuild previews).</summary>
    public string BodyTemplate { get; }

    /// <summary>Creates a clause option view model.</summary>
    public ClauseOptionViewModel(int id, string title, string bodyTemplate, bool isOptional, bool isSelected, string previewText)
    {
        Id = id;
        Title = title;
        BodyTemplate = bodyTemplate;
        IsOptional = isOptional;
        _isSelected = isSelected;
        _previewText = previewText;
    }

    /// <summary>Updates the preview text (called when the intake values change and the user returns to this step).</summary>
    public void UpdatePreview(string previewText) => PreviewText = previewText;
}
