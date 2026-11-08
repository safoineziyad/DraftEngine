using System.Text.Json;
using System.Text.Json.Serialization;
using DraftEngine.Analysis;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DraftEngine.App.Services.Analysis;

public sealed record AnalysisSummary(
    int Id,
    string FileName,
    string ContractType,
    DateTime CreatedAt,
    int FindingCount,
    bool IsArchived);

/// <summary>Runs, saves, lists and archives contract analyses (encrypted at rest).</summary>
public interface IAnalysisService
{
    Task<AnalysisResult> AnalyzeFileAsync(
        string path,
        string? contractType,
        bool useAi,
        CancellationToken cancellationToken = default);

    Task<int> SaveAsync(AnalysisResult result, int? contractId = null, CancellationToken cancellationToken = default);
    Task<AnalysisResult?> LoadAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnalysisSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task ArchiveAsync(int id, bool archived = true, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>EF Core + DPAPI-backed implementation; stores the result JSON encrypted.</summary>
public sealed class AnalysisService : IAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDatabaseContextFactory _contextFactory;
    private readonly IAppSettingsService _settings;
    private readonly IAiReviewer? _aiReviewer;
    private readonly ContractAnalyzer _deterministicAnalyzer = new();

    public AnalysisService(
        IDatabaseContextFactory contextFactory,
        IAppSettingsService settings,
        IAiReviewer? aiReviewer = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _aiReviewer = aiReviewer;
    }

    public async Task<AnalysisResult> AnalyzeFileAsync(
        string path,
        string? contractType,
        bool useAi,
        CancellationToken cancellationToken = default)
    {
        var document = await DocumentImporter.ImportAsync(path, cancellationToken);
        var consent = _settings.Current.AiDataConsentAccepted;
        var analyzer = useAi && consent && _aiReviewer is not null
            ? new ContractAnalyzer(_aiReviewer)
            : _deterministicAnalyzer;

        return await analyzer.AnalyzeAsync(document, new AnalysisOptions
        {
            ContractType = contractType,
            UseAi = useAi,
            AiConsentGranted = consent
        }, cancellationToken);
    }

    public async Task<int> SaveAsync(
        AnalysisResult result,
        int? contractId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var context = _contextFactory.CreateContext();
        var entity = new ContractAnalysis
        {
            ContractId = contractId,
            FileName = result.Document.FileName,
            ContractType = result.Playbook.ContractType,
            ResultJson = JsonSerializer.Serialize(result, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        Log.Information("Analysis saved ({Findings} findings).", result.Findings.Count);
        return entity.Id;
    }

    public async Task<AnalysisResult?> LoadAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateContext();
        var entity = await context.Set<ContractAnalysis>().AsNoTracking()
            .FirstOrDefaultAsync(analysis => analysis.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AnalysisResult>(entity.ResultJson, JsonOptions);
        }
        catch (JsonException)
        {
            Log.Warning("A stored analysis could not be read; it may be from an incompatible version.");
            return null;
        }
    }

    public async Task<IReadOnlyList<AnalysisSummary>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateContext();
        var entities = await context.Set<ContractAnalysis>().AsNoTracking()
            .Where(analysis => includeArchived || !analysis.IsArchived)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .ToListAsync(cancellationToken);

        var summaries = new List<AnalysisSummary>(entities.Count);
        foreach (var entity in entities)
        {
            var findingCount = 0;
            try
            {
                var result = JsonSerializer.Deserialize<AnalysisResult>(entity.ResultJson, JsonOptions);
                findingCount = result?.Findings.Count ?? 0;
            }
            catch (JsonException)
            {
            }

            summaries.Add(new AnalysisSummary(
                entity.Id, entity.FileName, entity.ContractType, entity.CreatedAt, findingCount, entity.IsArchived));
        }

        return summaries;
    }

    public async Task ArchiveAsync(int id, bool archived = true, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateContext();
        var entity = await context.Set<ContractAnalysis>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.IsArchived = archived;
        entity.ArchivedAt = archived ? DateTime.UtcNow : null;
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateContext();
        await context.Set<ContractAnalysis>().Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
