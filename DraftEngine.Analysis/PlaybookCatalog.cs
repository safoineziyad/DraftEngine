namespace DraftEngine.Analysis;

/// <summary>
/// Built-in playbooks for the four supported contract types. Rules are deterministic and
/// editable: required clause categories, red-flag regexes and required facts.
/// </summary>
public static class PlaybookCatalog
{
    public const string MutualNda = "MutualNda";
    public const string OneWayNda = "OneWayNda";
    public const string IndependentContractorAgreement = "IndependentContractorAgreement";
    public const string ConsultingAgreement = "ConsultingAgreement";

    public static IReadOnlyList<Playbook> All { get; } =
    [
        new Playbook
        {
            ContractType = MutualNda,
            DisplayName = "Mutual NDA",
            Rules =
            [
                Required("nda.confidentiality", "Confidentiality", FindingSeverity.Critical,
                    "The document should define confidentiality obligations.", "Confidentiality",
                    "confidential", "non-disclosure", "nondisclosure"),
                Required("nda.term", "Term", FindingSeverity.High,
                    "The confidentiality period should be stated.", "Term",
                    "term", "duration", "expiration"),
                Required("nda.governing-law", "Governing Law", FindingSeverity.High,
                    "The agreement should choose a governing law.", "Governing Law",
                    "governing law", "choice of law", "jurisdiction"),
                Required("nda.return", "Return of Materials", FindingSeverity.Medium,
                    "Confidential materials should be returned or destroyed on request.", "Return of Materials",
                    "return", "destroy"),
                Required("nda.remedies", "Remedies", FindingSeverity.Medium,
                    "The agreement should acknowledge equitable relief for breach.", "Remedies",
                    "injunction", "equitable relief", "specific performance"),
                RequiredFact("nda.fact.governing-law", "Governing Law", FindingSeverity.High,
                    "State which law governs the agreement.", "governing law"),
                RedFlag("nda.flag.unlimited-liability", "Liability", FindingSeverity.High,
                    "Unlimited liability is unusually risky.", @"\bunlimited liability\b|\bwithout (?:any )?limitation of liability\b"),
                RedFlag("nda.flag.perpetual", "Term", FindingSeverity.Medium,
                    "A perpetual or indefinite confidentiality duty may be unintended.", @"\bperpetual(?:ly)?\b|\bin perpetuity\b|\bindefinitely\b"),
                RedFlag("nda.flag.one-sided-indemnity", "Indemnification", FindingSeverity.High,
                    "Indemnification appears to run only one way.", @"\bindemnif\w+[^.\n]{0,160}\b(?:solely|only|exclusively)\b")
            ]
        },
        new Playbook
        {
            ContractType = OneWayNda,
            DisplayName = "One-Way NDA",
            Rules =
            [
                Required("oneway.confidentiality", "Confidentiality", FindingSeverity.Critical,
                    "The document should define the recipient's confidentiality obligations.", "Confidentiality",
                    "confidential", "non-disclosure", "nondisclosure"),
                Required("oneway.obligations", "Obligations", FindingSeverity.High,
                    "The recipient's standard of care should be stated.", "Obligations",
                    "obligation", "shall protect", "reasonable care"),
                Required("oneway.term", "Term", FindingSeverity.High,
                    "The confidentiality period should be stated.", "Term",
                    "term", "duration", "expiration"),
                Required("oneway.governing-law", "Governing Law", FindingSeverity.High,
                    "The agreement should choose a governing law.", "Governing Law",
                    "governing law", "choice of law", "jurisdiction"),
                Required("oneway.return", "Return of Materials", FindingSeverity.Medium,
                    "Confidential materials should be returned or destroyed.", "Return of Materials",
                    "return", "destroy"),
                RequiredFact("oneway.fact.governing-law", "Governing Law", FindingSeverity.High,
                    "State which law governs the agreement.", "governing law"),
                RedFlag("oneway.flag.unlimited-liability", "Liability", FindingSeverity.High,
                    "Unlimited liability is unusually risky.", @"\bunlimited liability\b|\bwithout (?:any )?limitation of liability\b")
            ]
        },
        new Playbook
        {
            ContractType = IndependentContractorAgreement,
            DisplayName = "Independent Contractor Agreement",
            Rules =
            [
                Required("ic.engagement", "Engagement", FindingSeverity.Critical,
                    "The services and engagement should be described.", "Engagement",
                    "engagement", "services", "scope of work", "contractor"),
                Required("ic.compensation", "Compensation", FindingSeverity.Critical,
                    "The contractor's compensation and payment terms should be stated.", "Compensation",
                    "compensation", "payment", "fees", "invoice"),
                Required("ic.term", "Term", FindingSeverity.High,
                    "The engagement term and termination should be stated.", "Term",
                    "term", "duration", "termination"),
                Required("ic.ip", "Intellectual Property", FindingSeverity.High,
                    "Ownership of work product should be allocated.", "Intellectual Property",
                    "intellectual property", "work product", "ownership", "copyright"),
                Required("ic.confidentiality", "Confidentiality", FindingSeverity.High,
                    "Confidential information handling should be addressed.", "Confidentiality",
                    "confidential", "non-disclosure"),
                Required("ic.governing-law", "Governing Law", FindingSeverity.High,
                    "The agreement should choose a governing law.", "Governing Law",
                    "governing law", "choice of law", "jurisdiction"),
                Required("ic.liability", "Liability", FindingSeverity.Medium,
                    "Liability should be allocated or capped.", "Liability",
                    "liability", "limitation of liability"),
                RequiredFact("ic.fact.governing-law", "Governing Law", FindingSeverity.High,
                    "State which law governs the agreement.", "governing law"),
                RedFlag("ic.flag.unlimited-liability", "Liability", FindingSeverity.High,
                    "Unlimited liability is unusually risky.", @"\bunlimited liability\b|\bwithout (?:any )?limitation of liability\b"),
                RedFlag("ic.flag.unilateral-change", "Miscellaneous", FindingSeverity.Medium,
                    "One party can change the agreement unilaterally.", @"\b(?:company|provider|client)\s+may\s+(?:unilaterally\s+)?(?:amend|modify|change)\b")
            ]
        },
        new Playbook
        {
            ContractType = ConsultingAgreement,
            DisplayName = "Consulting Agreement",
            Rules =
            [
                Required("consulting.engagement", "Engagement", FindingSeverity.Critical,
                    "The consulting services should be described.", "Engagement",
                    "engagement", "consulting", "services", "scope of work"),
                Required("consulting.compensation", "Compensation", FindingSeverity.Critical,
                    "Consulting fees and payment terms should be stated.", "Compensation",
                    "compensation", "payment", "fees", "invoice"),
                Required("consulting.term", "Term", FindingSeverity.High,
                    "The consulting term and termination should be stated.", "Term",
                    "term", "duration", "termination"),
                Required("consulting.ip", "Intellectual Property", FindingSeverity.High,
                    "Ownership of deliverables should be allocated.", "Intellectual Property",
                    "intellectual property", "work product", "ownership", "copyright"),
                Required("consulting.confidentiality", "Confidentiality", FindingSeverity.High,
                    "Confidential information handling should be addressed.", "Confidentiality",
                    "confidential", "non-disclosure"),
                Required("consulting.governing-law", "Governing Law", FindingSeverity.High,
                    "The agreement should choose a governing law.", "Governing Law",
                    "governing law", "choice of law", "jurisdiction"),
                RequiredFact("consulting.fact.governing-law", "Governing Law", FindingSeverity.High,
                    "State which law governs the agreement.", "governing law"),
                RedFlag("consulting.flag.unlimited-liability", "Liability", FindingSeverity.High,
                    "Unlimited liability is unusually risky.", @"\bunlimited liability\b|\bwithout (?:any )?limitation of liability\b"),
                RedFlag("consulting.flag.perpetual", "Term", FindingSeverity.Medium,
                    "A perpetual or indefinite duty may be unintended.", @"\bperpetual(?:ly)?\b|\bin perpetuity\b|\bindefinitely\b")
            ]
        }
    ];

