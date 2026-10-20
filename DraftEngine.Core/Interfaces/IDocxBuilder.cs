using System.Threading.Tasks;
using DraftEngine.Core.Models;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Builds the DOCX version of a contract document.
/// </summary>
public interface IDocxBuilder
{
    Task<string> BuildDocxAsync(Contract contract, string fullContractText, string outputPath);
}
