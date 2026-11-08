using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DraftEngine.App.Configuration;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Licensing;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DraftEngine.App.Services.Licensing;

/// <summary>
/// Offline license service. Activation keys are verified with the embedded public key and
/// stored encrypted with DPAPI under <c>%APPDATA%\DraftEngine\license.dat</c>. When the
/// feature is off, <see cref="LoadAsync"/> returns <see cref="LicenseStatus.Disabled"/>
/// without touching the file system.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly AppOptions _options;
    private readonly ISecretProtector _protector;
    private readonly ILicenseVerifier _verifier;
    private readonly IDatabaseContextFactory _databaseFactory;
    private readonly string _licensePath;

    private LicenseEvaluation _current =
        new(LicenseStatus.Missing, null, "No license has been activated.");

    /// <inheritdoc />
    public bool IsEnabled => _options.Features.LicensingEnabled;

    /// <inheritdoc />
    public LicenseEvaluation Current => _current;

    /// <summary>Creates the service and resolves the local activation file path.</summary>
    public LicenseService(
        AppOptions options,
        ISecretProtector protector,
        ILicenseVerifier verifier,
        IDatabaseContextFactory databaseFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _databaseFactory = databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory));
        _licensePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DraftEngine",
            "license.dat");
    }

    /// <inheritdoc />
    public async Task<LicenseEvaluation> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return _current = LicenseEvaluation.Disabled;
        }

        if (!File.Exists(_licensePath))
        {
            return _current = new LicenseEvaluation(
                LicenseStatus.Missing,
                null,
                "No license has been activated on this computer.");
        }

        StoredLicense? stored;
        try
        {
            var protectedText = await File.ReadAllTextAsync(_licensePath, cancellationToken);
            var plaintext = _protector.Unprotect(protectedText);
            if (string.IsNullOrWhiteSpace(plaintext))
            {
                return _current = new LicenseEvaluation(
                    LicenseStatus.InvalidPayload,
                    null,
                    "The saved license could not be read. Enter your license key again.");
            }

            stored = JsonSerializer.Deserialize<StoredLicense>(plaintext);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the saved license activation.");
            return _current = new LicenseEvaluation(
                LicenseStatus.InvalidPayload,
                null,
                "The saved license could not be read. Enter your license key again.");
        }

        if (stored is null || string.IsNullOrWhiteSpace(stored.LicenseKey))
        {
            return _current = new LicenseEvaluation(
                LicenseStatus.Missing,
                null,
                "No license has been activated on this computer.");
        }

        var grace = TimeSpan.FromDays(Math.Max(0, _options.Features.LicenseGracePeriodDays));
        var evaluation = _verifier.Verify(
            stored.LicenseKey,
            DateTimeOffset.UtcNow,
            grace,
            stored.LastVerifiedUtc);

        if (evaluation.IsUsable)
        {
            // Keep the rollback guard useful across restarts, then mirror the signed tier
            // into the existing local user row (no new subscription model).
            stored.LastVerifiedUtc = DateTimeOffset.UtcNow;
            TryPersist(stored);
            await SyncSubscriptionAsync(evaluation, cancellationToken);
        }

        return _current = evaluation;
    }

    /// <inheritdoc />
    public async Task<LicenseEvaluation> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return _current = LicenseEvaluation.Disabled;
        }

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return _current = new LicenseEvaluation(
                LicenseStatus.Missing,
                null,
                "Paste the license key you received.");
        }

        var grace = TimeSpan.FromDays(Math.Max(0, _options.Features.LicenseGracePeriodDays));
        var now = DateTimeOffset.UtcNow;
        var evaluation = _verifier.Verify(licenseKey.Trim(), now, grace);
        if (!evaluation.IsUsable)
        {
            return _current = evaluation;
        }

        var stored = new StoredLicense
        {
            LicenseKey = licenseKey.Trim(),
            LastVerifiedUtc = now,
        };

        await PersistAsync(stored, cancellationToken);
        await SyncSubscriptionAsync(evaluation, cancellationToken);
        return _current = evaluation;
    }

    private async Task PersistAsync(StoredLicense stored, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(stored);
        var protectedText = _protector.Protect(json);
        var directory = Path.GetDirectoryName(_licensePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(_licensePath, protectedText, cancellationToken);
    }

    private void TryPersist(StoredLicense stored)
    {
        try
        {
            PersistAsync(stored, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A valid license must not be rejected just because the last-check timestamp
            // could not be updated. Activation itself uses PersistAsync and does surface errors.
            Log.Warning(ex, "Could not update the saved license state.");
        }
    }

    private async Task SyncSubscriptionAsync(
        LicenseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var context = _databaseFactory.CreateContext();
            var user = await context.Set<User>()
                .SingleOrDefaultAsync(candidate => candidate.Id == 1, cancellationToken);
            if (user is null)
            {
                return;
            }

            user.SubscriptionTier = evaluation.Tier;
            user.SubscriptionExpiresAt = evaluation.ExpiresAt?.UtcDateTime;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The signed license remains authoritative; a read-only database must not make
            // an otherwise valid license unusable.
            Log.Warning(ex, "Could not mirror the license into the local user record.");
        }
    }

    /// <summary>On-disk shape; the whole JSON blob is DPAPI-protected before writing.</summary>
    private sealed class StoredLicense
    {
        public string LicenseKey { get; set; } = string.Empty;
        public DateTimeOffset? LastVerifiedUtc { get; set; }
    }
}
