using System;
using DraftEngine.Core.Enums;

namespace DraftEngine.Core.Models;

/// <summary>
/// Represents a generated document (DOCX/PDF).
/// </summary>
public class GeneratedDocument
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public FileType FileType { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the contract the document was generated from.
    /// </summary>
    public Contract Contract { get; set; } = null!;
}
