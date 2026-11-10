using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Theme resolution and persistence: the explicit Light/Dark choice always wins, Follow
/// Windows reads the (fake) system provider, and a system change re-applies only in System
/// mode. No WPF window is created, so rendering is not visually verified.
/// </summary>
public sealed class ThemeTests
{
    [Theory]
    [InlineData(ThemeMode.System, null, AppliedTheme.Light)]
    [InlineData(ThemeMode.System, true, AppliedTheme.Light)]
    [InlineData(ThemeMode.System, false, AppliedTheme.Dark)]
    [InlineData(ThemeMode.Light, true, AppliedTheme.Light)]
    [InlineData(ThemeMode.Light, false, AppliedTheme.Light)]
    [InlineData(ThemeMode.Dark, true, AppliedTheme.Dark)]
    [InlineData(ThemeMode.Dark, false, AppliedTheme.Dark)]
    public void Resolve_FollowsTheMatrix(ThemeMode mode, bool? systemUsesLight, AppliedTheme expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve(mode, systemUsesLight));
    }

    [Theory]
    [InlineData(null, ThemeMode.System)]
    [InlineData("", ThemeMode.System)]
    [InlineData("System", ThemeMode.System)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("DARK", ThemeMode.Dark)]
    [InlineData("nonsense", ThemeMode.System)]
    public void Parse_FallsBackToSystem(string? stored, ThemeMode expected)
    {
        Assert.Equal(expected, ThemeResolver.Parse(stored));
    }

    [Fact]
    public async Task SetMode_PersistsAndReloads()
    {
        var settings = new FakeSettingsService();
        var theme = new ThemeService(settings, new FakeSystemThemeProvider(usesLight: true));
        theme.Initialize(settings.Current.Theme);

        await theme.SetModeAsync(ThemeMode.Dark);

        Assert.Equal("Dark", settings.Current.Theme);
        Assert.Equal(AppliedTheme.Dark, theme.AppliedTheme);

        // A fresh service initializes from the persisted value.
        var reloaded = new ThemeService(settings, new FakeSystemThemeProvider(usesLight: true));
        reloaded.Initialize(settings.Current.Theme);
        Assert.Equal(ThemeMode.Dark, reloaded.Mode);
        Assert.Equal(AppliedTheme.Dark, reloaded.AppliedTheme);
    }

    [Fact]
    public void SystemMode_ReappliesWhenWindowsThemeChanges()
    {
        var settings = new FakeSettingsService();
        settings.Current.Theme = "System";
        var system = new FakeSystemThemeProvider(usesLight: true);
        var theme = new ThemeService(settings, system);

        theme.Initialize("System");
        Assert.Equal(ThemeMode.System, theme.Mode);
        Assert.Equal(AppliedTheme.Light, theme.AppliedTheme);

        system.RaiseChanged(usesLight: false);

        // Follow-Windows mode re-resolves when the (fake) system preference changes.
        Assert.Equal(AppliedTheme.Dark, theme.AppliedTheme);
    }

    [Fact]
    public void ExplicitMode_IgnoresSystemChanges()
    {
        var settings = new FakeSettingsService();
        var system = new FakeSystemThemeProvider(usesLight: false);
        var theme = new ThemeService(settings, system);

        theme.Initialize("Light");
        Assert.Equal(AppliedTheme.Light, theme.AppliedTheme);

        system.RaiseChanged(usesLight: false);

        Assert.Equal(AppliedTheme.Light, theme.AppliedTheme);
    }

    [Fact]
    public void SettingsViewModel_ThemeSelection_AppliesAndPersists()
    {
        var settings = new FakeSettingsService();
        var localization = new LocalizationService(
            new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        localization.InitializeAsync().GetAwaiter().GetResult();
        var system = new FakeSystemThemeProvider(usesLight: true);
        var theme = new ThemeService(settings, system);
        var viewModel = new SettingsViewModel(settings, localization: localization, themeService: theme);

        Assert.Equal(ThemeMode.System, viewModel.SelectedTheme);
        Assert.Equal(3, viewModel.ThemeOptions.Count);

        viewModel.SelectedTheme = ThemeMode.Dark;

        Assert.Equal("Dark", settings.Current.Theme);
        Assert.Equal(AppliedTheme.Dark, theme.AppliedTheme);
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }

    private sealed class FakeSystemThemeProvider : ISystemThemeProvider
    {
        private bool? _usesLight;

        public FakeSystemThemeProvider(bool? usesLight) => _usesLight = usesLight;

        public event EventHandler? SystemThemeChanged;

        public bool? SystemUsesLightMode() => _usesLight;

        public void RaiseChanged(bool? usesLight)
        {
            _usesLight = usesLight;
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();

        public event Action? SettingsChanged;

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings)
        {
            Current.Theme = settings.Theme;
            Current.Language = settings.Language;
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }
}
