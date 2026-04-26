using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

internal enum NudgeStartupAction
{
    Run,
    ShowHelp,
    ShowVersion
}

internal sealed class NudgeParsedArgs
{
    public NudgeStartupAction Action { get; init; } = NudgeStartupAction.Run;
    public int? IntervalMinutes { get; init; }
    public bool MlEnabled { get; init; }
    public bool ForceTrainedModel { get; init; }
    public string? CsvPath { get; init; }
}

internal enum NudgeNotifyAction
{
    SendResponse,
    ShowHelp,
    ShowVersion,
    MissingResponse,
    InvalidResponse
}

internal sealed class NudgeNotifyParsedArgs
{
    public NudgeNotifyAction Action { get; init; }
    public string? Response { get; init; }
    public string? RawInput { get; init; }
}

internal static class NudgeCoreLogic
{
    internal const string TraySingleInstanceMutexName = "Global\\NudgeTray.SingleInstance";

    internal static NudgeParsedArgs ParseNudgeArgs(string[] args)
    {
        int? intervalMinutes = null;
        bool mlEnabled = false;
        bool forceModel = false;
        string? csvPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return new NudgeParsedArgs { Action = NudgeStartupAction.ShowHelp };
            }
            if (arg == "--version" || arg == "-v")
            {
                return new NudgeParsedArgs { Action = NudgeStartupAction.ShowVersion };
            }
            if (arg == "--interval" || arg == "-i")
            {
                // Always consume the next token (whether it parses or not) so it is
                // never mistakenly treated as a positional CSV-path argument.
                if (i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int minutes))
                        intervalMinutes = minutes;
                    i++;
                }
                continue;
            }
            if (arg == "--ml")
            {
                mlEnabled = true;
                continue;
            }
            if (arg == "--force-model")
            {
                forceModel = true;
                continue;
            }
            if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith("-", StringComparison.Ordinal))
            {
                csvPath = arg;
            }
        }

        return new NudgeParsedArgs
        {
            Action = NudgeStartupAction.Run,
            IntervalMinutes = intervalMinutes,
            MlEnabled = mlEnabled,
            ForceTrainedModel = forceModel,
            CsvPath = csvPath
        };
    }

    internal static NudgeNotifyParsedArgs ParseNudgeNotifyArgs(string[] args)
    {
        if (args.Length > 0)
        {
            if (args[0] == "--help" || args[0] == "-h")
            {
                return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.ShowHelp };
            }
            if (args[0] == "--version" || args[0] == "-v")
            {
                return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.ShowVersion };
            }
        }

        if (args.Length == 0)
        {
            return new NudgeNotifyParsedArgs { Action = NudgeNotifyAction.MissingResponse };
        }

        string response = args[0].ToUpperInvariant();
        if (response != "YES" && response != "NO")
        {
            return new NudgeNotifyParsedArgs
            {
                Action = NudgeNotifyAction.InvalidResponse,
                RawInput = args[0]
            };
        }

        return new NudgeNotifyParsedArgs
        {
            Action = NudgeNotifyAction.SendResponse,
            Response = response
        };
    }

    internal static bool ShouldExitForExistingTrayInstance(bool createdNew) => !createdNew;

    internal static string RunCommand(string cmd, string args, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return "";

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "";
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return stdoutTask.Result;
        }
        catch
        {
            return "";
        }
    }

    // ─── Countdown formatting ─────────────────────────────────────────────────

    /// <summary>
    /// Formats a countdown TimeSpan into a short human-readable label used in
    /// the tray menu status item.
    /// </summary>
    internal static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "📸 Next snapshot: soon";

        return remaining.TotalSeconds < 60
            ? $"📸 Next snapshot in {(int)remaining.TotalSeconds}s"
            : $"📸 Next snapshot in {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s";
    }

    // ─── Sway JSON parsing ────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the focused app's <c>app_id</c> from a <c>swaymsg -t get_tree</c>
    /// JSON response.  Falls back to simple string scanning if JSON is malformed.
    /// </summary>
    internal static string ExtractFocusedAppFromSwayJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return "unknown";

        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindFocusedNodeInSwayJson(doc.RootElement);
        }
        catch
        {
            // Fallback: cheap string scan (handles truncated / non-UTF8 output)
            if (json.Contains("\"focused\":true"))
            {
                int idx = json.IndexOf("\"app_id\":\"");
                if (idx != -1)
                {
                    idx += 10;
                    int end = json.IndexOf('"', idx);
                    if (end > idx)
                        return json.Substring(idx, end - idx);
                }
            }
            return "unknown";
        }
    }

    /// <summary>
    /// Recursively searches a Sway tree node for the focused leaf and returns its
    /// <c>app_id</c>.  Searches <c>nodes</c> and <c>floating_nodes</c>.
    /// </summary>
    internal static string FindFocusedNodeInSwayJson(JsonElement node)
    {
        if (node.TryGetProperty("focused", out var focused) && focused.GetBoolean())
        {
            if (node.TryGetProperty("app_id", out var appId))
            {
                var id = appId.GetString();
                return string.IsNullOrEmpty(id) ? "unknown" : id;
            }
        }

        foreach (string arrayProp in new[] { "nodes", "floating_nodes" })
        {
            if (node.TryGetProperty(arrayProp, out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    var result = FindFocusedNodeInSwayJson(child);
                    if (result != "unknown")
                        return result;
                }
            }
        }

        return "unknown";
    }

    // ─── Quoted-string extraction (used for gdbus / GNOME output) ────────────

    /// <summary>
    /// Extracts the first quoted token from <paramref name="input"/>.
    /// Tries double-quotes first, then single-quotes, to handle both JSON-style
    /// (<c>"value"</c>) and gdbus-style (<c>'value'</c>) output.
    /// </summary>
    internal static string ExtractQuotedString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "unknown";

        foreach (char q in new[] { '"', '\'' })
        {
            int start = input.IndexOf(q) + 1;
            if (start > 0)                          // found opening quote
            {
                int end = input.IndexOf(q, start);
                if (end > start)
                    return input.Substring(start, end - start);
            }
        }

        return "unknown";
    }
}
