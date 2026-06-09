using Xunit;
using NudgeCore;
using System.Collections.Generic;
using System.IO;

namespace NudgeCrossPlatform.Tests;

public sealed class DomainReputationStoreTests
{
    [Fact]
    public void UnknownDomain_ReturnsNeutralPrior()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        Assert.Equal(0.5, store.DomainRate("unknown.com"), 3);
        Assert.Equal(0, store.DomainCount("unknown.com"));
    }

    [Fact]
    public void UnknownApp_ReturnsNeutralPrior()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        Assert.Equal(0.5, store.AppRate("someapp"), 3);
        Assert.Equal(0, store.AppCount("someapp"));
    }

    [Fact]
    public void EmptyDomain_ReturnsNeutralPrior()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        Assert.Equal(0.5, store.DomainRate(""), 3);
        Assert.Equal(0, store.DomainCount(""));
    }

    [Fact]
    public void Update_Productive_IncreasesRate()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        store.Update("github.com", "code", productive: true);
        Assert.Equal((1.0 + 2.0) / (1.0 + 2.0 + 2.0), store.DomainRate("github.com"), 3);
        Assert.Equal((1.0 + 2.0) / (1.0 + 2.0 + 2.0), store.AppRate("code"), 3);
        Assert.Equal(1, store.DomainCount("github.com"));
        Assert.Equal(1, store.AppCount("code"));
    }

    [Fact]
    public void Update_Unproductive_DecreasesRate()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        store.Update("youtube.com", "chrome", productive: false);
        Assert.Equal((0.0 + 2.0) / (1.0 + 2.0 + 2.0), store.DomainRate("youtube.com"), 3);
        Assert.Equal((0.0 + 2.0) / (1.0 + 2.0 + 2.0), store.AppRate("chrome"), 3);
    }

    [Fact]
    public void MultipleUpdates_SmoothsCorrectly()
    {
        var store = new DomainReputationStore(Path.GetTempFileName());
        store.Update("site.com", "app", productive: true);
        store.Update("site.com", "app", productive: true);
        store.Update("site.com", "app", productive: false);
        // p=2, n=1 => (2+2)/(2+1+4) = 4/7
        Assert.Equal(4.0 / 7.0, store.DomainRate("site.com"), 3);
        Assert.Equal(4.0 / 7.0, store.AppRate("app"), 3);
        Assert.Equal(3, store.DomainCount("site.com"));
    }

    [Fact]
    public void Persistence_RoundTrip()
    {
        var path = Path.GetTempFileName();
        var store1 = new DomainReputationStore(path);
        store1.Update("a.com", "b", productive: true);
        store1.Update("a.com", "b", productive: false);
        store1.Flush();

        var store2 = new DomainReputationStore(path);
        Assert.Equal(2, store2.DomainCount("a.com"));
        Assert.Equal(2, store2.AppCount("b"));
        Assert.Equal(3.0 / 6.0, store2.DomainRate("a.com"), 3);
    }

    [Fact]
    public void Clear_ResetsToNeutral()
    {
        var path = Path.GetTempFileName();
        var store = new DomainReputationStore(path);
        store.Update("x.com", "y", productive: true);
        store.Flush();
        store.Clear();
        Assert.Equal(0.5, store.DomainRate("x.com"), 3);
        Assert.Equal(0, store.DomainCount("x.com"));
        Assert.False(File.Exists(path));
    }

    // ── Shipped distraction priors (PRETRAINED_DISTRACTION_MODEL.md §2) ──

    private static DomainReputationStore StoreWithPriors() => new(
        Path.GetTempFileName(),
        new Dictionary<string, double> { ["x.com"] = 0.10, ["github.com"] = 0.90 },
        new Dictionary<string, double> { ["steam"] = 0.10 });

    [Fact]
    public void PriorOnlyKey_NoLabels_ReturnsExactPrior()
    {
        var store = StoreWithPriors();
        Assert.Equal(0.10, store.DomainRate("x.com"), 3);
        Assert.Equal(0.90, store.DomainRate("github.com"), 3);
        Assert.Equal(0.10, store.AppRate("steam"), 3);
    }

    [Fact]
    public void PriorOnlyKey_LabelCountStaysZero()
    {
        var store = StoreWithPriors();
        Assert.Equal(0, store.DomainCount("x.com"));
        Assert.Equal(0, store.AppCount("steam"));
    }

    [Fact]
    public void AbsentKey_WithPriorsLoaded_StaysNeutral()
    {
        var store = StoreWithPriors();
        Assert.Equal(0.5, store.DomainRate("unlisted.org"), 3);
        Assert.Equal(0.5, store.AppRate("unlisted"), 3);
    }

    [Fact]
    public void Prior_BlendsSmoothlyWithLabels()
    {
        var store = StoreWithPriors();
        // prior 0.10, S=4: one productive label → (1 + 0.4) / (1 + 4) = 0.28 — no cliff
        store.Update("x.com", "", productive: true);
        Assert.Equal(1.4 / 5.0, store.DomainRate("x.com"), 3);
        // after 4 labels user evidence equals the prior mass and then dominates
        store.Update("x.com", "", productive: true);
        store.Update("x.com", "", productive: true);
        store.Update("x.com", "", productive: true);
        Assert.Equal(4.4 / 8.0, store.DomainRate("x.com"), 3);
        Assert.Equal(4, store.DomainCount("x.com"));
    }

    [Fact]
    public void Clear_PreservesPriors()
    {
        var store = StoreWithPriors();
        store.Update("x.com", "steam", productive: true);
        store.Clear();
        Assert.Equal(0.10, store.DomainRate("x.com"), 3);
        Assert.Equal(0.10, store.AppRate("steam"), 3);
        Assert.Equal(0, store.DomainCount("x.com"));
    }

    [Fact]
    public void ConcurrentUpdateAndRead_DoesNotCorrupt()
    {
        // Labels arrive on the UDP thread while rates are read from the harvest loop.
        var store = new DomainReputationStore(Path.GetTempFileName());
        var writer = System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < 5000; i++)
                store.Update($"site{i % 50}.com", $"app{i % 50}", productive: (i & 1) == 0);
        });
        for (int i = 0; i < 5000; i++)
        {
            double rate = store.DomainRate($"site{i % 50}.com");
            Assert.InRange(rate, 0.0, 1.0);
            _ = store.AppRate($"app{i % 50}");
        }
        writer.Wait();
        Assert.Equal(100, store.DomainCount("site0.com"));
    }
}
