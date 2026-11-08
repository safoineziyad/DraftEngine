using System;
using System.Collections.Generic;

namespace DraftEngine.Core.Services;

/// <summary>Kind of a single line in a document diff.</summary>
public enum DiffLineKind
{
    /// <summary>The line is identical in both versions.</summary>
    Unchanged,

    /// <summary>The line only exists in the proposed version.</summary>
    Added,

    /// <summary>The line only exists in the current version.</summary>
    Removed,
}

/// <summary>One line of a document diff.</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text);

/// <summary>
/// Pure line-based diff used to preview an AI edit before it replaces the stored contract.
/// It deliberately has no database, UI or AI dependencies so it can be unit tested and
/// reused by every page.
/// </summary>
public static class ContractTextDiff
{
    // Above this many changed lines the quadratic LCS table would be wasteful for a
    // contract; the changed middle is reported as a block instead (still correct, just
    // less granular). Typical contracts stay far below this.
    private const int MaxLcsCells = 4_000_000;

    /// <summary>
    /// Computes the line diff from <paramref name="original"/> to <paramref name="updated"/>.
    /// Unchanged runs are collapsed to at most <paramref name="contextLines"/> lines around
    /// each change so the preview stays readable.
    /// </summary>
    public static IReadOnlyList<DiffLine> Compute(string? original, string? updated, int contextLines = 3)
    {
        if (string.Equals(original ?? string.Empty, updated ?? string.Empty, StringComparison.Ordinal))
        {
            return Array.Empty<DiffLine>();
        }

        var oldLines = SplitLines(original);
        var newLines = SplitLines(updated);

        var prefix = 0;
        while (prefix < oldLines.Count && prefix < newLines.Count &&
               string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var oldSuffix = oldLines.Count - 1;
        var newSuffix = newLines.Count - 1;
        while (oldSuffix >= prefix && newSuffix >= prefix &&
               string.Equals(oldLines[oldSuffix], newLines[newSuffix], StringComparison.Ordinal))
        {
            oldSuffix--;
            newSuffix--;
        }

        var diffs = new List<DiffLine>();
        var oldCount = oldSuffix - prefix + 1;
        var newCount = newSuffix - prefix + 1;

        if ((long)oldCount * newCount <= MaxLcsCells)
        {
            BuildLcsDiff(oldLines, newLines, prefix, oldSuffix, newSuffix, diffs);
        }
        else
        {
            for (var i = prefix; i <= oldSuffix; i++)
            {
                diffs.Add(new DiffLine(DiffLineKind.Removed, oldLines[i]));
            }

            for (var i = prefix; i <= newSuffix; i++)
            {
                diffs.Add(new DiffLine(DiffLineKind.Added, newLines[i]));
            }
        }

        return CollapseContext(oldLines, newLines, prefix, oldSuffix, newSuffix, diffs, contextLines);
    }

    /// <summary>True when the two texts differ in at least one line.</summary>
    public static bool HasChanges(string? original, string? updated) =>
        !string.Equals(original ?? string.Empty, updated ?? string.Empty, StringComparison.Ordinal);

    private static List<string> SplitLines(string? text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static void BuildLcsDiff(
        List<string> oldLines,
        List<string> newLines,
        int prefix,
        int oldSuffix,
        int newSuffix,
        List<DiffLine> diffs)
    {
        var oldCount = Math.Max(0, oldSuffix - prefix + 1);
        var newCount = Math.Max(0, newSuffix - prefix + 1);
        if (oldCount == 0 && newCount == 0)
        {
            return;
        }

        var lengths = new int[oldCount + 1, newCount + 1];
        for (var i = oldCount - 1; i >= 0; i--)
        {
            for (var j = newCount - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(oldLines[prefix + i], newLines[prefix + j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < oldCount && newIndex < newCount)
        {
            if (string.Equals(oldLines[prefix + oldIndex], newLines[prefix + newIndex], StringComparison.Ordinal))
            {
                diffs.Add(new DiffLine(DiffLineKind.Unchanged, oldLines[prefix + oldIndex]));
                oldIndex++;
                newIndex++;
            }
            else if (lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1])
            {
                diffs.Add(new DiffLine(DiffLineKind.Removed, oldLines[prefix + oldIndex]));
                oldIndex++;
            }
            else
            {
                diffs.Add(new DiffLine(DiffLineKind.Added, newLines[prefix + newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldCount)
        {
            diffs.Add(new DiffLine(DiffLineKind.Removed, oldLines[prefix + oldIndex++]));
        }

        while (newIndex < newCount)
        {
            diffs.Add(new DiffLine(DiffLineKind.Added, newLines[prefix + newIndex++]));
        }
    }

    private static IReadOnlyList<DiffLine> CollapseContext(
        List<string> oldLines,
        List<string> newLines,
        int prefix,
        int oldSuffix,
        int newSuffix,
        List<DiffLine> middle,
        int contextLines)
    {
        contextLines = Math.Max(0, contextLines);
        var result = new List<DiffLine>();

        // Leading unchanged context.
        var leadingStart = Math.Max(0, prefix - contextLines);
        for (var i = leadingStart; i < prefix; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, oldLines[i]));
        }

        if (result.Count > 0 && leadingStart > 0)
        {
            result.Insert(0, new DiffLine(DiffLineKind.Unchanged, "…"));
        }

        result.AddRange(middle);

        // Trailing unchanged context.
        var trailingCount = Math.Min(contextLines, oldLines.Count - 1 - oldSuffix);
        for (var i = 1; i <= trailingCount; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, oldLines[oldSuffix + i]));
        }

        if (trailingCount < oldLines.Count - 1 - oldSuffix)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, "…"));
        }

        return result;
    }
}
