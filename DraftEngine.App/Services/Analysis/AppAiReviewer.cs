using DraftEngine.Analysis;
using DraftEngine.App.Services.Settings;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Services.Analysis;

/// <summary>
/// Bridges the UI-agnostic analysis library to the existing AI client. Consent is checked
/// again here, party names are redacted with the existing service, and only the redacted
/// document is sent. Tokens are restored in the returned text before grounding.
/// </summary>
public sealed class AppAiReviewer : IAiReviewer
{
    private readonly IContractAiService _ai;
    private readonly IAiRedactionService _redaction;
    private readonly IAppSettingsService _settings;

    public AppAiReviewer(
        IContractAiService ai,
        IAiRedactionService redaction,
        IAppSettingsService settings)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<AiFindingCandidate>> ReviewAsync(
        AiReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.AiDataConsentAccepted)
        {
            throw new InvalidOperationException("AI review requires consent.");
        }

        var names = PartyNameDetector.Detect(request.DocumentText);
        _redaction.Configure(names);
        var redactedText = _redaction.Redact(request.DocumentText);

        var userPrompt = AiReviewPrompt.BuildUserPrompt(new AiReviewRequest
        {
            DocumentText = redactedText,
            ContractType = request.ContractType,
            PlaybookName = request.PlaybookName,
            RequiredCategories = request.RequiredCategories
        });

        var response = await _ai.CompletePromptAsync(AiReviewPrompt.SystemPrompt, userPrompt);
        var candidates = AiReviewPrompt.Parse(response);

        return candidates.Select(candidate => new AiFindingCandidate
        {
            Id = candidate.Id,
            Severity = candidate.Severity,
            Category = candidate.Category,
            Quote = _redaction.Restore(candidate.Quote),
            Explanation = _redaction.Restore(candidate.Explanation),
            SuggestedFix = _redaction.Restore(candidate.SuggestedFix)
        }).ToList();
    }
}
