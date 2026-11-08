using DraftEngine.Core.Models.ContractTypes;
using DraftEngine.Core.Services;

namespace DraftEngine.Tests.Generation;

/// <summary>
/// Unit tests for the pure title/summary helper shared by the wizard and the generation
/// pipeline. These run without a database or WPF, so a regression in the review step is
/// caught long before the UI is launched.
/// </summary>
public class ContractContentBuilderTests
{
    public static IEnumerable<object[]> ContractTypes =>
        ContractTypeRegistry.All.Select(definition => new object[] { definition.Id });

    private static Dictionary<string, string> MutualValues() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Party1"] = "Acme Corp",
        ["Party2"] = "Globex Ltd",
        ["Purpose"] = "evaluating a partnership",
    };

    [Fact]
    public void BuildTitle_MutualNda_JoinsBothPartiesFromRegistryKeys()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId);

        var title = ContractContentBuilder.BuildTitle(definition, MutualValues());

        Assert.Equal("Mutual NDA \u2013 Acme Corp & Globex Ltd", title);
    }

    [Fact]
    public void BuildTitle_OneWayNda_UsesToBetweenDiscloserAndRecipient()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.OneWayNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            ["Party2"] = "Globex Ltd",
        };

        var title = ContractContentBuilder.BuildTitle(definition, values);

        Assert.Equal("One-Way NDA \u2013 Acme Corp to Globex Ltd", title);
    }

    [Fact]
    public void BuildTitle_Contractor_UsesContractorAndClientKeys()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.ContractorAgreementId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ContractorName"] = "Acme Corp",
            ["ClientName"] = "Globex Ltd",
        };

        var title = ContractContentBuilder.BuildTitle(definition, values);

        Assert.Equal("Independent Contractor Agreement \u2013 Acme Corp & Globex Ltd", title);
    }

    [Fact]
    public void BuildTitle_BlankOptionalParty_IsOmittedWithoutDanglingSeparator()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            ["Party2"] = "   ",
        };

        var title = ContractContentBuilder.BuildTitle(definition, values);

        Assert.Equal("Mutual NDA \u2013 Acme Corp", title);
    }

    [Fact]
    public void BuildTitle_NoParties_FallsBackToDisplayName()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.ConsultingAgreementId);

        var title = ContractContentBuilder.BuildTitle(definition, new Dictionary<string, string>());

        Assert.Equal("Consulting Agreement", title);
    }

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void BuildTitleAndSummary_AllTypes_UseRegistryFieldsAndOmitBlankOptionalFields(string contractTypeId)
    {
        var definition = ContractTypeRegistry.GetById(contractTypeId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.IntakeFields)
        {
            if (string.Equals(field.Key, definition.FirstPartyNameKey, StringComparison.OrdinalIgnoreCase))
            {
                values[field.Key] = "Acme Corp";
            }
            else if (string.Equals(field.Key, definition.SecondPartyNameKey, StringComparison.OrdinalIgnoreCase))
            {
                values[field.Key] = "Globex Ltd";
            }
            else
            {
                values[field.Key] = field.Required ? $"Value for {field.Key}" : string.Empty;
            }
        }

        var title = ContractContentBuilder.BuildTitle(definition, values);
        var summary = ContractContentBuilder.BuildSummary(definition, values);
        var requiredFields = definition.IntakeFields.Where(field => field.Required).ToArray();

        Assert.Contains(definition.DisplayName, title, StringComparison.Ordinal);
        Assert.Contains("Acme Corp", title, StringComparison.Ordinal);
        Assert.Contains("Globex Ltd", title, StringComparison.Ordinal);
        Assert.Equal(requiredFields.Length, summary.Count);
        Assert.Equal(requiredFields.Select(field => field.Key), summary.Select(row => row.Label));
        Assert.All(summary, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
    }

    [Fact]
    public void BuildSummary_UsesStableFieldKeysAndRegistryOrder()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.ContractorAgreementId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ContractorName"] = "Acme Corp",
            ["ContractorAddress"] = "1 Market Street",
            ["ClientName"] = "Globex Ltd",
            ["ClientAddress"] = "2 Market Street",
            ["ClientEmail"] = "legal@globex.example",
            ["EffectiveDate"] = "January 15, 2026",
            ["GoverningLaw"] = "Delaware",
            ["TermMonths"] = "24",
            ["Purpose"] = "software development",
        };

        var rows = ContractContentBuilder.BuildSummary(definition, values);

        Assert.Equal(definition.IntakeFields.Count, rows.Count);
        Assert.Equal(definition.IntakeFields.Select(field => field.Key), rows.Select(row => row.Label));
        Assert.Contains(rows, row => row.Label == "ContractorName" && row.Value == "Acme Corp");
        Assert.Contains(rows, row => row.Label == "ClientName" && row.Value == "Globex Ltd");
        Assert.Contains(rows, row => row.Label == "Purpose" && row.Value == "software development");
    }

    [Fact]
    public void BuildSummary_ReturnsFieldKeysRatherThanDisplayLabels()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            ["Party2"] = "Globex Ltd",
        };

        var summary = ContractContentBuilder.BuildSummary(definition, values);

        Assert.Equal(
            new[] { "Party1", "Party1Address", "Party2", "Party2Address", "EffectiveDate", "GoverningLaw", "TermMonths" },
            summary.Select(row => row.Label));
        Assert.DoesNotContain(summary, row => row.Label == "Party 1 name");
    }

    [Fact]
    public void BuildSummary_SkipsBlankOptionalFieldsButKeepsRequiredOnes()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.MutualNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            ["Party1Address"] = "1 Market Street",
            ["Party2"] = "Globex Ltd",
            ["Party2Address"] = "2 Market Street",
            ["EffectiveDate"] = "January 15, 2026",
            ["GoverningLaw"] = "Delaware",
            ["TermMonths"] = "24",
            // Purpose and ClientEmail are optional and intentionally left blank.
        };

        var rows = ContractContentBuilder.BuildSummary(definition, values);

        Assert.DoesNotContain(rows, row => row.Label == "Purpose");
        Assert.DoesNotContain(rows, row => row.Label == "ClientEmail");
        Assert.Contains(rows, row => row.Label == "Party1" && row.Value == "Acme Corp");
        Assert.Contains(rows, row => row.Label == "GoverningLaw" && row.Value == "Delaware");
    }

    [Fact]
    public void BuildSummary_MissingRequiredField_StillProducesAnEmptyRow()
    {
        var definition = ContractTypeRegistry.GetById(ContractTypeRegistry.OneWayNdaId);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Party1"] = "Acme Corp",
            // Party1Address is required but missing.
        };

        var rows = ContractContentBuilder.BuildSummary(definition, values);

        var addressRow = Assert.Single(rows, row => row.Label == "Party1Address");
        Assert.Equal(string.Empty, addressRow.Value);
    }
}
