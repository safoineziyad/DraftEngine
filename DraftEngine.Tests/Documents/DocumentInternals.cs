using System.Collections;
using System.Reflection;
using DraftEngine.Documents.Builders;

namespace DraftEngine.Tests.Documents;

/// <summary>
/// Reflection-based facade over the internal helper types of DraftEngine.Documents
/// (<c>ContractTextParser</c>, <c>ContractSegment</c> and <c>ContractParties</c>).
/// The Documents assembly does not expose <c>InternalsVisibleTo</c> and production code is
/// out of bounds for the test suite, so these internals are reached via reflection instead
/// of direct references.
/// </summary>
internal static class DocumentInternals
{
    private static readonly Assembly DocumentsAssembly = typeof(DocxBuilder).Assembly;

    private static readonly Type SegmentType = DocumentsAssembly.GetType(
        "DraftEngine.Documents.Builders.ContractSegment",
        throwOnError: true)!;

    private static readonly PropertyInfo SegmentIsHeadingProperty =
        SegmentType.GetProperty("IsHeading", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ContractSegment.IsHeading could not be located.");

    private static readonly PropertyInfo SegmentTextProperty =
        SegmentType.GetProperty("Text", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ContractSegment.Text could not be located.");

    /// <summary>
    /// A single parsed piece of contract text, mirroring the internal <c>ContractSegment</c>.
    /// </summary>
    /// <param name="IsHeading">True when the parser classified the line as a clause heading.</param>
    /// <param name="Text">The trimmed line text as stored by the parser.</param>
    internal readonly record struct Segment(bool IsHeading, string Text);

    /// <summary>
    /// Calls the internal <c>ContractTextParser.Parse</c> method on the given assembled contract text.
    /// </summary>
    /// <param name="fullContractText">The assembled contract text to parse; may be null.</param>
    /// <returns>The heading/paragraph segments the parser produces, in order.</returns>
    internal static IReadOnlyList<Segment> Parse(string? fullContractText)
    {
        var method = GetInternalStaticMethod("ContractTextParser", "Parse");
        var parsed = (IEnumerable)method.Invoke(null, new object?[] { fullContractText })!;

        var segments = new List<Segment>();
        foreach (var item in parsed)
        {
            var isHeading = (bool)SegmentIsHeadingProperty.GetValue(item)!;
            var text = (string)SegmentTextProperty.GetValue(item)!;
            segments.Add(new Segment(isHeading, text));
        }

        return segments;
    }

    /// <summary>
    /// Calls the internal <c>ContractParties.GetNames</c> method on the given PartiesJson.
    /// </summary>
    /// <param name="partiesJson">The raw PartiesJson value; may be null.</param>
    /// <returns>The extracted party names, or an empty list when nothing can be extracted.</returns>
    internal static IReadOnlyList<string> GetPartyNames(string? partiesJson)
    {
        var method = GetInternalStaticMethod("ContractParties", "GetNames");
        return (IReadOnlyList<string>)method.Invoke(null, new object?[] { partiesJson })!;
    }

    /// <summary>
    /// Calls the internal <c>ContractParties.ToDisplayText</c> method on the given PartiesJson.
    /// </summary>
    /// <param name="partiesJson">The raw PartiesJson value; may be null.</param>
    /// <returns>The human-readable parties text, the trimmed raw text, or "N/A".</returns>
    internal static string GetPartiesDisplayText(string? partiesJson)
    {
        var method = GetInternalStaticMethod("ContractParties", "ToDisplayText");
        return (string)method.Invoke(null, new object?[] { partiesJson })!;
    }

    private static MethodInfo GetInternalStaticMethod(string typeName, string methodName)
    {
        var type = DocumentsAssembly.GetType($"DraftEngine.Documents.Builders.{typeName}", throwOnError: true);
        var method = type!.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        return method ?? throw new InvalidOperationException($"{typeName}.{methodName} could not be located.");
    }
}
