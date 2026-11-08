using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Updates;
using DraftEngine.App.Services;
using DraftEngine.App.ViewModels;
using Xunit;

namespace DraftEngine.App.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReadsLatestReleaseAndFindsInstallerAsset()
    {
        var options = new AppOptions();
        options.Features.AutoUpdateEnabled = true;
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                  "tag_name": "v99.0.0",
                  "body": "Release notes",
                  "assets": [
                    {
                      "name": "DraftEngine-Setup-99.0.0.exe",
                      "browser_download_url": "https://github.com/safoineziyad/DraftEngine/releases/download/v99.0.0/DraftEngine-Setup-99.0.0.exe"
                    }
                  ]
                }
                """),
        });
        var service = new UpdateService(options, client);

        var update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal("v99.0.0", update.Version);
        Assert.Equal(
            "https://github.com/safoineziyad/DraftEngine/releases/download/v99.0.0/DraftEngine-Setup-99.0.0.exe",
            update.DownloadUrl);
        Assert.Equal("Release notes", update.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_RejectsReleaseWithoutInstaller()
    {
        var options = new AppOptions();
        options.Features.AutoUpdateEnabled = true;
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                  "tag_name": "v99.0.0",
                  "assets": [
                    {
                      "name": "draftengine-symbols.zip",
                      "browser_download_url": "https://github.com/safoineziyad/DraftEngine/releases/download/v99.0.0/draftengine-symbols.zip"
                    }
                  ]
                }
                """),
        });
        var service = new UpdateService(options, client);

        Assert.Null(await service.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullWhenAlreadyOnLatestVersion()
    {
        var options = new AppOptions();
        options.Features.AutoUpdateEnabled = true;
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent($$"""
                {
                  "tag_name": "v{{DraftEngine.App.AppVersion.Display}}",
                  "assets": [
                    {
                      "name": "DraftEngine-Setup-{{DraftEngine.App.AppVersion.Display}}.exe",
                      "browser_download_url": "https://github.com/safoineziyad/DraftEngine/releases/download/v{{DraftEngine.App.AppVersion.Display}}/DraftEngine-Setup-{{DraftEngine.App.AppVersion.Display}}.exe"
                    }
                  ]
                }
                """),
        });
        var service = new UpdateService(options, client);

        Assert.Null(await service.CheckForUpdateAsync());
    }

    [Fact]
    public void MainViewModel_ShowsAndDismissesAnAvailableUpdate()
    {
        var options = new AppOptions();
        options.Features.AutoUpdateEnabled = true;
        var update = new UpdateInfo(
            "v99.0.0",
            "https://github.com/safoineziyad/DraftEngine/releases/download/v99.0.0/DraftEngine-Setup-99.0.0.exe",
            string.Empty);
        var viewModel = new MainViewModel(new TestNavigationService(), options, new StubUpdateService(update));

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.True(viewModel.IsUpdateBannerVisible);
        Assert.Contains("99.0.0", viewModel.UpdateBannerText, StringComparison.Ordinal);
        Assert.True(viewModel.DownloadAndInstallUpdateCommand.CanExecute(null));

        viewModel.DismissUpdateCommand.Execute(null);

        Assert.False(viewModel.IsUpdateBannerVisible);
        Assert.False(viewModel.DownloadAndInstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadInstallerAsync_SavesExecutableToRequestedPath()
    {
        var options = new AppOptions();
        var executable = new byte[] { (byte)'M', (byte)'Z', 1, 2, 3 };
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(executable),
        });
        var service = new UpdateService(options, client);
        var directory = Path.Combine(Path.GetTempPath(), $"draftengine-update-test-{Guid.NewGuid():N}");
        var destination = Path.Combine(directory, "DraftEngineUpdate.exe");

        try
        {
            await service.DownloadInstallerAsync(
                new UpdateInfo(
                    "v99.0.0",
                    "https://github.com/safoineziyad/DraftEngine/releases/download/v99.0.0/DraftEngine-Setup-99.0.0.exe",
                    string.Empty),
                destination);

            Assert.Equal(executable, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsUntrustedAssetUrl()
    {
        using var client = CreateClient(_ =>
            throw new InvalidOperationException("The untrusted URL must not be requested."));
        var service = new UpdateService(new AppOptions(), client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadInstallerAsync(
            new UpdateInfo("v99.0.0", "https://example.com/installer.exe", string.Empty),
            Path.Combine(Path.GetTempPath(), "DraftEngineUpdate.exe")));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new CallbackHandler(respond));

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed class StubUpdateService(UpdateInfo update) : IUpdateService
    {
        public Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateInfo?>(update);

        public Task DownloadInstallerAsync(
            UpdateInfo update,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestNavigationService : INavigationService
    {
        public event Action<object?>? CurrentViewModelChanged;

        public object? CurrentViewModel { get; private set; }

        public string CurrentKey { get; private set; } = string.Empty;

        public bool Navigate(string key, object? parameter = null)
        {
            CurrentKey = key;
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
            return true;
        }
    }

    private sealed class CallbackHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
