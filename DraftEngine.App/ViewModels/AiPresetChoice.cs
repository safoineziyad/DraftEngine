using CommunityToolkit.Mvvm.ComponentModel;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Models;

namespace DraftEngine.App.ViewModels;

/// <summary>Localized display wrapper for an <see cref="AiProviderPreset"/> in the settings picker.</summary>
public sealed class AiPresetChoice : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string _resourceKey;

    /// <summary>Creates the choice; <paramref name="resourceKey"/> names the localized preset label.</summary>
    public AiPresetChoice(AiProviderPreset preset, string resourceKey, ILocalizationService localization)
    {
        Preset = preset;
        _resourceKey = resourceKey;
        _localization = localization;
        _name = localization.GetString(resourceKey);
    }

    private string _name;

    /// <summary>Localized preset name shown in the picker.</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>The preset this choice represents.</summary>
    public AiProviderPreset Preset { get; }

    /// <summary>Re-reads the label after a language change.</summary>
    public void RefreshLocalization() => Name = _localization.GetString(_resourceKey);
}