    /// <summary>Returns the playbook for a canonical contract type or a safe generic fallback.</summary>
    public static Playbook Get(string? contractType)
    {
        if (!string.IsNullOrWhiteSpace(contractType))
        {
            var match = All.FirstOrDefault(playbook =>
                string.Equals(playbook.ContractType, contractType, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return new Playbook
        {
            ContractType = "Generic",
            DisplayName = "Generic contract",
            Rules =
            [
                Required("generic.confidentiality", "Confidentiality", FindingSeverity.Medium,
                    "Consider whether confidentiality obligations are needed.", "Confidentiality",
                    "confidential"),
                Required("generic.governing-law", "Governing Law", FindingSeverity.High,
                    "The agreement should choose a governing law.", "Governing Law",
                    "governing law", "choice of law", "jurisdiction"),
                RequiredFact("generic.fact.governing-law", "Governing Law", FindingSeverity.High,
                    "State which law governs the agreement.", "governing law")
            ]
        };
    }

    private static PlaybookRule Required(
        string id,
        string category,
        FindingSeverity severity,
        string description,
        string suggestedCategory,
        params string[] keywords) => new()
    {
        Id = id,
        Kind = PlaybookRuleKind.RequiredClause,
        Category = category,
        Description = description,
        Severity = severity,
        Keywords = keywords,
        SuggestedClauseCategory = suggestedCategory
    };

    private static PlaybookRule RequiredFact(
        string id,
        string category,
        FindingSeverity severity,
        string description,
        params string[] keywords) => new()
    {
        Id = id,
        Kind = PlaybookRuleKind.RequiredFact,
        Category = category,
        Description = description,
        Severity = severity,
        Keywords = keywords
    };

    private static PlaybookRule RedFlag(
        string id,
        string category,
        FindingSeverity severity,
        string description,
        string pattern) => new()
    {
        Id = id,
        Kind = PlaybookRuleKind.RedFlag,
        Category = category,
        Description = description,
        Severity = severity,
        Pattern = pattern
    };
}
