using System.ComponentModel;
using System.IO;
using DraftEngine.Analysis;
using DraftEngine.App.Services;
using DraftEngine.App.Services.Analysis;
using DraftEngine.App.Services.Settings;
using DraftEngine.App.ViewModels;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Tests;

/// <summary>
/// Simple ViewModel-level checks for the Analyze empty-state hints. No WPF rendering or
/// visual-tree traversal is involved: we only assert binding-facing properties and notifications.
/// </summary>
public sealed class AnalyzeEmptyStateTests
{
    [Fact]
    public void NewViewModel_HasNoSavedAnalysesAndNoDocument()
    {
        var viewModel = new AnalyzeViewModel(new StubAnalysisService(), new StubSettings());

        Assert.False(viewModel.HasSavedAnalyses);
        Assert.False(viewModel.HasDocument);
    }

    [Fact]
    public void AddingSavedAnalysis_TogglesHasSavedAnalysesAndNotifies()
    {
        var viewModel = new AnalyzeViewModel(new StubAnalysisService(), new StubSettings());
        var changed = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.RecentAnalyses.Add(new AnalysisSummary(1, "draft.docx", "MutualNda", DateTime.UtcNow, 2, false));

        Assert.True(viewModel.HasSavedAnalyses);
        Assert.Contains(nameof(viewModel.HasSavedAnalyses), changed);
    }

    [Fact]
    public void SettingDocumentText_TogglesHasDocumentAndNotifies()
    {
        var viewModel = new AnalyzeViewModel(new StubAnalysisService(), new StubSettings());
        var changed = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.DocumentText = "Imported contract text";

        Assert.True(viewModel.HasDocument);
        Assert.Contains(nameof(viewModel.HasDocument), changed);

        viewModel.DocumentText = "   ";
        Assert.False(viewModel.HasDocument);
    }

    [Fact]
    public void AnalyzeView_EmptyHintsAreBoundThroughInverseVisibilityConverter()
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "DraftEngine.App", "Views"));
        var xaml = File.ReadAllText(Path.Combine(viewsDir, "AnalyzeView.xaml"));

        Assert.Contains("InverseBooleanToVisibilityConverter", xaml);
        Assert.Contains("Text=\"{loc:T Analyze.SavedEmpty}\"", xaml);
        Assert.Contains("HasSavedAnalyses, Converter={StaticResource BoolToVisInverse}", xaml);
        Assert.Contains("Text=\"{loc:T Analyze.DocumentEmpty}\"", xaml);
        Assert.Contains("HasDocument, Converter={StaticResource BoolToVisInverse}", xaml);
    }

    private sealed class StubAnalysisService : IAnalysisService
    {
        public Task<AnalysisResult> AnalyzeFileAsync(
            string path, string? contractType, bool useAi, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SaveAsync(
            AnalysisResult result, int? contractId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AnalysisResult?> LoadAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<AnalysisResult?>(null);

        public Task<IReadOnlyList<AnalysisSummary>> ListAsync(
            bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnalysisSummary>>([]);

        public Task ArchiveAsync(int id, bool archived = true, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event Action? SettingsChanged { add { } remove { } }

        public Task<AppSettings> ReloadAsync() => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task<IContractAiService?> CreateAiServiceAsync() => Task.FromResult<IContractAiService?>(null);

        public IContractAiService? CreateAiService(string apiKey, string model) => null;
    }
}
