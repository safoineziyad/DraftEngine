using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using DraftEngine.Core.Services;
using DraftEngine.Data;
using DraftEngine.Documents.Builders;
using DraftEngine.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace DraftEngine.Tests.Generation;

/// <summary>
/// Offline end-to-end coverage for all four registry contract types: placeholder alignment,
/// template-only generation (no AI, no network), DOCX/PDF export, and a Mutual NDA snapshot.
/// </summary>
public class MultiTypeGenerationTests : IDisposable
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{([\w\-.]+)\}\}", RegexOptions.Compiled);

    private readonly string _dbPath;
    private readonly string _outputDir;
    private readonly DbContextOptions<DraftEngineDbContext> _options;
    private readonly TestDatabaseContextFactory _factory;

    public MultiTypeGenerationTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), $"draftengine_multitype_{id}.db");
        _outputDir = Path.Combine(Path.GetTempPath(), "draftengine-tests", "multitype-" + id);
        Directory.CreateDirectory(_outputDir);

        _options = new DbContextOptionsBuilder<DraftEngineDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _factory = new TestDatabaseContextFactory(_options);
    }

    public void Dispose()
    {
        TryDelete(_dbPath);
        try
        {
            if (Directory.Exists(_outputDir))
            {
                Directory.Delete(_outputDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    public static IEnumerable<object[]> ContractTypes =>
        ContractTypeRegistry.All.Select(definition => new object[] { definition.Id });

    [Fact]
    public async Task SeededClauses_UseOnlyRegistrySupportedAndResolvablePlaceholders()
    {
        await MigrateAsync();
        await using var database = new DraftEngineDbContext(_options);
        var clauses = await database.Clauses.AsNoTracking().ToListAsync();

        foreach (var definition in ContractTypeRegistry.All)
        {
            var typeClauses = clauses.Where(clause => clause.ContractType == definition.Id).ToList();
            Assert.NotEmpty(typeClauses);

            foreach (var clause in typeClauses)
            {
                foreach (Match match in PlaceholderPattern.Matches(clause.BodyTemplate))
                {
                    var key = match.Groups[1].Value;

                    Assert.True(
                        definition.SupportedPlaceholderKeys.Contains(key),
                        $"{definition.Id} clause {clause.Id} ('{clause.Title}') uses unsupported placeholder '{key}'.");

                    // Every placeholder must be collectable from the registry intake form or
                    // safely defaulted by FieldService; otherwise generation can ship "{{...}}".
                    var collectable = definition.IntakeFields.Any(field =>
                        string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
                    Assert.True(
                        collectable || FieldService.Defaults.ContainsKey(key),
                        $"{definition.Id} clause {clause.Id} placeholder '{key}' is neither collected nor defaulted.");
                }
            }

            // The wizard builds fields from IntakeFields, so each one must be supported too.
            foreach (var field in definition.IntakeFields)
            {
                Assert.Contains(field.Key, definition.SupportedPlaceholderKeys);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public async Task GenerateAndExport_EachType_IsCompleteAndRoleAware(string contractTypeId)
    {
        await MigrateAsync();
        var definition = ContractTypeRegistry.GetById(contractTypeId);
        var values = SampleValues(definition);
        var contractId = await CreateContractAsync(definition, values);

        var fieldService = new FieldService(_factory);
        await fieldService.MapFieldsAsync(contractId, values);

        var clauseIds = await GetClauseIdsAsync(definition.Id);
        var service = new ContractService(_factory, fieldService);
        var text = await service.GenerateContractAsync(contractId, clauseIds);

        // ---- Offline generation: no unresolved placeholders, numbered headings intact. ----
        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("}}", text, StringComparison.Ordinal);
        Assert.StartsWith(definition.DisplayName, text, StringComparison.Ordinal);
        Assert.Equal(clauseIds.Count, CountNumberedHeadingsInText(text));
        Assert.Equal(1, CountOccurrences(text, "IN WITNESS WHEREOF"));

        var firstParty = values[definition.FirstPartyNameKey];
        var secondParty = values[definition.SecondPartyNameKey];
        Assert.Contains(firstParty, text, StringComparison.Ordinal);
        Assert.Contains(secondParty, text, StringComparison.Ordinal);

        // Role labels are the type-aware part of the signature block (Mutual NDA uses the
        // generic Party 1/Party 2 labels, which its template omits by design).
        if (definition.Id != ContractTypeRegistry.MutualNdaId)
        {
            Assert.Contains($"({definition.SignatureFirstPartyLabel})", text, StringComparison.Ordinal);
            Assert.Contains($"({definition.SignatureSecondPartyLabel})", text, StringComparison.Ordinal);
        }

        // ---- Export both formats and verify their contents. ----
        await using (var database = new DraftEngineDbContext(_options))
        {
            var contract = await database.Contracts.AsNoTracking().SingleAsync(row => row.Id == contractId);
            var docxPath = Path.Combine(_outputDir, $"{contractTypeId}.docx");
            var pdfPath = Path.Combine(_outputDir, $"{contractTypeId}.pdf");

            await new DocxBuilder().BuildDocxAsync(contract, text, docxPath);
            await new PdfBuilder().BuildPdfAsync(contract, text, pdfPath);

            // Assert against the clauses actually generated (all seeded clauses for the type),
            // not the registry's default selection, which is a subset for some types.
            AssertDocx(docxPath, definition, firstParty, secondParty, clauseIds.Count);
            Assert.True(CountPdfPages(await File.ReadAllBytesAsync(pdfPath)) > 0, "PDF contained no pages.");
        }
    }

    [Fact]
    public async Task MutualNda_AssembledText_MatchesSnapshot()
    {
        await MigrateAsync();
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            ["Party1Address"] = "1 Market Street",
            ["Party2"] = "Globex Ltd",
            ["Party2Address"] = "2 Market Street",
            ["ClientName"] = "Acme Corp",
            ["ClientEmail"] = "legal@acme.example",
            ["EffectiveDate"] = "2026-01-15",
            ["GoverningLaw"] = "Delaware",
            ["TermMonths"] = "24",
            ["Purpose"] = "evaluating a partnership",
            ["ConfidentialInformation"] = "non-public business information",
        };

        var contractId = await CreateContractAsync(definition, values);
        var fieldService = new FieldService(_factory);
        await fieldService.MapFieldsAsync(contractId, values);

        var clauseIds = await GetClauseIdsAsync(definition.Id);
        var text = await new ContractService(_factory, fieldService).GenerateContractAsync(contractId, clauseIds);

        Assert.Equal(ExpectedMutualNdaSnapshot, Normalize(text));
    }

    private async Task MigrateAsync()
    {
        await using var database = new DraftEngineDbContext(_options);
        await database.Database.MigrateAsync();
    }

    private async Task<int> CreateContractAsync(
        ContractTypeDefinition definition,
        Dictionary<string, string> values)
    {
        await using var database = new DraftEngineDbContext(_options);
        var contract = new Contract
        {
            UserId = 1,
            Title = ContractContentBuilder.BuildTitle(definition, values),
            ContractType = definition.Id,
            ClientName = values.GetValueOrDefault("ClientName")
                ?? values[definition.SecondPartyNameKey],
            ClientEmail = values.GetValueOrDefault("ClientEmail") ?? string.Empty,
            PartiesJson = BuildPartiesJson(definition, values),
            EffectiveDate = new DateTime(2026, 1, 15),
            GoverningLaw = values.GetValueOrDefault("GoverningLaw") ?? string.Empty,
            TermMonths = int.TryParse(values.GetValueOrDefault("TermMonths"), out var months) ? months : 12,
            Status = ContractStatus.Draft,
        };

        database.Contracts.Add(contract);
        await database.SaveChangesAsync();
        return contract.Id;
    }

    private async Task<List<int>> GetClauseIdsAsync(string contractTypeId)
    {
        await using var database = new DraftEngineDbContext(_options);
        return await database.Clauses.AsNoTracking()
            .Where(clause => clause.ContractType == contractTypeId)
            .OrderBy(clause => clause.SortOrder)
            .Select(clause => clause.Id)
            .ToListAsync();
    }

    private static Dictionary<string, string> SampleValues(ContractTypeDefinition definition)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.IntakeFields)
        {
            values[field.Key] = field.Key switch
            {
                "Party1" or "ContractorName" or "ConsultantName" => "Acme Corp",
                "Party2" or "ClientName" => "Globex Ltd",
                "Party1Address" or "ContractorAddress" or "ConsultantAddress" => "1 Market Street",
                "Party2Address" or "ClientAddress" => "2 Market Street",
                "ClientEmail" => "legal@globex.example",
                "EffectiveDate" => "2026-01-15",
                "GoverningLaw" => "Delaware",
                "TermMonths" => "24",
                "Purpose" => "evaluating a partnership",
                _ => $"{field.Key} value",
            };
        }

        values["ConfidentialInformation"] = "non-public business information";
        return values;
    }

    private static string BuildPartiesJson(
        ContractTypeDefinition definition,
        Dictionary<string, string> values) =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, string>
            {
                ["name"] = values.GetValueOrDefault(definition.FirstPartyNameKey) ?? string.Empty,
                ["address"] = values.GetValueOrDefault(definition.FirstPartyAddressKey) ?? string.Empty,
            },
            new Dictionary<string, string>
            {
                ["name"] = values.GetValueOrDefault(definition.SecondPartyNameKey) ?? string.Empty,
                ["address"] = values.GetValueOrDefault(definition.SecondPartyAddressKey) ?? string.Empty,
            },
        });

    private static void AssertDocx(
        string path,
        ContractTypeDefinition definition,
        string firstParty,
        string secondParty,
        int expectedClauses)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        Assert.NotNull(body);
        var innerText = body!.InnerText;

        Assert.Equal(1, CountOccurrences(innerText, "IN WITNESS WHEREOF"));
        Assert.Contains(firstParty, innerText, StringComparison.Ordinal);
        Assert.Contains(secondParty, innerText, StringComparison.Ordinal);
        // InnerText concatenates runs without line breaks, so headings must be counted
        // paragraph by paragraph rather than with a multiline regex over the whole body.
        Assert.Equal(expectedClauses, CountNumberedHeadingParagraphs(body!));

        if (definition.Id != ContractTypeRegistry.MutualNdaId)
        {
            Assert.Contains($"({definition.SignatureFirstPartyLabel})", innerText, StringComparison.Ordinal);
            Assert.Contains($"({definition.SignatureSecondPartyLabel})", innerText, StringComparison.Ordinal);
        }
    }

    private static int CountNumberedHeadingsInText(string text) =>
        Regex.Matches(text, @"^\s*\d+\.\s+\S", RegexOptions.Multiline).Count;

    private static int CountNumberedHeadingParagraphs(DocumentFormat.OpenXml.Wordprocessing.Body body) =>
        body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Count(paragraph => Regex.IsMatch(paragraph.InnerText.Trim(), @"^\d+\.\s+\S"));

    private static int CountOccurrences(string text, string value) =>
        Regex.Matches(text, Regex.Escape(value), RegexOptions.IgnoreCase).Count;

    private static int CountPdfPages(byte[] bytes) =>
        Regex.Matches(Encoding.Latin1.GetString(bytes), @"/Type\s*/Page\b").Count;

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
        catch
        {
            // Best effort: a leaked temp file must not fail the suite.
        }
    }

    private const string ExpectedMutualNdaSnapshot = """
        Mutual NDA – Acme Corp & Globex Ltd

        This Mutual Non-Disclosure Agreement (the "Agreement") is entered into by and between Acme Corp and Globex Ltd (collectively the "Parties") for the purpose of evaluating a partnership.

        1. Confidentiality
        The parties agree to keep all non-public business information confidential and not disclose it to third parties except as permitted by this Agreement.

        2. Definition of Confidential Information
        Confidential Information means any non-public information disclosed by Acme Corp or Globex Ltd that is marked confidential or would reasonably be considered confidential.

        3. Exclusions
        Confidential Information does not include information that is public, already known, independently developed, or rightfully received from third parties.

        4. Term
        This Agreement shall remain in effect for 24 months from the Effective Date (January 15, 2026).

        5. Governing Law
        This Agreement shall be governed by and construed in accordance with the laws of Delaware.

        6. Entire Agreement
        This Agreement constitutes the entire agreement between the parties with respect to the subject matter hereof.

        7. No License
        Nothing in this Agreement grants any license or rights to the other party's intellectual property.

        8. Return of Materials
        Upon termination, each party shall return or destroy all Confidential Information received from the other party.

        9. Remedies
        Each party acknowledges that breach may cause irreparable harm and monetary damages may be inadequate.

        10. Assignment
        Neither party may assign this Agreement without the prior written consent of the other party.

        11. Severability
        If any provision is found invalid, the remainder shall remain in full force and effect.

        12. Counterparts
        This Agreement may be executed in counterparts, each of which shall be deemed an original.

        IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.

        ___________________________
        Acme Corp

        ___________________________
        Globex Ltd
        """;
}
