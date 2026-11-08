using System;
using System.Collections.Generic;
using System.Linq;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Models.ContractTypes;

public sealed class ContractTypeRegistry : IContractTypeRegistry
{
    public const string MutualNdaId = "MutualNda";
    public const string OneWayNdaId = "OneWayNda";
    public const string ContractorAgreementId = "IndependentContractorAgreement";
    public const string ConsultingAgreementId = "ConsultingAgreement";

    public static IContractTypeRegistry Instance { get; } = new ContractTypeRegistry();

    private static readonly List<ContractTypeDefinition> Definitions = new()
    {
        new()
        {
            Id = MutualNdaId,
            DisplayName = "Mutual NDA",
            Description = "Mutual confidentiality agreement protecting both parties.",
            PartyRoleLabels = new[] { "Party 1", "Party 2" },
            SignatureFirstPartyLabel = "Party 1",
            SignatureSecondPartyLabel = "Party 2",
            FirstPartyNameKey = "Party1",
            FirstPartyAddressKey = "Party1Address",
            SecondPartyNameKey = "Party2",
            SecondPartyAddressKey = "Party2Address",
            ClauseLookupName = "Mutual NDA",
            DefaultClauseIds = Enumerable.Range(1, 12).ToArray(),
            PreambleTemplate = "This Mutual Non-Disclosure Agreement (the \"Agreement\") is entered into by and between {{Party1}} and {{Party2}} (collectively the \"Parties\") for the purpose of {{Purpose}}.",
            SignatureBlockTemplate = "IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.\n\n___________________________\n{{Party1}}\n\n___________________________\n{{Party2}}",
            SupportedPlaceholderKeys = new()
            {
                "Party1", "Party1Address", "Party2", "Party2Address", "ClientName", "ClientEmail",
                "EffectiveDate", "GoverningLaw", "TermMonths", "Purpose", "ConfidentialInformation"
            },
            IntakeFields = new()
            {
                new() { Key = "Party1", Label = "Party 1 name", Kind = "Text", Required = true },
                new() { Key = "Party1Address", Label = "Party 1 address", Kind = "Text", Required = true },
                new() { Key = "Party2", Label = "Party 2 name", Kind = "Text", Required = true },
                new() { Key = "Party2Address", Label = "Party 2 address", Kind = "Text", Required = true },
                new() { Key = "EffectiveDate", Label = "Effective date", Kind = "Date", Required = true },
                new() { Key = "GoverningLaw", Label = "Governing law", Kind = "Choice", Required = true, DefaultValueKey = "DefaultGoverningLaw" },
                new() { Key = "TermMonths", Label = "Term (months)", Kind = "Number", Required = true, DefaultValueKey = "DefaultTermMonths" },
                new() { Key = "Purpose", Label = "Purpose of disclosure", Kind = "Text", Required = false },
                new() { Key = "ClientEmail", Label = "Client email", Kind = "Text", Required = false }
            }
        },
        new()
        {
            Id = OneWayNdaId,
            DisplayName = "One-Way NDA",
            Description = "One-way confidentiality agreement protecting information disclosed by a single party.",
            PartyRoleLabels = new[] { "Disclosing Party", "Receiving Party" },
            SignatureFirstPartyLabel = "Disclosing Party",
            SignatureSecondPartyLabel = "Receiving Party",
            FirstPartyNameKey = "Party1",
            FirstPartyAddressKey = "Party1Address",
            SecondPartyNameKey = "Party2",
            SecondPartyAddressKey = "Party2Address",
            ClauseLookupName = "Non-Disclosure Agreement (One-Way)",
            DefaultClauseIds = Enumerable.Range(13, 10).ToArray(),
            PreambleTemplate = "This Non-Disclosure Agreement (the \"Agreement\") is entered into by and between {{Party1}} (the \"Disclosing Party\") and {{Party2}} (the \"Receiving Party\") for the purpose of {{Purpose}}.",
            SignatureBlockTemplate = "IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.\n\n___________________________\n{{Party1}} (Disclosing Party)\n\n___________________________\n{{Party2}} (Receiving Party)",
            SupportedPlaceholderKeys = new()
            {
                "Party1", "Party1Address", "Party2", "Party2Address", "ClientName", "ClientEmail",
                "EffectiveDate", "GoverningLaw", "TermMonths", "Purpose", "ConfidentialInformation"
            },
            IntakeFields = new()
            {
                new() { Key = "Party1", Label = "Disclosing party name", Kind = "Text", Required = true },
                new() { Key = "Party1Address", Label = "Disclosing party address", Kind = "Text", Required = true },
                new() { Key = "Party2", Label = "Receiving party name", Kind = "Text", Required = true },
                new() { Key = "Party2Address", Label = "Receiving party address", Kind = "Text", Required = true },
                new() { Key = "EffectiveDate", Label = "Effective date", Kind = "Date", Required = true },
                new() { Key = "GoverningLaw", Label = "Governing law", Kind = "Choice", Required = true, DefaultValueKey = "DefaultGoverningLaw" },
                new() { Key = "TermMonths", Label = "Term (months)", Kind = "Number", Required = true, DefaultValueKey = "DefaultTermMonths" },
                new() { Key = "Purpose", Label = "Purpose of disclosure", Kind = "Text", Required = false },
                new() { Key = "ClientEmail", Label = "Client email", Kind = "Text", Required = false }
            }
        },
        new()
        {
            Id = ContractorAgreementId,
            DisplayName = "Independent Contractor Agreement",
            Description = "Defines scope, fees, deliverables and ownership for contract work.",
            PartyRoleLabels = new[] { "Contractor", "Client" },
            SignatureFirstPartyLabel = "Contractor",
            SignatureSecondPartyLabel = "Client",
            FirstPartyNameKey = "ContractorName",
            FirstPartyAddressKey = "ContractorAddress",
            SecondPartyNameKey = "ClientName",
            SecondPartyAddressKey = "ClientAddress",
            ClauseLookupName = "Independent Contractor Agreement",
            DefaultClauseIds = Enumerable.Range(23, 10).ToArray(),
            PreambleTemplate = "This Independent Contractor Agreement (the \"Agreement\") is entered into by and between {{ContractorName}} (\"Contractor\") and {{ClientName}} (\"Client\") for the provision of services described herein.",
            SignatureBlockTemplate = "IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.\n\n___________________________\n{{ContractorName}} (Contractor)\n\n___________________________\n{{ClientName}} (Client)",
            SupportedPlaceholderKeys = new()
            {
                "ContractorName", "ContractorAddress", "ClientName", "ClientAddress", "ClientEmail",
                "Party1", "Party1Address", "Party2", "Party2Address",
                "EffectiveDate", "GoverningLaw", "TermMonths", "Purpose", "ConfidentialInformation"
            },
            IntakeFields = new()
            {
                new() { Key = "ContractorName", Label = "Contractor name", Kind = "Text", Required = true },
                new() { Key = "ContractorAddress", Label = "Contractor address", Kind = "Text", Required = true },
                new() { Key = "ClientName", Label = "Client name", Kind = "Text", Required = true },
                new() { Key = "ClientAddress", Label = "Client address", Kind = "Text", Required = true },
                new() { Key = "ClientEmail", Label = "Client email", Kind = "Text", Required = false },
                new() { Key = "EffectiveDate", Label = "Effective date", Kind = "Date", Required = true },
                new() { Key = "GoverningLaw", Label = "Governing law", Kind = "Choice", Required = true, DefaultValueKey = "DefaultGoverningLaw" },
                new() { Key = "TermMonths", Label = "Term (months)", Kind = "Number", Required = true, DefaultValueKey = "DefaultTermMonths" },
                new() { Key = "Purpose", Label = "Scope of work", Kind = "Text", Required = false }
            }
        },
        new()
        {
            Id = ConsultingAgreementId,
            DisplayName = "Consulting Agreement",
            Description = "Sets the terms of an ongoing advisory engagement.",
            PartyRoleLabels = new[] { "Consultant", "Client" },
            SignatureFirstPartyLabel = "Consultant",
            SignatureSecondPartyLabel = "Client",
            FirstPartyNameKey = "ConsultantName",
            FirstPartyAddressKey = "ConsultantAddress",
            SecondPartyNameKey = "ClientName",
            SecondPartyAddressKey = "ClientAddress",
            ClauseLookupName = "Consulting Agreement",
            DefaultClauseIds = new[] { 33, 34, 35, 36, 37, 38, 40, 41, 42 },
            PreambleTemplate = "This Consulting Agreement (the \"Agreement\") is entered into by and between {{ConsultantName}} (\"Consultant\") and {{ClientName}} (\"Client\") for the provision of consulting services.",
            SignatureBlockTemplate = "IN WITNESS WHEREOF, the Parties have executed this Agreement as of the Effective Date.\n\n___________________________\n{{ConsultantName}} (Consultant)\n\n___________________________\n{{ClientName}} (Client)",
            SupportedPlaceholderKeys = new()
            {
                "ConsultantName", "ConsultantAddress", "ClientName", "ClientAddress", "ClientEmail",
                "Party1", "Party1Address", "Party2", "Party2Address",
                "EffectiveDate", "GoverningLaw", "TermMonths", "Purpose", "ConfidentialInformation"
            },
            IntakeFields = new()
            {
                new() { Key = "ConsultantName", Label = "Consultant name", Kind = "Text", Required = true },
                new() { Key = "ConsultantAddress", Label = "Consultant address", Kind = "Text", Required = true },
                new() { Key = "ClientName", Label = "Client name", Kind = "Text", Required = true },
                new() { Key = "ClientAddress", Label = "Client address", Kind = "Text", Required = true },
                new() { Key = "ClientEmail", Label = "Client email", Kind = "Text", Required = false },
                new() { Key = "EffectiveDate", Label = "Effective date", Kind = "Date", Required = true },
                new() { Key = "GoverningLaw", Label = "Governing law", Kind = "Choice", Required = true, DefaultValueKey = "DefaultGoverningLaw" },
                new() { Key = "TermMonths", Label = "Term (months)", Kind = "Number", Required = true, DefaultValueKey = "DefaultTermMonths" },
                new() { Key = "Purpose", Label = "Scope of services", Kind = "Text", Required = false }
            }
        }
    };

