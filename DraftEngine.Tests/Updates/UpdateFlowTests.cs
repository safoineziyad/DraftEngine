using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftEngine.Updates;

namespace DraftEngine.Tests.Updates;

/// <summary>
/// Signed-manifest parsing/verification and the update service: cadence, downgrade refusal, host
/// policy, hash/truncation checks, snapshot-before-launch and zero requests when disabled.
/// </summary>
public sealed class UpdateFlowTests : IDisposable
{
    private const string InstallerUrl =
        "https://github.com/safoineziyad/DraftEngine/releases/download/v0.9.0/DraftEngine-Setup-0.9.0.exe";
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("MZ fake installer bytes");

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKey;
    private readonly string _updatesDirectory = Path.Combine(
        Path.GetTempPath(), "de-updates-" + Guid.NewGuid().ToString("N"));

    public UpdateFlowTests()
    {
        _publicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    }

    public void Dispose()
    {
        _key.Dispose();
        try
        {
            if (Directory.Exists(_updatesDirectory)) Directory.Delete(_updatesDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------ parser

    [Fact]
    public void Parser_AcceptsAValidManifest_AndExposesLocalizedNotes()
    {
        var json = BuildJson(Sign(Manifest()));

        Assert.True(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out var manifest, out var error));
        Assert.Null(error);
        Assert.Equal("0.9.0", manifest.Version);
        Assert.Equal("English notes", manifest.NotesFor("en-US"));
        Assert.Equal("Notes françaises", manifest.NotesFor("fr"));
        Assert.Equal("English notes", manifest.NotesFor("de")); // falls back to en
    }

    [Theory]
    [InlineData("", UpdateErrorCode.MalformedManifest)]
    [InlineData("{not json", UpdateErrorCode.MalformedManifest)]
    [InlineData("[]", UpdateErrorCode.MalformedManifest)]
    public void Parser_RejectsMalformedJson(string json, UpdateErrorCode expected)
    {
        Assert.False(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out _, out var error));
        Assert.Equal(expected, error!.Code);
    }

    [Fact]
    public void Parser_RejectsUnsupportedSchema()
    {
        var manifest = Manifest();
        var json = BuildJson(Sign(manifest)).Replace("\"schemaVersion\":1", "\"schemaVersion\":2");

        Assert.False(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out _, out var error));
        Assert.Equal(UpdateErrorCode.UnsupportedSchema, error!.Code);
    }

    [Fact]
    public void Parser_RejectsBadHashAndFileName()
    {
        var manifest = Manifest();
        var json = BuildJson(Sign(manifest)).Replace(manifest.Installer.Sha256, "not-a-hash");
        Assert.False(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out _, out var error));
        Assert.Equal(UpdateErrorCode.MalformedManifest, error!.Code);

