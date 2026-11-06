using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.ViewModels.NewContract;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;
using DraftEngine.Core.Services;
using Serilog;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// Step 4 (review &amp; generate) logic for the New Contract wizard.
/// </summary>
public partial class NewContractViewModel
{
    /// <summary>Builds the step 4 summary rows from the registry intake fields and current values.</summary>
    private void BuildSummary()
    {
        SummaryItems.Clear();
        var definition = SelectedDefinition;
        SummaryItems.Add(new SummaryItemViewModel("Contract type", definition.DisplayName));

        foreach (var row in ContractContentBuilder.BuildSummary(definition, GetFieldValues()))
        {
            SummaryItems.Add(new SummaryItemViewModel(row.Label, row.Value));
        }
    }

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
            var definition = SelectedDefinition;
            var contract = new Contract
            {
                UserId = CurrentUserId,
                Title = ContractContentBuilder.BuildTitle(definition, values),
                ContractType = definition.Id,
                ClientName = FirstNonEmpty(values, "ClientName", definition.SecondPartyNameKey),
                ClientEmail = FirstNonEmpty(values, "ClientEmail"),
                PartiesJson = BuildPartiesJson(definition, values), // array of {"name","address"}
                EffectiveDate = IntakeFields
                    .FirstOrDefault(field => field.Kind == IntakeFieldKind.Date)?.DateValue,
                GoverningLaw = FirstNonEmpty(values, "GoverningLaw"),
                TermMonths = int.TryParse(FirstNonEmpty(values, "TermMonths"), out var months) ? months : 12
            };

            var created = await _contractService.CreateAsync(contract);
            createdId = created.Id;
            await _fieldService.MapFieldsAsync(created.Id, values);

            var selectedClauseIds = AllClauseOptions()
                .Where(option => option.IsSelected)
                .Select(option => option.Id)
                .ToList();
            await _contractService.GenerateContractAsync(created.Id, selectedClauseIds, useAiPolish: UseAiPolish);

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
    private static string BuildPartiesJson(
        ContractTypeDefinition definition,
        IReadOnlyDictionary<string, string> values)
    {
        var parties = new List<Dictionary<string, string>>();

        AddParty(
            parties,
            values,
            definition.FirstPartyNameKey,
            definition.FirstPartyAddressKey,
            "Party1",
            "Party1Address");
        AddParty(
            parties,
            values,
            definition.SecondPartyNameKey,
            definition.SecondPartyAddressKey,
            "Party2",
            "Party2Address");

        return JsonSerializer.Serialize(parties);
    }

    private static void AddParty(
        List<Dictionary<string, string>> parties,
        IReadOnlyDictionary<string, string> values,
        string nameKey,
        string addressKey,
        string fallbackNameKey,
        string fallbackAddressKey)
    {
        var name = FirstNonEmpty(values, nameKey, fallbackNameKey);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        parties.Add(new Dictionary<string, string>
        {
            ["name"] = name,
            ["address"] = FirstNonEmpty(values, addressKey, fallbackAddressKey)
        });
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return string.Empty;
    }
}
