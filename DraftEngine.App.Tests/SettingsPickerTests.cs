using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Settings pickers: the language choice applies immediately and persists, each language
/// is offered in its own name, and the theme options follow the active language.
/// </summary>
public sealed class SettingsPickerTests : IDisposable
{
    public SettingsPickerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void LanguagePicker_OffersEachLanguageInItsOwnName()
    {
        var (viewModel, _, _) = Create();

        Assert.Equal(
            new[] { "English", "Français", "العربية", "Español" },
            viewModel.SupportedLanguages.Select(language => language.NativeName));
        Assert.All(viewModel.SupportedLanguages, language =>
            Assert.False(string.IsNullOrWhiteSpace(language.Code)));
    }

    [Fact]
    public void LanguagePicker_AppliesImmediatelyAndPersists()
    {
        var (viewModel, localization, store) = Create();

        viewModel.SelectedLanguage = "fr";

        Assert.Equal("fr", localization.CurrentLanguage);
        Assert.Equal("fr", store.Load());
        Assert.Equal("fr", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task ThemePicker_OptionsFollowTheActiveLanguage()
    {
        var (viewModel, localization, _) = Create();

        Assert.Equal(
            new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark },
            viewModel.ThemeOptions.Select(option => option.Mode));
        Assert.Equal("Follow Windows", viewModel.ThemeOptions[0].DisplayName);
        Assert.Equal("Light", viewModel.ThemeOptions[1].DisplayName);

        await localization.SetLanguageAsync("fr");

        Assert.Equal("Suivre Windows", viewModel.ThemeOptions[0].DisplayName);
        Assert.Equal("Clair", viewModel.ThemeOptions[1].DisplayName);
    }

    private static (SettingsViewModel ViewModel, LocalizationService Localization, MemoryLanguageStore Store) Create()
    {
        var store = new MemoryLanguageStore();
        var localization = new LocalizationService(store, () => CultureInfo.GetCultureInfo("en-US"));
        localization.InitializeAsync().GetAwaiter().GetResult();
        var viewModel = new SettingsViewModel(
            new FakeSettingsService(),
            localization: localization,
            themeService: new ThemeService(
                new FakeSettingsService(),
                new FakeSystemThemeProvider()));
        return (viewModel, localization, store);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }

    private sealed class FakeSystemThemeProvider : ISystemThemeProvider
    {
        public event EventHandler? SystemThemeChanged
        {
            add { }
            remove { }
        }

        public bool? SystemUsesLightMode() => true;
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();

        public event Action? SettingsChanged
        {
            add { }
            remove { }
        }

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings)
        {
            Current.Theme = settings.Theme;
            Current.Language = settings.Language;
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }
}
