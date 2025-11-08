using System.Runtime.InteropServices;

namespace NudgeCommon.Monitoring;

/// <summary>
/// Factory for creating platform-specific activity monitors
/// </summary>
public static class ActivityMonitorFactory
{
    public static IActivityMonitor Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxActivityMonitor();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Windows support requires porting the original Windows-specific code. " +
                "This cross-platform version is designed for Linux.");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException(
                "macOS is not yet supported. Contributions welcome!");
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform not supported: {RuntimeInformation.OSDescription}");
        }
    }
}
