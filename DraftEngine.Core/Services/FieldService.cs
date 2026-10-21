using System.Globalization;
using System.Text.RegularExpressions;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DraftEngine.Core.Services;

/// <summary>
/// Maps intake form values onto stored <see cref="Models.ContractField"/> rows and performs
/// deterministic <c>{{Placeholder}}</c> substitution in clause templates. Placeholder replacement
/// is synchronous and free of side effects, so it is trivially unit-testable; only
/// <see cref="MapFieldsAsync"/> touches the database.
/// </summary>
public class FieldService : IFieldService
{
    // Matches {{Placeholder}} keys, allowing word characters plus hyphens and dots
    // (e.g. {{Party-1.Name}}) — audit Finding #17: the previous \w-only pattern left
    // hyphenated or dotted keys literal even when a matching value existed.
    private static readonly Regex PlaceholderPattern = new(@"\{\{([\w\-.]+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Defaults for the supported placeholder keys, so a contract never contains a raw {{...}}.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SupportedKeyDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Party1"] = "the Disclosing Party",
            ["Party2"] = "the Receiving Party",
            ["Party1Address"] = "N/A",
            ["Party2Address"] = "N/A",
            ["ClientName"] = "the Client",
            ["ClientEmail"] = "N/A",
            ["EffectiveDate"] = "the date of this Agreement",
            ["GoverningLaw"] = "the applicable jurisdiction",
            ["TermMonths"] = "12",
            ["Purpose"] = "the purpose of evaluating a potential business relationship",
            ["ConfidentialInformation"] = "non-public information disclosed by either party under this Agreement",
        };

    private readonly IDatabaseContextFactory? _databaseFactory;
    private readonly ILogger<FieldService>? _logger;

    /// <summary>Creates the field service bound to the application database.</summary>
    /// <param name="databaseFactory">Factory handing out one short-lived context per operation.</param>
    /// <param name="logger">Optional logger used to report unmapped placeholders.</param>
    public FieldService(IDatabaseContextFactory databaseFactory, ILogger<FieldService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates a database-less field service. Only <see cref="ReplacePlaceholders"/> works;
    /// <see cref="MapFieldsAsync"/> throws a friendly error. Useful for unit tests.
    /// </summary>
    public FieldService()
    {
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> MapFieldsAsync(int contractId, Dictionary<string, string> formValues)
    {
        if (_databaseFactory is null)
        {
            throw new InvalidOperationException("The database is not available in this field service instance.");
        }

        using var database = _databaseFactory.CreateContext();

        var stored = await database.Set<ContractField>()
            .Where(field => field.ContractId == contractId)
            .ToListAsync();

        if (formValues is not null)
        {
            foreach (var pair in formValues)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var key = pair.Key.Trim();
                var value = pair.Value ?? string.Empty;
                var row = stored.FirstOrDefault(field =>
                    string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

                if (row is null)
                {
                    row = new ContractField { ContractId = contractId, Key = key, Value = value };
                    database.Set<ContractField>().Add(row);
                    stored.Add(row);
                }
                else
                {
                    row.Value = value;
                }
            }

            await database.SaveChangesAsync();
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in stored)
        {
            if (!string.IsNullOrWhiteSpace(row.Key))
            {
                merged[row.Key.Trim()] = row.Value ?? string.Empty;
            }
        }

        return merged;
    }

    /// <inheritdoc />
    public string ReplacePlaceholders(string template, Dictionary<string, string> fieldValues)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        if (template.IndexOf("{{", StringComparison.Ordinal) < 0)
        {
            return template;
        }

        var unknownKeys = new List<string>();
        var replaced = PlaceholderPattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;

            if (TryResolveValue(fieldValues, key, out var value))
            {
                return string.Equals(key, "EffectiveDate", StringComparison.Ordinal)
                    ? FormatEffectiveDate(value)
                    : value;
            }

            if (SupportedKeyDefaults.TryGetValue(key, out var fallback))
            {
                return string.Equals(key, "EffectiveDate", StringComparison.Ordinal)
                    ? FormatEffectiveDate(fallback)
                    : fallback;
            }

            if (!unknownKeys.Contains(key))
            {
                unknownKeys.Add(key);
            }

            return match.Value; // Unknown placeholder: leave it untouched.
        });

        if (unknownKeys.Count > 0)
        {
            _logger?.LogWarning(
                "Unmapped placeholders left untouched in template: {Placeholders}",
                string.Join(", ", unknownKeys));
        }

        return replaced;
    }

    /// <summary>Looks a key up exactly first, then case-insensitively; whitespace values count as missing.</summary>
    private static bool TryResolveValue(
        Dictionary<string, string>? fieldValues,
        string key,
        out string value)
    {
        value = string.Empty;
        if (fieldValues is null)
        {
            return false;
        }

        if (fieldValues.TryGetValue(key, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            value = exact.Trim();
            return true;
        }

        foreach (var pair in fieldValues)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value.Trim();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Formats a date value as a long-form date ("October 20, 2026"). Values that do not parse
    /// as a date (e.g. already long-form text) pass through unchanged.
    /// </summary>
    private static string FormatEffectiveDate(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
        {
            return date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        }

        return value;
    }
}
