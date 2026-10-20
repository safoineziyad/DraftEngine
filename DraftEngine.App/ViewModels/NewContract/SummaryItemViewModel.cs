namespace DraftEngine.App.ViewModels.NewContract;

/// <summary>
/// One label/value row in the step 4 review summary.
/// </summary>
public class SummaryItemViewModel
{
    /// <summary>The summary label (e.g. "Governing law").</summary>
    public string Label { get; }

    /// <summary>The summary value from the intake form.</summary>
    public string Value { get; }

    /// <summary>Creates a review summary row.</summary>
    public SummaryItemViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }
}
