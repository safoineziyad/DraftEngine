using System;
using System.Globalization;
using DraftEngine.App.Services.Localization;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;
using DraftEngine.Core.Models.ContractTypes;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for one contract row in the dashboard's recent contracts list.
/// </summary>
public class ContractRowViewModel
{
    private readonly ILocalizationService _localization;

    /// <summary>The underlying contract entity.</summary>
    public Contract Contract { get; }

    /// <summary>The contract title.</summary>
    public string Title => string.IsNullOrWhiteSpace(Contract.Title)
        ? _localization.GetString("Contract.Untitled")
        : Contract.Title;

    /// <summary>The contract type (e.g. "Mutual NDA").</summary>
    public string ContractType => ContractTypeRegistry.GetByDisplayName(Contract.ContractType).DisplayName;

    /// <summary>The status text shown in the badge (Draft / Generated / Exported).</summary>
    public string StatusText => Contract.Status switch
    {
        ContractStatus.Generated => _localization.GetString("Status.Generated"),
        ContractStatus.Exported => _localization.GetString("Status.Exported"),
        _ => _localization.GetString("Status.Draft")
    };

    /// <summary>Whether the status badge uses the generated (accent) style.</summary>
    public bool IsGenerated => Contract.Status == ContractStatus.Generated;

    /// <summary>Whether the status badge uses the exported (success) style.</summary>
    public bool IsExported => Contract.Status == ContractStatus.Exported;

    /// <summary>The last update date, formatted for the active culture.</summary>
    public string UpdatedAtText => Contract.UpdatedAt.ToLocalTime().ToString(
        _localization.GetString("Common.DateTimeFormat"),
        CultureInfo.CurrentCulture);

    /// <summary>Accessible name for the row's open action.</summary>
    public string OpenAutomationName =>
        _localization.GetString("Dashboard.OpenContractAutomation", Title);

    /// <summary>Creates a dashboard contract row view model.</summary>
    public ContractRowViewModel(Contract contract, ILocalizationService? localization = null)
    {
        Contract = contract;
        _localization = localization ?? LocalizationSource.Current.Service ?? new LocalizationService();
    }
}
