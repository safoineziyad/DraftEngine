using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.Analysis;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Analysis;
using DraftEngine.App.Services.Settings;
using DraftEngine.Documents.Builders;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>Row wrapper for a finding in the Analyze page.</summary>
public sealed class FindingRowViewModel
{
    public FindingRowViewModel(AnalysisFinding finding)
    {
        Finding = finding;
        SeverityLabel = finding.Severity.ToString();
        IsAi = finding.IsAiGenerated;
    }

    public AnalysisFinding Finding { get; }
    public string SeverityLabel { get; }
    public string Title => Finding.Title;
    public string Category => Finding.Category;
    public string Explanation => Finding.Explanation;
    public string SuggestedFix => Finding.SuggestedFix;
    public string Quote => Finding.Quote;
    public bool IsAi { get; }
    public bool HasFix => !string.IsNullOrWhiteSpace(Finding.SuggestedFix);
}

/// <summary>Analyze page: import, deterministic review, optional consent-gated AI review and history.</summary>
public partial class AnalyzeViewModel : ObservableObject, INavigationAware
{
    private readonly IAnalysisService _analysisService;
    private readonly IAppSettingsService _settings;
    private AnalysisResult? _currentResult;

    /// <summary>Page title shown in the header.</summary>
    public string PageTitle => "Analyze";

    /// <summary>Mandatory disclaimer shown with every result and export.</summary>
    public string Disclaimer => AnalysisResult.Disclaimer;

    public IReadOnlyList<ContractTypeOption> ContractTypes { get; } =
    [
        new("MutualNda", "Mutual NDA"),
        new("OneWayNda", "One-Way NDA"),
        new("IndependentContractorAgreement", "Independent Contractor Agreement"),
        new("ConsultingAgreement", "Consulting Agreement"),
        new("Generic", "Other / generic contract")
    ];

    public ObservableCollection<FindingRowViewModel> Findings { get; } = new();
    public ObservableCollection<AnalyzedClause> Clauses { get; } = new();
    public ObservableCollection<AnalysisSummary> RecentAnalyses { get; } = new();

