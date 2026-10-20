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
            Log.Error(ex, "Contract generation failed.");
            _dialogs.ShowError(
                "The contract could not be generated. Please review your entries and try again.\n\n" +
                ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGenerate() => !IsBusy && SelectedClauseCount > 0;

    private string BuildTitle(IReadOnlyDictionary<string, string> values)
    {
        var party1 = values.GetValueOrDefault("Party1", string.Empty);
        var party2 = values.GetValueOrDefault("Party2", string.Empty);
        var parties = string.IsNullOrWhiteSpace(party1) && string.IsNullOrWhiteSpace(party2)
            ? string.Empty
            : $" \u2013 {party1} & {party2}".TrimEnd();
        return $"{MutualNdaType}{parties}";
    }

    private static string BuildPartiesJson(IReadOnlyDictionary<string, string> values) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["party1"] = values.GetValueOrDefault("Party1", string.Empty),
            ["party1Address"] = values.GetValueOrDefault("Party1Address", string.Empty),
            ["party2"] = values.GetValueOrDefault("Party2", string.Empty),
            ["party2Address"] = values.GetValueOrDefault("Party2Address", string.Empty),
            ["purpose"] = values.GetValueOrDefault("Purpose", string.Empty)
        });
}
