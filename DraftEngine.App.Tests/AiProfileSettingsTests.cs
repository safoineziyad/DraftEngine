using System.IO;
using System.Net.Http;
using System.Text;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Models;

namespace DraftEngine.App.Tests;

/// <summary>
/// AI provider profiles round-trip through settings.json with per-profile encrypted keys, and a
/// legacy single-key file is migrated into one profile without losing the key.
/// </summary>
public sealed class AiProfileSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "de-ai-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public AiProfileSettingsTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; the temp folder is disposable.
        }
    }

    [Fact]
    public async Task Profiles_RoundTrip_AndKeysAreEncryptedOnDisk()
    {
        var service = CreateService();
        await service.SaveAsync(new AppSettings
        {
            AiProfiles =
            {
                new AiProfile
                {
                    Id = "local",
                    Name = "Local Ollama",
                    Preset = AiProviderPreset.Ollama,
                    BaseUrl = "http://localhost:11434/v1/",
                    Model = "llama3.1:8b",
                    ApiKey = string.Empty,
                },
                new AiProfile
                {
                    Id = "remote",
                    Name = "Remote",
                    Preset = AiProviderPreset.OpenAiCompatible,
                    BaseUrl = "https://api.example.test/v1/",
                    Model = "self-hosted-model",
                    ApiKey = "sk-super-secret-value",
                },
            },
            ActiveAiProfileId = "remote",
        });

        var onDisk = await File.ReadAllTextAsync(_path);
        Assert.DoesNotContain("sk-super-secret-value", onDisk);

        var reloaded = CreateService();
        Assert.Equal(2, reloaded.Current.AiProfiles.Count);
        Assert.Equal("remote", reloaded.Current.ActiveAiProfileId);
        Assert.Equal("sk-super-secret-value", reloaded.Current.ActiveAiProfile!.ApiKey);
        Assert.Equal("self-hosted-model", reloaded.Current.ActiveAiProfile.Model);
        // The legacy single-provider fields mirror the active profile.
        Assert.Equal("self-hosted-model", reloaded.Current.OpenAiModel);
        Assert.Equal("sk-super-secret-value", reloaded.Current.OpenAiApiKey);
    }

    [Fact]
    public void LegacySingleKeyFile_BecomesOneProfile()
    {
        var json = """
        {
          "OpenAiApiKeyProtected": "enc:c2stbGVnYWN5LWtleQ==",
          "OpenAiModel": "legacy-model",
          "OpenAiBaseUrl": "http://localhost:11434/v1/",
          "UseCustomOpenAiEndpoint": true
        }
        """;
        File.WriteAllText(_path, json, Encoding.UTF8);

        var service = CreateService();

        var profile = Assert.Single(service.Current.AiProfiles);
        Assert.Equal("sk-legacy-key", profile.ApiKey);
        Assert.Equal("legacy-model", profile.Model);
        Assert.Equal(AiProviderPreset.Custom, profile.Preset);
        Assert.Equal("http://localhost:11434/v1/", profile.BaseUrl);
    }

    [Fact]
    public async Task SavingWithoutProfiles_DoesNotWipeConfiguredProfiles()
    {
        var service = CreateService();
        await service.SaveAsync(new AppSettings
        {
            AiProfiles = { new AiProfile { Id = "p1", Name = "P1", Model = "m1", ApiKey = "sk-key-1" } },
            ActiveAiProfileId = "p1",
        });

        // Theme/language stores build a fresh settings object; that must preserve providers.
        await service.SaveAsync(new AppSettings { Theme = "Dark" });

        Assert.Single(service.Current.AiProfiles);
        Assert.Equal("m1", service.Current.ActiveAiProfile!.Model);
        Assert.Equal("Dark", service.Current.Theme);
    }

    private AppSettingsService CreateService() => new(new EncodingProtector(), new NullHttpClientFactory(), _path);

    /// <summary>Reversible stand-in for DPAPI: DPAPI itself is unavailable in some test hosts.</summary>
    private sealed class EncodingProtector : ISecretProtector
    {
        public string Protect(string plaintext) =>
            "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return null;
            if (!protectedValue.StartsWith("enc:", StringComparison.Ordinal)) return null;
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[4..]));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
