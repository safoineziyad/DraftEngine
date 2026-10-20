using CommunityToolkit.Mvvm.ComponentModel;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// View model for a selectable contract type card on step 1 of the wizard.
/// Types that are not yet implemented are shown as "Coming soon" and are not selectable.
/// </summary>
public partial class ContractTypeOptionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    /// <summary>The display name of the contract type (also persisted as <c>Contract.ContractType</c>).</summary>
    public string Name { get; }

    /// <summary>A short one-line description shown on the card.</summary>
    public string Description { get; }

    /// <summary>Whether the user can select this contract type. False shows the "Coming soon" badge.</summary>
    public bool IsAvailable { get; }

    /// <summary>Whether the card is visibly marked "Coming soon" (inverse of <see cref="IsAvailable"/>).</summary>
    public bool IsComingSoon => !IsAvailable;

    /// <summary>Creates a contract type card option.</summary>
    public ContractTypeOptionViewModel(string name, string description, bool isAvailable)
    {
        Name = name;
        Description = description;
        IsAvailable = isAvailable;
    }
}
