using System.IO;
using Microsoft.Extensions.Configuration;

namespace DraftEngine.App.Configuration;

/// <summary>
/// Strongly-typed application settings bound from appsettings.json (plus environment
/// overrides). Registered as a singleton so any service can consume it via DI.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Gets the display settings (window title, message box captions).</summary>
    public AppDisplayOptions App { get; } = new();

    /// <summary>Gets the OpenAI settings.</summary>
    public OpenAiOptions OpenAi { get; } = new();

    /// <summary>Gets the database settings.</summary>
    public DataOptions Data { get; } = new();

    /// <summary>Gets the file logging settings.</summary>
    public SerilogOptions Serilog { get; } = new();

    /// <summary>Gets the commercial-readiness feature switches.</summary>
    public FeatureOptions Features { get; } = new();

    /// <summary>
    /// Loads the options from configuration and applies environment-variable overrides.
    /// The <c>OPENAI_API_KEY</c> environment variable wins over <c>OpenAI:ApiKey</c> so a
    /// real key never has to live in (or be committed to) appsettings.json.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound options.</returns>
    public static AppOptions Load(IConfiguration configuration)
    {
        var options = new AppOptions();

        options.App.DisplayName = configuration["App:DisplayName"] ?? options.App.DisplayName;
        options.OpenAi.ApiKey = configuration["OpenAI:ApiKey"] ?? options.OpenAi.ApiKey;
        options.OpenAi.Model = configuration["OpenAI:Model"] ?? options.OpenAi.Model;
        options.OpenAi.BaseUrl = configuration["OpenAI:BaseUrl"] ?? options.OpenAi.BaseUrl;
        if (bool.TryParse(configuration["OpenAI:UseCustomEndpoint"], out var useCustom))
        {
            options.OpenAi.UseCustomEndpoint = useCustom;
        }
        options.Data.ConnectionString = configuration["Data:ConnectionString"] ?? options.Data.ConnectionString;
        options.Serilog.FilePath = configuration["Serilog:FilePath"] ?? options.Serilog.FilePath;

        // Commercial features stay opt-in except for the non-identifying GitHub auto-update check.
        options.Features.LicensingEnabled = ReadBool(configuration, "Features:LicensingEnabled");
        options.Features.LicenseGracePeriodDays = ReadNonNegativeInt(
            configuration, "Features:LicenseGracePeriodDays", options.Features.LicenseGracePeriodDays);
        options.Features.AutoUpdateEnabled = ReadBool(
            configuration, "Features:AutoUpdateEnabled", options.Features.AutoUpdateEnabled);
        options.Features.UpdateCheckEnabled = ReadBool(configuration, "Features:UpdateCheckEnabled");
        options.Features.UpdateManifestUrl = configuration["Features:UpdateManifestUrl"] ?? options.Features.UpdateManifestUrl;
        options.Features.OnboardingEnabled = ReadBool(configuration, "Features:OnboardingEnabled");
        options.Features.AboutEnabled = ReadBool(configuration, "Features:AboutEnabled");

        // Environment variable override: OPENAI_API_KEY always wins over appsettings.json.
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            options.OpenAi.ApiKey = envApiKey.Trim();
        }
        var envBaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        if (!string.IsNullOrWhiteSpace(envBaseUrl))
        {
            options.OpenAi.BaseUrl = envBaseUrl.Trim();
            options.OpenAi.UseCustomEndpoint = true;
        }

        var dataDirectory = Environment.GetEnvironmentVariable("DRAFTENGINE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            options.Data.ConnectionString = $"Data Source={Path.Combine(dataDirectory.Trim(), "draftengine.db")}";
        }

        return options;
    }

    private static bool ReadBool(IConfiguration configuration, string key) =>
        bool.TryParse(configuration[key], out var value) && value;

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;

    private static int ReadNonNegativeInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value >= 0 ? value : fallback;
}

/// <summary>Display settings.</summary>
public sealed class AppDisplayOptions
{
    /// <summary>Gets or sets the application display name used as the window title.</summary>
    public string DisplayName { get; set; } = "DraftEngine";
}

/// <summary>OpenAI connection settings.</summary>
public sealed class OpenAiOptions
{
    /// <summary>Gets or sets the OpenAI API key. Empty means "not configured"; AI features
    /// degrade gracefully until a key is provided (via config or the OPENAI_API_KEY
    /// environment variable).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the chat model id.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Gets or sets the API base URL. Defaults to OpenAI's endpoint; can be set to any OpenAI-compatible server.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>Gets or sets whether to use a custom endpoint.</summary>
    public bool UseCustomEndpoint { get; set; } = false;
}

/// <summary>Database settings.</summary>
public sealed class DataOptions
{
    /// <summary>Gets or sets the SQLite connection string. Environment variables
    /// (e.g. <c>%APPDATA%</c>) are expanded at startup.</summary>
    public string ConnectionString { get; set; } = "Data Source=%APPDATA%\\DraftEngine\\draftengine.db";
}

/// <summary>Rolling file log settings.</summary>
public sealed class SerilogOptions
{
    /// <summary>Gets or sets the rolling log file path. Environment variables
    /// (e.g. <c>%APPDATA%</c>) are expanded at startup.</summary>
    public string FilePath { get; set; } = "%APPDATA%\\DraftEngine\\logs\\app-.log";
}

/// <summary>
/// Commercial-readiness switches. The GitHub auto-update check is enabled by default; all
/// other feature flags remain disabled.
/// </summary>
public sealed class FeatureOptions
{
    /// <summary>Gets or sets whether the offline license check/activation screen is enabled.</summary>
    public bool LicensingEnabled { get; set; }

    /// <summary>Gets or sets how many days after expiry the app keeps working offline.</summary>
    public int LicenseGracePeriodDays { get; set; } = 14;

    /// <summary>Gets or sets whether the GitHub auto-update check runs at startup.</summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>Gets or sets whether the legacy manifest check is available on the About page.</summary>
    public bool UpdateCheckEnabled { get; set; }

    /// <summary>Gets or sets the HTTPS URL of the static update manifest.</summary>
    public string UpdateManifestUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the first-run onboarding dialog is shown.</summary>
    public bool OnboardingEnabled { get; set; }

    /// <summary>Gets or sets whether the About/Help navigation entry is shown.</summary>
    public bool AboutEnabled { get; set; }
}
