using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.Themes;
using DraftEngine.App.ViewModels;

namespace DraftEngine.App.Tests;

/// <summary>First-run order, profile cards and the Skip-defaults-to-Lawyer rule.</summary>
public sealed class OnboardingProfileTests
{
    [Fact]
    public void FirstScreenShowsLanguageAndTheme_ThenProfile()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.True(viewModel.IsFirst);
        Assert.Equal(4, viewModel.StepCount);
        Assert.Equal(3, viewModel.ThemeOptions.Count);

        viewModel.NextCommand.Execute(null);

        Assert.Equal(1, viewModel.StepIndex);
        Assert.True(viewModel.IsProfileStep);
        Assert.Equal(3, viewModel.ProfileOptions.Count);
        Assert.Equal(
            new[] { UserProfile.Lawyer, UserProfile.Business, UserProfile.Mediator },
            viewModel.ProfileOptions.Select(option => option.Profile));
    }

    [Fact]
    public void SkippingWithoutAChoice_PersistsLawyer()
    {
        var viewModel = CreateViewModel(out var profiles, out _);

        viewModel.SkipCommand.Execute(null);

        Assert.Equal(UserProfile.Lawyer, profiles.Current);
    }

    [Fact]
    public void ChosenProfile_IsSavedWhenFinishing()
    {
        var viewModel = CreateViewModel(out var profiles, out _);
        viewModel.NextCommand.Execute(null);
        viewModel.SelectedProfile = UserProfile.Business;

        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        Assert.Equal(UserProfile.Business, profiles.Current);
    }

    [Fact]
    public void ThemeChoice_IsAppliedLive()
    {
        var viewModel = CreateViewModel(out _, out var theme);

        viewModel.SelectedTheme = ThemeMode.Dark;

        Assert.Equal(ThemeMode.Dark, theme.Mode);
    }

    private static OnboardingViewModel CreateViewModel(
        out FakeProfileService profiles, out FakeThemeService theme)
    {
        var localization = new LocalizationService(
            new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        localization.InitializeAsync().GetAwaiter().GetResult();
        LocalizationSource.Initialize(localization);
        profiles = new FakeProfileService();
        theme = new FakeThemeService();
        return new OnboardingViewModel(localization, theme, profiles);
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
    }

    private sealed class FakeProfileService : IProfileService
    {
        public UserProfile? Current { get; private set; }

        public event EventHandler<UserProfile>? ProfileChanged;

        public bool IsFeatureVisible(FeatureKey feature) =>
            ProfileFeatureMatrix.IsVisible(Current, feature);

        public Task SetProfileAsync(UserProfile profile)
        {
            Current = profile;
            ProfileChanged?.Invoke(this, profile);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode Mode { get; private set; } = ThemeMode.System;

        public AppliedTheme AppliedTheme => AppliedTheme.Light;

        public event EventHandler? ThemeChanged;

        public void Initialize(string? storedMode) => Mode = ThemeMode.System;

        public Task SetModeAsync(ThemeMode mode)
        {
            Mode = mode;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
