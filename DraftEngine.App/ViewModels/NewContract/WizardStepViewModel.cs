using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// View model for one entry of the wizard step indicator bar.
/// </summary>
public partial class WizardStepViewModel : ObservableObject
{
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isCompleted;
    private readonly string _labelKey;
    private readonly ILocalizationService _localization;

    /// <summary>The 1-based step number.</summary>
    public int Index { get; }

    /// <summary>The short step label shown in the indicator.</summary>
    public string Label => _localization.GetString(_labelKey);

    /// <summary>Creates a step indicator entry.</summary>
    public WizardStepViewModel(int index, string labelKey, ILocalizationService localization)
    {
        Index = index;
        _labelKey = labelKey;
        _localization = localization;
    }

    public void RefreshLocalization() =>
        OnPropertyChanged(nameof(Label));
}
