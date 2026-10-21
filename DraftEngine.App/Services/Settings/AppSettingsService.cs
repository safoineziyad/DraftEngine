using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DraftEngine.AI.Services;
using DraftEngine.Core.Interfaces;
using Serilog;

namespace DraftEngine.App.Services.Settings;

/// <summary>
/// <see cref="IAppSettingsService"/> backed by %APPDATA%\DraftEngine\settings.json.
/// Non-secret values are stored as plain JSON; the OpenAI API key is encrypted with
/// <see cref="ISecretProtector"/> (DPAPI, current Windows user) and only decrypted into
/// <see cref="Current"/> in memory. The raw key is never written to disk, logs or exceptions.
/// Settings are loaded synchronously at construction (tiny local file) and saved on demand.
/// HTTP goes through the shared "openai" <see cref="IHttpClientFactory"/> client
/// (audit Finding #6: no <see cref="HttpClient"/> is constructed by hand).
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    /// <summary>Name of the factory client used for every OpenAI call.</summary>
    private const string OpenAiHttpClientName = "openai";

    private readonly ISecretProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _settingsPath;

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public event Action? SettingsChanged;

    /// <summary>Creates the service and loads existing settings from %APPDATA%\DraftEngine\settings.json (defaults on any failure).</summary>
    public AppSettingsService(ISecretProtector protector, IHttpClientFactory httpClientFactory)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DraftEngine",
            "settings.json");
        Current = LoadFromDisk();
    }

    /// <inheritdoc />
    public async Task<AppSettings> ReloadAsync()
    {
        Current = await Task.Run(LoadFromDisk);
        SettingsChanged?.Invoke();
        return Current;
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var stored = new StoredSettings
        {
            // Empty keys are stored as empty — never encrypted, never touched on disk meaningfully.
            OpenAiApiKeyProtected = string.IsNullOrEmpty(settings.OpenAiApiKey)
                ? string.Empty
                : _protector.Protect(settings.OpenAiApiKey),
            OpenAiModel = string.IsNullOrWhiteSpace(settings.OpenAiModel)
                ? AppSettings.FallbackModel
                : settings.OpenAiModel,
            DefaultGoverningLaw = string.IsNullOrWhiteSpace(settings.DefaultGoverningLaw)
                ? "California"
                : settings.DefaultGoverningLaw,
            DefaultTermMonths = settings.DefaultTermMonths <= 0 ? 12 : settings.DefaultTermMonths,
            AiDataConsentAccepted = settings.AiDataConsentAccepted,
        };

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(_settingsPath, json);

        // Keep the in-memory copy in sync (decrypt fresh from what we just stored).
        Current = LoadFromDisk();
        SettingsChanged?.Invoke();
        Log.Information("Settings saved to {Path}.", _settingsPath);
    }

    /// <inheritdoc />
    public Task<IContractAiService?> CreateAiServiceAsync()
    {
        IContractAiService? service = CreateAiService(Current.OpenAiApiKey, Current.OpenAiModel);
        return Task.FromResult(service);
    }

    /// <inheritdoc />
    public IContractAiService? CreateAiService(string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var resolvedModel = string.IsNullOrWhiteSpace(model) ? AppSettings.FallbackModel : model;
        // Each CreateClient() call yields a distinct HttpClient instance (OpenAiContractService
        // pins its Bearer header at construction, so instances must not be shared between
        // services with different keys), while the factory pools and rotates the handlers.
        return new OpenAiContractService(
            _httpClientFactory.CreateClient(OpenAiHttpClientName), apiKey.Trim(), resolvedModel);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var stored = JsonSerializer.Deserialize<StoredSettings>(json, JsonOptions);
            if (stored is null)
                return new AppSettings();

            return new AppSettings
            {
                OpenAiApiKey = string.IsNullOrWhiteSpace(stored.OpenAiApiKeyProtected)
                    ? string.Empty
                    : _protector.Unprotect(stored.OpenAiApiKeyProtected) ?? string.Empty,
                OpenAiModel = string.IsNullOrWhiteSpace(stored.OpenAiModel)
                    ? AppSettings.FallbackModel
                    : stored.OpenAiModel,
                DefaultGoverningLaw = string.IsNullOrWhiteSpace(stored.DefaultGoverningLaw)
                    ? "California"
                    : stored.DefaultGoverningLaw,
                DefaultTermMonths = stored.DefaultTermMonths <= 0 ? 12 : stored.DefaultTermMonths,
                AiDataConsentAccepted = stored.AiDataConsentAccepted,
            };
        }
        catch (Exception ex)
        {
            // A corrupt settings file must never block startup; fall back to defaults.
            Log.Warning(ex, "Could not read settings file; using defaults.");
            return new AppSettings();
        }
    }

    /// <summary>On-disk JSON shape. The API key exists here only in encrypted form.</summary>
    private sealed class StoredSettings
    {
        public string OpenAiApiKeyProtected { get; set; } = string.Empty;
        public string OpenAiModel { get; set; } = AppSettings.FallbackModel;
        public string DefaultGoverningLaw { get; set; } = "California";
        public int DefaultTermMonths { get; set; } = 12;
        public bool AiDataConsentAccepted { get; set; }
    }
}
