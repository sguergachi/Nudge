using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Tests for WP2 — ReputationAuthority.From (V4_REDESIGN/02_REPUTATION_AUTHORITY.md §5).
/// The classifier is pure over the four overlaid reputation fields plus BrowserWindowFlag /
/// FocusedDomainHash; vectors are constructed directly (no store, no other engine component).
/// Tuning constants under test: CONFIDENT_LABELS=3, STRONG_PRIOR_MARGIN=0.35,
/// PRODUCTIVE_RATE_HI=0.65, LOWVALUE_RATE_LO=0.35.
/// </summary>
public sealed class ReputationAuthorityTests
{
    // Readable builder over the 25 positional fields of FeatureVectorV4: only the reputation
    // fields and the browser/domain selectors matter to WP2; everything else is a neutral 0.
    private static FeatureVectorV4 Vec(
        double domainRate = 0.5, int domainCount = 0,
        double appRate = 0.5, int appCount = 0,
        int browserFlag = 0, int domainHash = 0)
        => new FeatureVectorV4(
            HourOfDay: 0,
            DayOfWeek: 0,
            FocusedAppHash: 0,
            FocusedDomainHash: domainHash,
            IdleMs: 0,
            FocusedSinceMs: 0,
            TitleStabilityMs: 0,
            SwitchCount60s: 0,
            SwitchCount300s: 0,
            DistinctApps300s: 0,
            DistinctDomains300s: 0,
            ReturnedToAnchorApp300s: 0,
            CurrentAppShare300s: 0.0,
            CurrentDomainShare300s: 0.0,
            BrowserWindowFlag: browserFlag,
            AfkFlag: 0,
            FullscreenFlag: 0,
            WorkspaceSwitchCount300s: 0,
            AudioPlayingFlag: 0,
            MediaSessionActiveFlag: 0,
            MicActiveFlag: 0,
            DomainProductiveRate: domainRate,
            DomainLabelCount: domainCount,
            AppProductiveRate: appRate,
            AppLabelCount: appCount);

    // A browser vector whose primary is the domain.
    private static FeatureVectorV4 BrowserVec(double domainRate, int domainCount,
                                              double appRate = 0.5, int appCount = 0)
        => Vec(domainRate: domainRate, domainCount: domainCount,
               appRate: appRate, appCount: appCount,
               browserFlag: 1, domainHash: 12345);

    // ── §5 core cases ──────────────────────────────────────────────────────────

