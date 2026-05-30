using System;
using Xunit;
using NudgeCore;

namespace NudgeCrossPlatform.Tests;

public sealed class GetStableHashTests
{
    [Fact]
    public void EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0, NudgeCoreLogic.GetStableHash(""));
        Assert.Equal(0, NudgeCoreLogic.GetStableHash(null!));
    }

    [Fact]
    public void SameInput_ProducesSameHash()
    {
        Assert.Equal(NudgeCoreLogic.GetStableHash("hello"), NudgeCoreLogic.GetStableHash("hello"));
    }

    [Fact]
    public void DifferentInputs_ProduceDifferentHashes()
    {
        Assert.NotEqual(NudgeCoreLogic.GetStableHash("hello"), NudgeCoreLogic.GetStableHash("world"));
    }

    [Fact]
    public void CaseSensitive()
    {
        Assert.NotEqual(NudgeCoreLogic.GetStableHash("Hello"), NudgeCoreLogic.GetStableHash("hello"));
    }

    [Fact]
    public void DeterministicAcrossCalls()
    {
        int a = NudgeCoreLogic.GetStableHash("test");
        int b = NudgeCoreLogic.GetStableHash("test");
        int c = NudgeCoreLogic.GetStableHash("test");
        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }
}

public sealed class EscapeCsvTests
{
    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", NudgeCoreLogic.EscapeCsv(null));
        Assert.Equal("", NudgeCoreLogic.EscapeCsv(""));
    }

    [Fact]
    public void PlainValue_ReturnsUnchanged()
    {
        Assert.Equal("hello", NudgeCoreLogic.EscapeCsv("hello"));
    }

    [Fact]
    public void ValueWithComma_WrapsInQuotes()
    {
        Assert.Equal("\"a,b\"", NudgeCoreLogic.EscapeCsv("a,b"));
    }

    [Fact]
    public void ValueWithQuote_EscapesQuote()
    {
        Assert.Equal("\"\"\"hello\"\"\"", NudgeCoreLogic.EscapeCsv("\"hello\""));
    }

    [Fact]
    public void ValueWithCommaAndQuote_EscapesQuoteInsideQuotes()
    {
        Assert.Equal("\"a\"\"b,c\"", NudgeCoreLogic.EscapeCsv("a\"b,c"));
    }

    [Fact]
    public void Newlines_ReplacedWithSpace()
    {
        Assert.Equal("a b", NudgeCoreLogic.EscapeCsv("a\rb"));
        Assert.Equal("a b", NudgeCoreLogic.EscapeCsv("a\nb"));
    }

    [Fact]
    public void MixedNewlineAndComma_WrapsAfterSanitize()
    {
        Assert.Equal("\"a b,c\"", NudgeCoreLogic.EscapeCsv("a\rb,c"));
    }
}

public sealed class GetFocusSourceNameTests
{
    [Fact]
    public void WindowsApi_ReturnsExpectedName() =>
        Assert.Equal("windows_api", NudgeCoreLogic.GetFocusSourceName(FocusSource.WindowsApi));

    [Fact]
    public void WaylandActivatedProtocol_ReturnsExpectedName() =>
        Assert.Equal("wayland_activated_protocol", NudgeCoreLogic.GetFocusSourceName(FocusSource.WaylandActivatedProtocol));

    [Fact]
    public void SwayIpc_ReturnsExpectedName() =>
        Assert.Equal("sway_ipc", NudgeCoreLogic.GetFocusSourceName(FocusSource.SwayIpc));

    [Fact]
    public void KWinScript_ReturnsExpectedName() =>
        Assert.Equal("kwin_script", NudgeCoreLogic.GetFocusSourceName(FocusSource.KWinScript));

    [Fact]
    public void GnomeShell_ReturnsExpectedName() =>
        Assert.Equal("gnome_shell", NudgeCoreLogic.GetFocusSourceName(FocusSource.GnomeShell));

    [Fact]
    public void X11Ewmh_ReturnsExpectedName() =>
        Assert.Equal("x11_ewmh", NudgeCoreLogic.GetFocusSourceName(FocusSource.X11Ewmh));

    [Fact]
    public void HeuristicProcessScan_ReturnsExpectedName() =>
        Assert.Equal("heuristic_process_scan", NudgeCoreLogic.GetFocusSourceName(FocusSource.HeuristicProcessScan));

    [Fact]
    public void UnknownValue_ReturnsUnknown() =>
        Assert.Equal("unknown", NudgeCoreLogic.GetFocusSourceName((FocusSource)999));
}

public sealed class GetSignalQualityNameTests
{
    [Fact]
    public void Trusted_ReturnsExpectedName() =>
        Assert.Equal("trusted", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Trusted));

    [Fact]
    public void Usable_ReturnsExpectedName() =>
        Assert.Equal("usable", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Usable));

    [Fact]
    public void Poor_ReturnsExpectedName() =>
        Assert.Equal("poor", NudgeCoreLogic.GetSignalQualityName(SignalQuality.Poor));

    [Fact]
    public void UnknownValue_ReturnsPoor() =>
        Assert.Equal("poor", NudgeCoreLogic.GetSignalQualityName((SignalQuality)999));
}
