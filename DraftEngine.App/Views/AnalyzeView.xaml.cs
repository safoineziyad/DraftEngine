using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DraftEngine.App.ViewModels;

namespace DraftEngine.App.Views;

public partial class AnalyzeView : UserControl
{
    private AnalyzeViewModel? _viewModel;

    public AnalyzeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _viewModel = e.NewValue as AnalyzeViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void Detach()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnalyzeViewModel.SelectedFinding)
            or nameof(AnalyzeViewModel.HighlightStart)
            or nameof(AnalyzeViewModel.HighlightLength))
        {
            Dispatcher.BeginInvoke(Highlight, DispatcherPriority.Background);
        }
    }

    private void Highlight()
    {
        if (_viewModel is null || DocumentBox is null)
        {
            return;
        }

        var start = _viewModel.HighlightStart;
        var length = _viewModel.HighlightLength;
        if (start < 0 || start >= DocumentBox.Text.Length)
        {
            return;
        }

        length = System.Math.Min(length, DocumentBox.Text.Length - start);
        DocumentBox.Focus();
        DocumentBox.Select(start, length);
        var line = DocumentBox.GetLineIndexFromCharacterIndex(start);
        if (line >= 0)
        {
            DocumentBox.ScrollToLine(line);
        }
    }
}
