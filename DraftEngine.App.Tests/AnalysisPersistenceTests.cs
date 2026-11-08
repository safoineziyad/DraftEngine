using System.Data.Common;
using System.IO;
using DraftEngine.Analysis;
using DraftEngine.App.Services.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DraftEngine.App.Tests;

/// <summary>Analyze-run persistence must be encrypted and restorable.</summary>
public sealed class AnalysisPersistenceTests : IClassFixture<StartupFixture>
{
    private readonly StartupFixture _fixture;

    public AnalysisPersistenceTests(StartupFixture fixture)
    {
        _fixture = fixture;
    }

    private IAnalysisService Service => _fixture.Provider.GetRequiredService<IAnalysisService>();

    [Fact]
    public async Task Analysis_IsSavedEncryptedAndLoadsBack()
    {
        var path = Path.Combine(_fixture.Directory, "review.txt");
        await File.WriteAllTextAsync(path, string.Join("\n",
        [
            "MUTUAL NON-DISCLOSURE AGREEMENT",
            "1. Confidentiality",
            "Acme Secret Holdings shall protect confidential information.",
            "2. Governing Law",
            "This agreement is governed by California law.",
            "3. Return of Materials",
            "Each party shall return or destroy materials.",
            "4. Remedies",
            "Injunctive relief is available."
        ]));

        var result = await Service.AnalyzeFileAsync(path, PlaybookCatalog.MutualNda, useAi: false);
        Assert.Contains(result.Findings, finding => finding.RuleId == "nda.term");
        var id = await Service.SaveAsync(result);

        var raw = await ReadRawResultAsync(id);
        Assert.StartsWith("DEENC1:", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Secret Holdings", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confidential information", raw, StringComparison.OrdinalIgnoreCase);

        var loaded = await Service.LoadAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("review.txt", loaded!.Document.FileName);
        Assert.Contains(loaded.Findings, finding => finding.RuleId == "nda.term");
        Assert.Contains("Acme Secret Holdings", loaded.Document.Text);

        var summaries = await Service.ListAsync();
        Assert.Contains(summaries, summary => summary.Id == id && summary.FindingCount > 0);
        await Service.ArchiveAsync(id);
        Assert.DoesNotContain(await Service.ListAsync(), summary => summary.Id == id);
        Assert.Contains(await Service.ListAsync(includeArchived: true), summary => summary.Id == id);
    }

    [Fact]
    public async Task MissingFile_IsReportedAsImportFailure()
    {
        await Assert.ThrowsAsync<DocumentImportException>(() =>
            Service.AnalyzeFileAsync(Path.Combine(_fixture.Directory, "missing.docx"), null, useAi: false));
    }

    private async Task<string> ReadRawResultAsync(int id)
    {
        await using var context = _fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ResultJson FROM ContractAnalyses WHERE Id = $id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }
}
