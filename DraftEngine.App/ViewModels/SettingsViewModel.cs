using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for the settings page: OpenAI API key (masked, encrypted at rest), model
/// selection and contract defaults, with connection testing and explicit saving.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settingsService;
    private readonly IAiRedactionService? _redactionService;
    private bool _isLoading;

    /// <summary>The page title shown in the view header.</summary>
    public string PageTitle => "Settings";

    /// <summary>OpenAI chat model choices.</summary>
    public IReadOnlyList<string> ModelOptions { get; } = new[] { "gpt-4o-mini", "gpt-4o" };

    /// <summary>Default governing law choices for new contracts.</summary>
    public IReadOnlyList<string> GoverningLawOptions { get; } = new[] { "California", "New York", "Delaware", "Texas" };

    /// <summary>The OpenAI API key being edited (plaintext in memory only; encrypted at rest).</summary>
    [ObservableProperty]
    private string openAiApiKey = string.Empty;

    /// <summary>True when the API key is shown as plain text instead of masked.</summary>
    [ObservableProperty]
    private bool isKeyVisible;

    /// <summary>The selected OpenAI chat model.</summary>
    [ObservableProperty]
    private string selectedModel = AppSettings.FallbackModel;

    /// <summary>The default governing law for new contracts.</summary>
    [ObservableProperty]
    private string selectedGoverningLaw = "California";

    /// <summary>The default contract term in months.</summary>
    [ObservableProperty]
    private int defaultTermMonths = 12;

    /// <summary>True while a connection test is running.</summary>
    [ObservableProperty]
    private bool isTestingConnection;

    /// <summary>True when the last connection test succeeded (controls the status color).</summary>
    [ObservableProperty]
    private bool isTestSuccessful;

    /// <summary>Friendly status text for the last connection test; null when there is none.</summary>
    [ObservableProperty]
    private string? testStatusMessage;

    /// <summary>Friendly status text after saving; null when there is none.</summary>
    [ObservableProperty]
    private string? saveStatusMessage;

    /// <summary>True when there are unsaved changes.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>True when a custom OpenAI-compatible endpoint (Azure, gateway, Ollama…) is used.</summary>
    [ObservableProperty]
    private bool useCustomOpenAiEndpoint;

    /// <summary>The custom OpenAI-compatible base URL, e.g. http://localhost:11434/v1/.</summary>
    [ObservableProperty]
    private string openAiBaseUrl = string.Empty;

    /// <summary>True when real party names and addresses are replaced before AI requests.</summary>
    [ObservableProperty]
    private bool hidePartyNamesFromAi;

    /// <summary>True when the user has granted AI data-sharing consent.</summary>
    [ObservableProperty]
    private bool aiConsentGranted;

    /// <summary>True when the term-months validation error should be shown.</summary>
    public bool HasTermMonthsError => !string.IsNullOrEmpty(TermMonthsError);

    /// <summary>True when settings can be saved (no blocking validation errors).</summary>
    public bool CanSave => string.IsNullOrEmpty(TermMonthsError);

    /// <summary>API key status text for UI display.</summary>
    [ObservableProperty]
    private string apiKeyStatusText = "No API key configured";

    /// <summary>True if an API key is configured.</summary>
    [ObservableProperty]
    private bool hasApiKey;

    /// <summary>Inline validation message for default term months.</summary>
    [ObservableProperty]
    private string? termMonthsError;

    /// <summary>The Show/Hide toggle label for the API key field.</summary>
    public string ShowHideKeyText => IsKeyVisible ? "Hide" : "Show";

    /// <summary>True when a connection test status message should be shown.</summary>
    public bool HasTestStatus => TestStatusMessage is not null;

    /// <summary>True when a save status message should be shown.</summary>
    public bool HasSaveStatus => SaveStatusMessage is not null;

    /// <summary>Creates the settings view model and loads the saved settings.</summary>
    public SettingsViewModel(IAppSettingsService settingsService, IAiRedactionService? redactionService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _redactionService = redactionService;
        _settingsService.SettingsChanged += OnSettingsChanged;
        ApplyFromCurrentSettings();
    }

    /// <summary>Masks or reveals the API key.</summary>
    [RelayCommand]
    private void ToggleKeyVisibility() => IsKeyVisible = !IsKeyVisible;

    /// <summary>Withdraws AI data-sharing consent; AI editing is blocked until granted again.</summary>
    [RelayCommand]
    private async Task RevokeAiConsentAsync()
    {
        try
        {
            var current = _settingsService.Current;
            current.AiDataConsentAccepted = false;
            await _settingsService.SaveAsync(current);
            AiConsentGranted = false;
            SaveStatusMessage = "AI consent revoked. AI editing is now blocked.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to revoke AI consent.");
            SaveStatusMessage = "We couldn't update the AI consent setting. Please try again.";
        }
    }

    /// <summary>
    /// Tests the API key currently typed in the field (saved or not) against OpenAI and
    /// shows a friendly result. Raw exceptions are never surfaced.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        var key = OpenAiApiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            IsTestSuccessful = false;
            TestStatusMessage = "Please paste your OpenAI API key first.";
            return;
        }

        IsTestingConnection = true;
        TestStatusMessage = "Connecting…";
        try
        {
            var aiService = _settingsService.CreateAiService(key, SelectedModel);
            if (aiService is null)
            {
                IsTestSuccessful = false;
                TestStatusMessage = "Please paste your OpenAI API key first.";
                return;
            }

            var result = await aiService.TestConnectionDetailedAsync();
            IsTestSuccessful = result.Success;
            TestStatusMessage = result.Success
                ? "Connected to OpenAI successfully."
                : string.IsNullOrWhiteSpace(result.Message)
                    ? "Could not connect to OpenAI. Please try again."
                    : result.Message;
        }
        catch (Exception ex)
        {
            // Log the detail (never the key); show only a friendly sentence.
            Log.Error(ex, "OpenAI connection test failed.");
            IsTestSuccessful = false;
            TestStatusMessage = "Something went wrong while testing the connection. Please try again.";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Persists the settings (the API key is encrypted before writing).</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var termMonths = DefaultTermMonths;
        if (termMonths < 1 || termMonths > 120)
        {
            SaveStatusMessage = "Term must be between 1 and 120 months.";
            return;
        }

        try
        {
            SetIsLoadingState(true);
            var settings = new AppSettings
            {
                OpenAiApiKey = OpenAiApiKey?.Trim() ?? string.Empty,
                OpenAiModel = SelectedModel,
                DefaultGoverningLaw = SelectedGoverningLaw,
                DefaultTermMonths = termMonths,
                // Preserve the AI data-sharing acknowledgement — editing settings must not
                // silently withdraw the consent given during a chat edit.
                AiDataConsentAccepted = AiConsentGranted,
                OnboardingCompleted = _settingsService.Current.OnboardingCompleted,
                OpenAiBaseUrl = string.IsNullOrWhiteSpace(OpenAiBaseUrl) ? null : OpenAiBaseUrl.Trim(),
                UseCustomOpenAiEndpoint = UseCustomOpenAiEndpoint,
                HidePartyNamesFromAi = HidePartyNamesFromAi,
            };
            await _settingsService.SaveAsync(settings);
            if (_redactionService is not null)
            {
                _redactionService.IsEnabled = HidePartyNamesFromAi;
            }
            IsDirty = false;
            SaveStatusMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings.");
            SaveStatusMessage = "We couldn't save your settings. Please try again.";
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }

    partial void OnOpenAiApiKeyChanged(string value)
    {
        MarkDirty();
        var hasKey = !string.IsNullOrWhiteSpace(value);
        HasApiKey = hasKey;
        ApiKeyStatusText = hasKey ? "API key configured" : "No API key configured";
    }

    partial void OnSelectedModelChanged(string value) => MarkDirty();

    partial void OnUseCustomOpenAiEndpointChanged(bool value) => MarkDirty();

    partial void OnOpenAiBaseUrlChanged(string value) => MarkDirty();

    partial void OnHidePartyNamesFromAiChanged(bool value) => MarkDirty();

    partial void OnSelectedGoverningLawChanged(string value) => MarkDirty();

    partial void OnDefaultTermMonthsChanged(int value)
    {
        ValidateTermMonths(value);
        MarkDirty();
    }

    partial void OnIsKeyVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowHideKeyText));

    partial void OnTermMonthsErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasTermMonthsError));
    }

    partial void OnTestStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasTestStatus));

    partial void OnSaveStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasSaveStatus));

    private void MarkDirty()
    {
        if (!_isLoading)
            IsDirty = true;
    }

    private void SetIsLoadingState(bool isLoading)
    {
        _isLoading = isLoading;
    }

    private void OnSettingsChanged() => ApplyFromCurrentSettings();

    private void ApplyFromCurrentSettings()
    {
        var saved = _settingsService.Current;

        SetIsLoadingState(true);
        try
        {
            OpenAiApiKey = saved.OpenAiApiKey;
            UpdateApiKeyStatus(saved.OpenAiApiKey);
            SelectedModel = ModelOptions.Contains(saved.OpenAiModel) ? saved.OpenAiModel : AppSettings.FallbackModel;
            SelectedGoverningLaw = saved.DefaultGoverningLaw;
            DefaultTermMonths = saved.DefaultTermMonths;
            ValidateTermMonths(saved.DefaultTermMonths);
            UseCustomOpenAiEndpoint = saved.UseCustomOpenAiEndpoint;
            OpenAiBaseUrl = saved.OpenAiBaseUrl ?? string.Empty;
            HidePartyNamesFromAi = saved.HidePartyNamesFromAi;
            AiConsentGranted = saved.AiDataConsentAccepted;
            IsDirty = false;
        }
        finally
        {
            SetIsLoadingState(false);
        }
    }

    private void UpdateApiKeyStatus(string? key)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            HasApiKey = false;
            ApiKeyStatusText = "No API key configured";
            return;
        }

        HasApiKey = true;
        if (trimmed.Length > 8)
        {
            ApiKeyStatusText = $"API key configured ({trimmed.Substring(0, 3)}...{trimmed.Substring(trimmed.Length - 4)})";
        }
        else
        {
            ApiKeyStatusText = "API key configured";
        }
    }

    private void ValidateTermMonths(int value)
    {
        if (value < 1 || value > 120)
        {
            TermMonthsError = "Term must be between 1 and 120 months.";
        }
        else
        {
            TermMonthsError = null;
        }
    }
}
