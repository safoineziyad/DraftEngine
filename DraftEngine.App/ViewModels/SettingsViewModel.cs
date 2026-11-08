using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Services.Backup;
using DraftEngine.App.Services.Localization;
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
    private readonly IPortableBackupService? _backupService;
    private readonly ILocalizationService _localization;
    private bool _isLoading;
    private bool _isApplyingLanguage;

    /// <summary>The page title shown in the view header (recomputed on language change).</summary>
    public string PageTitle => _localization.GetString("Settings.PageTitle");

    /// <summary>Languages offered in the picker, each shown in its own name.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages => _localization.SupportedLanguages;

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

    /// <summary>Language code selected in the picker (e.g. "en", "fr", "ar", "es").</summary>
    [ObservableProperty]
    private string selectedLanguage = "en";

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
    public SettingsViewModel(
        IAppSettingsService settingsService,
        IAiRedactionService? redactionService = null,
        IPortableBackupService? backupService = null,
        ILocalizationService? localization = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _redactionService = redactionService;
        _backupService = backupService;
        _localization = localization ?? LocalizationSource.Current.Service
            ?? throw new ArgumentNullException(nameof(localization));
        _settingsService.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _isApplyingLanguage = true;
        SelectedLanguage = _localization.CurrentLanguage;
        _isApplyingLanguage = false;
        ApplyFromCurrentSettings();
    }

    /// <summary>Password used for encrypted backup export/restore (never persisted, never logged).</summary>
    [ObservableProperty]
    private string backupPassword = string.Empty;

    /// <summary>True while a backup export/restore is running.</summary>
    [ObservableProperty]
    private bool isBackupBusy;

    /// <summary>Friendly result text for the last backup operation.</summary>
    [ObservableProperty]
    private string? backupStatusMessage;

    /// <summary>True when a backup status message should be shown.</summary>
    public bool HasBackupStatus => !string.IsNullOrEmpty(BackupStatusMessage);

    /// <summary>False while a backup operation is running.</summary>
    public bool CanUseBackup => !IsBackupBusy;

    partial void OnIsBackupBusyChanged(bool value) => OnPropertyChanged(nameof(CanUseBackup));

    /// <summary>Writes a password-encrypted portable backup of all local data.</summary>
    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (_backupService is null)
        {
            return;
        }

        if (BackupPassword.Length < 8)
        {
            BackupStatusMessage = "Choose a backup password of at least 8 characters.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export encrypted backup",
            Filter = "DraftEngine backup (*.debk)|*.debk",
            DefaultExt = ".debk",
            FileName = $"DraftEngine-backup-{DateTime.Now:yyyyMMdd}.debk"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            var summary = await _backupService.ExportEncryptedAsync(stream, BackupPassword, includeApiKey: false);
            BackupStatusMessage = $"Encrypted backup saved: {summary.Contracts} contracts, {summary.Clauses} clauses. Keep the password safe; it cannot be recovered.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Portable backup export failed.");
            BackupStatusMessage = "The backup could not be written. Check the destination and try again.";
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    /// <summary>Restores a portable backup after an explicit confirmation.</summary>
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (_backupService is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Restore encrypted backup",
            Filter = "DraftEngine backup (*.debk)|*.debk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            "Restoring replaces all current contracts, clauses, intake answers, chat history and versions on this computer. A pre-import backup is written first. Continue?",
            "Restore backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        if (BackupPassword.Length < 8)
        {
            BackupStatusMessage = "Enter the password used when the backup was created.";
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var summary = await _backupService.ImportEncryptedAsync(stream, BackupPassword, confirmReplace: true);
            BackupStatusMessage = $"Backup restored: {summary.Contracts} contracts, {summary.Clauses} clauses. Restart the app if a page still shows old data.";
        }
        catch (BackupPasswordException)
        {
            BackupStatusMessage = "Wrong password, or the backup file was modified. Nothing was changed.";
        }
        catch (BackupFormatException ex)
        {
            BackupStatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Portable backup import failed.");
            BackupStatusMessage = "The backup could not be restored. Nothing was changed.";
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    /// <summary>Writes a readable JSON export after an unencrypted-file warning.</summary>
    [RelayCommand]
    private async Task ExportDataAsync(string? format)
    {
        if (_backupService is null)
        {
            return;
        }

        var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
        var warning = isCsv
            ? "This CSV export is NOT encrypted and includes contract titles, parties and intake answers. Store it somewhere protected. Continue?"
            : "This JSON export is NOT encrypted and includes contract text, intake answers and chat history. Store it somewhere protected. Continue?";
        if (MessageBox.Show(warning, "Export my data", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export my data",
            Filter = isCsv ? "CSV (*.csv)|*.csv" : "JSON (*.json)|*.json",
            DefaultExt = isCsv ? ".csv" : ".json",
            FileName = $"DraftEngine-data-{DateTime.Now:yyyyMMdd}.{(isCsv ? "csv" : "json")}"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBackupBusy = true;
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            if (isCsv)
            {
                await _backupService.WriteCsvAsync(stream);
            }
            else
            {
                await _backupService.WriteJsonAsync(stream, includeApiKey: false);
            }

            BackupStatusMessage = "Data exported. The file is unencrypted; protect it or delete it when done.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Plain data export failed.");
            BackupStatusMessage = "The export could not be written.";
        }
        finally
        {
            IsBackupBusy = false;
        }
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

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isApplyingLanguage || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Fire-and-forget is safe: SetLanguageAsync persists and raises LanguageChanged,
        // which refreshes PageTitle and every live {loc:T} binding.
        _ = _localization.SetLanguageAsync(value);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SupportedLanguages));

        if (!string.Equals(SelectedLanguage, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _isApplyingLanguage = true;
            SelectedLanguage = _localization.CurrentLanguage;
            _isApplyingLanguage = false;
        }
    }

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
