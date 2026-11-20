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
/// End-to-end update flow over an in-process transport (the app's HTTPS host policy means a
/// plain local HTTP listener cannot serve manifests). Uses a throwaway ECDSA key created in the
/// test only: check -> download -> confirm -> snapshot -> launch, plus tampered installer,
/// tampered manifest, downgrade, wrong key and blocked host.
/// </summary>
public sealed class UpdateEndToEndTests : IDisposable
{
    private const string FileName = "DraftEngine-Setup-9.9.9.exe";
    private const string InstallerUrl = "https://github.com/safoineziyad/DraftEngine/releases/download/v9.9.9/" + FileName;
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("MZ e2e fake installer payload");

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "de-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly FakePrompt _prompt = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeHandler _handler = new();
    private readonly LocalizationService _localization;

    public UpdateEndToEndTests()
    {
        Directory.CreateDirectory(_directory);
        _localization = new LocalizationService(new MemoryLanguageStore(), () => CultureInfo.GetCultureInfo("en-US"));
        _localization.InitializeAsync().GetAwaiter().GetResult();
        LocalizationSource.Initialize(_localization);
    }

    public void Dispose()
    {
        _signingKey.Dispose();
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
    public async Task FullFlow_ChecksDownloadVerifiesConfirmsSnapshotsAndLaunches()
    {
        _prompt.Answer = true;
        _handler.ManifestJson = SignManifest("9.9.9");
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();
        Assert.True(viewModel.IsBannerVisible);

        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(1, _launcher.LaunchCount);
        Assert.Equal(FileName, Path.GetFileName(_launcher.LastPath));
        Assert.True(File.Exists(Path.Combine(_directory, "snapshots", "pre-update-9.9.9.db")));
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task TamperedInstaller_IsDeletedAndNeverLaunched()
    {
        _prompt.Answer = true;
        _handler.ManifestJson = SignManifest("9.9.9");
        var tampered = (byte[])InstallerBytes.Clone();
        tampered[^1] ^= 0xFF; // same length, different content: exercises the SHA-256 check
        _handler.InstallerBytes = tampered;
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();
        await viewModel.DownloadAndInstallAsync();

        Assert.Equal(0, _launcher.LaunchCount);
        Assert.Equal(_localization.GetString("Update.Error.Hash"), viewModel.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_directory, "updates", FileName)));
        Assert.False(_prompt.Asked);
    }

    [Fact]
    public async Task TamperedManifest_IsRejectedBeforeAnyDownload()
    {
        _handler.ManifestJson = SignManifest("9.9.9").Replace("\"version\":\"9.9.9\"", "\"version\":\"9.9.10\"");
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(_localization.GetString("Update.Error.Signature"), viewModel.ErrorMessage);
        Assert.Equal(1, _handler.RequestCount); // manifest only, never the installer
    }

    [Fact]
    public async Task Downgrade_IsRefused()
    {
        _handler.ManifestJson = SignManifest("0.0.1");
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(_localization.GetString("Update.Error.Downgrade"), viewModel.ErrorMessage);
        Assert.Equal(1, _handler.RequestCount);
    }

    [Fact]
    public async Task WrongKey_IsRejected()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _handler.ManifestJson = SignManifest("9.9.9", otherKey);
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(_localization.GetString("Update.Error.Signature"), viewModel.ErrorMessage);
    }

    [Fact]
    public async Task BlockedInstallerHost_IsRefused()
    {
        _handler.ManifestJson = SignManifest(
            "9.9.9",
            installerUrl: "https://evil.example/DraftEngine-Setup-9.9.9.exe");
        var viewModel = CreateViewModel();

        await viewModel.CheckAsync();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(_localization.GetString("Update.Error.BlockedHost"), viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------ helpers

    private UpdateFlowViewModel CreateViewModel()
    {
        var options = new UpdateOptions
        {
            ChecksEnabled = true,
            ManifestUrl = "https://github.com/safoineziyad/DraftEngine/releases/latest/download/latest.json",
            AllowedInstallerHosts = ["github.com"],
            UpdatesDirectory = Path.Combine(_directory, "updates"),
            StartupDelay = TimeSpan.Zero,
        };

        var service = new UpdateService(
            new HttpClient(_handler),
            options,
            new SettingsUpdateStateStore(_settings),
            new FixedClock(),
            new EcdsaManifestVerifier(Convert.ToBase64String(_signingKey.ExportSubjectPublicKeyInfo())),
            SemanticVersion.Parse("0.4.0"),
            _launcher);

        var snapshot = new UpdateSnapshotService(
            "Data Source=" + Path.Combine(_directory, "app.db"),
            Path.Combine(_directory, "snapshots"));

        return new UpdateFlowViewModel(_settings, options, service, _prompt, snapshot, _localization);
    }

    private string SignManifest(string version, ECDsa? key = null, string installerUrl = InstallerUrl)
    {
        var installerHash = Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant();
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English notes",
            ["fr"] = "Notes françaises",
        };
        var manifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Version = version,
            PublishedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            ReleaseNotes = notes,
            Installer = new UpdateInstaller(installerUrl, FileName, InstallerBytes.Length, installerHash),
        };

        var signature = Convert.ToBase64String((key ?? _signingKey).SignData(
            ManifestCanonicalizer.CanonicalBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version,
            publishedAt = "2026-01-01T00:00:00Z",
            releaseNotes = notes,
            installer = new
            {
                url = installerUrl,
                fileName = FileName,
                sizeBytes = InstallerBytes.Length,
                sha256 = installerHash,
            },
            signature,
        });
    }

    private sealed class FixedClock : IUpdateClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakePrompt : IUpdatePrompt
    {
        public bool Answer { get; set; }
        public bool Asked { get; private set; }

        public bool ConfirmInstall(string message, string title)
        {
            Asked = true;
            return Answer;
        }

        public void ShowError(string message, string title)
        {
        }
    }

    private sealed class FakeLauncher : IInstallerLauncher
    {
        public int LaunchCount { get; private set; }
        public string LastPath { get; private set; } = string.Empty;

        public Task<bool> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            LastPath = installerPath;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? ManifestJson { get; set; }
        public byte[] InstallerBytes { get; set; } = UpdateEndToEndTests.InstallerBytes;
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
