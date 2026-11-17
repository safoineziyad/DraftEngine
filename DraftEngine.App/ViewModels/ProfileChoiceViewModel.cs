using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;

namespace DraftEngine.App.ViewModels;

/// <summary>A profile option in the Settings card, localized against the active language.</summary>
public sealed class ProfileChoiceViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string _labelKey;

    public ProfileChoiceViewModel(UserProfile profile, string labelKey, ILocalizationService localization)
    {
        Profile = profile;
        _labelKey = labelKey;
        _localization = localization;
    }

    public UserProfile Profile { get; }

    public string DisplayName => _localization.GetString(_labelKey);

    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}
