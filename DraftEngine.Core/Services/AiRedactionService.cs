using DraftEngine.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DraftEngine.Core.Services;

/// <summary>
/// Replaces party names, addresses and party placeholder keys with stable opaque tokens
/// before text is sent to an AI provider, and restores the original values in the reply.
/// With <see cref="IsEnabled"/> off the text is returned unchanged, so every caller can
/// use the service unconditionally.
/// </summary>
public class AiRedactionService : IAiRedactionService
{
    // Party placeholders still present in raw templates (e.g. {{Party1}}).
    private static readonly Regex NameToken = new(
        @"\{\{(Party1|Party2|ContractorName|ConsultantName|ClientName|Party1Address|Party2Address|ClientAddress|ClientEmail)\}\}",
        RegexOptions.Compiled);

    // Stable tokens survive a round-trip through a language model far better than
    // free-form names, and never collide with contract placeholders.
    private const string ValueTokenPrefix = "[[PERSON_";
    private const string ValueTokenSuffix = "]]";
    private const string PlaceholderTokenPrefix = "{{__AI_REDACT_";
    private const string PlaceholderTokenSuffix = "__}}";

    private readonly Dictionary<string, string> _tokenToValue = new(StringComparer.Ordinal);
    private readonly List<string> _sensitiveValues = new();
    private int _counter;

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Registers the real values (party names, addresses, e-mail addresses) that must never
    /// leave the machine while redaction is enabled. Call this before every <see cref="Redact"/>.
    /// </summary>
    public void Configure(IEnumerable<string> sensitiveValues)
    {
        _sensitiveValues.Clear();
        _tokenToValue.Clear();
        _counter = 0;
        if (sensitiveValues is null)
        {
            return;
        }

        // Longest first so "Acme Holdings Inc." is replaced before "Acme".
        _sensitiveValues.AddRange(sensitiveValues
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 2)
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length));
    }

    /// <inheritdoc />
    public string Redact(string text)
    {
        if (!IsEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Replace configured real values first (party identities), then any leftover
        // template placeholders.
        var result = new StringBuilder(text);
        foreach (var value in _sensitiveValues)
        {
            if (!result.ToString().Contains(value, StringComparison.Ordinal))
            {
                continue;
            }

            var token = ValueTokenPrefix + _counter++ + ValueTokenSuffix;
            _tokenToValue[token] = value;
            result.Replace(value, token);
        }

        return NameToken.Replace(result.ToString(), match =>
        {
            var token = PlaceholderTokenPrefix + _counter++ + PlaceholderTokenSuffix;
            _tokenToValue[token] = match.Value;
            return token;
        });
    }

    /// <inheritdoc />
    public string Restore(string text)
    {
        if (!IsEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        // Longest tokens first keeps [[PERSON_1]] from matching [[PERSON_10]].
        foreach (var pair in _tokenToValue.OrderByDescending(pair => pair.Key.Length))
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return result;
    }
}
