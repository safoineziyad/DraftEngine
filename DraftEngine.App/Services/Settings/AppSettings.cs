using System;
using System.Collections.Generic;
using System.Linq;
using DraftEngine.Core.Models;

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

    /// <summary>The default governing law for new contracts (display text).</summary>
    public string DefaultGoverningLaw { get; set; } = "California";

    /// <summary>Default governing-law country (ISO alpha-2), initially the Windows region.</summary>
    public string DefaultGoverningLawCountry { get; set; } = DraftEngine.Core.Data.CountryCatalog.DefaultCode();

    /// <summary>Default governing-law subdivision, when the country has a built-in list.</summary>
    public string DefaultGoverningLawSubdivision { get; set; } = string.Empty;

    /// <summary>Recently used country codes, most recent first (max 5), for picker ordering.</summary>
    public List<string> RecentCountryCodes { get; set; } = new();

    /// <summary>The default contract term length in months.</summary>
    public int DefaultTermMonths { get; set; } = 12;

    /// <summary>
    /// True once the user has acknowledged that AI edits send the contract text to OpenAI
    /// (audit Finding #10). Persisted so the acknowledgement is only ever asked once.
    /// </summary>
    public bool AiDataConsentAccepted { get; set; }

    /// <summary>
    /// True once the first-run onboarding dialog has been completed or skipped.
    /// Only consulted when <c>Features:OnboardingEnabled</c> is on.
    /// </summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>Custom OpenAI-compatible base URL (e.g., http://localhost:11434/v1/ for Ollama).</summary>
    public string? OpenAiBaseUrl { get; set; }

    /// <summary>Whether to use a custom base URL instead of OpenAI default.</summary>
    public bool UseCustomOpenAiEndpoint { get; set; }

    /// <summary>When true, replace party names and addresses with placeholders before sending to AI.</summary>
    public bool HidePartyNamesFromAi { get; set; }

    /// <summary>
    /// Configured AI providers. <see cref="OpenAiApiKey"/> and <see cref="OpenAiModel"/> mirror
    /// the active profile so older code paths keep working; the profile list is the source of truth.
    /// </summary>
    public List<AiProfile> AiProfiles { get; set; } = new();

    /// <summary>Id of the profile used for drafting; empty means the first profile.</summary>
    public string ActiveAiProfileId { get; set; } = string.Empty;

    /// <summary>The profile in effect for AI calls, or null when none is configured.</summary>
    public AiProfile? ActiveAiProfile =>
        AiProfiles.FirstOrDefault(profile => string.Equals(profile.Id, ActiveAiProfileId, StringComparison.Ordinal))
        ?? AiProfiles.FirstOrDefault();

    /// <summary>
    /// Copies the active profile's key/model into the legacy single-provider fields so every
    /// existing consumer (backup, consent checks, AI service resolution) sees the same values.
    /// </summary>
    internal AppSettings MirrorActiveProfile()
    {
        if (ActiveAiProfile is { } active)
        {
            ActiveAiProfileId = active.Id;
            OpenAiApiKey = active.ApiKey;
            OpenAiModel = string.IsNullOrWhiteSpace(active.Model) ? FallbackModel : active.Model;
            OpenAiBaseUrl = string.IsNullOrWhiteSpace(active.BaseUrl) ? null : active.BaseUrl;
            UseCustomOpenAiEndpoint = active.Preset is AiProviderPreset.Custom;
        }

        return this;
    }

    /// <summary>True when automatic update checks run at startup (default on, notify-only).</summary>
    public bool UpdateChecksEnabled { get; set; } = true;

    /// <summary>True when the beta channel is used (stable is the default).</summary>
    public bool UpdateIncludeBeta { get; set; }

    /// <summary>Version the user chose to skip; that exact version is not offered again.</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last update check (null when never checked).</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// How the user uses DraftEngine. Null means "not chosen" and keeps the full lawyer
    /// experience, so installs created before profiles existed behave exactly as before.
    /// Local preference only: never an account, never sent anywhere.
    /// </summary>
    public Services.Profiles.UserProfile? Profile { get; set; }

    /// <summary>UI language code (en, fr, ar, es) or "system" to follow Windows.</summary>
    public string Language { get; set; } = "system";

    /// <summary>Theme preference: System, Light or Dark.</summary>
    public string Theme { get; set; } = "System";
}
