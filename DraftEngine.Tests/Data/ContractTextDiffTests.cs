using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Data;

/// <summary>
/// Tests for <see cref="ContractTextDiff"/>: a pure line diff used to preview AI edits
/// before the user accepts them.
/// </summary>
public class ContractTextDiffTests
{
    [Fact]
    public void Compute_IdenticalTexts_ReturnsNoLines()
    {
        var diff = ContractTextDiff.Compute("one\ntwo\nthree", "one\ntwo\nthree");

        Assert.Empty(diff);
        Assert.False(ContractTextDiff.HasChanges("a", "a"));
    }

    [Fact]
    public void Compute_ReplacedLine_ReportsRemovedAndAdded()
    {
        var diff = ContractTextDiff.Compute("one\ntwo\nthree", "one\nTWO\nthree");

        Assert.Contains(diff, line => line.Kind == DiffLineKind.Removed && line.Text == "two");
        Assert.Contains(diff, line => line.Kind == DiffLineKind.Added && line.Text == "TWO");
        Assert.Contains(diff, line => line.Kind == DiffLineKind.Unchanged && line.Text == "one");
        Assert.True(ContractTextDiff.HasChanges("one", "two"));
    }

    [Fact]
    public void Compute_InsertedLine_ReportsOnlyThatAddition()
    {
        var diff = ContractTextDiff.Compute("one\nthree", "one\ntwo\nthree");

        Assert.Equal(1, diff.Count(line => line.Kind == DiffLineKind.Added));
        Assert.Equal(0, diff.Count(line => line.Kind == DiffLineKind.Removed));
        Assert.Contains(diff, line => line.Kind == DiffLineKind.Added && line.Text == "two");
    }

    [Fact]
    public void Compute_HandlesCrLfAndMissingText()
    {
        var diff = ContractTextDiff.Compute("one\r\ntwo", "one\ntwo\r\nthree");

        Assert.Contains(diff, line => line.Kind == DiffLineKind.Added && line.Text == "three");
        Assert.Empty(ContractTextDiff.Compute(null, null));
        Assert.NotEmpty(ContractTextDiff.Compute(null, "new"));
    }
}
