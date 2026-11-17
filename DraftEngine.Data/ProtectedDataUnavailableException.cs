using System;

namespace DraftEngine.Data;

/// <summary>
/// Thrown or raised when a DPAPI-protected database column cannot be decrypted because the
/// data was encrypted under a different Windows user profile or the ciphertext is damaged.
/// The raw ciphertext is preserved on disk and in the tracked entity so a later save never
/// overwrites it with an empty value.  The UI should catch this exception (or check that the
/// in-memory property value starts with
/// <see cref="SensitiveDataInterceptor.MarkerPrefix"/>) and display a friendly, localized
/// message such as <c>Audit.ProtectedDataUnavailable</c> instead of surfacing ciphertext.
/// </summary>
public sealed class ProtectedDataUnavailableException : Exception
{
    /// <summary>Gets the name of the entity type that owns the undecryptable column.</summary>
    public string EntityTypeName { get; }

    /// <summary>Gets the name of the column that could not be decrypted.</summary>
    public string PropertyName { get; }

    /// <inheritdoc cref="ProtectedDataUnavailableException"/>
    public ProtectedDataUnavailableException(string entityTypeName, string propertyName, Exception innerException)
        : base(
            $"Column '{propertyName}' on '{entityTypeName}' is protected under a different Windows account and cannot be opened here. Restore from a portable backup.",
            innerException)
    {
        EntityTypeName = entityTypeName;
        PropertyName = propertyName;
    }
}
