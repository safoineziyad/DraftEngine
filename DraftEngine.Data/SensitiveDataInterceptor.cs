using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DraftEngine.Data;

/// <summary>
/// Encrypts the sensitive text columns of the contract database at rest (audit Finding #3)
/// with Windows DPAPI (current user), so the SQLite file cannot be read by anyone who copies
/// it: the assembled document, the intake answers, the chat history and every version
/// snapshot are ciphertext on disk and plaintext in memory.
/// </summary>
/// <remarks>
/// <para>
/// Design: a <see cref="SaveChangesInterceptor"/> encrypts the designated properties of
/// added/modified entities just before the save, and a <see cref="SavedChangesInterceptor"/>
/// restores the plaintext in memory right after it — the application never observes the
/// ciphertext. An <see cref="IMaterializationInterceptor"/> decrypts the values as entities
/// are loaded.
/// </para>
/// <para>
/// Values written by v0.1 (plain text, no marker) are left untouched on load, so existing
/// databases keep working; they are re-encrypted with the next save that touches the row.
/// The entropy parameter only hardens against accidental mixing with other DPAPI blobs;
/// DPAPI itself provides the security boundary (the Windows user profile).
/// </para>
/// </remarks>
public sealed class SensitiveDataInterceptor : SaveChangesInterceptor, IMaterializationInterceptor
{
    /// <summary>Marker prefixed to every encrypted value so plaintext is never decrypted twice.</summary>
    internal const string MarkerPrefix = "DEENC1:";

    /// <summary>DPAPI entropy, app-specific so blobs never mix with other applications'.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DraftEngine.v1.SensitiveData");

    /// <summary>Entity types and the string properties encrypted at rest.</summary>
    private static readonly IReadOnlyDictionary<Type, string[]> EncryptedProperties =
        new Dictionary<Type, string[]>
        {
            [typeof(Contract)] = new[]
            {
                nameof(Contract.ClientName),
                nameof(Contract.ClientEmail),
                nameof(Contract.PartiesJson),
                nameof(Contract.AssembledText),
            },
            [typeof(ContractField)] = new[] { nameof(ContractField.Value) },
            [typeof(ChatMessage)] = new[] { nameof(ChatMessage.Content) },
            [typeof(ContractVersion)] = new[] { nameof(ContractVersion.Text) },
        };

    private readonly ILogger<SensitiveDataInterceptor>? _logger;

    /// <summary>Creates the interceptor; the logger is optional (tests construct it without one).</summary>
    public SensitiveDataInterceptor(ILogger<SensitiveDataInterceptor>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            EncryptChangedValues(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            EncryptChangedValues(eventData.Context);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(
        SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null)
        {
            DecryptChangedValues(eventData.Context);
        }

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            DecryptChangedValues(eventData.Context);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        var entityType = entity.GetType();
        if (!EncryptedProperties.TryGetValue(entityType, out var propertyNames))
        {
            return entity;
        }

        try
        {
            foreach (var propertyName in propertyNames)
            {
                var propertyInfo = entityType.GetProperty(propertyName);
                if (propertyInfo?.GetValue(entity) is string value &&
                    value.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                {
                    propertyInfo.SetValue(entity, Unprotect(value));
                }
            }
        }
        catch (Exception exception)
        {
            // A value that cannot be decrypted (for example after a Windows profile change)
            // must not prevent the row from loading; the stored ciphertext is returned as-is
            // and the problem is logged for diagnosis.
            _logger?.LogWarning(exception, "Decrypting a sensitive column failed; the stored value is returned unchanged.");
        }

        return entity;
    }

    /// <summary>Encrypts the designated properties of added and modified entities in place.</summary>
    private void EncryptChangedValues(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (!EncryptedProperties.TryGetValue(entry.Metadata.ClrType, out var propertyNames))
            {
                continue;
            }

            foreach (var propertyName in propertyNames)
            {
                var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
                if (property?.CurrentValue is not string value || value.Length == 0)
                {
                    continue;
                }

                if (value.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                {
                    continue; // Already ciphertext (for example a failed earlier save).
                }

                property.CurrentValue = Protect(value);
            }
        }
    }

    /// <summary>
    /// Restores the plaintext in memory right after a successful save, so the entities the
    /// application keeps using (and returns to view models) never expose ciphertext.
    /// Note: EF has already reset the entry states to Unchanged at this point, so every
    /// tracked entry is inspected and the marker alone decides what to decrypt.
    /// </summary>
    private void DecryptChangedValues(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!EncryptedProperties.TryGetValue(entry.Metadata.ClrType, out var propertyNames))
            {
                continue;
            }

            foreach (var propertyName in propertyNames)
            {
                var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
                if (property?.CurrentValue is not string value || !value.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    property.CurrentValue = Unprotect(value);
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(exception, "Restoring an in-memory value after save failed.");
                }
            }
        }
    }

    /// <summary>Encrypts a plaintext value with DPAPI (current user) and adds the marker.</summary>
    private static string Protect(string plaintext)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return MarkerPrefix + Convert.ToBase64String(protectedBytes);
    }

    /// <summary>Decrypts a marker-prefixed value with DPAPI (current user).</summary>
    private static string Unprotect(string ciphertext)
    {
        var payload = Convert.FromBase64String(ciphertext.Substring(MarkerPrefix.Length));
        var bytes = ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
