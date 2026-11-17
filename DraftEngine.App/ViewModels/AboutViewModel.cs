using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Licensing;
using DraftEngine.App.Services.Updates;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// About &amp; Help page: version, log folder, privacy summary, notify-only update check and
/// license status. It performs no network call unless the update check feature is enabled and
/// the user presses the button.
/// </summary>
public partial class AboutViewModel : ObservableObject, INavigationAware
{
    private readonly AppOptions _options;
    private readonly UpdateFlowViewModel? _updates;
    private readonly ILicenseService _licenseService;

    /// <summary>Page title shown in the header.</summary>
    public string PageTitle =>
        DraftEngine.App.Services.Localization.LocalizationSource.Current.Service?.GetString("About.PageTitle")
        ?? "About & Help";

    /// <summary>The running version (single source of truth: the csproj Version).</summary>
    public string VersionText => AppVersion.Display;

    /// <summary>The commit and UTC build date embedded in the running assembly.</summary>
    public string BuildStampText => AppVersion.BuildStamp;

    /// <summary>The folder that holds rolling log files.</summary>
    public string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DraftEngine",
        "logs");

    /// <summary>The local data folder holding the database and settings.</summary>
    public string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DraftEngine");

    /// <summary>Version, OS and runtime only: no contract data, paths or identifiers.</summary>
    public string DiagnosticsText =>
        $"DraftEngine {AppVersion.Display}{Environment.NewLine}" +
        $"Windows {Environment.OSVersion.VersionString}{Environment.NewLine}" +
        $".NET {RuntimeInformation.FrameworkDescription}";

    /// <summary>Copies the content-free diagnostics summary to the clipboard.</summary>
    [RelayCommand]
    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(DiagnosticsText);
            UpdateStatus = "Diagnostics copied (version, Windows and .NET only).";
        }
        catch (Exception)
        {
            UpdateStatus = "The clipboard is unavailable.";
        }
    }

    /// <summary>Signed update flow shared with the banner and Settings (null in unit tests).</summary>
    public UpdateFlowViewModel? Updates => _updates;

    /// <summary>True when offline licensing is enabled in configuration.</summary>
    public bool IsLicensingEnabled => _options.Features.LicensingEnabled;

    /// <summary>Status line for the last update check.</summary>
    [ObservableProperty]
    private string updateStatus = string.Empty;

    /// <summary>The license key being entered (never logged).</summary>
    [ObservableProperty]
    private string licenseKey = string.Empty;

    /// <summary>Friendly license status text.</summary>
    [ObservableProperty]
    private string licenseStatus = "No license has been activated.";

    /// <summary>True when a usable license is active.</summary>
    [ObservableProperty]
    private bool isLicensed;

    /// <summary>Creates the page.</summary>
    public AboutViewModel(
        AppOptions options,
        ILicenseService licenseService,
        UpdateFlowViewModel? updates = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _updates = updates;
    }

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        _ = LoadLicenseAsync();
    }

    /// <summary>Opens the log folder in Explorer; failures are logged, never surfaced as errors.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            Process.Start(new ProcessStartInfo(LogFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open the log folder.");
            UpdateStatus = "The log folder could not be opened.";
        }
    }

    /// <summary>Opens the local data folder in Explorer.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open the data folder.");
            UpdateStatus = "The data folder could not be opened.";
        }
    }

    /// <summary>Verifies and stores a signed license key locally.</summary>
    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        if (!IsLicensingEnabled)
        {
            LicenseStatus = "Licensing is disabled in this build.";
            return;
        }

        try
        {
            var evaluation = await _licenseService.ActivateAsync(LicenseKey);
            ApplyLicense(evaluation);
            if (evaluation.IsUsable)
            {
                LicenseKey = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "License activation failed.");
            LicenseStatus = "The license could not be activated. Check the key and try again.";
        }
    }

    private async Task LoadLicenseAsync()
    {
        if (!IsLicensingEnabled)
        {
            IsLicensed = false;
            LicenseStatus = "Licensing is disabled in this build.";
            return;
        }

        try
        {
            ApplyLicense(await _licenseService.LoadAsync());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the saved license.");
            LicenseStatus = "The saved license could not be read.";
        }
    }

    private void ApplyLicense(DraftEngine.Core.Licensing.LicenseEvaluation evaluation)
    {
        IsLicensed = evaluation.IsUsable;
        LicenseStatus = evaluation.IsUsable
            ? $"{evaluation.Tier} license · expires {evaluation.ExpiresAt?.ToLocalTime():d}"
            : evaluation.Message;
    }
}
