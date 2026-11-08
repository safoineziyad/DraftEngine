using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using DraftEngine.App.Services.Backup;
using Microsoft.Extensions.DependencyInjection;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>
/// Portable backup requirements: AES-GCM round trip, password failure, tamper detection,
/// no plaintext intake values in the file, and a pre-import restore point.
/// </summary>
public sealed class PortableBackupTests : IClassFixture<StartupFixture>
{
    private const string Password = "correct horse battery staple";
    private readonly StartupFixture _fixture;

    public PortableBackupTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    private PortableBackupService CreateService() => new(_fixture.Provider
        .GetRequiredService<DraftEngine.Core.Interfaces.IDatabaseContextFactory>());

    private int Seed()
    {
        using var context = _fixture.CreateContext();
        var contract = new Contract
        {
            UserId = 1,
            Title = "Backup Round Trip NDA",
            ContractType = "MutualNda",
            ClientName = "Acme Secret Holdings",
            GoverningLaw = "Delaware",
            TermMonths = 18,
            PartiesJson = "[\"Acme Secret Holdings\",\"Beta LLC\"]",
            AssembledText = "Backup Round Trip NDA\n\n1. Confidentiality\nAcme Secret Holdings keeps this secret.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Contracts.Add(contract);
        context.SaveChanges();

        context.ContractFields.Add(new ContractField
        {
            ContractId = contract.Id,
            Key = "Party1",
            Value = "Acme Secret Holdings"
        });
        context.ContractVersions.Add(new ContractVersion
        {
            ContractId = contract.Id,
            VersionNumber = 1,
            Text = "earlier secret text",
            Reason = "test",
            CreatedAt = DateTime.UtcNow
        });
        context.ChatMessages.Add(new ChatMessage
        {
            ContractId = contract.Id,
            Role = DraftEngine.Core.Enums.MessageRole.User,
            Content = "Please add a notice clause.",
            CreatedAt = DateTime.UtcNow
        });
        context.SaveChanges();
        return contract.Id;
    }

    [Fact]
    public async Task EncryptedBackup_RoundTripsContractsFieldsVersionsAndChat()
    {
        var contractId = Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        var exported = await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        Assert.True(exported.Contracts >= 1);
        encrypted.Position = 0;

        // Destroy the source data, then restore it.
        using (var context = _fixture.CreateContext())
        {
            await context.Set<ContractField>().ExecuteDeleteAsync();
            await context.Set<ContractVersion>().ExecuteDeleteAsync();
            await context.Set<ChatMessage>().ExecuteDeleteAsync();
            await context.Set<Contract>().ExecuteDeleteAsync();
        }

        var restored = await service.ImportEncryptedAsync(
            encrypted, Password, confirmReplace: true,
            preImportBackupPath: Path.Combine(_fixture.Directory, "pre-import.debk"));

        Assert.True(restored.Contracts >= 1);
        using var verify = _fixture.CreateContext();
        var contract = await verify.Set<Contract>().SingleAsync(c => c.Id == contractId);
        Assert.Equal("Backup Round Trip NDA", contract.Title);
        Assert.Contains("Acme Secret Holdings", contract.AssembledText);
        var restoredField = await verify.Set<ContractField>()
            .SingleAsync(f => f.ContractId == contractId && f.Key == "Party1");
        Assert.Equal("Acme Secret Holdings", restoredField.Value);
        Assert.Equal(1, await verify.Set<ContractVersion>().CountAsync(v => v.ContractId == contractId));
        Assert.Equal(1, await verify.Set<ChatMessage>().CountAsync(m => m.ContractId == contractId));
        Assert.True(File.Exists(Path.Combine(_fixture.Directory, "pre-import.debk")));
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        encrypted.Position = 0;

        await Assert.ThrowsAsync<BackupPasswordException>(() =>
            service.ImportEncryptedAsync(encrypted, "definitely the wrong password", confirmReplace: true,
                preImportBackupPath: Path.Combine(_fixture.Directory, "wrong-password.debk")));
    }

    [Fact]
    public async Task TamperedFile_IsRejected()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        var bytes = encrypted.ToArray();
        bytes[^1] ^= 0xFF;
        using var tampered = new MemoryStream(bytes);

        await Assert.ThrowsAsync<BackupPasswordException>(() =>
            service.ImportEncryptedAsync(tampered, Password, confirmReplace: true,
                preImportBackupPath: Path.Combine(_fixture.Directory, "tampered.debk")));
    }