        var evilName = Manifest();
        var evilJson = BuildJson(Sign(evilName)).Replace("DraftEngine-Setup-0.9.0.exe", "../evil.exe");
        Assert.False(UpdateManifestParser.TryParse(evilJson, new UpdateHostPolicy(), out _, out error));
        Assert.Equal(UpdateErrorCode.MalformedManifest, error!.Code);
    }

    [Theory]
    [InlineData("https://github.com.evil.example/x/DraftEngine-Setup-0.9.0.exe")]
    [InlineData("https://evilgithub.com/x/DraftEngine-Setup-0.9.0.exe")]
    [InlineData("https://github.com@evil.example/x.exe")]
    [InlineData("http://github.com/x/DraftEngine-Setup-0.9.0.exe")]
    [InlineData("https://objects.githubusercontent.com.evil.example/a.exe")]
    public void Parser_BlocksLookAlikeAndHttpInstallerHosts(string url)
    {
        var manifest = Manifest(url);
        var json = BuildJson(Sign(manifest));

        Assert.False(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out _, out var error));
        Assert.Equal(UpdateErrorCode.BlockedHost, error!.Code);
    }

    [Theory]
    [InlineData("https://github.com/a/DraftEngine-Setup-0.9.0.exe")]
    [InlineData("https://objects.githubusercontent.com/a/DraftEngine-Setup-0.9.0.exe")]
    public void Parser_AllowsConfiguredInstallerHosts(string url)
    {
        var manifest = Manifest(url);
        var json = BuildJson(Sign(manifest));

        Assert.True(UpdateManifestParser.TryParse(json, new UpdateHostPolicy(), out _, out _));
    }

    // ------------------------------------------------------------ signature

    [Fact]
    public void Signature_ValidManifestVerifies()
    {
        var manifest = Sign(Manifest());
        using var verifier = new EcdsaManifestVerifier(_publicKey);
        Assert.True(verifier.Verify(manifest));
    }

    [Fact]
    public void Signature_TamperedFieldFails()
    {
        var manifest = Sign(Manifest());
        var tampered = new UpdateManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            Version = manifest.Version,
            PublishedAt = manifest.PublishedAt,
            MinimumSupportedVersion = manifest.MinimumSupportedVersion,
            ReleaseNotes = manifest.ReleaseNotes,
            Installer = manifest.Installer with { SizeBytes = manifest.Installer.SizeBytes + 1 },
            Signature = manifest.Signature,
        };

        using var verifier = new EcdsaManifestVerifier(_publicKey);
        Assert.False(verifier.Verify(tampered));
    }

    [Fact]
    public void Signature_WrongKeyFails()
    {
        var manifest = Sign(Manifest());
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new EcdsaManifestVerifier(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()));
        Assert.False(verifier.Verify(manifest));
    }

    [Fact]
    public void Signature_MissingOrMalformedFails()
    {
        using var verifier = new EcdsaManifestVerifier(_publicKey);
        var manifest = Manifest();
        Assert.False(verifier.Verify(manifest));
        manifest.Signature = "not-base64!!";
        Assert.False(verifier.Verify(manifest));
    }

    [Fact]
    public void CanonicalForm_IsStable_AndIgnoresNotesOrdering()
    {
        var first = Manifest();
        var second = Manifest();
        second.ReleaseNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English notes",
            ["fr"] = "Notes françaises",
        };

        Assert.Equal(
            ManifestCanonicalizer.CanonicalJson(first),
            ManifestCanonicalizer.CanonicalJson(second));
    }

    // ------------------------------------------------------------ check

    [Fact]
    public async Task Check_Disabled_MakesNoRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No HTTP when disabled."));
        var service = CreateService(handler, options => options.ChecksEnabled = false);

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Check_AutomaticWithinInterval_IsThrottledWithoutRequest()
    {
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(Manifest()))));
        var state = new InMemoryUpdateStateStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(handler, state: state, clock: clock);

        await service.CheckAsync(manual: false);
        var second = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.Throttled, second.Status);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Check_ManualBypassesCadence()
    {
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(Manifest()))));
        var state = new InMemoryUpdateStateStore();
        var service = CreateService(handler, state: state);

        await service.CheckAsync(manual: true);
        await service.CheckAsync(manual: true);

        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Check_NewerVerifiedManifest_IsOffered()
    {
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(Manifest()))));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.9.0", result.Manifest!.Version);
    }

    [Fact]
    public async Task Check_SameVersion_IsUpToDate()
    {
        var manifest = Manifest("0.4.0-beta.1", "0.4.0-beta.1");
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(manifest))));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Check_OlderVersion_IsRefusedAsDowngrade()
    {
        var manifest = Manifest("0.2.2", "0.2.2");
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(manifest))));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Error, result.Status);
        Assert.Equal(UpdateErrorCode.Downgrade, result.Error!.Code);
    }

    [Fact]
    public async Task Check_StableChannel_IgnoresPrerelease()
    {
        var manifest = Manifest("0.9.0-beta.1", "0.9.0.1");
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(manifest))));
        var result = await CreateService(handler).CheckAsync(UpdateChannel.Stable, manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Check_BetaChannel_AcceptsPrerelease()
    {
        var manifest = Manifest("0.9.0-beta.1", "0.9.0.1");
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(manifest))));
        var service = CreateService(handler, options =>
            options.BetaManifestUrl = "https://example.test/beta/latest.json");

        var result = await service.CheckAsync(UpdateChannel.Beta, manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public async Task Check_SkippedVersion_IsNotOffered()
    {
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(Manifest()))));
        var state = new InMemoryUpdateStateStore();
        state.SkipVersion("0.9.0");
        var result = await CreateService(handler, state: state).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task Check_InvalidSignature_IsRejected()
    {
        var json = BuildJson(Manifest()); // unsigned
        var handler = new RecordingHandler(_ => Json(json));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Error, result.Status);
        Assert.Equal(UpdateErrorCode.InvalidSignature, result.Error!.Code);
    }

    [Fact]
    public async Task Check_TamperedManifest_IsRejected()
    {
        var signed = Sign(Manifest());
        var json = BuildJson(signed).Replace("\"sizeBytes\":" + signed.Installer.SizeBytes,
            "\"sizeBytes\":" + (signed.Installer.SizeBytes + 5));
        var result = await CreateService(new RecordingHandler(_ => Json(json))).CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.InvalidSignature, result.Error!.Code);
    }

    [Fact]
    public async Task Check_404_ReportsNotFound()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Check_Timeout_ReportsTimeout()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Json("{}");
        });
        var service = CreateService(handler, options => options.ManifestTimeout = TimeSpan.FromMilliseconds(50));

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.Timeout, result.Error!.Code);
    }

    [Fact]
    public async Task Check_NetworkFailure_ReportsOffline()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("no network"));
        var result = await CreateService(handler).CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.Offline, result.Error!.Code);
    }

    [Fact]
    public async Task Check_MalformedJson_ReportsMalformed()
    {
        var result = await CreateService(new RecordingHandler(_ => Json("{oops"))).CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.MalformedManifest, result.Error!.Code);
    }

    [Fact]
    public async Task Check_BlocksUnsafeManifestUrl()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not request"));
        var service = CreateService(handler, options => options.ManifestUrl = "http://example.test/latest.json");

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateErrorCode.BlockedHost, result.Error!.Code);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Check_LogsOnlyVersionsAndErrorTypes()
    {
        var lines = new List<string>();
        var manifest = Manifest("0.2.2", "0.2.2");
        var handler = new RecordingHandler(_ => Json(BuildJson(Sign(manifest))));
        var service = CreateService(handler, log: lines.Add);

        await service.CheckAsync(manual: true);

        Assert.Contains(lines, line =>
            line.Contains("downgrade", StringComparison.OrdinalIgnoreCase) && line.Contains("0.2.2"));
        Assert.DoesNotContain(lines, line => line.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------ download

    [Fact]
    public async Task Download_ValidFile_IsVerifiedAndMoved()
    {
        var manifest = Sign(Manifest(installerBytes: InstallerBytes));
        var handler = new RecordingHandler(_ => Bytes(InstallerBytes));
        var progress = new List<double>();

        var result = await CreateService(handler).DownloadAsync(manifest, new Progress<double>(progress.Add));

        Assert.True(result.Success, result.Error?.Code.ToString());
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(InstallerBytes.Length, result.SizeBytes);
        Assert.False(File.Exists(result.FilePath + ".partial"));
        Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(result.FilePath!));
    }

    [Fact]
    public async Task Download_HashMismatch_IsRejectedAndDeleted()
    {
        var manifest = Sign(Manifest(installerBytes: InstallerBytes));
        var handler = new RecordingHandler(_ => Bytes(Encoding.UTF8.GetBytes("MZ different bytes")));

        var result = await CreateService(handler).DownloadAsync(manifest);

        Assert.False(result.Success);
        Assert.Equal(UpdateErrorCode.TruncatedDownload, result.Error!.Code);
        Assert.Empty(Directory.GetFiles(_updatesDirectory, "*.partial", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Download_HashMismatchWithCorrectLength_ReportsHash()
    {
        var manifest = Sign(Manifest(installerBytes: InstallerBytes));
        var different = (byte[])InstallerBytes.Clone();
        different[0] = (byte)'X';
        var handler = new RecordingHandler(_ => Bytes(different));

        var result = await CreateService(handler).DownloadAsync(manifest);

        Assert.Equal(UpdateErrorCode.HashMismatch, result.Error!.Code);
        Assert.Empty(Directory.GetFiles(_updatesDirectory, "*.partial", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Download_TruncatedBody_ReportsTruncation()
    {
        var manifest = Sign(Manifest(installerBytes: InstallerBytes));
        var handler = new RecordingHandler(_ => Bytes(InstallerBytes[..5]));

        var result = await CreateService(handler).DownloadAsync(manifest);

        Assert.Equal(UpdateErrorCode.TruncatedDownload, result.Error!.Code);
    }

    [Fact]
    public async Task Download_BlockedInstallerHost_IsRejectedBeforeAnyRequest()
    {
        var manifest = Sign(Manifest("https://evil.example/DraftEngine-Setup-0.9.0.exe", InstallerBytes));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not request"));

        var result = await CreateService(handler).DownloadAsync(manifest);

        Assert.Equal(UpdateErrorCode.BlockedHost, result.Error!.Code);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Download_RequiredAuthenticodeFailure_IsRejected()
    {
        var manifest = Sign(Manifest(installerBytes: InstallerBytes));
        var handler = new RecordingHandler(_ => Bytes(InstallerBytes));
        var service = CreateService(handler, options => options.RequireAuthenticode = true,
            authenticode: new StubAuthenticode(false));

        var result = await service.DownloadAsync(manifest);

        Assert.Equal(UpdateErrorCode.AuthenticodeInvalid, result.Error!.Code);
    }

    // ------------------------------------------------------------ install

    [Fact]
    public async Task Install_TakesSnapshotThenLaunchesOnce()
    {
        var path = Path.Combine(_updatesDirectory, "DraftEngine-Setup-0.9.0.exe");
        Directory.CreateDirectory(_updatesDirectory);
        await File.WriteAllBytesAsync(path, InstallerBytes);
        var launcher = new StubLauncher();
        var order = new List<string>();
        launcher.OnLaunch = _ => order.Add("launch");

        var result = await CreateService(new RecordingHandler(_ => Json("{}")), launcher: launcher)
            .InstallAsync(path, () =>
            {
                order.Add("snapshot");
                return Task.CompletedTask;
            });

        Assert.True(result.Success);
        Assert.Equal(new[] { "snapshot", "launch" }, order);
        Assert.Equal(path, launcher.LastPath);
    }

    [Fact]
    public async Task Install_SnapshotFailure_DoesNotLaunch()
    {
        var path = Path.Combine(_updatesDirectory, "installer.exe");
        Directory.CreateDirectory(_updatesDirectory);
        await File.WriteAllBytesAsync(path, InstallerBytes);
        var launcher = new StubLauncher();

        var result = await CreateService(new RecordingHandler(_ => Json("{}")), launcher: launcher)
            .InstallAsync(path, () => throw new IOException("disk full"));

        Assert.Equal(UpdateErrorCode.SnapshotFailed, result.Error!.Code);
        Assert.Null(launcher.LastPath);
    }

    [Fact]
    public async Task Install_LauncherFailure_KeepsAppRunning()
    {
        var path = Path.Combine(_updatesDirectory, "installer.exe");
        Directory.CreateDirectory(_updatesDirectory);
        await File.WriteAllBytesAsync(path, InstallerBytes);

        var result = await CreateService(new RecordingHandler(_ => Json("{}")), launcher: new StubLauncher { Result = false })
            .InstallAsync(path);

        Assert.Equal(UpdateErrorCode.InstallerLaunchFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Install_MissingInstaller_IsRejected()
    {
        var result = await CreateService(new RecordingHandler(_ => Json("{}")))
            .InstallAsync(Path.Combine(_updatesDirectory, "missing.exe"));

        Assert.Equal(UpdateErrorCode.InstallerLaunchFailed, result.Error!.Code);
    }

    // ------------------------------------------------------------ helpers

    private UpdateService CreateService(
        HttpMessageHandler handler,
        Action<UpdateOptions>? configure = null,
        InMemoryUpdateStateStore? state = null,
        FakeClock? clock = null,
        Action<string>? log = null,
        IInstallerLauncher? launcher = null,
        IAuthenticodeVerifier? authenticode = null)
    {
        var options = new UpdateOptions
        {
            ChecksEnabled = true,
            ManifestUrl = "https://updates.example.test/latest.json",
            PublicKeySpkiBase64 = _publicKey,
            UpdatesDirectory = _updatesDirectory,
            UserAgent = "DraftEngine/0.4.0-beta.1",
            ManifestTimeout = TimeSpan.FromSeconds(1),
        };
        configure?.Invoke(options);

        return new UpdateService(
            new HttpClient(handler),
            options,
            state ?? new InMemoryUpdateStateStore(),
            clock ?? new FakeClock(DateTimeOffset.UtcNow),
            new EcdsaManifestVerifier(options.PublicKeySpkiBase64),
            SemanticVersion.Parse("0.4.0-beta.1"),
            launcher,
            authenticode,
            log);
    }

    private UpdateManifest Manifest(string url = InstallerUrl) => Manifest("0.9.0", "0.9.0.1", url, InstallerBytes);

    private UpdateManifest Manifest(string version, string fileVersion) =>
        Manifest(version, fileVersion, InstallerUrl, InstallerBytes);

    private static UpdateManifest Manifest(string version, string fileVersion, string url, byte[] bytes)
    {
        _ = fileVersion;
        return new UpdateManifest
        {
            SchemaVersion = 1,
            Version = version,
            PublishedAt = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero),
            ParsedVersion = SemanticVersion.Parse(version),
            ReleaseNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = "English notes",
                ["fr"] = "Notes françaises",
            },
            Installer = new UpdateInstaller(
                url,
                "DraftEngine-Setup-0.9.0.exe",
                bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
        };
    }

    private static UpdateManifest Manifest(string url, byte[] bytes) =>
        Manifest("0.9.0", "0.9.0.1", url, bytes);

    private static UpdateManifest Manifest(byte[] installerBytes) => Manifest(InstallerUrl, installerBytes);

    private UpdateManifest Sign(UpdateManifest manifest)
    {
        manifest.Signature = Convert.ToBase64String(_key.SignData(
            ManifestCanonicalizer.CanonicalBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return manifest;
    }

    private static string BuildJson(UpdateManifest manifest) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = manifest.SchemaVersion,
            version = manifest.Version,
            publishedAt = ManifestCanonicalizer.FormatTimestamp(manifest.PublishedAt),
            minimumSupportedVersion = manifest.MinimumSupportedVersion,
            releaseNotes = manifest.ReleaseNotes,
            installer = new
            {
                url = manifest.Installer.Url,
                fileName = manifest.Installer.FileName,
                sizeBytes = manifest.Installer.SizeBytes,
                sha256 = manifest.Installer.Sha256,
            },
            signature = manifest.Signature,
        });

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class FakeClock : IUpdateClock
    {
        public FakeClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StubLauncher : IInstallerLauncher
    {
        public bool Result { get; set; } = true;
        public Action<string>? OnLaunch { get; set; }
        public string? LastPath { get; private set; }

        public Task<bool> LaunchAsync(string installerPath, CancellationToken cancellationToken = default)
        {
            LastPath = installerPath;
            OnLaunch?.Invoke(installerPath);
            return Task.FromResult(Result);
        }
    }

    private sealed class StubAuthenticode : IAuthenticodeVerifier
    {
        private readonly bool _result;
        public StubAuthenticode(bool result) => _result = result;
        public bool IsValid(string filePath) => _result;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = (request, _) => Task.FromResult(respond(request));

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return _respond(request, cancellationToken);
        }
    }
}
