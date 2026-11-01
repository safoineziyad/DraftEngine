using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Redacts party names and addresses from text before sending to AI, and restores
/// them after. Guarantees no real party data is sent to the AI provider when enabled.
/// </summary>
public interface IAiRedactionService
{
    string Redact(string text);
    string Restore(string text);
    bool IsEnabled { get; set; }
}
