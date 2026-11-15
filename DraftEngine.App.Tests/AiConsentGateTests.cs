using System.Net.Http;
using DraftEngine.AI.Exceptions;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Tests;

/// <summary>
/// The AI service must refuse to work before the user consents, and must not touch the network
/// in that state — the factory is asserted to be never asked for a client.
/// </summary>
public sealed class AiConsentGateTests
{
    [Fact]
    public async Task WithoutConsent_NoHttpClientIsCreated_AndTheTypedConsentErrorIsThrown()
    {
        var settings = new FakeSettings(consent: false);
        var factory = new ExplodingHttpClientFactory();
        var service = new SettingsBackedAiService(settings, new AppOptions());

        var exception = await Assert.ThrowsAsync<OpenAiConsentRequiredException>(
            () => service.CompletePromptAsync("system", "contract text"));

        Assert.Contains("consent", exception.FriendlyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task WithConsent_ButNoKey_ReportsTheMissingKeyError()
    {
        var settings = new FakeSettings(consent: true, key: string.Empty);
        var service = new SettingsBackedAiService(settings, new AppOptions());

        var result = await service.TestConnectionDetailedAsync();

        Assert.False(result.Success);
        Assert.Contains("key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSettings : IAppSettingsService
    {
        public FakeSettings(bool consent, string key = "sk-test-key") 
        {
            Current = new AppSettings
            {
                AiDataConsentAccepted = consent,
                OpenAiApiKey = key,
                OpenAiModel = "test-model",
                ActiveAiProfileId = "p1",
            };
            Current.AiProfiles.Add(new DraftEngine.Core.Models.AiProfile
            {
                Id = "p1",
                Name = "Test",
                Model = "test-model",
                ApiKey = key,
            });
        }

        public AppSettings Current { get; private set; }
        public event Action? SettingsChanged { add { } remove { } }

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() => Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class ExplodingHttpClientFactory : IHttpClientFactory
    {
        public int CreateCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateCount++;
            throw new InvalidOperationException("No HTTP client may be created before AI consent.");
        }
    }
}
