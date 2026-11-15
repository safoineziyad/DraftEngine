using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftEngine.App.Services.Localization;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.Services.Updates;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;
using DraftEngine.Updates;

namespace DraftEngine.App.Tests;

/// <summary>
/// The update UI flow: banner state, localized notes, explicit confirmation before installing,
/// skip/remind persistence and no network traffic when checks are disabled.
/// </summary>
public sealed class UpdateFlowViewModelTests : IDisposable
{
    private const string Version = "9.9.9";
    private const string FileName = "DraftEngine-Setup-9.9.9.exe";
    private const string InstallerUrl = "https://github.com/safoineziyad/DraftEngine/releases/download/v9.9.9/" + FileName;
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("MZ fake installer");
    private static readonly string InstallerHash = Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant();

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "de-loss-" + Guid.NewGuid().ToString("N"));
    private readonly FakePrompt _prompt = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeHandler _handler = new();
    private readonly LocalizationService _localization;
    private readonly FakeSettings _settings = new();

    public UpdateFlowViewModelTests()
    {
        Directory.CreateDirectory(_directory);
        _localization = new LocalizationService(new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        _localization.InitializeAsync().GetAwaiter().GetResult();
        LocalizationSource.Initialize(_localization);
    }

    public void Dispose()
    {
        _key.Dispose();
        _prompt.Dispose();
        _launcher.Dispose();
        _handler.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Check_Available_ShowsBannerWithLocalizedVersionAndNotes()
    {
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.True(viewModel.IsBannerVisible);
        Assert.Contains(Version, viewModel.BannerText, StringComparison.Ordinal);
        Assert.Contains("English notes", viewModel.ReleaseNotes, StringComparison.Ordinal);
        Assert.Equal(viewModel.BannerText, viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadAndInstall_DeclinedConfirmation_NeverLaunchesInstaller()
    {
        _prompt.Answer = false;
        var viewModel = CreateViewModel();
        await viewModel.CheckAsync();

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(0, _launcher.LaunchCount);
        Assert.True(viewModel.IsBannerVisible);
        Assert.Equal(_localization.GetString("Update.Cancelled"), viewModel.StatusMessage);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task DownloadAndInstall_Confirmed_LaunchesVerifiedInstallerAfterSnapshot()
    {
        _prompt.Answer = true;
        var viewModel = CreateViewModel();
        await viewModel.CheckAsync();

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(1, _launcher.LaunchCount);
        Assert.Equal(FileName, Path.GetFileName(_launcher.LastPath));
        Assert.True(File.Exists(_launcher.LastPath));
        Assert.Equal(1d, viewModel.DownloadProgress);
        Assert.True(_prompt.Asked);
    }

    [Fact]
    public async Task ChecksDisabled_MakesNoRequestAndNeverShowsBanner()
    {
        _settings.Current.UpdateChecksEnabled = false;
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();
        await viewModel.CheckOnStartupAsync(TimeSpan.Zero);

        Assert.Equal(0, _handler.RequestCount);
        Assert.False(viewModel.IsUpdateAvailable);
        Assert.False(viewModel.AutomaticChecksEnabled);
    }

    [Fact]
    public async Task SkipVersion_PersistsVersionAndHidesBanner()
    {
        var viewModel = CreateViewModel();
        await viewModel.CheckAsync();
        Assert.True(viewModel.IsBannerVisible);

        viewModel.SkipVersionCommand.Execute(null);

        Assert.False(viewModel.IsBannerVisible);
        Assert.Equal(Version, _settings.Current.SkippedUpdateVersion);
    }

    [Fact]
    public async Task RemindLater_HidesBannerForTheSessionOnly()
    {
        var viewModel = CreateViewModel();
        await viewModel.CheckAsync();

        viewModel.RemindLaterCommand.Execute(null);

        Assert.False(viewModel.IsBannerVisible);
        Assert.True(viewModel.IsUpdateAvailable);
        Assert.True(string.IsNullOrEmpty(_settings.Current.SkippedUpdateVersion));
    }

    [Fact]
    public async Task InvalidSignature_ShowsLocalizedErrorAndNoBanner()
    {
        _handler.ManifestJson = SignManifest(tamper: true);
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(_localization.GetString("Update.Error.Signature"), viewModel.ErrorMessage);
        Assert.True(_prompt.LastErrorShown);
    }

    [Fact]
    public void ToggleAutomaticChecks_PersistsInSettings()
    {
        var viewModel = CreateViewModel();

        viewModel.AutomaticChecksEnabled = false;
        viewModel.IncludeBeta = true;

        Assert.False(_settings.Current.UpdateChecksEnabled);
        Assert.True(_settings.Current.UpdateIncludeBeta);
    }

    [Fact]
    public async Task Startup_ThrottledWithinTwentyFourHours()
    {
        _settings.Current.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        var viewModel = CreateViewModel();

        await viewModel.CheckOnStartupAsync(TimeSpan.Zero);

        Assert.Equal(0, _handler.RequestCount);
    }

    [Fact]
    public async Task ReleaseNotes_UseTheSelectedLanguage()
    {
        var viewModel = CreateViewModel();
        await viewModel.CheckAsync();
        Assert.Contains("English notes", viewModel.ReleaseNotes, StringComparison.Ordinal);

        await _localization.SetLanguageAsync("fr");

        Assert.Contains("Notes françaises", viewModel.ReleaseNotes, StringComparison.Ordinal);
        Assert.Contains("La version 9.9.9 est disponible.", viewModel.BannerText, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUpdateString_ExistsInAllFourLanguages()
    {
        var resources = new System.Resources.ResourceManager(
            "DraftEngine.App.Resources.Strings", typeof(LocalizationService).Assembly);
        foreach (var key in new[]
                 {
                     "Update.Banner", "Update.WhatsNew", "Update.Install", "Update.Later", "Update.Skip",
                     "Update.Downloading", "Update.ConfirmTitle", "Update.ConfirmBody", "Update.Installing",
                     "Update.Skipped", "Update.UpToDate", "Update.Checking", "Update.LastCheckedAt",
                     "Settings.UpdatesSection", "Settings.AutomaticUpdateChecks", "Settings.CheckNow",
                     "Update.Error.Signature", "Update.Error.Hash", "Update.Error.Generic",
                 })
        {
            foreach (var language in new[] { "en", "fr", "ar", "es" })
            {
                var value = resources.GetString(key, CultureInfo.GetCultureInfo(language));
                Assert.False(string.IsNullOrWhiteSpace(value), $"{key} missing in {language}");
            }
        }
    }

    // ------------------------------------------------------------ helpers

    private UpdateFlowViewModel CreateViewModel()
    {
        var options = new UpdateOptions
        {
            ChecksEnabled = _settings.Current.UpdateChecksEnabled,
            ManifestUrl = "https://github.com/safoineziyad/DraftEngine/releases/latest/download/latest.json",
            AllowedInstallerHosts = ["github.com"],
            UpdatesDirectory = Path.Combine(_directory, "updates"),
            StartupDelay = TimeSpan.Zero,
        };

        _handler.ManifestJson ??= SignManifest(tamper: false);
        var http = new HttpClient(_handler);
        var service = new UpdateService(
            http,
            options,
            new SettingsUpdateStateStore(_settings),
            new FixedClock(),
            new EcdsaManifestVerifier(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())),
            SemanticVersion.Parse("0.4.0"),
            _launcher);

        var snapshot = new UpdateSnapshotService(
            "Data Source=" + Path.Combine(_directory, "app.db"),
            Path.Combine(_directory, "snapshots"));

        return new UpdateFlowViewModel(_settings, options, service, _prompt, snapshot, _localization);
    }

    private string SignManifest(bool tamper)
    {
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English notes",
            ["fr"] = "Notes françaises",
            ["ar"] = "ملاحظات",
            ["es"] = "Notas",
        };
        var manifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Version = Version,
            PublishedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            ReleaseNotes = notes,
            Installer = new UpdateInstaller(InstallerUrl, FileName, InstallerBytes.Length, InstallerHash),
        };
        var signature = Convert.ToBase64String(_key.SignData(
            ManifestCanonicalizer.CanonicalBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = tamper ? "9.9.10" : Version,
            publishedAt = "2026-01-01T00:00:00Z",
            releaseNotes = notes,
            installer = new
            {
                url = InstallerUrl,
                fileName = FileName,
                sizeBytes = InstallerBytes.Length,
                sha256 = InstallerHash,
            },
            signature,
        });
        return json;
    }

    private sealed class FixedClock : IUpdateClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakePrompt : IUpdatePrompt, IDisposable
    {
        public bool Answer { get; set; }
        public bool Asked { get; private set; }
        public bool LastErrorShown { get; private set; }

        public bool ConfirmInstall(string message, string title)
        {
            Asked = true;
            return Answer;
        }

        public void ShowError(string message, string title) => LastErrorShown = true;

        public void Dispose()
        {
        }
    }

    private sealed class FakeLauncher : IInstallerLauncher, IDisposable
    {
        public int LaunchCount { get; private set; }
        public string LastPath { get; private set; } = string.Empty;

        public Task<bool> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            LastPath = installerPath;
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? ManifestJson { get; set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ManifestJson ?? "{}", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(InstallerBytes),
            });
        }
    }

    private sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public event Action? SettingsChanged { add { } remove { } }
        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
        public Task<IContractAiService?> CreateAiServiceAsync() => Task.FromResult<IContractAiService?>(null);
        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }

    private sealed class MemoryLanguageStore : ILanguagePreferenceStore
    {
        private string? _value;
        public string? Load() => _value;
        public void Save(string language) => _value = language;
    }
}