    IReadOnlyList<ContractTypeDefinition> IContractTypeRegistry.All => Definitions;

    ContractTypeDefinition IContractTypeRegistry.GetById(string id) => GetById(id);

    ContractTypeDefinition IContractTypeRegistry.GetByDisplayName(string displayName) =>
        GetByDisplayName(displayName);

    ContractTypeDefinition? IContractTypeRegistry.TryGetByDisplayName(string? displayName) =>
        TryGetByDisplayName(displayName);

    string IContractTypeRegistry.NormalizeDisplayName(string displayName) =>
        NormalizeDisplayName(displayName);

    public static IReadOnlyList<ContractTypeDefinition> All => Definitions;

    public static ContractTypeDefinition GetById(string id) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Definitions.First(d => d.Id == MutualNdaId);

    public static ContractTypeDefinition GetByDisplayName(string displayName) =>
        TryGetByDisplayName(displayName) ?? GetById(MutualNdaId);

    /// <summary>
    /// Resolves a type by its display name, aliases or canonical id, returning null when the
    /// name is not a known contract type (unlike <see cref="GetByDisplayName"/>, which falls
    /// back to Mutual NDA for unknown types).
    /// </summary>
    public static ContractTypeDefinition? TryGetByDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        // Handle naming conflicts and legacy aliases.
        if (string.Equals(displayName, "Non-Disclosure Agreement (One-Way)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(displayName, "One-Way NDA", StringComparison.OrdinalIgnoreCase))
            return GetById(OneWayNdaId);
        if (string.Equals(displayName, "Independent Contractor Agreement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(displayName, "Contractor Agreement", StringComparison.OrdinalIgnoreCase))
            return GetById(ContractorAgreementId);
        if (string.Equals(displayName, "Consulting Agreement", StringComparison.OrdinalIgnoreCase))
            return GetById(ConsultingAgreementId);
        if (string.Equals(displayName, "Mutual NDA", StringComparison.OrdinalIgnoreCase))
            return GetById(MutualNdaId);

        return Definitions.FirstOrDefault(d =>
            string.Equals(d.DisplayName, displayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Id, displayName, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeDisplayName(string displayName) => GetByDisplayName(displayName).DisplayName;
}
