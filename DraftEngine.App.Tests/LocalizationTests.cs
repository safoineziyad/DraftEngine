using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.ViewModels;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Localization behaviour tests: live switching, event/notification contract, RTL,
/// European digits, persistence and missing-key fallback. These tests do not create
/// WPF windows, so they run on the normal xUnit thread; window rendering is not
/// visually verified.
/// </summary>
public sealed class LocalizationTests : IDisposable
{
    private readonly MemoryLanguageStore _store = new();

    public LocalizationTests()
    {
        // Keep tests deterministic: the service resolves "system" to English here.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public async Task SetLanguage_RaisesEvent_AndUpdatesLiveSource()
    {
        var service = CreateService();
        await service.InitializeAsync();
        LocalizationSource.Initialize(service);

        var source = LocalizationSource.Current;
        var itemNotifications = 0;
        var languageEvents = 0;
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                itemNotifications++;
            }
        };
        service.LanguageChanged += (_, _) => languageEvents++;

        Assert.Equal("Language", source["Settings.Language"]);

        await service.SetLanguageAsync("fr");

        Assert.Equal(1, languageEvents);
        Assert.Equal(1, itemNotifications);
        Assert.Equal("Langue", source["Settings.Language"]);
        Assert.Equal("fr", service.CurrentLanguage);
    }

    [Fact]
    public async Task Arabic_IsRightToLeft_AndUsesEuropeanDigits()
    {
        var service = CreateService();
        await service.InitializeAsync();
        LocalizationSource.Initialize(service);

        await service.SetLanguageAsync("ar");

        Assert.True(service.IsRtl());
        Assert.Equal(FlowDirection.RightToLeft, service.GetFlowDirection());
        Assert.Equal(FlowDirection.RightToLeft, LocalizationSource.Current.FlowDirection);
        Assert.Equal(NumberSubstitutionMethod.European, service.DigitsMethod);

        await service.SetLanguageAsync("es");

        Assert.False(service.IsRtl());
        Assert.Equal(FlowDirection.LeftToRight, service.GetFlowDirection());
        Assert.Equal(FlowDirection.LeftToRight, LocalizationSource.Current.FlowDirection);
    }

    [Fact]
    public async Task SetLanguage_UpdatesCurrentCultureAndUiCulture()
    {
        var service = CreateService();
        await service.InitializeAsync();

        await service.SetLanguageAsync("fr");

        Assert.Equal("fr", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
        Assert.Equal("fr", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task LanguageChoice_PersistsAndReloads()
    {
        var first = CreateService();
        await first.InitializeAsync();
        await first.SetLanguageAsync("es");

        Assert.Equal("es", _store.Load());

        var second = CreateService();
        await second.InitializeAsync();

        Assert.Equal("es", second.CurrentLanguage);
    }

    [Fact]
    public async Task Initialize_UsesWindowsLanguage_WhenSupported_ElseEnglish()
    {
        var french = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("fr-CA"));
        await french.InitializeAsync();
        Assert.Equal("fr", french.CurrentLanguage);

        var japanese = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("ja-JP"));
        await japanese.InitializeAsync();
        Assert.Equal("en", japanese.CurrentLanguage);
    }

    [Fact]
    public async Task MissingTranslation_FallsBackToEnglish()
    {
        // English has the key; French does not. The service must return English text
        // rather than the raw key.
        var service = new LocalizationService(
            new MemoryLanguageStore(),
            () => CultureInfo.GetCultureInfo("en-US"),
            (key, culture) => key == "Only.English" && culture.TwoLetterISOLanguageName == "en"
                ? "English text"
                : null);
        await service.InitializeAsync();
        await service.SetLanguageAsync("fr");

        Assert.Equal("English text", service.GetString("Only.English"));
    }

    [Fact]
    public async Task MissingKey_DebugShowsBracketedKey()
    {
        var service = CreateService();
        await service.InitializeAsync();

        var result = service.GetString("This.Key.Does.Not.Exist");

#if DEBUG
        Assert.Equal("[This.Key.Does.Not.Exist]", result);
#else
        Assert.Equal("This.Key.Does.Not.Exist", result);
#endif
    }

    [Fact]
    public async Task FormatArguments_AreAppliedPerLanguage()
    {
        var service = CreateService();
        await service.InitializeAsync();

        Assert.Equal("Version 0.4.0", service.GetString("Common.Version", "0.4.0"));

        await service.SetLanguageAsync("ar");
        Assert.Equal("الإصدار 0.4.0", service.GetString("Common.Version", "0.4.0"));
    }

    [Fact]
    public async Task SettingsViewModel_RerendersComposedText_OnLanguageChange()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var viewModel = new SettingsViewModel(new StubSettingsService(), localization: service);

        Assert.Equal("No API key configured", viewModel.ApiKeyStatusText);
        Assert.Equal("AI data-sharing consent: not granted", viewModel.AiConsentStatusText);
        Assert.Equal("Show", viewModel.ShowHideKeyText);

        await service.SetLanguageAsync("fr");

        Assert.Equal("Aucune clé API configurée", viewModel.ApiKeyStatusText);
        Assert.Equal("Consentement au partage de données avec l'IA : non accordé", viewModel.AiConsentStatusText);
        Assert.Equal("Afficher", viewModel.ShowHideKeyText);
    }

    private LocalizationService CreateService() =>
        new(_store, () => CultureInfo.GetCultureInfo("en-US"));

    public void Dispose()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    private sealed class StubSettingsService : DraftEngine.App.Services.Settings.IAppSettingsService
    {
        public DraftEngine.App.Services.Settings.AppSettings Current { get; } = new();

        public event Action? SettingsChanged;

        public Task<DraftEngine.App.Services.Settings.AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(DraftEngine.App.Services.Settings.AppSettings settings)
        {
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task<DraftEngine.Core.Interfaces.IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<DraftEngine.Core.Interfaces.IContractAiService?>(null);

        public DraftEngine.Core.Interfaces.IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }
}
