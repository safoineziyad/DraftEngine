using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DraftEngine.AI.Services;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
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
    public AppSettingsService(ISecretProtector protector, IHttpClientFactory httpClientFactory, string? settingsPath = null)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settingsPath = settingsPath ?? Path.Combine(
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

        // Callers that build a fresh AppSettings (theme/language/backup restore) must not wipe
        // the configured providers; only an explicitly supplied list replaces them.
        var profiles = settings.AiProfiles.Count > 0 ? settings.AiProfiles : Current.AiProfiles;
        if (profiles.Count > 0)
        {
            var active = profiles.FirstOrDefault(profile => string.Equals(profile.Id, settings.ActiveAiProfileId, StringComparison.Ordinal))
                ?? profiles.First();
            settings.ActiveAiProfileId = active.Id;
            settings.OpenAiApiKey = active.ApiKey;
            settings.OpenAiModel = string.IsNullOrWhiteSpace(active.Model) ? AppSettings.FallbackModel : active.Model;
        }

        var stored = new StoredSettings
        {
            // Empty keys are stored as empty — never encrypted, never touched on disk meaningfully.
            OpenAiApiKeyProtected = string.IsNullOrEmpty(settings.OpenAiApiKey)
                ? string.Empty
                : _protector.Protect(settings.OpenAiApiKey),
            OpenAiModel = string.IsNullOrWhiteSpace(settings.OpenAiModel)
                ? AppSettings.FallbackModel
                : settings.OpenAiModel,
            AiProfiles = profiles.Select(profile => new StoredAiProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Preset = profile.Preset,
                BaseUrl = profile.BaseUrl,
                Model = profile.Model,
                // Each profile's key is encrypted independently with the same DPAPI scope.
                ApiKeyProtected = string.IsNullOrEmpty(profile.ApiKey) ? string.Empty : _protector.Protect(profile.ApiKey),
            }).ToList(),
            ActiveAiProfileId = settings.ActiveAiProfileId,
            DefaultGoverningLaw = string.IsNullOrWhiteSpace(settings.DefaultGoverningLaw)
                ? "California"
                : settings.DefaultGoverningLaw,
            DefaultGoverningLawCountry = string.IsNullOrWhiteSpace(settings.DefaultGoverningLawCountry)
                ? DraftEngine.Core.Data.CountryCatalog.DefaultCode()
                : settings.DefaultGoverningLawCountry,
            DefaultGoverningLawSubdivision = settings.DefaultGoverningLawSubdivision ?? string.Empty,
            RecentCountryCodes = settings.RecentCountryCodes ?? new List<string>(),
            DefaultTermMonths = settings.DefaultTermMonths <= 0 ? 12 : settings.DefaultTermMonths,
            AiDataConsentAccepted = settings.AiDataConsentAccepted,
            OnboardingCompleted = settings.OnboardingCompleted,
            OpenAiBaseUrl = string.IsNullOrWhiteSpace(settings.OpenAiBaseUrl) ? null : settings.OpenAiBaseUrl,
            UseCustomOpenAiEndpoint = settings.UseCustomOpenAiEndpoint,
            HidePartyNamesFromAi = settings.HidePartyNamesFromAi,
            UpdateChecksEnabled = settings.UpdateChecksEnabled,
            UpdateIncludeBeta = settings.UpdateIncludeBeta,
            SkippedUpdateVersion = settings.SkippedUpdateVersion ?? string.Empty,
            LastUpdateCheckUtc = settings.LastUpdateCheckUtc,
            Language = settings.Language,
            Theme = settings.Theme,
            Profile = settings.Profile,
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
        var profile = Current.ActiveAiProfile;
        var keylessLocal = string.IsNullOrWhiteSpace(apiKey) && profile is { AllowsMissingKey: true };
        if (string.IsNullOrWhiteSpace(apiKey) && !keylessLocal)
            return null;

        var resolvedModel = string.IsNullOrWhiteSpace(model) ? AppSettings.FallbackModel : model;
        var baseUrl = profile is not null
            ? profile.EffectiveBaseUrl
            : Current.UseCustomOpenAiEndpoint && !string.IsNullOrWhiteSpace(Current.OpenAiBaseUrl)
                ? Current.OpenAiBaseUrl
                : null;
        // Each CreateClient() call yields a distinct HttpClient instance (OpenAiContractService
        // pins its Bearer header at construction, so instances must not be shared between
        // services with different keys), while the factory pools and rotates the handlers.
        return new OpenAiContractService(
            _httpClientFactory.CreateClient(OpenAiHttpClientName), apiKey?.Trim() ?? string.Empty, resolvedModel, null, 2, null, baseUrl, profile?.ApiKeyHeader);
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
                DefaultGoverningLawCountry = string.IsNullOrWhiteSpace(stored.DefaultGoverningLawCountry)
                    ? DraftEngine.Core.Data.CountryCatalog.DefaultCode()
                    : stored.DefaultGoverningLawCountry,
                DefaultGoverningLawSubdivision = stored.DefaultGoverningLawSubdivision ?? string.Empty,
                RecentCountryCodes = stored.RecentCountryCodes ?? new List<string>(),
                DefaultTermMonths = stored.DefaultTermMonths <= 0 ? 12 : stored.DefaultTermMonths,
                AiDataConsentAccepted = stored.AiDataConsentAccepted,
                OnboardingCompleted = stored.OnboardingCompleted,
                OpenAiBaseUrl = string.IsNullOrWhiteSpace(stored.OpenAiBaseUrl) ? null : stored.OpenAiBaseUrl,
                UseCustomOpenAiEndpoint = stored.UseCustomOpenAiEndpoint,
                HidePartyNamesFromAi = stored.HidePartyNamesFromAi,
                UpdateChecksEnabled = stored.UpdateChecksEnabled,
                UpdateIncludeBeta = stored.UpdateIncludeBeta,
                SkippedUpdateVersion = stored.SkippedUpdateVersion ?? string.Empty,
                LastUpdateCheckUtc = stored.LastUpdateCheckUtc,
                Language = string.IsNullOrWhiteSpace(stored.Language) ? "system" : stored.Language,
                Theme = string.IsNullOrWhiteSpace(stored.Theme) ? "System" : stored.Theme,
                Profile = stored.Profile,
            };
        }
        catch (Exception ex)
        {
            // A corrupt settings file must never block startup; fall back to defaults.
            Log.Warning(ex, "Could not read settings file; using defaults.");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Decrypts the stored profiles. Files written before profiles existed keep only the single
    /// legacy key/model/base URL, which becomes one default profile so nothing is lost.
    /// </summary>
    private List<AiProfile> LoadProfiles(StoredSettings stored)
    {
        var profiles = new List<AiProfile>();
        foreach (var saved in stored.AiProfiles ?? new List<StoredAiProfile>())
        {
            profiles.Add(new AiProfile
            {
                Id = string.IsNullOrWhiteSpace(saved.Id) ? Guid.NewGuid().ToString("N") : saved.Id,
                Name = string.IsNullOrWhiteSpace(saved.Name) ? "AI provider" : saved.Name,
                Preset = saved.Preset,
                BaseUrl = saved.BaseUrl ?? string.Empty,
                Model = saved.Model ?? string.Empty,
                ApiKey = string.IsNullOrWhiteSpace(saved.ApiKeyProtected)
                    ? string.Empty
                    : _protector.Unprotect(saved.ApiKeyProtected) ?? string.Empty,
            });
        }

        if (profiles.Count > 0)
        {
            return profiles;
        }

        var legacyKey = string.IsNullOrWhiteSpace(stored.OpenAiApiKeyProtected)
            ? string.Empty
            : _protector.Unprotect(stored.OpenAiApiKeyProtected) ?? string.Empty;
        if (legacyKey.Length == 0 && string.IsNullOrWhiteSpace(stored.OpenAiBaseUrl))
        {
            return profiles;
        }

        profiles.Add(new AiProfile
        {
            Id = "default",
            Name = "Default provider",
            Preset = stored.UseCustomOpenAiEndpoint ? AiProviderPreset.Custom : AiProviderPreset.OpenAiCompatible,
            BaseUrl = stored.OpenAiBaseUrl ?? string.Empty,
            Model = stored.OpenAiModel ?? AppSettings.FallbackModel,
            ApiKey = legacyKey,
        });
        return profiles;
    }

    /// <summary>On-disk JSON shape. API keys exist here only in encrypted form.</summary>
    private sealed class StoredSettings
    {
        public string OpenAiApiKeyProtected { get; set; } = string.Empty;
        public string OpenAiModel { get; set; } = AppSettings.FallbackModel;
        public List<StoredAiProfile>? AiProfiles { get; set; } = new();
        public string? ActiveAiProfileId { get; set; }
        public string DefaultGoverningLaw { get; set; } = "California";
        public string DefaultGoverningLawCountry { get; set; } = DraftEngine.Core.Data.CountryCatalog.DefaultCode();
        public string DefaultGoverningLawSubdivision { get; set; } = string.Empty;
        public List<string>? RecentCountryCodes { get; set; } = new();
        public int DefaultTermMonths { get; set; } = 12;
        public bool AiDataConsentAccepted { get; set; }
        public bool OnboardingCompleted { get; set; }
        public string? OpenAiBaseUrl { get; set; }
        public bool UseCustomOpenAiEndpoint { get; set; }
        public bool HidePartyNamesFromAi { get; set; }
        public bool UpdateChecksEnabled { get; set; } = true;
        public bool UpdateIncludeBeta { get; set; }
        public string? SkippedUpdateVersion { get; set; } = string.Empty;
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
        public string Language { get; set; } = "system";
        public string Theme { get; set; } = "System";
        public Services.Profiles.UserProfile? Profile { get; set; }
    }

    /// <summary>Stored provider profile; the key is encrypted, everything else is plain metadata.</summary>
    private sealed class StoredAiProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AiProviderPreset Preset { get; set; }
        public string? BaseUrl { get; set; }
        public string? Model { get; set; }
        public string ApiKeyProtected { get; set; } = string.Empty;
    }
}
