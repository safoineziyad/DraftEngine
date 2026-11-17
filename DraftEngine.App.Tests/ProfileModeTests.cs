using System.Globalization;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.ViewModels;

namespace DraftEngine.App.Tests;

/// <summary>Sidebar gating, live profile switching and the Dashboard fallback.</summary>
public sealed class ProfileModeTests : IDisposable
{
    public ProfileModeTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void BusinessProfile_HidesClauseLibrary_ButKeepsSharedPages()
    {
        var localization = CreateLocalization();
        var (viewModel, _) = CreateViewModel(localization, UserProfile.Business);

        Assert.DoesNotContain(viewModel.AllItems, item => item.Key == "clause-management");
        Assert.Contains(viewModel.AllItems, item => item.Key == "dashboard");
        Assert.Contains(viewModel.AllItems, item => item.Key == "new-contract");
        Assert.Contains(viewModel.AllItems, item => item.Key == "analyze");
        Assert.Contains(viewModel.AllItems, item => item.Key == "settings");
    }

    [Fact]
    public void LawyerProfile_ShowsEveryExistingPage()
    {
        var localization = CreateLocalization();
        var (viewModel, _) = CreateViewModel(localization, UserProfile.Lawyer);

        Assert.Equal(
            new[] { "dashboard", "new-contract", "contracts", "archived-contracts" },
            viewModel.WorkspaceItems.Select(item => item.Key));
        Assert.Equal(
            new[] { "analyze", "clause-management" },
            viewModel.ToolsItems.Select(item => item.Key));
    }

    [Fact]
    public async Task SwitchingToBusiness_LiveHidesClausePage_AndFallsBackFromIt()
    {
        var localization = CreateLocalization();
        var profiles = new FakeProfileService(UserProfile.Lawyer);
        var (viewModel, navigation) = CreateViewModel(localization, profiles);

        Assert.True(navigation.Navigate("clause-management"));
        Assert.Equal("clause-management", navigation.CurrentKey);

        await profiles.SetProfileAsync(UserProfile.Business);

        Assert.DoesNotContain(viewModel.AllItems, item => item.Key == "clause-management");
        Assert.Equal("dashboard", navigation.CurrentKey);
        Assert.Contains(viewModel.AllItems, item => item.Key == "dashboard");
    }

    [Fact]
    public void HiddenClauseShortcut_IsInert()
    {
        var localization = CreateLocalization();
        var (viewModel, navigation) = CreateViewModel(localization, UserProfile.Business);

        viewModel.NavigateClauseManagementCommand.Execute(null);

        Assert.NotEqual("clause-management", navigation.CurrentKey);
    }

    [Theory]
    [InlineData(UserProfile.Business, true, false)]
    [InlineData(UserProfile.Mediator, false, true)]
    [InlineData(UserProfile.Lawyer, false, false)]
    public void Notices_FollowTheProfile(
        UserProfile profile, bool showsLegalNotice, bool showsMediationNote)
    {
        Assert.Equal(showsLegalNotice, ProfileFeatureMatrix.ShowsNotLegalAdviceNotice(profile));
        Assert.Equal(showsMediationNote, ProfileFeatureMatrix.ShowsMediationPlannedNote(profile));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    private static (MainViewModel ViewModel, FakeNavigationService Navigation) CreateViewModel(
        ILocalizationService localization, UserProfile profile) =>
        CreateViewModel(localization, new FakeProfileService(profile));

    private static (MainViewModel ViewModel, FakeNavigationService Navigation) CreateViewModel(
        ILocalizationService localization, IProfileService profiles)
    {
        var navigation = new FakeNavigationService();
        var viewModel = new MainViewModel(
            navigation,
            new AppOptions(),
            updateService: null,
            localization: localization,
            profileService: profiles);
        return (viewModel, navigation);
    }

    private static ILocalizationService CreateLocalization()
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

    private sealed class FakeNavigationService : INavigationService
    {
        public event Action<object?>? CurrentViewModelChanged;

        public object? CurrentViewModel { get; private set; }

        public string CurrentKey { get; private set; } = string.Empty;

        public bool Navigate(string key, object? parameter = null)
        {
            CurrentKey = key;
            CurrentViewModel = new object();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
            return true;
        }
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
