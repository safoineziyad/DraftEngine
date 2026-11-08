using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Updates;

namespace DraftEngine.Tests.Commercial;

/// <summary>
/// Tests for <see cref="UpdateCheckService" />: when <c>Features:UpdateManifestUrl</c> is null
/// the service performs no request and returns null.
/// </summary>
public class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_ManifestUrlNull_ReturnsNull()
    {
        var options = new AppOptions
        {
            Features = { UpdateCheckEnabled = true, UpdateManifestUrl = null }
        };

        var service = new UpdateCheckService(options, new FakeHttpClientFactory());
        var result = await service.CheckAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckAsync_UpdateCheckDisabled_ReturnsNull()
    {
        var options = new AppOptions
        {
            Features = { UpdateCheckEnabled = false, UpdateManifestUrl = "https://example.com/manifest.json" }
        };

        var service = new UpdateCheckService(options, new FakeHttpClientFactory());
        var result = await service.CheckAsync();

        Assert.Null(result);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
