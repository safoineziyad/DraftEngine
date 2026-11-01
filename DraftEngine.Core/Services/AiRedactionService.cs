using DraftEngine.Core.Interfaces;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DraftEngine.Core.Services;

/// <summary>
/// Replaces party names/addresses with stable tokens before sending to AI and
/// restores them after. No real party data leaves the machine when enabled.
/// </summary>
public class AiRedactionService : IAiRedactionService
{
    private static readonly Regex NameToken = new(@"\{\{(Party1|Party2|ContractorName|ConsultantName|ClientName)\}\}", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
    private int _counter;

    public bool IsEnabled { get; set; }

    public string Redact(string text)
    {
        if (!IsEnabled) return text;
        _map.Clear(); _counter = 0;
        return NameToken.Replace(text, m =>
        {
            var key = m.Groups[1].Value;
            var token = $"__AI_REDACT_{_counter++}__";
            _map[token] = key;
            return $"{{{{{token}}}}}";
        });
    }

    public string Restore(string text)
    {
        if (!IsEnabled) return text;
        var result = text;
        foreach (var pair in _map)
            result = result.Replace($"{{{{{pair.Key}}}}}", $"{{{{{pair.Value}}}}}");
        return result;
    }
}
