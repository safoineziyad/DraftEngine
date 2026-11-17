using System.Globalization;
using DraftEngine.App.Services;

namespace DraftEngine.App.Tests;

public sealed class ExportFileNameSanitizerTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void ReservedNames_ArePrefixedUnderEveryCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal("_CON.docx", ExportFileNameSanitizer.Sanitize("CON", "docx"));
            Assert.Equal("_con.pdf", ExportFileNameSanitizer.Sanitize("con", "pdf"));
            Assert.Equal("_COM1.docx", ExportFileNameSanitizer.Sanitize(" COM1 .", "docx"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void InvalidAndControlCharacters_AreReplaced_AndEmptyNamesFallBack()
    {
        Assert.Equal("A_B_C.docx", ExportFileNameSanitizer.Sanitize("A\"B<C", "docx"));
        Assert.Equal("A_B.docx", ExportFileNameSanitizer.Sanitize("A\u0001B", "docx"));
        Assert.Equal("Contract.docx", ExportFileNameSanitizer.Sanitize("  ...  ", "docx"));
        Assert.Equal("Contract.docx", ExportFileNameSanitizer.Sanitize(null, "docx"));
    }

    [Fact]
    public void LongNames_AreCappedWithoutTrailingDotOrSpace()
    {
        var name = ExportFileNameSanitizer.Sanitize(new string('a', 300) + " . ", "docx");

        Assert.Equal(ExportFileNameSanitizer.MaxStemLength + ".docx".Length, name.Length);
        Assert.EndsWith(".docx", name, StringComparison.Ordinal);
        Assert.DoesNotContain(" .", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Extensions_AreNormalizedInvariantly()
    {
        var extension = "DOCX";
        Assert.Equal("Contract.docx", ExportFileNameSanitizer.Sanitize("Contract", extension));

        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Equal("Contract.docx", ExportFileNameSanitizer.Sanitize("Contract", extension));
        }
        finally
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }
    }
}
