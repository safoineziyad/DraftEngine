using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Service for building and exporting documents in every supported format.
/// Combines <see cref="IDocxBuilder"/> and <see cref="IPdfBuilder"/> so callers can
/// export either format through a single service; register the composite
/// <c>DocumentBuilder</c> implementation in DI.
/// </summary>
public interface IDocumentBuilder : IDocxBuilder, IPdfBuilder
{
}
