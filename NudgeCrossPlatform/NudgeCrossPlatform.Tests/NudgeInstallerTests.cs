using System;
using System.IO;
using Xunit;

namespace NudgeCrossPlatform.Tests;

public sealed class NudgeInstallerTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ReleaseYml =
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root via .git");
    }

    [Fact]
    public void ReleaseWorkflow_ContainsVpkPackCommand()
    {
        Assert.Contains("vpk pack", ReleaseYml);
    }

    [Fact]
    public void ReleaseWorkflow_VpkPack_TargetsNudgeTrayExe()
    {
        Assert.Contains("--mainExe \"nudge-tray.exe\"", ReleaseYml);
    }

    [Fact]
    public void ReleaseWorkflow_StagingAndReleaseAssets_IncludeNudgeSetupExe()
    {
        Assert.Contains("Nudge-win-Setup.exe", ReleaseYml);
    }

    [Fact]
    public void ReleaseWorkflow_HasNoMsixArtifacts()
    {
        Assert.DoesNotContain("Nudge.msix", ReleaseYml);
        Assert.DoesNotContain("AppxManifest", ReleaseYml);
        Assert.DoesNotContain("MakeAppx", ReleaseYml);
        Assert.DoesNotContain("Install-Nudge.ps1", ReleaseYml);
    }

    [Fact]
    public void TrayProject_HasVelopackPackageReference()
    {
        var csproj = File.ReadAllText(
            Path.Combine(RepoRoot, "NudgeCrossPlatform", "nudge-tray.csproj"));
        Assert.Contains("Velopack", csproj);
    }

    [Fact]
    public void TraySource_VelopackInit_PrecedesMutexAndArgParsing()
    {
        var src = File.ReadAllText(
            Path.Combine(RepoRoot, "NudgeCrossPlatform", "nudge-tray.cs"));

        int mainIdx     = src.IndexOf("static void Main(", StringComparison.Ordinal);
        int velopackIdx = src.IndexOf("VelopackApp.Build().Run()", StringComparison.Ordinal);
        int mutexIdx    = src.IndexOf("new Mutex(", StringComparison.Ordinal);
        int argsIdx     = src.IndexOf("Array.IndexOf(args,", StringComparison.Ordinal);

        Assert.True(velopackIdx > mainIdx,
            "VelopackApp.Build().Run() must appear inside Main");
        Assert.True(velopackIdx < mutexIdx,
            "VelopackApp.Build().Run() must precede the single-instance mutex");
        Assert.True(velopackIdx < argsIdx,
            "VelopackApp.Build().Run() must precede argument parsing");
    }

    [Fact]
    public void MsixAssets_DoNotExistInRepo()
    {
        var windowsAssets = Path.Combine(RepoRoot, "NudgeCrossPlatform", "assets", "windows");
        Assert.False(File.Exists(Path.Combine(windowsAssets, "AppxManifest.xml")));
        Assert.False(File.Exists(Path.Combine(windowsAssets, "Install-Nudge.ps1")));
    }
}
