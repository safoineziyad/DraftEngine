using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Tests;

/// <summary>The Settings profile card persists the choice and confirms it.</summary>
public sealed class SettingsProfileTests
{
    [Fact]
    public void Card_OffersThreeProfiles_AndStartsFromTheSavedChoice()
    {
        var localization = CreateLocalization();
        var profiles = new FakeProfileService(UserProfile.Mediator);
        var viewModel = new SettingsViewModel(
            new FakeSettingsService(),
            localization: localization,
            profileService: profiles);

        Assert.Equal(3, viewModel.ProfileOptions.Count);
        Assert.Equal(UserProfile.Mediator, viewModel.SelectedProfile);
        Assert.False(viewModel.HasProfileConfirmation);
    }

    [Fact]
    public async Task ChangingProfile_PersistsAndShowsALocalizedConfirmation()
    {
        var localization = CreateLocalization();
        var profiles = new FakeProfileService(UserProfile.Lawyer);
        var viewModel = new SettingsViewModel(
            new FakeSettingsService(),
            localization: localization,
            profileService: profiles);

        viewModel.SelectedProfile = UserProfile.Business;

        for (var attempt = 0; attempt < 50 && !viewModel.HasProfileConfirmation; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(UserProfile.Business, profiles.Current);
        Assert.True(viewModel.HasProfileConfirmation);
        Assert.Contains("Business", viewModel.ProfileConfirmation);
        Assert.DoesNotContain("{0}", viewModel.ProfileConfirmation);
    }

    private static LocalizationService CreateLocalization()
    {
        var localization = new LocalizationService(
            new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        localization.InitializeAsync().GetAwaiter().GetResult();
        LocalizationSource.Initialize(localization);
        return localization;
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;

        public string? Load() => _value;

        public void Save(string languageCode) => _value = languageCode;
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
            Current.Language = settings.Language;
            Current.Theme = settings.Theme;
            Current.Profile = settings.Profile;
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class FakeProfileService : IProfileService
    {
        public FakeProfileService(UserProfile profile) => Current = profile;

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
}
