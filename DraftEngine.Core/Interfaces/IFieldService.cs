using System.Collections.Generic;
using System.Threading.Tasks;

namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Service for mapping intake form fields to placeholder values.
/// </summary>
public interface IFieldService
{
    /// <summary>
    /// Persists the given form values as contract fields (upsert by key) and returns the
    /// merged dictionary of every field stored for the contract.
    /// </summary>
    Task<Dictionary<string, string>> MapFieldsAsync(int contractId, Dictionary<string, string> formValues);

    /// <summary>
    /// Replaces every {{Key}} placeholder in <paramref name="template"/> with its value.
    /// Unknown placeholders are left untouched; supported keys with no value fall back to
    /// a sensible default so no raw {{...}} remains in the output.
    /// </summary>
    string ReplacePlaceholders(string template, Dictionary<string, string> fieldValues);
}