    [ObservableProperty] private ContractTypeOption? selectedContractType;
    [ObservableProperty] private string? currentFilePath;
    [ObservableProperty] private string documentText = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private FindingRowViewModel? selectedFinding;
    [ObservableProperty] private bool useAi;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private int highlightStart;
    [ObservableProperty] private int highlightLength;
    [ObservableProperty] private AnalysisSummary? selectedRecent;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool CanUseAi => _settings.Current.AiDataConsentAccepted;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.Current.OpenAiApiKey);
    public string AiConsentHint => CanUseAi
        ? (HasApiKey ? "AI review uses the key and endpoint from Settings." : "Configure an API key in Settings to use AI review.")
        : "AI review is off until you grant consent in Settings.";
    public int FindingCount => Findings.Count;
    public string FindingCountText => Findings.Count == 1 ? "1 finding" : $"{Findings.Count} findings";

    public AnalyzeViewModel(IAnalysisService analysisService, IAppSettingsService settings)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        SelectedContractType = ContractTypes[0];
        _settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(AiConsentHint));
        if (!CanUseAi)
        {
            UseAi = false;
        }
    }

    public async void OnNavigatedTo(object? parameter)
    {
        await ReloadRecentAsync();
        if (parameter is int analysisId && analysisId > 0)
        {
            await OpenAsync(analysisId);
        }
    }

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a contract to analyze",
            Filter = "Contracts (*.docx;*.pdf;*.txt)|*.docx;*.pdf;*.txt|DOCX (*.docx)|*.docx|PDF (*.pdf)|*.pdf|Text (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        CurrentFilePath = dialog.FileName;
        await AnalyzeAsync();
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath) || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var result = await _analysisService.AnalyzeFileAsync(
                CurrentFilePath,
                SelectedContractType?.Id,
                UseAi && CanUseAi);

            ApplyResult(result, await _analysisService.SaveAsync(result));
            StatusMessage = result.Statistics.AiUsed
                ? $"Analysis saved. AI reviewed the document; {result.Statistics.AiFindingsAccepted} AI findings kept, {result.Statistics.AiFindingsDiscarded} discarded as ungrounded."
                : "Analysis saved. Deterministic checks completed offline.";
            if (result.Statistics.AiError is not null)
            {
                StatusMessage = result.Statistics.AiError;
            }

            await ReloadRecentAsync();
        }
        catch (DocumentImportException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Contract analysis failed.");
            StatusMessage = "The document could not be analyzed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(AnalysisResult result, int savedId)
    {
        _currentResult = result;
        Findings.Clear();
        Clauses.Clear();
        foreach (var finding in result.Findings)
        {
            Findings.Add(new FindingRowViewModel(finding));
        }

        foreach (var clause in result.Clauses)
        {
            Clauses.Add(clause);
        }

        DocumentText = result.Document.Text;
        SelectedFinding = Findings.FirstOrDefault();
        HasResult = true;
        OnPropertyChanged(nameof(FindingCount));
        OnPropertyChanged(nameof(FindingCountText));
    }

    partial void OnSelectedFindingChanged(FindingRowViewModel? value)
    {
        if (value is null)
        {
            HighlightStart = 0;
            HighlightLength = 0;
            return;
        }

        HighlightStart = Math.Max(0, value.Finding.StartOffset);
        HighlightLength = string.IsNullOrEmpty(value.Quote) ? 0 : value.Quote.Length;
        if (HighlightStart + HighlightLength > DocumentText.Length)
        {
            HighlightLength = Math.Max(0, DocumentText.Length - HighlightStart);
        }

        OnPropertyChanged(nameof(HighlightStart));
        OnPropertyChanged(nameof(HighlightLength));
    }

    [RelayCommand]
    private async Task ExportReportAsync(string? format)
    {
        if (_currentResult is null)
        {
            return;
        }

        var isPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export review report",
            Filter = isPdf ? "PDF (*.pdf)|*.pdf" : "Word document (*.docx)|*.docx",
            DefaultExt = isPdf ? ".pdf" : ".docx",
            FileName = $"review-{Path.GetFileNameWithoutExtension(_currentResult.Document.FileName)}.{(isPdf ? "pdf" : "docx")}"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var builder = new AnalysisReportBuilder();
            if (isPdf)
            {
                await builder.BuildPdfAsync(_currentResult, dialog.FileName);
            }
            else
            {
                await builder.BuildDocxAsync(_currentResult, dialog.FileName);
            }

            StatusMessage = "Review report exported. It includes the legal-review disclaimer.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Review report export failed.");
            StatusMessage = "The report could not be exported.";
        }
    }

    [RelayCommand]
    private void CopySuggestedFix()
    {
        if (SelectedFinding is null || string.IsNullOrWhiteSpace(SelectedFinding.SuggestedFix))
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedFinding.SuggestedFix);
            StatusMessage = "Suggested fix copied to the clipboard.";
        }
        catch (Exception)
        {
            StatusMessage = "The clipboard is unavailable.";
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(AnalysisSummary? summary)
    {
        if (summary is not null)
        {
            await OpenAsync(summary.Id);
        }
    }

    private async Task OpenAsync(int id)
    {
        var result = await _analysisService.LoadAsync(id);
        if (result is null)
        {
            StatusMessage = "That analysis could not be opened.";
            return;
        }

        ApplyResult(result, id);
        SelectedContractType = ContractTypes.FirstOrDefault(type => type.Id == result.Playbook.ContractType)
            ?? ContractTypes[^1];
        CurrentFilePath = result.Document.FileName;
        StatusMessage = "Saved analysis opened.";
    }

    [RelayCommand]
    private async Task ArchiveCurrentAsync()
    {
        if (SelectedRecent is null)
        {
            StatusMessage = "Choose a saved analysis to archive.";
            return;
        }

        await _analysisService.ArchiveAsync(SelectedRecent.Id);
        await ReloadRecentAsync();
        StatusMessage = "Analysis archived. It remains in the database.";
    }

    private async Task ReloadRecentAsync()
    {
        var summaries = await _analysisService.ListAsync(includeArchived: true);
        RecentAnalyses.Clear();
        foreach (var summary in summaries)
        {
            RecentAnalyses.Add(summary);
        }
    }

    [RelayCommand]
    private async Task ReloadRecentCommandAsync() => await ReloadRecentAsync();
}

public sealed record ContractTypeOption(string Id, string DisplayName);
