using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
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

    private static readonly ConcurrentDictionary<(Type EntityType, string PropertyName), PropertyInfo?> PropertyCache = new();

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

        foreach (var propertyName in propertyNames)
        {
            try
            {
                var propertyInfo = GetCachedProperty(entityType, propertyName);
                if (propertyInfo?.GetValue(entity) is string value &&
                    value.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                {
                    try
                    {
                        var decrypted = Unprotect(value);
                        propertyInfo.SetValue(entity, decrypted);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex,
                            "Decrypting sensitive column '{PropertyName}' on entity '{EntityType}' failed during materialization; applying defensive value.",
                            propertyName,
                            entityType.Name);
                        try
                        {
                            if (propertyInfo != null && propertyInfo.CanWrite)
                            {
                                var propType = propertyInfo.PropertyType;
                                if (propType == typeof(string))
                                {
                                    propertyInfo.SetValue(entity, string.Empty);
                                }
                                else if (Nullable.GetUnderlyingType(propType) == typeof(string))
                                {
                                    propertyInfo.SetValue(entity, null);
                                }
                                // For non-nullable required strings, setting empty is defensive; avoid leaving DEENC1: visible
                            }
                        }
                        catch
                        {
                            // Avoid breaking materialization
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Continue to other properties
            }
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

                try
                {
                    property.CurrentValue = Protect(value);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex,
                        "Encrypting sensitive column '{PropertyName}' on entity '{EntityType}' failed.",
                        propertyName,
                        entry.Metadata.ClrType.Name);
                    throw new InvalidOperationException(
                        "Failed to protect sensitive data. Ensure the application is running under a valid Windows user profile with DPAPI available.",
                        ex);
                }
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
                    _logger?.LogWarning(exception,
                        "Restoring in-memory value for sensitive column '{PropertyName}' on entity '{EntityType}' after save failed; leaving stored value unchanged.",
                        propertyName,
                        entry.Metadata.ClrType.Name);
                }
            }
        }
    }

    /// <summary>Encrypts a plaintext value with DPAPI (current user) and adds the marker.</summary>
    [SupportedOSPlatform("windows")]
    private string Protect(string plaintext)
    {
        try
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
            return MarkerPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DPAPI Protect failed.");
            throw;
        }
    }

    /// <summary>Decrypts a marker-prefixed value with DPAPI (current user).</summary>
    [SupportedOSPlatform("windows")]
    private string Unprotect(string ciphertext)
    {
        try
        {
            var payload = Convert.FromBase64String(ciphertext.Substring(MarkerPrefix.Length));
            var bytes = ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DPAPI Unprotect failed.");
            throw;
        }
    }

    private static PropertyInfo? GetCachedProperty(Type entityType, string propertyName)
    {
        var key = (entityType, propertyName);
        if (PropertyCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var prop = entityType.GetProperty(propertyName);
        PropertyCache[key] = prop;
        return prop;
    }
}
