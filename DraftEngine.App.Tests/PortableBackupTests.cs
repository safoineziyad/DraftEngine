using System.IO;
using System.Text;
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
}
