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
        options.Data.ConnectionString = configuration["Data:ConnectionString"] ?? options.Data.ConnectionString;
        options.Serilog.FilePath = configuration["Serilog:FilePath"] ?? options.Serilog.FilePath;

        // Environment variable override: OPENAI_API_KEY always wins over appsettings.json.
        var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            options.OpenAi.ApiKey = envApiKey.Trim();
        }

        return options;
    }
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