    [Fact]
    public async Task EncryptedBackupWithUnexpectedSqlIdentifiers_IsRejectedBeforeDatabaseChanges()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        using var malicious = RewriteEncryptedBackup(encrypted.ToArray(), payload =>
        {
            payload["tableName"] = "Contracts; DROP TABLE Contracts;--";
            payload["columnName"] = "Title; DROP TABLE Contracts;--";
        });

        List<(int Id, string Title)> before;
        using (var context = _fixture.CreateContext())
        {
            before = await context.Contracts.AsNoTracking()
                .OrderBy(contract => contract.Id)
                .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
                .ToListAsync();
        }

        var preImportPath = Path.Combine(_fixture.Directory, "malicious-pre-import.debk");
        await Assert.ThrowsAsync<BackupFormatException>(() =>
            service.ImportEncryptedAsync(malicious, Password, confirmReplace: true, preImportPath));

        using var verify = _fixture.CreateContext();
        var after = await verify.Contracts.AsNoTracking()
            .OrderBy(contract => contract.Id)
            .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
            .ToListAsync();
        Assert.Equal(before, after);
        Assert.False(File.Exists(preImportPath));
    }

    [Fact]
    public async Task FailedRestoreStep_RollsBackDatabaseChanges()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        using var invalid = RewriteEncryptedBackup(encrypted.ToArray(), payload =>
        {
            var contractId = payload["contracts"]![0]!["id"]!.GetValue<int>();
            payload["contractClauses"]!.AsArray().Add(new JsonObject
            {
                ["id"] = 987654321,
                ["contractId"] = contractId,
                ["clauseId"] = 987654322,
                ["selected"] = true,
                ["customBodyOverride"] = string.Empty
            });
        });

        List<(int Id, string Title)> before;
        using (var context = _fixture.CreateContext())
        {
            before = await context.Contracts.AsNoTracking()
                .OrderBy(contract => contract.Id)
                .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
                .ToListAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.ImportEncryptedAsync(
                invalid, Password, confirmReplace: true,
                Path.Combine(_fixture.Directory, "failed-restore-pre-import.debk")));

        using var verify = _fixture.CreateContext();
        var after = await verify.Contracts.AsNoTracking()
            .OrderBy(contract => contract.Id)
            .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
            .ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task FailedSettingsRestore_RollsBackDatabaseChanges()
    {
        Seed();
        var settings = new FailingSettingsService();
        var service = new PortableBackupService(
            _fixture.Provider.GetRequiredService<DraftEngine.Core.Interfaces.IDatabaseContextFactory>(),
            settings);
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        encrypted.Position = 0;

        List<(int Id, string Title)> before;
        using (var context = _fixture.CreateContext())
        {
            before = await context.Contracts.AsNoTracking()
                .OrderBy(contract => contract.Id)
                .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
                .ToListAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportEncryptedAsync(
                encrypted, Password, confirmReplace: true,
                Path.Combine(_fixture.Directory, "failed-settings-pre-import.debk")));

        using var verify = _fixture.CreateContext();
        var after = await verify.Contracts.AsNoTracking()
            .OrderBy(contract => contract.Id)
            .Select(contract => new ValueTuple<int, string>(contract.Id, contract.Title))
            .ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task EncryptedBackup_ContainsNoPlaintextContentOrPartyNames()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        var bytes = encrypted.ToArray();

        Assert.DoesNotContain("Acme Secret Holdings", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("earlier secret text", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("Please add a notice clause", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("Delaware", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task ImportWithoutConfirmation_ChangesNothing()
    {
        Seed();
        var service = CreateService();
        using var encrypted = new MemoryStream();
        await service.ExportEncryptedAsync(encrypted, Password, includeApiKey: false);
        encrypted.Position = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportEncryptedAsync(encrypted, Password, confirmReplace: false));
    }

    [Fact]
    public async Task JsonAndCsvExports_ContainReadableContractData()
    {
        Seed();
        var service = CreateService();
        using var json = new MemoryStream();
        await service.WriteJsonAsync(json, includeApiKey: false);
        var jsonText = Encoding.UTF8.GetString(json.ToArray());
        Assert.Contains("Backup Round Trip NDA", jsonText);

        using var csv = new MemoryStream();
        await service.WriteCsvAsync(csv);
        var csvText = Encoding.UTF8.GetString(csv.ToArray());
        Assert.Contains("Title,ContractType,Status", csvText);
        Assert.Contains("Backup Round Trip NDA", csvText);
        Assert.Contains("Acme Secret Holdings", csvText);
    }

    private static MemoryStream RewriteEncryptedBackup(byte[] container, Action<JsonObject> updatePayload)
    {
        const int magicLength = 5;
        const int saltLength = 16;
        const int nonceLength = 12;
        const int tagLength = 16;
        const int keyLength = 32;
        const int iterations = 600_000;
        var offset = magicLength;
        var salt = container.AsSpan(offset, saltLength).ToArray();
        offset += saltLength;
        var nonce = container.AsSpan(offset, nonceLength).ToArray();
        offset += nonceLength;
        var tag = container.AsSpan(offset, tagLength).ToArray();
        offset += tagLength;
        var ciphertext = container.AsSpan(offset).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Password, salt, iterations, HashAlgorithmName.SHA256, keyLength);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, tagLength))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, "DraftEngine.Backup.v1"u8);
        }

        CryptographicOperations.ZeroMemory(key);
        var payload = JsonNode.Parse(plaintext)!.AsObject();
        updatePayload(payload);
        var changedPlaintext = Encoding.UTF8.GetBytes(payload.ToJsonString());

        var changedSalt = RandomNumberGenerator.GetBytes(saltLength);
        var changedNonce = RandomNumberGenerator.GetBytes(nonceLength);
        var changedKey = Rfc2898DeriveBytes.Pbkdf2(
            Password, changedSalt, iterations, HashAlgorithmName.SHA256, keyLength);
        var changedCiphertext = new byte[changedPlaintext.Length];
        var changedTag = new byte[tagLength];
        using (var aes = new AesGcm(changedKey, tagLength))
        {
            aes.Encrypt(
                changedNonce, changedPlaintext, changedCiphertext, changedTag,
                "DraftEngine.Backup.v1"u8);
        }

        CryptographicOperations.ZeroMemory(changedKey);
        var result = new byte[magicLength + saltLength + nonceLength + tagLength + changedCiphertext.Length];
        container.AsSpan(0, magicLength).CopyTo(result);
        var resultOffset = magicLength;
        changedSalt.CopyTo(result, resultOffset);
        resultOffset += saltLength;
        changedNonce.CopyTo(result, resultOffset);
        resultOffset += nonceLength;
        changedTag.CopyTo(result, resultOffset);
        resultOffset += tagLength;
        changedCiphertext.CopyTo(result, resultOffset);
        return new MemoryStream(result);
    }

    private sealed class FailingSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();

        public event Action? SettingsChanged
        {
            add { }
            remove { }
        }

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings) =>
            throw new InvalidOperationException("Settings persistence failed.");

        public Task<IContractAiService?> CreateAiServiceAsync() =>
            Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }
}
