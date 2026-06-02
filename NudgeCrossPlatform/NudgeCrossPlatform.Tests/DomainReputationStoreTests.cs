using Xunit;
using NudgeCore;
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
}
