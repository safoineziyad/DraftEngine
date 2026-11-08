using DraftEngine.App.Services.Settings;

namespace DraftEngine.App.Services.Backup;

/// <summary>
/// The versioned payload inside an encrypted portable backup. Plain entity graphs are not
/// serialized directly (they have navigation properties and cycles); these records contain
/// every persisted scalar field, with ids preserved so relationships survive a restore.
/// </summary>
public sealed class BackupPayload
{
    public const int CurrentVersion = 1;

    public int FormatVersion { get; set; } = CurrentVersion;
    public string Product { get; set; } = "DraftEngine";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BackupContract> Contracts { get; set; } = [];
    public List<BackupContractField> ContractFields { get; set; } = [];
    public List<BackupContractClause> ContractClauses { get; set; } = [];
    public List<BackupClause> Clauses { get; set; } = [];
    public List<BackupChatMessage> ChatMessages { get; set; } = [];
    public List<BackupContractVersion> ContractVersions { get; set; } = [];
    public List<BackupGeneratedDocument> GeneratedDocuments { get; set; } = [];
    public List<BackupWizardDraft> WizardDrafts { get; set; } = [];

    /// <summary>Non-secret settings; the API key is only present when the user opted in.</summary>
    public BackupSettings? Settings { get; set; }
}

public sealed class BackupContract
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public int Status { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string PartiesJson { get; set; } = string.Empty;
    public DateTime? EffectiveDate { get; set; }
    public string GoverningLaw { get; set; } = string.Empty;
    public int TermMonths { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastGeneratedAt { get; set; }
    public string FinalFilePath { get; set; } = string.Empty;
    public string AssembledText { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public sealed class BackupContractField
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class BackupContractClause
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int ClauseId { get; set; }
    public bool Selected { get; set; }
    public string CustomBodyOverride { get; set; } = string.Empty;
}

public sealed class BackupClause
{
    public int Id { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public bool IsDefaultSelected { get; set; }
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public bool IsUserDefined { get; set; }
    public int? OwnerUserId { get; set; }
    public bool IsArchived { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class BackupChatMessage
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class BackupContractVersion
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int VersionNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class BackupGeneratedDocument
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int FileType { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupWizardDraft
{
    public string DraftKey { get; set; } = string.Empty;
    public string EncryptedPayload { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Settings copied into a backup; API key only when explicitly included.</summary>
public sealed class BackupSettings
{
    public string? OpenAiApiKey { get; set; }
    public string OpenAiModel { get; set; } = AppSettings.FallbackModel;
    public string DefaultGoverningLaw { get; set; } = "California";
    public int DefaultTermMonths { get; set; } = 12;
    public bool AiDataConsentAccepted { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string? OpenAiBaseUrl { get; set; }
    public bool UseCustomOpenAiEndpoint { get; set; }
    public bool HidePartyNamesFromAi { get; set; }
}

/// <summary>Counts of what an export wrote or an import restored.</summary>
public sealed record BackupSummary(
    int Contracts,
    int Clauses,
    int ContractFields,
    int ContractClauses,
    int ChatMessages,
    int Versions,
    int GeneratedDocuments,
    int WizardDrafts,
    bool IncludedSettings);

/// <summary>Thrown when a backup cannot be decrypted (wrong password or tampered file).</summary>
public sealed class BackupPasswordException : Exception
{
    public BackupPasswordException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Thrown when a backup is structurally invalid or from an unsupported version.</summary>
public sealed class BackupFormatException : Exception
{
    public BackupFormatException(string message) : base(message)
    {
    }
}
