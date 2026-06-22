using System;
using System.IO;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

/// <summary>
/// Tests for the shipped distraction prior table (model_exp/distraction_priors.tsv)
/// and its loader. The TSV reaches the test output via the nudge-tray.csproj
/// model_exp content glob.
/// </summary>
public sealed class DistractionPriorsTests
{
    private static string ShippedTsvPath =>
        Path.Combine(AppContext.BaseDirectory, "model_exp", "distraction_priors.tsv");

    [Fact]
    public void LoadPriors_MissingFile_ReturnsEmpty()
    {
        var (domains, apps) = DomainReputationStore.LoadPriors(
            Path.Combine(Path.GetTempPath(), "nudge-no-such-priors.tsv"));
        Assert.Empty(domains);
        Assert.Empty(apps);
    }

    private static readonly string[] MixedValidityRows =
    {
        "# comment line",
        "",
        "x.com\tdomain\t0.10",
        "steam\tapp\t0.10",
        "github.com\tdomain\t0.90",
        "missing-fields\tdomain",            // wrong column count
        "badfloat.com\tdomain\tnot-a-number", // unparseable prior
        "weird.com\tgadget\t0.5",             // unknown kind
        "\tdomain\t0.5",                      // empty key
        "clamped.com\tdomain\t1.7",           // clamped to 1.0
    };

    [Fact]
    public void LoadPriors_ParsesValidRows_SkipsMalformed()
    {
        string path = Path.GetTempFileName();
        File.WriteAllLines(path, MixedValidityRows);

        var (domains, apps) = DomainReputationStore.LoadPriors(path);

        Assert.Equal(3, domains.Count);
        Assert.Single(apps);
        Assert.Equal(0.10, domains["x.com"], 3);
        Assert.Equal(0.90, domains["github.com"], 3);
        Assert.Equal(1.0, domains["clamped.com"], 3);
        Assert.Equal(0.10, apps["steam"], 3);
    }

    [Fact]
    public void ShippedTsv_LoadsWithSubstantialCoverage()
    {
        var (domains, apps) = DomainReputationStore.LoadPriors(ShippedTsvPath);
        Assert.True(domains.Count >= 300, $"expected >=300 domains, got {domains.Count}");
        Assert.True(apps.Count >= 100, $"expected >=100 apps, got {apps.Count}");

        // The motivating cases from PRETRAINED_DISTRACTION_MODEL.md
        Assert.True(domains["x.com"] <= 0.2, "x.com must ship as a strong distraction prior");
        Assert.True(domains["youtube.com"] <= 0.2);
        Assert.True(domains["github.com"] >= 0.8);
        Assert.True(apps["steam"] <= 0.2);
        Assert.True(apps["code"] >= 0.8);

        foreach (var (key, prior) in domains)
            Assert.InRange(prior, 0.0, 1.0);
        foreach (var (key, prior) in apps)
            Assert.InRange(prior, 0.0, 1.0);
    }

    [Fact]
    public void ShippedTsv_OmitsAmbiguousCommunicationKeys()
    {
        // WP9 §2: webmail / chat / meeting tools are deliberately left unlisted so they
        // fall to the neutral 0.5 prior and the user's own Yes/No labels decide. Shipping
        // a guess here (the old slack=0.55) is what caused "Slack always flagged". This
        // test encodes the decision so a future edit can't silently re-add them.
        var (domains, apps) = DomainReputationStore.LoadPriors(ShippedTsvPath);

        string[] ambiguousApps =
        {
            "slack", "zoom", "discord", "teams", "ms-teams", "teams-for-linux", "webex",
            "skype", "telegram", "telegram-desktop", "whatsapp", "signal-desktop",
            "thunderbird", "outlook", "evolution", "vesktop", "webcord", "element",
        };
        foreach (var app in ambiguousApps)
            Assert.False(apps.ContainsKey(app), $"ambiguous comms app must be omitted: {app}");

        string[] ambiguousDomains =
        {
            "gmail.com", "mail.google.com", "slack.com", "app.slack.com", "zoom.us",
            "discord.com", "discordapp.com", "teams.microsoft.com", "meet.google.com",
            "webex.com", "outlook.com", "messenger.com", "whatsapp.com",
        };
        foreach (var domain in ambiguousDomains)
            Assert.False(domains.ContainsKey(domain), $"ambiguous comms domain must be omitted: {domain}");
    }

    [Fact]
    public void ShippedTsv_DomainKeys_RoundTripTryNormalizeDomain()
    {
        // A key that TryNormalizeDomain would not itself produce is a silent
        // no-op at runtime (the store would never be queried with it).
        var (domains, _) = DomainReputationStore.LoadPriors(ShippedTsvPath);
        foreach (var key in domains.Keys)
        {
            Assert.True(BrowserDetector.TryNormalizeDomain(key, out var normalized),
                $"shipped key not a normalizable domain: {key}");
            Assert.Equal(key, normalized);
        }
    }
}
