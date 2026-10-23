using System;
using DraftEngine.Core.Enums;
using DraftEngine.Core.Models;

namespace DraftEngine.App.ViewModels;

/// <summary>
/// View model for one contract row in the dashboard's recent contracts list.
/// </summary>
public class ContractRowViewModel
{
    /// <summary>The underlying contract entity.</summary>
    public Contract Contract { get; }

    /// <summary>The contract title.</summary>
    public string Title => string.IsNullOrWhiteSpace(Contract.Title) ? "Untitled contract" : Contract.Title;

    /// <summary>The contract type (e.g. "Mutual NDA").</summary>
    public string ContractType => Contract.ContractType;

    /// <summary>The status text shown in the badge (Draft / Generated / Exported).</summary>
    public string StatusText => Contract.Status.ToString();

    /// <summary>Whether the status badge uses the generated (accent) style.</summary>
    public bool IsGenerated => Contract.Status == ContractStatus.Generated;

    /// <summary>Whether the status badge uses the exported (success) style.</summary>
    public bool IsExported => Contract.Status == ContractStatus.Exported;

    /// <summary>The last update date, formatted for display.</summary>
    public string UpdatedAtText => Contract.UpdatedAt.ToLocalTime().ToString("MMM d, yyyy 'at' HH:mm");

    /// <summary>Creates a dashboard contract row view model.</summary>
    public ContractRowViewModel(Contract contract)
    {
        Contract = contract;
    }
}
