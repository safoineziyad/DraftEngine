using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for the at-rest encryption of sensitive columns (audit Finding #3): values written
/// through the production options (interceptor registered, exactly like
/// <c>AddDraftEngineData</c> does) must be DPAPI ciphertext in the SQLite file itself, while
/// the application keeps seeing plaintext. A second, interceptor-free context plays the role
/// of "someone reading the copied database file".
/// </summary>
public class SensitiveDataEncryptionTests : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Production-shaped options: the encryption interceptor is registered.</summary>
    private readonly DbContextOptions<DraftEngineDbContext> _encryptedOptions;

    /// <summary>Interceptor-free options, used to inspect what actually lands on disk.</summary>
    private readonly DbContextOptions<DraftEngineDbContext> _plainOptions;

    public SensitiveDataEncryptionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _encryptedOptions = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SensitiveDataInterceptor())
            .Options;

        _plainOptions = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DraftEngineDbContext(_encryptedOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveContract_StoresSensitiveColumnsAsCiphertext()
    {
        var contractId = await SaveContractAsync("The parties agree to keep all information confidential.");

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);

        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.AssembledText, StringComparison.Ordinal);
        Assert.NotEqual("The parties agree to keep all information confidential.", stored.AssembledText);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.ClientName, StringComparison.Ordinal);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.ClientEmail, StringComparison.Ordinal);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.PartiesJson, StringComparison.Ordinal);

        // Unencrypted columns stay readable: the list view needs the title without DPAPI.
        Assert.Equal("Mutual NDA — Acme", stored.Title);
    }

    [Fact]
    public async Task SaveContract_ReadsBackAsPlaintextThroughTheProductionOptions()
    {
        var contractId = await SaveContractAsync("Confidentiality applies for three years.");

        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var loaded = await appContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);

        Assert.Equal("Confidentiality applies for three years.", loaded.AssembledText);
        Assert.Equal("Acme Inc", loaded.ClientName);
    }

    [Fact]
    public async Task SaveChatMessageFieldAndVersion_EncryptTheirTextToo()
    {
        var contractId = await SaveContractAsync("Document text");

        using (var appContext = new DraftEngineDbContext(_encryptedOptions))
        {
            appContext.ChatMessages.Add(new ChatMessage
            {
                ContractId = contractId,
                Role = MessageRole.User,
                Content = "Please shorten the term to six months.",
            });
            appContext.ContractFields.Add(new ContractField
            {
                ContractId = contractId,
                Key = "Purpose",
                Value = "Evaluation of a potential merger.",
            });
            appContext.ContractVersions.Add(new ContractVersion
            {
                ContractId = contractId,
                VersionNumber = 1,
                Text = "Snapshot text with client names.",
                Reason = ContractServiceReasons.AiEdit,
            });
            await appContext.SaveChangesAsync();
        }

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var message = await rawContext.ChatMessages.AsNoTracking().SingleAsync();
        var field = await rawContext.ContractFields.AsNoTracking().SingleAsync();
        var version = await rawContext.ContractVersions.AsNoTracking().SingleAsync();

        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, message.Content, StringComparison.Ordinal);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, field.Value, StringComparison.Ordinal);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, version.Text, StringComparison.Ordinal);

        using var appReadContext = new DraftEngineDbContext(_encryptedOptions);
        Assert.Equal("Please shorten the term to six months.",
            (await appReadContext.ChatMessages.AsNoTracking().SingleAsync()).Content);
        Assert.Equal("Evaluation of a potential merger.",
            (await appReadContext.ContractFields.AsNoTracking().SingleAsync()).Value);
        Assert.Equal("Snapshot text with client names.",
            (await appReadContext.ContractVersions.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public async Task LegacyPlaintextRows_FromV01Databases_StillLoad()
    {
        // Simulate a v0.1 database: values written without the interceptor, no marker.
        int contractId;
        using (var plainContext = new DraftEngineDbContext(_plainOptions))
        {
            var contract = CreateContract("Plaintext document from an old install.");
            plainContext.Contracts.Add(contract);
            await plainContext.SaveChangesAsync();
            contractId = contract.Id;
        }

        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var loaded = await appContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);

        Assert.Equal("Plaintext document from an old install.", loaded.AssembledText);
    }

    [Fact]
    public async Task Save_KeepsPlaintextInMemory_AndNeverDoubleEncrypts()
    {
        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var contract = CreateContract("Original text");
        appContext.Contracts.Add(contract);
        await appContext.SaveChangesAsync();

        // Right after the save the tracked entity must hold plaintext, not ciphertext.
        Assert.Equal("Original text", contract.AssembledText);

        // A second save of the same (untouched) row must not corrupt the value either.
        contract.UpdatedAt = DateTime.UtcNow;
        await appContext.SaveChangesAsync();
        Assert.Equal("Original text", contract.AssembledText);

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.AssembledText, StringComparison.Ordinal);

        using var reloadContext = new DraftEngineDbContext(_encryptedOptions);
        var reloaded = await reloadContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal("Original text", reloaded.AssembledText);
    }

    [Fact]
    public async Task UpdateThroughTheAppService_KeepsTextRoundTripIntact()
    {
        // The ContractService attaches entities and marks every property modified; the
        // interceptor must re-encrypt on save and still round-trip the plaintext.
        var contractId = await SaveContractAsync("Text before the update");

        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var contract = await appContext.Contracts.SingleAsync(c => c.Id == contractId);
        contract.Title = "Renamed";
        await appContext.SaveChangesAsync();

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.AssembledText, StringComparison.Ordinal);
        Assert.Equal("Text before the update", contract.AssembledText);
    }

    /// <summary>
    /// Changing an encrypted column of an already stored row must write ciphertext.
    /// EF Core runs the <c>SavingChanges</c> interception <em>before</em> the change tracker
    /// detects the changes, so the row was still "Unchanged" when the interceptor looked at
    /// it and the update was written to disk in plaintext. That defeated the at-rest
    /// encryption for every in-place update - which is how the contract body, the chat
    /// edits and the re-mapped intake answers are actually stored.
    /// </summary>
    [Fact]
    public async Task UpdateOfAnEncryptedColumn_WritesCiphertextToDisk()
    {
        var contractId = await SaveContractAsync("Text before the update");

        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var contract = await appContext.Contracts.SingleAsync(c => c.Id == contractId);
        contract.AssembledText = "Confidentiality now applies for five years.";
        await appContext.SaveChangesAsync();

        // The application keeps seeing plaintext.
        Assert.Equal("Confidentiality now applies for five years.", contract.AssembledText);

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);

        // The database file must not contain the new plaintext.
        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.AssembledText, StringComparison.Ordinal);
        Assert.DoesNotContain("five years", stored.AssembledText, StringComparison.Ordinal);

        // The update really reached the database (otherwise the assertions above would
        // only prove that the old ciphertext was left untouched).
        using var reloadContext = new DraftEngineDbContext(_encryptedOptions);
        var reloaded = await reloadContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);
        Assert.Equal("Confidentiality now applies for five years.", reloaded.AssembledText);
    }

    /// <summary>
    /// The generated document body - the text that contains the party names, the client
    /// e-mail and the purpose of the contract - must be ciphertext in the database file.
    /// This is the write path the wizard uses for every contract the user creates.
    /// </summary>
    [Fact]
    public async Task GeneratedContractBody_IsStoredAsCiphertextOnDisk()
    {
        int contractId;
        using (var appContext = new DraftEngineDbContext(_encryptedOptions))
        {
            var contract = CreateContract(string.Empty);
            appContext.Contracts.Add(contract);
            await appContext.SaveChangesAsync();
            contractId = contract.Id;
        }

        var factory = new TestDatabaseContextFactory(_encryptedOptions);
        var service = new ContractService(factory, new FieldService(factory));

        var text = await service.GenerateContractAsync(contractId, new List<int>());

        // The service returns the plaintext the user expects to see.
        Assert.Contains("Acme Inc", text);

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);

        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.AssembledText, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Inc", stored.AssembledText, StringComparison.Ordinal);

        // The body really reached the database.
        using var reloadContext = new DraftEngineDbContext(_encryptedOptions);
        var reloaded = await reloadContext.Contracts.AsNoTracking().SingleAsync(c => c.Id == contractId);
        Assert.Equal(text, reloaded.AssembledText);
    }

    /// <summary>
    /// Re-mapping an intake answer updates an already stored row. It must stay ciphertext:
    /// the field value is free-form text the user typed (names, addresses, purposes).
    /// </summary>
    [Fact]
    public async Task RemappingAnIntakeAnswer_WritesCiphertextToDisk()
    {
        var contractId = await SaveContractAsync("Document text");

        var factory = new TestDatabaseContextFactory(_encryptedOptions);
        var fieldService = new FieldService(factory);

        await fieldService.MapFieldsAsync(
            contractId, new Dictionary<string, string> { ["Purpose"] = "Initial purpose" });
        await fieldService.MapFieldsAsync(
            contractId, new Dictionary<string, string> { ["Purpose"] = "Revised purpose" });

        using var rawContext = new DraftEngineDbContext(_plainOptions);
        var stored = await rawContext.ContractFields.AsNoTracking().SingleAsync();

        Assert.StartsWith(SensitiveDataInterceptor.MarkerPrefix, stored.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Revised purpose", stored.Value, StringComparison.Ordinal);

        // The re-mapped value really reached the database.
        using var reloadContext = new DraftEngineDbContext(_encryptedOptions);
        var reloaded = await reloadContext.ContractFields.AsNoTracking().SingleAsync();
        Assert.Equal("Revised purpose", reloaded.Value);
    }

    /// <summary>Saves a contract through the production-shaped (encrypted) context.</summary>
    private async Task<int> SaveContractAsync(string assembledText)
    {
        using var appContext = new DraftEngineDbContext(_encryptedOptions);
        var contract = CreateContract(assembledText);
        appContext.Contracts.Add(contract);
        await appContext.SaveChangesAsync();
        return contract.Id;
    }

    private static Contract CreateContract(string assembledText) => new()
    {
        User = new User { Email = "dev@example.com", DisplayName = "Dev" },
        Title = "Mutual NDA — Acme",
        ContractType = "Mutual NDA",
        ClientName = "Acme Inc",
        ClientEmail = "legal@acme.example",
        PartiesJson = """[{"name":"Acme Inc"},{"name":"Globex"}]""",
        AssembledText = assembledText,
        Status = ContractStatus.Generated,
    };

    /// <summary>Reason constants without taking a dependency on ContractService internals.</summary>
    private static class ContractServiceReasons
    {
        public const string AiEdit = "AI edit";
    }
}
