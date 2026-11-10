using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Localization;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// One sidebar navigation entry: a localized label, an icon glyph and an active marker.
/// Labels are resolved from the localization service and re-raised on language change so
/// the sidebar updates live without rebuilding the view.
/// </summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly string _labelKey;
    private readonly ILocalizationService? _localization;

    /// <summary>Creates a navigation entry.</summary>
    /// <param name="key">Navigation page key (e.g. "dashboard").</param>
    /// <param name="labelKey">Resource key for the label and automation name.</param>
    /// <param name="icon">Segoe Fluent Icons / Segoe MDL2 Assets glyph.</param>
    /// <param name="command">Command that navigates to the page.</param>
    /// <param name="localization">Service used to resolve the label.</param>
    public NavigationItemViewModel(
        string key,
        string labelKey,
        string icon,
        IRelayCommand command,
        ILocalizationService? localization)
    {
        Key = key;
        _labelKey = labelKey;
        Icon = icon;
        Command = command;
        _localization = localization;
    }

    /// <summary>Navigation page key compared against the navigation service's current key.</summary>
    public string Key { get; }

    /// <summary>Segoe Fluent Icons glyph shown beside the label.</summary>
    public string Icon { get; }

    /// <summary>Command that navigates to this page.</summary>
    public IRelayCommand Command { get; }

    /// <summary>The localized label (falls back to the resource key when unavailable).</summary>
    public string Label => _localization?.GetString(_labelKey) ?? _labelKey;

    /// <summary>The localized accessible name (same text as the label).</summary>
    public string AutomationName => Label;

    /// <summary>True when this item represents the page currently shown.</summary>
    [ObservableProperty]
    private bool isActive;

    /// <summary>Re-resolves the localized label after a language change.</summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(AutomationName));
    }
}
