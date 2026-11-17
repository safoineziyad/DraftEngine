using System.IO;
using System.Net.Http;
using DraftEngine.App.Services.Profiles;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Tests;

/// <summary>
/// The local profile choice: the feature matrix for every profile, legacy installs (no profile)
/// behaving as Lawyer, persistence through the settings store, and the change event.
/// </summary>
public sealed class ProfileServiceTests
{
    [Theory]
    [InlineData(UserProfile.Lawyer, FeatureKey.Dashboard, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.NewContract, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.Contracts, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.Archive, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.Analyze, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.Clauses, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.Settings, true)]
    [InlineData(UserProfile.Lawyer, FeatureKey.About, true)]
    [InlineData(UserProfile.Business, FeatureKey.Dashboard, true)]
    [InlineData(UserProfile.Business, FeatureKey.NewContract, true)]
    [InlineData(UserProfile.Business, FeatureKey.Contracts, true)]
    [InlineData(UserProfile.Business, FeatureKey.Archive, true)]
    [InlineData(UserProfile.Business, FeatureKey.Analyze, true)]
    [InlineData(UserProfile.Business, FeatureKey.Clauses, false)]
    [InlineData(UserProfile.Business, FeatureKey.Settings, true)]
    [InlineData(UserProfile.Business, FeatureKey.About, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.Dashboard, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.NewContract, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.Contracts, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.Archive, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.Analyze, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.Clauses, false)]
    [InlineData(UserProfile.Mediator, FeatureKey.Settings, true)]
    [InlineData(UserProfile.Mediator, FeatureKey.About, true)]
    public void Matrix_MatchesTheDocumentedVisibility(UserProfile profile, FeatureKey feature, bool expected) =>
        Assert.Equal(expected, ProfileFeatureMatrix.IsVisible(profile, feature));

    [Fact]
    public void NoProfile_BehavesExactlyLikeLawyer()
    {
        foreach (var feature in Enum.GetValues<FeatureKey>())
        {
            Assert.Equal(
                ProfileFeatureMatrix.IsVisible(UserProfile.Lawyer, feature),
                ProfileFeatureMatrix.IsVisible(null, feature));
        }
    }

    [Fact]
    public void OnlyBusiness_ShowsTheNotLegalAdviceNotice_AndOnlyMediator_ShowsThePlannedNote()
    {
        Assert.True(ProfileFeatureMatrix.ShowsNotLegalAdviceNotice(UserProfile.Business));
        Assert.False(ProfileFeatureMatrix.ShowsNotLegalAdviceNotice(UserProfile.Lawyer));
        Assert.False(ProfileFeatureMatrix.ShowsNotLegalAdviceNotice(UserProfile.Mediator));
        Assert.False(ProfileFeatureMatrix.ShowsNotLegalAdviceNotice(null));

        Assert.True(ProfileFeatureMatrix.ShowsMediationPlannedNote(UserProfile.Mediator));
        Assert.False(ProfileFeatureMatrix.ShowsMediationPlannedNote(UserProfile.Business));
        Assert.False(ProfileFeatureMatrix.ShowsMediationPlannedNote(UserProfile.Lawyer));
        Assert.False(ProfileFeatureMatrix.ShowsMediationPlannedNote(null));
    }

    [Fact]
    public async Task SetProfile_PersistsAndRaisesTheEventOnce()
    {
        var settings = new FakeSettings();
        var service = new ProfileService(settings);
        var changes = new List<UserProfile>();
        service.ProfileChanged += (_, profile) => changes.Add(profile);

        await service.SetProfileAsync(UserProfile.Business);
        await service.SetProfileAsync(UserProfile.Business); // no-op
        await service.SetProfileAsync(UserProfile.Mediator);

        Assert.Equal(UserProfile.Mediator, service.Current);
        Assert.Equal(new[] { UserProfile.Business, UserProfile.Mediator }, changes);
        Assert.Equal(2, settings.SaveCount);
        Assert.False(service.IsFeatureVisible(FeatureKey.Clauses));
        Assert.True(service.IsFeatureVisible(FeatureKey.Analyze));
    }

    [Fact]
    public async Task SettingsService_RoundTripsTheProfile_AndMissingFileMeansNoProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "de-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var factory = new FakeHttpClientFactory();
        try
        {
            var fresh = new AppSettingsService(new IdentityProtector(), factory, path);
            Assert.Null(fresh.Current.Profile);
            Assert.True(ProfileFeatureMatrix.IsVisible(fresh.Current.Profile, FeatureKey.Clauses));

            var settings = fresh.Current;
            settings.Profile = UserProfile.Business;
            await fresh.SaveAsync(settings);

            var reloaded = new AppSettingsService(new IdentityProtector(), factory, path);
            Assert.Equal(UserProfile.Business, reloaded.Current.Profile);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"Profile\": \"Business\"", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public int SaveCount { get; private set; }
        public event Action? SettingsChanged { add { } remove { } }

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings)
        {
            SaveCount++;
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() => Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class IdentityProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string? Unprotect(string protectedValue) => protectedValue;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
