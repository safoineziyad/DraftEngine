using System.Collections.Generic;
using DraftEngine.Core.Models.ContractTypes;

namespace DraftEngine.Core.Interfaces;

/// <summary>Looks up the canonical definitions of supported contract types.</summary>
public interface IContractTypeRegistry
{
    IReadOnlyList<ContractTypeDefinition> All { get; }

    ContractTypeDefinition GetById(string id);

    ContractTypeDefinition GetByDisplayName(string displayName);

    ContractTypeDefinition? TryGetByDisplayName(string? displayName);

    string NormalizeDisplayName(string displayName);
}
