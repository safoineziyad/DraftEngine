namespace DraftEngine.Core.Models;

/// <summary>
/// Stores free-form intake answers for a contract.
/// </summary>
public class ContractField
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the contract the field answers belong to.
    /// </summary>
    public Contract Contract { get; set; } = null!;
}
