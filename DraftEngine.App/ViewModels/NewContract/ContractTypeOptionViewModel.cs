using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Models.ContractTypes;

namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// View model for a selectable contract type card on step 1 of the wizard.
/// Types that are not yet implemented are shown as "Coming soon" and are not selectable.
/// </summary>
public partial class ContractTypeOptionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    private readonly ILocalizationService _localization;

    /// <summary>The registry definition backing this card, including its canonical id.</summary>
    public ContractTypeDefinition Definition { get; }

    /// <summary>The canonical registry id (e.g. <c>MutualNda</c>).</summary>
    public string Id => Definition.Id;

    /// <summary>The display name of the contract type shown on the card.</summary>
    public string Name => Localized("Name", Definition.DisplayName);

    /// <summary>A short one-line description shown on the card.</summary>
    public string Description => Localized("Description", Definition.Description);

    /// <summary>Accessible description for the selection card.</summary>
    public string SelectAutomationName =>
        _localization.GetString("Wizard.SelectTypeAutomation", Name);

    /// <summary>Whether the user can select this contract type. False shows the "Coming soon" badge.</summary>
    public bool IsAvailable { get; }

    /// <summary>Whether the card is visibly marked "Coming soon" (inverse of <see cref="IsAvailable"/>).</summary>
    public bool IsComingSoon => !IsAvailable;

    /// <summary>Creates a contract type card option from a registry definition.</summary>
    public ContractTypeOptionViewModel(
        ContractTypeDefinition definition,
        bool isAvailable,
        ILocalizationService? localization = null)
    {
        Definition = definition ?? throw new System.ArgumentNullException(nameof(definition));
        IsAvailable = isAvailable;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
    }

    private string Localized(string property, string fallback)
    {
        var key = $"Wizard.Type.{Definition.Id}.{property}";
        var value = _localization.GetString(key);
        return string.Equals(value, key, System.StringComparison.Ordinal) ? fallback : value;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SelectAutomationName));
    }
}