    [Fact]
    public void From_UnknownApp_LowEvidence()
    {
        // Native app, count 0, neutral 0.5 ⇒ no evidence either way.
        var v = Vec(appRate: 0.5, appCount: 0);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_StrongPriorYoutube_ConfidentLowValue()
    {
        // Browser on a youtube-style domain: prior ≈ 0.08, count 0 ⇒ confident & low value.
        var v = BrowserVec(domainRate: 0.08, domainCount: 0);
        Assert.Equal(ReputationStance.ConfidentLowValue, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_StrongPriorGithub_ConfidentProductive()
    {
        // Browser on a github-style domain: prior ≈ 0.90, count 0 ⇒ confident & productive.
        var v = BrowserVec(domainRate: 0.90, domainCount: 0);
        Assert.Equal(ReputationStance.ConfidentProductive, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_UserLabeledSlackProductive_FlipsToConfidentProductive()
    {
        // Native app labeled productive: rate high with >= CONFIDENT_LABELS real labels.
        // Mirrors the "I told you Slack is fine" instant-personalization case.
        var v = Vec(appRate: 0.80, appCount: 3);
        Assert.Equal(ReputationStance.ConfidentProductive, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_Browser_PrefersDomainOverApp()
    {
        // Browser app rep is neutral/unknown but the domain is a strong distraction prior.
        // Domain must win ⇒ ConfidentLowValue (not LowEvidence from the app side).
        var v = BrowserVec(domainRate: 0.08, domainCount: 0, appRate: 0.5, appCount: 0);
        Assert.Equal(ReputationStance.ConfidentLowValue, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_NonBrowser_PrefersAppOverDomain()
    {
        // Native app (BrowserWindowFlag 0): app reputation is primary, domain ignored even if
        // a stray strong domain rate is present.
        var v = Vec(domainRate: 0.90, domainCount: 5, appRate: 0.5, appCount: 0, browserFlag: 0);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_Browser_NoDomainPresent_FallsBackToApp()
    {
        // Browser flag set but no domain (hash 0): primary falls back to the app reputation.
        var v = Vec(domainRate: 0.90, domainCount: 5, appRate: 0.5, appCount: 0,
                    browserFlag: 1, domainHash: 0);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void From_MiddlingConfidentRate_LowEvidence()
    {
        // High count (definitely confident) but rate sits between the thresholds ⇒ abstain.
        var v = Vec(appRate: 0.5, appCount: 20);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    // ── Boundary tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Confidence_CountAtConfidentLabels_IsConfident()
    {
        // count == CONFIDENT_LABELS (3) ⇒ confident; rate 0.0 ⇒ ConfidentLowValue.
        var v = Vec(appRate: 0.0, appCount: 3);
        Assert.Equal(ReputationStance.ConfidentLowValue, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Confidence_CountBelowConfidentLabels_NeutralRate_LowEvidence()
    {
        // count == 2 (< 3) and rate neutral ⇒ not confident.
        var v = Vec(appRate: 0.5, appCount: 2);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Confidence_StrongPriorMargin_AtBoundary_IsConfident()
    {
        // |rate - 0.5| == STRONG_PRIOR_MARGIN (0.35) exactly ⇒ confident (>=).
        // rate 0.15 ⇒ low value.
        var v = Vec(appRate: 0.15, appCount: 0);
        Assert.Equal(ReputationStance.ConfidentLowValue, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Confidence_JustInsideStrongPriorMargin_LowEvidence()
    {
        // |rate - 0.5| just under 0.35 (rate 0.16) at count 0 ⇒ not confident ⇒ LowEvidence.
        var v = Vec(appRate: 0.16, appCount: 0);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Stance_ProductiveRateHi_AtBoundary_IsProductive()
    {
        // rate == PRODUCTIVE_RATE_HI (0.65) exactly, confident via count ⇒ ConfidentProductive (>=).
        var v = Vec(appRate: 0.65, appCount: 3);
        Assert.Equal(ReputationStance.ConfidentProductive, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Stance_JustBelowProductiveRateHi_LowEvidence()
    {
        // rate just below 0.65 (0.64), confident via count, between thresholds ⇒ LowEvidence.
        var v = Vec(appRate: 0.64, appCount: 3);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Stance_LowValueRateLo_AtBoundary_IsLowValue()
    {
        // rate == LOWVALUE_RATE_LO (0.35) exactly, confident via count ⇒ ConfidentLowValue (<=).
        var v = Vec(appRate: 0.35, appCount: 3);
        Assert.Equal(ReputationStance.ConfidentLowValue, ReputationAuthority.From(in v).Stance);
    }

    [Fact]
    public void Stance_JustAboveLowValueRateLo_LowEvidence()
    {
        // rate just above 0.35 (0.36), confident via count, between thresholds ⇒ LowEvidence.
        var v = Vec(appRate: 0.36, appCount: 3);
        Assert.Equal(ReputationStance.LowEvidence, ReputationAuthority.From(in v).Stance);
    }

    // ── Verdict payload: all four rates/counts are passed through verbatim ──────────

    [Fact]
    public void From_PopulatesAllFourFields_Verbatim()
    {
        var v = Vec(domainRate: 0.12, domainCount: 4, appRate: 0.77, appCount: 6,
                    browserFlag: 1, domainHash: 999);
        var verdict = ReputationAuthority.From(in v);
        Assert.Equal(0.12, verdict.DomainRate, 6);
        Assert.Equal(4, verdict.DomainCount);
        Assert.Equal(0.77, verdict.AppRate, 6);
        Assert.Equal(6, verdict.AppCount);
        // Browser primary == domain (0.12) ⇒ ConfidentLowValue regardless of the app rate.
        Assert.Equal(ReputationStance.ConfidentLowValue, verdict.Stance);
    }
}
