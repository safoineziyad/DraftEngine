using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// View model for one entry of the wizard step indicator bar.
/// </summary>
public partial class WizardStepViewModel : ObservableObject
{
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isCompleted;

    /// <summary>The 1-based step number.</summary>
    public int Index { get; }

    /// <summary>The short step label shown in the indicator.</summary>
    public string Label { get; }

    /// <summary>Creates a step indicator entry.</summary>
    public WizardStepViewModel(int index, string label)
    {
        Index = index;
        Label = label;
    }
}
