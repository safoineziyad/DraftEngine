using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Themes;

namespace DraftEngine.App.ViewModels;

/// <summary>One entry in the Settings theme picker (Follow Windows / Light / Dark).</summary>
public sealed class ThemeChoiceViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string _labelKey;

    /// <summary>Creates a theme option whose display name follows the active language.</summary>
    public ThemeChoiceViewModel(ThemeMode mode, string labelKey, ILocalizationService localization)
    {
        Mode = mode;
        _labelKey = labelKey;
        _localization = localization;
    }

    /// <summary>The theme mode this option selects.</summary>
    public ThemeMode Mode { get; }

    /// <summary>The localized display name.</summary>
    public string DisplayName => _localization.GetString(_labelKey);

    /// <summary>Re-resolves the display name after a language change.</summary>
    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}
