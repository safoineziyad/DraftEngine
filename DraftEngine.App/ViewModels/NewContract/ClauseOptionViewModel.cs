using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;

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
    private readonly ILocalizationService _localization;

    /// <summary>The clause template id (sent to the contract service on generation).</summary>
    public int Id { get; }

    /// <summary>The clause title shown next to the checkbox.</summary>
    public string Title { get; }

    /// <summary>Whether the clause is optional (informational badge on the row).</summary>
    public bool IsOptional { get; }

    /// <summary>The raw clause template with <c>{{Placeholder}}</c> tokens (used to rebuild previews).</summary>
    public string BodyTemplate { get; }

    /// <summary>Localized show/hide label for the clause preview toggle.</summary>
    public string PreviewButtonText =>
        _localization.GetString(IsExpanded ? "Wizard.HidePreview" : "Wizard.Preview");

    /// <summary>Localized accessible name for the selection checkbox.</summary>
    public string IncludeAutomationName => _localization.GetString("Wizard.IncludeClauseAutomation", Title);

    /// <summary>Localized accessible name for the preview toggle.</summary>
    public string PreviewAutomationName => _localization.GetString("Wizard.PreviewClauseAutomation", Title);

    /// <summary>Creates a clause option view model.</summary>
    public ClauseOptionViewModel(
        int id,
        string title,
        string bodyTemplate,
        bool isOptional,
        bool isSelected,
        string previewText,
        ILocalizationService? localization = null)
    {
        Id = id;
        Title = title;
        BodyTemplate = bodyTemplate;
        IsOptional = isOptional;
        _isSelected = isSelected;
        _previewText = previewText;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(PreviewButtonText));

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PreviewButtonText));
        OnPropertyChanged(nameof(IncludeAutomationName));
        OnPropertyChanged(nameof(PreviewAutomationName));
    }

    /// <summary>Updates the preview text (called when the intake values change and the user returns to this step).</summary>
    public void UpdatePreview(string previewText) => PreviewText = previewText;
}
