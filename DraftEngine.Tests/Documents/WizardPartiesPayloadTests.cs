using DocumentFormat.OpenXml.Packaging;
using DraftEngine.Core.Models;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using DraftEngine.Documents.Builders;
using DraftEngine.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Documents;

/// <summary>
/// Tests for the parties payload the New Contract wizard stores on a contract.
/// <para>
/// The wizard is the only producer of <c>Contract.PartiesJson</c>, and both consumers
/// (<c>ContractService.AddPartyValues</c>, which feeds the placeholder substitution, and
/// <c>ContractParties</c>, which feeds the exported DOCX/PDF) only understand a JSON
/// array of <c>{name, address}</c> objects. These tests pin that producer/consumer
/// contract: the payload the wizard writes must be understood by both consumers.
/// </para>
/// </summary>
public class WizardPartiesPayloadTests : IDisposable
{
    /// <summary>
    /// The payload shape written by <c>NewContractViewModel.Generation.BuildPartiesJson</c>:
    /// a JSON array of <c>{name, address}</c> objects.
    /// </summary>
    private const string WizardPartiesJson =
        """[{"name":"Acme Corp","address":"1 Market Street"},{"name":"Globex Ltd","address":"2 Market Street"}]""";

    private const string DocumentText = """
        1. Definition of Confidential Information
        Confidential Information means any non-public information disclosed by Acme Corp or Globex Ltd that is marked confidential or would reasonably be considered confidential.
        """;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DraftEngineDbContext> _options;
    private readonly string _outputDir;

    public WizardPartiesPayloadTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DraftEngineDbContext>().UseSqlite(_connection).Options;

        _outputDir = Path.Combine(Path.GetTempPath(), "draftengine-wizard-parties", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);

        using var context = new DraftEngineDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try { _connection.Dispose(); } catch { /* best effort */ }
        try { if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The exported document shows the party names, not the raw payload, and the signature
    /// block names the counterparty instead of falling back to the "Counterparty" label.
    /// </summary>
    [Fact]
    public async Task DocxExport_WizardPayload_ShowsPartyNamesAndNoRawJson()
    {
        var contract = new Contract
        {
            Title = "Mutual Non-Disclosure Agreement",
            ContractType = "Mutual NDA",
            ClientName = "Acme Corp",
            ClientEmail = "legal@acme.example",
            PartiesJson = WizardPartiesJson,
            EffectiveDate = new DateTime(2026, 1, 15),
            GoverningLaw = "England and Wales",
            TermMonths = 12
        };

        var path = Path.Combine(_outputDir, "wizard-parties.docx");
        await new DocxBuilder().BuildDocxAsync(contract, DocumentText, path);

        using var doc = WordprocessingDocument.Open(path, false);
        var inner = doc.MainDocumentPart!.Document!.Body!.InnerText;

        // The title page lists the parties by name.
        Assert.Contains("Acme Corp and Globex Ltd", inner);

        // Nothing of the raw payload leaks into the client-facing document.
        Assert.DoesNotContain("\"name\"", inner);
        Assert.DoesNotContain("party1", inner);
        Assert.DoesNotContain("{", inner);

        // The signature block names the counterparty instead of the fallback label.
        Assert.Contains("Globex Ltd", inner);
        Assert.DoesNotContain("Counterparty", inner);
    }

    /// <summary>
    /// The party placeholders of a clause template are filled from the wizard payload.
    /// The client name is deliberately different from the party names, so the names can
    /// only reach the document text through <c>Contract.PartiesJson</c>.
    /// </summary>
    [Fact]
    public async Task GenerateContract_WizardPayload_FillsThePartyPlaceholders()
    {
        var factory = new TestDatabaseContextFactory(_options);

        int contractId;
        using (var context = new DraftEngineDbContext(_options))
        {
            var contract = new Contract
            {
                UserId = 1,
                Title = "Mutual Non-Disclosure Agreement",
                ContractType = "Mutual NDA",
                ClientName = "Signing Account",
                ClientEmail = "legal@acme.example",
                PartiesJson = WizardPartiesJson,
                EffectiveDate = new DateTime(2026, 1, 15),
                GoverningLaw = "England and Wales",
                TermMonths = 12
            };

            context.Contracts.Add(contract);
            await context.SaveChangesAsync();

            // Clause 2 is "Confidential Information means any non-public information
            // disclosed by {{Party1}} or {{Party2}} ...".
            context.ContractClauses.Add(new ContractClause
            {
                ContractId = contract.Id,
                ClauseId = 2,
                Selected = true
            });
            await context.SaveChangesAsync();

            contractId = contract.Id;
        }

        var service = new ContractService(factory, new FieldService(factory));

        var text = await service.GenerateContractAsync(contractId, new List<int>());

        Assert.Contains("disclosed by Acme Corp or Globex Ltd", text);
        Assert.DoesNotContain("{{Party1}}", text);
        Assert.DoesNotContain("{{Party2}}", text);
        Assert.DoesNotContain("{{", text);
    }

    /// <summary>
    /// The wizard is the only producer of the payload and it lives in the WPF application
    /// project, which the net8.0 test project cannot reference. The producer is therefore
    /// guarded at the source level: it must keep building the array shape every reader
    /// expects, and must not fall back to the object shape
    /// (<c>{"party1": ..., "party1Address": ...}</c>) that made the exported documents print
    /// raw JSON and left the signature block without the counterparty's name.
    /// </summary>
    [Fact]
    public void WizardProducer_KeepsBuildingTheArrayPayloadShape()
    {
        var method = ExtractWizardMethod("BuildPartiesJson");

        // The documented payload keys.
        Assert.Contains("\"name\"", method, StringComparison.Ordinal);
        Assert.Contains("\"address\"", method, StringComparison.Ordinal);

        // The object shape that no reader understands must not come back.
        Assert.DoesNotContain("\"party1\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"party1Address\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"party2\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"party2Address\"", method, StringComparison.Ordinal);
    }

    private static string ExtractWizardMethod(string methodName)
    {
        var source = ReadWizardSource();

        var start = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{methodName}' was not found in the wizard source.");

        var length = Math.Min(source.Length - start, 4000);
        return source.Substring(start, length);
    }

    private static string ReadWizardSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DraftEngine.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var path = Path.Combine(
            directory.FullName, "DraftEngine.App", "ViewModels", "NewContractViewModel.Generation.cs");

        Assert.True(File.Exists(path), $"The wizard source file was not found at '{path}'.");

        return File.ReadAllText(path);
    }
}
