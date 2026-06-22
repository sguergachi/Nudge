using System;

namespace NudgeCore;

// WP2 — Reputation authority (read-side verdict over DomainReputationStore values).
// SPEC: V4_REDESIGN/02_REPUTATION_AUTHORITY.md. Implement against V4Engine.Types.cs.
// Pure: uses ONLY the four reputation fields already overlaid onto the vector by the daemon
// (DomainProductiveRate/DomainLabelCount/AppProductiveRate/AppLabelCount) plus BrowserWindowFlag
// and FocusedDomainHash for primary selection. Do NOT change DomainReputationStore.
// Fully unit-tested (ReputationAuthorityTests.cs).
internal static class ReputationAuthority
{
    // ── Tuning constants (02_REPUTATION_AUTHORITY.md §2, suggested values) ──

    /// <summary>Real user labels at/above which a rate is trusted regardless of its value.</summary>
    private const int CONFIDENT_LABELS = 3;

    /// <summary>How far a (possibly prior-only) rate must sit from neutral 0.5 to be confident
    /// at low evidence. 0.35 ⇒ a shipped 0.10 or 0.90 prior is confident at count 0.</summary>
    private const double STRONG_PRIOR_MARGIN = 0.35;

    /// <summary>At/above this productive rate a confident verdict is ConfidentProductive.</summary>
    private const double PRODUCTIVE_RATE_HI = 0.65;

    /// <summary>At/below this productive rate a confident verdict is ConfidentLowValue.</summary>
    private const double LOWVALUE_RATE_LO = 0.35;

    private const double NeutralRate = 0.5;

    /// <summary>Pure given the four already-overlaid reputation fields on the vector.</summary>
    public static ReputationVerdict From(in FeatureVectorV4 f)
    {
        double domainRate = f.DomainProductiveRate;
        int domainCount = f.DomainLabelCount;
        double appRate = f.AppProductiveRate;
        int appCount = f.AppLabelCount;

        // Browsers carry no useful app reputation (the domain is the evidence); native apps
        // carry no domain. Pick the primary accordingly.
        bool browserWithDomain = f.BrowserWindowFlag == 1 && f.FocusedDomainHash != 0;
        double primaryRate;
        int primaryCount;
        if (browserWithDomain)
        {
            primaryRate = domainRate;
            primaryCount = domainCount;
        }
        else
        {
            primaryRate = appRate;
            primaryCount = appCount;
        }

        ReputationStance stance;
        if (!IsConfident(primaryRate, primaryCount))
            stance = ReputationStance.LowEvidence;
        else if (primaryRate >= PRODUCTIVE_RATE_HI)
            stance = ReputationStance.ConfidentProductive;
        else if (primaryRate <= LOWVALUE_RATE_LO)
            stance = ReputationStance.ConfidentLowValue;
        else
            stance = ReputationStance.LowEvidence; // confident-but-middling ⇒ let behavior decide

        return new ReputationVerdict(domainRate, domainCount, appRate, appCount, stance);
    }

    // Confidence: prior-only (count 0) is still usable when the prior is strong (shipped DKB
    // entries like youtube/netflix ship near 0.05–0.1, github/docs near 0.9). A rate far from
    // neutral is confident even at count 0; near-neutral is low evidence until CONFIDENT_LABELS
    // real labels accumulate.
    private static bool IsConfident(double rate, int count)
        => count >= CONFIDENT_LABELS
           || Math.Abs(rate - NeutralRate) >= STRONG_PRIOR_MARGIN;
}
