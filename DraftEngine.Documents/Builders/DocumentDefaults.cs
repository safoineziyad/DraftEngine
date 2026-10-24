namespace DraftEngine.Documents.Builders;

/// <summary>
/// Text shared by the DOCX and PDF builders. Keeping these values in one place ensures both
/// formats render the same fallback text and makes the wording easy to change in one spot.
/// </summary>
internal static class DocumentDefaults
{
    /// <summary>Title used when <c>Contract.Title</c> is null, empty or whitespace.</summary>
    internal const string DefaultContractTitle = "Contract";

    /// <summary>Placeholder for optional contract metadata that was not supplied.</summary>
    internal const string NotApplicable = "N/A";

    /// <summary>Execution sentence printed directly above the signature block.</summary>
    internal const string WitnessClause =
        "IN WITNESS WHEREOF, the parties have executed this Agreement as of the Effective Date.";

    /// <summary>Generic first signatory label used when no party data can be read.</summary>
    internal const string FallbackFirstParty = "Party A";

    /// <summary>Generic second signatory label used when no counterparty can be identified.</summary>
    internal const string FallbackSecondParty = "Party B";

    /// <summary>Trims a title and falls back to <see cref="DefaultContractTitle"/> when blank.</summary>
    internal static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? DefaultContractTitle : title.Trim();

    /// <summary>Trims an optional value and falls back to <see cref="NotApplicable"/> when blank.</summary>
    internal static string OrNotApplicable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? NotApplicable : value.Trim();

    /// <summary>Formats the effective date, or <see cref="NotApplicable"/> when it is unset.</summary>
    internal static string FormatEffectiveDate(DateTime? effectiveDate) =>
        effectiveDate?.ToString("yyyy-MM-dd") ?? NotApplicable;

    /// <summary>Formats the term in months, or <see cref="NotApplicable"/> when it is unset.</summary>
    internal static string FormatTerm(int termMonths) =>
        termMonths > 0 ? $"{termMonths} month(s)" : NotApplicable;
}
