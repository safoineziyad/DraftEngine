using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Models;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Step 4 (review &amp; generate) logic for the New Contract wizard.
/// </summary>
public partial class NewContractViewModel
{
    /// <summary>Builds the step 4 summary rows from the current intake values.</summary>
    private void BuildSummary()
    {
        SummaryItems.Clear();
        SummaryItems.Add(new SummaryItemViewModel(
            "Contract type", SelectedContractType?.Name ?? MutualNdaType));
        AddSummaryRow("Party 1", "Party1");
        AddSummaryRow("Party 1 address", "Party1Address");
        AddSummaryRow("Party 2", "Party2");
        AddSummaryRow("Party 2 address", "Party2Address");
        AddSummaryRow("Client", "ClientName");
        AddSummaryRow("Client email", "ClientEmail");
        AddSummaryRow("Effective date", "EffectiveDate");
        AddSummaryRow("Governing law", "GoverningLaw");
        AddSummaryRow("Term (months)", "TermMonths");
        AddSummaryRow("Purpose of disclosure", "Purpose");
    }

    private void AddSummaryRow(string label, string fieldKey) =>
        SummaryItems.Add(new SummaryItemViewModel(label, FieldValue(fieldKey)));

    /// <summary>Creates the contract, saves the intake fields, generates the document text and opens the detail page.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        int createdId = 0;
        try
        {
            var values = GetFieldValues();
            var contract = new Contract
            {
                UserId = CurrentUserId,
                Title = BuildTitle(values),
                ContractType = SelectedContractType?.Name ?? MutualNdaType,
                ClientName = values.GetValueOrDefault("ClientName", string.Empty),
                ClientEmail = values.GetValueOrDefault("ClientEmail", string.Empty),
                PartiesJson = BuildPartiesJson(values),
                EffectiveDate = IntakeFields
                    .FirstOrDefault(field => field.Key == "EffectiveDate")?.DateValue,
                GoverningLaw = values.GetValueOrDefault("GoverningLaw", string.Empty),
                TermMonths = int.TryParse(values.GetValueOrDefault("TermMonths"), out var months) ? months : 12
            };

            var created = await _contractService.CreateAsync(contract);
            createdId = created.Id;
            await _fieldService.MapFieldsAsync(created.Id, values);

            var selectedClauseIds = AllClauseOptions()
                .Where(option => option.IsSelected)
                .Select(option => option.Id)
                .ToList();
            await _contractService.GenerateContractAsync(created.Id, selectedClauseIds);

            _navigation.Navigate("contract-detail", created.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Contract generation failed for contract {ContractId}", createdId);
            _dialogs.ShowError(
                "Contract generation failed. Please try again. If the problem persists, check your API key in Settings.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGenerate() => !IsBusy && SelectedClauseCount > 0;

    private string BuildTitle(IReadOnlyDictionary<string, string> values)
    {
        var party1 = values.GetValueOrDefault("Party1", string.Empty)?.Trim();
        var party2 = values.GetValueOrDefault("Party2", string.Empty)?.Trim();

        // Join only the parties that were actually entered: a single party must not
        // leave a dangling " & " in the title ("Mutual NDA – Acme &").
        var parties = string.Join(
            " & ",
            new[] { party1, party2 }.Where(name => !string.IsNullOrWhiteSpace(name)));

        return string.IsNullOrWhiteSpace(parties)
            ? MutualNdaType
            : $"{MutualNdaType} \u2013 {parties}";
    }

    /// <summary>
    /// Builds the <c>PartiesJson</c> payload stored on the contract.
    /// </summary>
    /// <remarks>
    /// The payload must be a JSON <em>array</em> of <c>{name, address}</c> objects:
    /// <c>ContractService.AddPartyValues</c> and <c>ContractParties.GetNames</c> (used by
    /// the DOCX/PDF builders) only understand that shape. Storing a JSON object instead
    /// made the exported document print the raw JSON on the "Parties" line and left the
    /// signature block showing "Counterparty" instead of the counterparty's name.
    /// <para>
    /// Parties without a name are omitted; the "Purpose" answer is persisted
    /// separately as a <c>ContractField</c> row and is not part of the parties payload.
    /// </para>
    /// </remarks>
    private static string BuildPartiesJson(IReadOnlyDictionary<string, string> values)
    {
        var parties = new List<Dictionary<string, string>>();

        var party1 = values.GetValueOrDefault("Party1", string.Empty);
        if (!string.IsNullOrWhiteSpace(party1))
        {
            parties.Add(new Dictionary<string, string>
            {
                ["name"] = party1,
                ["address"] = values.GetValueOrDefault("Party1Address", string.Empty)
            });
        }

        var party2 = values.GetValueOrDefault("Party2", string.Empty);
        if (!string.IsNullOrWhiteSpace(party2))
        {
            parties.Add(new Dictionary<string, string>
            {
                ["name"] = party2,
                ["address"] = values.GetValueOrDefault("Party2Address", string.Empty)
            });
        }

        return JsonSerializer.Serialize(parties);
    }
}
