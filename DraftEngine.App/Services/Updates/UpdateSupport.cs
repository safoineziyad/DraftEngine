using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DraftEngine.App.Services.Settings;
using DraftEngine.Updates;
using Microsoft.Data.Sqlite;

namespace DraftEngine.App.Services.Updates;

/// <summary>Persists "last checked" and "skip this version" in the app settings file.</summary>
public sealed class SettingsUpdateStateStore : IUpdateStateStore
{
    private readonly IAppSettingsService _settings;

    /// <summary>Creates the store over the current settings service.</summary>
    public SettingsUpdateStateStore(IAppSettingsService settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public string? SkippedVersion => _settings.Current.SkippedUpdateVersion;

    /// <inheritdoc />
    public DateTimeOffset? LastCheckedUtc => _settings.Current.LastUpdateCheckUtc;

    /// <inheritdoc />
    public void MarkChecked(DateTimeOffset checkedAt)
    {
        var current = _settings.Current;
        current.LastUpdateCheckUtc = checkedAt;
        _ = _settings.SaveAsync(current);
    }

    /// <inheritdoc />
    public void SkipVersion(string version)
    {
        var current = _settings.Current;
        current.SkippedUpdateVersion = version;
        _ = _settings.SaveAsync(current);
    }
}

/// <summary>Confirmation and error dialogs used by the update flow; faked in tests.</summary>
public interface IUpdatePrompt
{
    /// <summary>True only when the user explicitly agrees to close the app and install.</summary>
    bool ConfirmInstall(string message, string title);

    /// <summary>Shows a localized failure message.</summary>
    void ShowError(string message, string title);
}

/// <summary>WPF implementation of the update prompts.</summary>
public sealed class WpfUpdatePrompt : IUpdatePrompt
{
    /// <inheritdoc />
    public bool ConfirmInstall(string message, string title) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <inheritdoc />
    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>
/// Writes a consistent SQLite snapshot before an update replaces application files. Uses
/// <see cref="SqliteConnection.BackupDatabase"/> so a live WAL database is copied safely.
/// </summary>
public sealed class UpdateSnapshotService
{
    private readonly string _connectionString;
    private readonly string _updatesDirectory;

    /// <summary>Creates the service from the configured database connection string.</summary>
    public UpdateSnapshotService(string connectionString, string updatesDirectory)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _updatesDirectory = updatesDirectory ?? throw new ArgumentNullException(nameof(updatesDirectory));
    }

    /// <summary>Copies the live database to the updates folder and returns the snapshot path.</summary>
    public async Task<string> CreatePreUpdateSnapshotAsync(string version, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_updatesDirectory);
        var safeVersion = string.Concat((version ?? "update").Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'));
        if (safeVersion.Length == 0) safeVersion = "update";

        var destinationPath = Path.Combine(_updatesDirectory, $"pre-update-{safeVersion}.db");

        await using var source = new SqliteConnection(_connectionString);
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection($"Data Source={destinationPath}");
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return destinationPath;
    }
}

/// <summary>Verifier used when no public key is configured: every manifest is rejected.</summary>
public sealed class RejectAllManifestVerifier : IManifestVerifier
{
    /// <inheritdoc />
    public bool Verify(UpdateManifest manifest) => false;
}
