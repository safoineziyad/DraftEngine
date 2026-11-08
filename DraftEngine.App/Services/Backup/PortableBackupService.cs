using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using DraftEngine.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DraftEngine.App.Services.Backup;

/// <summary>Portable encrypted backup/restore plus plain JSON/CSV data export.</summary>
public interface IPortableBackupService
{
    /// <summary>Writes a password-encrypted, versioned backup to <paramref name="output"/>.</summary>
    Task<BackupSummary> ExportEncryptedAsync(Stream output, string password, bool includeApiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and restores a backup. Requires <paramref name="confirmReplace"/>; writes a
    /// pre-import backup first. Returns what was restored.
    /// </summary>
    Task<BackupSummary> ImportEncryptedAsync(
        Stream input,
        string password,
        bool confirmReplace,
        string? preImportBackupPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a readable JSON export. Unencrypted by design; the caller warns the user.</summary>
    Task WriteJsonAsync(Stream output, bool includeApiKey, CancellationToken cancellationToken = default);

    /// <summary>Writes a one-row-per-contract CSV export (UTF-8 with BOM for Excel).</summary>
    Task WriteCsvAsync(Stream output, CancellationToken cancellationToken = default);
}

/// <summary>
/// AES-256-GCM backup container.
/// Layout: magic "DEBK1" | salt(16) | nonce(12) | tag(16) | ciphertext.
/// Key: PBKDF2-HMAC-SHA256, 600,000 iterations, 32 bytes. No secret is ever logged.
/// </summary>
public sealed class PortableBackupService : IPortableBackupService
{
    private static readonly byte[] Magic = "DEBK1"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private const int MinimumPasswordLength = 8;
    private static readonly byte[] AssociatedData = "DraftEngine.Backup.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDatabaseContextFactory _contextFactory;
    private readonly IAppSettingsService? _settingsService;
    private readonly string _defaultBackupDirectory;

    public PortableBackupService(
        IDatabaseContextFactory contextFactory,
        IAppSettingsService? settingsService = null,
        string? backupDirectory = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _settingsService = settingsService;
        _defaultBackupDirectory = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DraftEngine",
            "backups");
    }

    public async Task<BackupSummary> ExportEncryptedAsync(
        Stream output,
        string password,
        bool includeApiKey,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var payload = await CollectAsync(includeApiKey, cancellationToken);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var container = Encrypt(plaintext, password);
        await output.WriteAsync(container, cancellationToken);

        Log.Information(
            "Portable backup exported ({Contracts} contracts, {Clauses} clauses).",
            payload.Contracts.Count,
            payload.Clauses.Count);
        return ToSummary(payload);
    }

    public async Task<BackupSummary> ImportEncryptedAsync(
        Stream input,
        string password,
        bool confirmReplace,
        string? preImportBackupPath = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (!confirmReplace)
        {
            throw new InvalidOperationException("Import requires explicit confirmation before replacing local data.");
        }

        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var payload = DecryptPayload(buffer.ToArray(), password);
        ValidatePayload(payload);

        // Always keep a restore point of what is about to be replaced.
        var backupPath = preImportBackupPath ?? Path.Combine(
            _defaultBackupDirectory,
            $"pre-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.debk");
        var directory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var preImport = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var current = await CollectAsync(includeApiKey: true, cancellationToken);
            await preImport.WriteAsync(Encrypt(JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions), password), cancellationToken);
        }

        await ReplaceAllAsync(payload, cancellationToken);

        if (payload.Settings is not null && _settingsService is not null)
        {
            await RestoreSettingsAsync(payload.Settings);
        }

