namespace DraftEngine.App.Services.Settings;

/// <summary>
/// In-memory application settings. <see cref="OpenAiApiKey"/> only ever lives in plaintext
/// in memory; on disk it is stored encrypted by <see cref="IAppSettingsService"/>.
/// Never log this object.
/// </summary>
public class AppSettings
{
    /// <summary>The default OpenAI chat model used when the user has not chosen one.</summary>
    public const string FallbackModel = "gpt-4o-mini";

    /// <summary>The user's OpenAI API key (plaintext in memory only; encrypted at rest).</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>The selected OpenAI chat model id (e.g. "gpt-4o-mini" or "gpt-4o").</summary>
    public string OpenAiModel { get; set; } = FallbackModel;

    /// <summary>The default governing law for new contracts.</summary>
    public string DefaultGoverningLaw { get; set; } = "California";

    /// <summary>The default contract term length in months.</summary>
    public int DefaultTermMonths { get; set; } = 12;
}
