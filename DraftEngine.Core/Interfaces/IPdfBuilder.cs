using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Builds the PDF version of a contract document.
/// </summary>
public interface IPdfBuilder
{
    Task<string> BuildPdfAsync(Contract contract, string fullContractText, string outputPath);
}
