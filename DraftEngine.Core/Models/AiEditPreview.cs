namespace DraftEngine.Core.Models;

/// <summary>
/// A proposed AI edit that has been validated but not yet applied. The caller can show a
/// diff and let the user accept or reject it; nothing is stored until it is applied.
/// </summary>
/// <param name="OriginalText">The contract text currently stored.</param>
/// <param name="UpdatedText">The validated replacement proposed by the AI.</param>
public sealed record AiEditPreview(string OriginalText, string UpdatedText);