        Log.Information(
            "Portable backup imported ({Contracts} contracts, {Clauses} clauses).",
            payload.Contracts.Count,
            payload.Clauses.Count);
        return ToSummary(payload);
    }

    public async Task WriteJsonAsync(Stream output, bool includeApiKey, CancellationToken cancellationToken = default)
    {
        var payload = await CollectAsync(includeApiKey, cancellationToken);
        await JsonSerializer.SerializeAsync(output, payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }, cancellationToken);
    }

    public async Task WriteCsvAsync(Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync(
            "Title,ContractType,Status,ClientName,GoverningLaw,TermMonths,EffectiveDate,CreatedAt,UpdatedAt,Archived,IntakeAnswers");

        await using var context = _contextFactory.CreateContext();
        var contracts = await context.Set<Contract>().AsNoTracking()
            .OrderBy(contract => contract.Id)
            .ToListAsync(cancellationToken);
        var fields = await context.Set<ContractField>().AsNoTracking()
            .ToListAsync(cancellationToken);
        var byContract = fields
            .GroupBy(field => field.ContractId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(field => field.Key, field => field.Value));

        foreach (var contract in contracts)
        {
            var intake = byContract.TryGetValue(contract.Id, out var values)
                ? JsonSerializer.Serialize(values)
                : string.Empty;
            var line = string.Join(",", new[]
            {
                Csv(contract.Title),
                Csv(contract.ContractType),
                Csv(contract.Status.ToString()),
                Csv(contract.ClientName),
                Csv(contract.GoverningLaw),
                contract.TermMonths.ToString(CultureInfo.InvariantCulture),
                Csv(contract.EffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(contract.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
                Csv(contract.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)),
                contract.IsArchived ? "true" : "false",
                Csv(intake)
            });
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private async Task<BackupPayload> CollectAsync(bool includeApiKey, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateContext();

        // Encrypted columns are decrypted by the materialization interceptor, so these
        // entities must be loaded as entities (not projected) before being mapped to DTOs.
        var contracts = await context.Set<Contract>().AsNoTracking().ToListAsync(cancellationToken);
        var contractFields = await context.Set<ContractField>().AsNoTracking().ToListAsync(cancellationToken);
        var chatMessages = await context.Set<ChatMessage>().AsNoTracking().ToListAsync(cancellationToken);
        var contractVersions = await context.Set<ContractVersion>().AsNoTracking().ToListAsync(cancellationToken);

        var payload = new BackupPayload
        {
            Contracts = contracts.Select(contract => new BackupContract
            {
                Id = contract.Id,
                UserId = contract.UserId,
                Title = contract.Title,
                ContractType = contract.ContractType,
                Status = (int)contract.Status,
                ClientName = contract.ClientName,
                ClientEmail = contract.ClientEmail,
                PartiesJson = contract.PartiesJson,
                EffectiveDate = contract.EffectiveDate,
                GoverningLaw = contract.GoverningLaw,
                TermMonths = contract.TermMonths,
                CreatedAt = contract.CreatedAt,
                UpdatedAt = contract.UpdatedAt,
                LastGeneratedAt = contract.LastGeneratedAt,
                FinalFilePath = contract.FinalFilePath,
                AssembledText = contract.AssembledText,
                IsDeleted = contract.IsDeleted,
                DeletedAt = contract.DeletedAt,
                IsArchived = contract.IsArchived,
                ArchivedAt = contract.ArchivedAt
            }).ToList(),
            ContractFields = contractFields.Select(field => new BackupContractField
            {
                Id = field.Id,
                ContractId = field.ContractId,
                Key = field.Key,
                Value = field.Value
            }).ToList(),
            ContractClauses = await context.Set<ContractClause>().AsNoTracking().Select(selection => new BackupContractClause
            {
                Id = selection.Id,
                ContractId = selection.ContractId,
                ClauseId = selection.ClauseId,
                Selected = selection.Selected,
                CustomBodyOverride = selection.CustomBodyOverride
            }).ToListAsync(cancellationToken),
            Clauses = await context.Set<Clause>().AsNoTracking().Select(clause => new BackupClause
            {
                Id = clause.Id,
                ContractType = clause.ContractType,
                Category = clause.Category,
                Title = clause.Title,
                BodyTemplate = clause.BodyTemplate,
                IsOptional = clause.IsOptional,
                IsDefaultSelected = clause.IsDefaultSelected,
                SortOrder = clause.SortOrder,
                IsSystem = clause.IsSystem,
                IsUserDefined = clause.IsUserDefined,
                OwnerUserId = clause.OwnerUserId,
                IsArchived = clause.IsArchived,
                Source = clause.Source
            }).ToListAsync(cancellationToken),
            ChatMessages = chatMessages.Select(message => new BackupChatMessage
            {
                Id = message.Id,
                ContractId = message.ContractId,
                Role = (int)message.Role,
                Content = message.Content,
                CreatedAt = message.CreatedAt
            }).ToList(),
            ContractVersions = contractVersions.Select(version => new BackupContractVersion
            {
                Id = version.Id,
                ContractId = version.ContractId,
                VersionNumber = version.VersionNumber,
                Text = version.Text,
                Reason = version.Reason,
                CreatedAt = version.CreatedAt
            }).ToList(),
            GeneratedDocuments = await context.Set<GeneratedDocument>().AsNoTracking().Select(document => new BackupGeneratedDocument
            {
                Id = document.Id,
                ContractId = document.ContractId,
                FilePath = document.FilePath,
                FileType = (int)document.FileType,
                GeneratedAt = document.GeneratedAt,
                Version = document.Version
            }).ToListAsync(cancellationToken),
            WizardDrafts = await context.Set<WizardDraft>().AsNoTracking().Select(draft => new BackupWizardDraft
            {
                DraftKey = draft.DraftKey,
                EncryptedPayload = draft.EncryptedPayload,
                UpdatedAt = draft.UpdatedAt
            }).ToListAsync(cancellationToken)
        };

        if (_settingsService is not null)
        {
            var current = _settingsService.Current;
            payload.Settings = new BackupSettings
            {
                OpenAiApiKey = includeApiKey && !string.IsNullOrEmpty(current.OpenAiApiKey) ? current.OpenAiApiKey : null,
                OpenAiModel = current.OpenAiModel,
                DefaultGoverningLaw = current.DefaultGoverningLaw,
                DefaultTermMonths = current.DefaultTermMonths,
                AiDataConsentAccepted = current.AiDataConsentAccepted,
                OnboardingCompleted = current.OnboardingCompleted,
                OpenAiBaseUrl = current.OpenAiBaseUrl,
                UseCustomOpenAiEndpoint = current.UseCustomOpenAiEndpoint,
                HidePartyNamesFromAi = current.HidePartyNamesFromAi
            };
        }

        return payload;
    }

    private async Task ReplaceAllAsync(BackupPayload payload, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Children first, then parents, so foreign keys are never violated.
        await context.Set<ContractClause>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<ContractField>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<ChatMessage>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<ContractVersion>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<GeneratedDocument>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<Contract>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<Clause>().ExecuteDeleteAsync(cancellationToken);
        await context.Set<WizardDraft>().ExecuteDeleteAsync(cancellationToken);

        context.AddRange(payload.Clauses.Select(clause => new Clause
        {
            Id = clause.Id,
            ContractType = clause.ContractType,
            Category = clause.Category,
            Title = clause.Title,
            BodyTemplate = clause.BodyTemplate,
            IsOptional = clause.IsOptional,
            IsDefaultSelected = clause.IsDefaultSelected,
            SortOrder = clause.SortOrder,
            IsSystem = clause.IsSystem,
            IsUserDefined = clause.IsUserDefined,
            OwnerUserId = clause.OwnerUserId,
            IsArchived = clause.IsArchived,
            Source = string.IsNullOrWhiteSpace(clause.Source) ? (clause.IsSystem ? "System" : "User") : clause.Source
        }));

        // Clauses must exist before the selections that reference them.
        await context.SaveChangesAsync(cancellationToken);

        context.AddRange(payload.Contracts.Select(contract => new Contract
        {
            Id = contract.Id,
            UserId = contract.UserId == 0 ? 1 : contract.UserId,
            Title = contract.Title,
            ContractType = contract.ContractType,
            Status = Enum.IsDefined(typeof(ContractStatus), contract.Status)
                ? (ContractStatus)contract.Status
                : ContractStatus.Draft,
            ClientName = contract.ClientName,
            ClientEmail = contract.ClientEmail,
            PartiesJson = contract.PartiesJson,
            EffectiveDate = contract.EffectiveDate,
            GoverningLaw = contract.GoverningLaw,
            TermMonths = contract.TermMonths,
            CreatedAt = contract.CreatedAt,
            UpdatedAt = contract.UpdatedAt,
            LastGeneratedAt = contract.LastGeneratedAt,
            FinalFilePath = contract.FinalFilePath,
            AssembledText = contract.AssembledText,
            IsDeleted = contract.IsDeleted,
            DeletedAt = contract.DeletedAt,
            IsArchived = contract.IsArchived,
            ArchivedAt = contract.ArchivedAt
        }));

        await context.SaveChangesAsync(cancellationToken);

        context.AddRange(payload.ContractFields.Select(field => new ContractField
        {
            Id = field.Id,
            ContractId = field.ContractId,
            Key = field.Key,
            Value = field.Value
        }));

        context.AddRange(payload.ContractClauses.Select(selection => new ContractClause
        {
            Id = selection.Id,
            ContractId = selection.ContractId,
            ClauseId = selection.ClauseId,
            Selected = selection.Selected,
            CustomBodyOverride = selection.CustomBodyOverride
        }));

        context.AddRange(payload.ChatMessages.Select(message => new ChatMessage
        {
            Id = message.Id,
            ContractId = message.ContractId,
            Role = Enum.IsDefined(typeof(MessageRole), message.Role)
                ? (MessageRole)message.Role
                : MessageRole.User,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        }));

        context.AddRange(payload.ContractVersions.Select(version => new ContractVersion
        {
            Id = version.Id,
            ContractId = version.ContractId,
            VersionNumber = version.VersionNumber,
            Text = version.Text,
            Reason = version.Reason,
            CreatedAt = version.CreatedAt
        }));

        context.AddRange(payload.GeneratedDocuments.Select(document => new GeneratedDocument
        {
            Id = document.Id,
            ContractId = document.ContractId,
            FilePath = document.FilePath,
            FileType = Enum.IsDefined(typeof(FileType), document.FileType)
                ? (FileType)document.FileType
                : FileType.Docx,
            GeneratedAt = document.GeneratedAt,
            Version = document.Version
        }));

        context.AddRange(payload.WizardDrafts.Select(draft => new WizardDraft
        {
            DraftKey = draft.DraftKey,
            EncryptedPayload = draft.EncryptedPayload,
            UpdatedAt = draft.UpdatedAt
        }));

        await context.SaveChangesAsync(cancellationToken);
        await ResetSequencesAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ResetSequencesAsync(DbContext context, CancellationToken cancellationToken)
    {
        // Explicit ids were restored, so AUTOINCREMENT counters must catch up or the next
        // insert would collide with a restored row.
        foreach (var table in new[]
                 {
                     "Contracts", "Clauses", "ContractFields", "ContractClauses",
                     "ChatMessages", "ContractVersions", "GeneratedDocuments"
                 })
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE sqlite_sequence SET seq = (SELECT COALESCE(MAX(Id), 0) FROM {table}) WHERE name = '{table}'",
                    cancellationToken);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // sqlite_sequence is created only after the first AUTOINCREMENT insert; a
                // brand-new database legitimately has no rows to update.
            }
        }
    }

    private async Task RestoreSettingsAsync(BackupSettings backup)
    {
        var current = _settingsService!.Current;
        var restored = new AppSettings
        {
            OpenAiApiKey = backup.OpenAiApiKey ?? current.OpenAiApiKey,
            OpenAiModel = backup.OpenAiModel,
            DefaultGoverningLaw = backup.DefaultGoverningLaw,
            DefaultTermMonths = backup.DefaultTermMonths,
            AiDataConsentAccepted = backup.AiDataConsentAccepted,
            OnboardingCompleted = backup.OnboardingCompleted,
            OpenAiBaseUrl = backup.OpenAiBaseUrl,
            UseCustomOpenAiEndpoint = backup.UseCustomOpenAiEndpoint,
            HidePartyNamesFromAi = backup.HidePartyNamesFromAi
        };
        await _settingsService.SaveAsync(restored);
    }

    private static BackupPayload DecryptPayload(byte[] container, string password)
    {
        if (container.Length < Magic.Length + SaltSize + NonceSize + TagSize + 1)
        {
            throw new BackupFormatException("This file is not a DraftEngine backup.");
        }

        if (!container.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new BackupFormatException("This file is not a DraftEngine backup.");
        }

        var offset = Magic.Length;
        var salt = container.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = container.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        var tag = container.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;
        var ciphertext = container.AsSpan(offset).ToArray();

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new BackupPasswordException("The password is incorrect or the backup file was modified.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        try
        {
            return JsonSerializer.Deserialize<BackupPayload>(plaintext, JsonOptions)
                ?? throw new BackupFormatException("The backup payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException("The backup payload could not be read.");
        }
    }

    private static byte[] Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var output = new byte[Magic.Length + SaltSize + NonceSize + TagSize + ciphertext.Length];
        var offset = 0;
        Magic.CopyTo(output, offset);
        offset += Magic.Length;
        salt.CopyTo(output, offset);
        offset += SaltSize;
        nonce.CopyTo(output, offset);
        offset += NonceSize;
        tag.CopyTo(output, offset);
        offset += TagSize;
        ciphertext.CopyTo(output, offset);
        return output;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"The backup password must be at least {MinimumPasswordLength} characters.", nameof(password));
        }
    }

    private static void ValidatePayload(BackupPayload payload)
    {
        if (!string.Equals(payload.Product, "DraftEngine", StringComparison.Ordinal))
        {
            throw new BackupFormatException("The backup was not created by DraftEngine.");
        }

        if (payload.FormatVersion != BackupPayload.CurrentVersion)
        {
            throw new BackupFormatException(
                $"Backup format version {payload.FormatVersion} is not supported by this build.");
        }

        var contractIds = payload.Contracts.Select(contract => contract.Id).ToHashSet();
        if (payload.ContractFields.Any(field => !contractIds.Contains(field.ContractId)) ||
            payload.ContractClauses.Any(selection => !contractIds.Contains(selection.ContractId)) ||
            payload.ChatMessages.Any(message => !contractIds.Contains(message.ContractId)) ||
            payload.ContractVersions.Any(version => !contractIds.Contains(version.ContractId)) ||
            payload.GeneratedDocuments.Any(document => !contractIds.Contains(document.ContractId)))
        {
            throw new BackupFormatException("The backup contains records that reference a missing contract.");
        }
    }

    private static BackupSummary ToSummary(BackupPayload payload) => new(
        payload.Contracts.Count,
        payload.Clauses.Count,
        payload.ContractFields.Count,
        payload.ContractClauses.Count,
        payload.ChatMessages.Count,
        payload.ContractVersions.Count,
        payload.GeneratedDocuments.Count,
        payload.WizardDrafts.Count,
        payload.Settings is not null);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{escaped}\"" : escaped;
    }
}
