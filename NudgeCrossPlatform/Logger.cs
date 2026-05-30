// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// FileLogger - lightweight console-mirroring file logger
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//
// Once Initialize() is called, everything written to Console.Out / Console.Error
// is transparently appended to ~/.nudge/nudge.log. This captures the tray's own
// output AND the harvest daemon's output (the tray re-emits the daemon's stdout
// via Console.WriteLine), giving a single unified log for the Send Feedback flow.
//
// Logging must never crash the app, so every operation is wrapped defensively.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NudgeCore;

internal static class FileLogger
{
    private const long MaxLogBytes = 1_000_000; // rotate at ~1 MB

    private static readonly object _fileGate = new();
    private static string _tag = "app";
    private static bool _initialized;

    // Strips ANSI colour/escape sequences so the log file stays plain text.
    private static readonly Regex AnsiPattern =
        new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static string LogFilePath => PlatformConfig.LogFilePath;

    /// <summary>
    /// Redirect Console.Out and Console.Error so all output is also appended to
    /// the log file. Idempotent — safe to call more than once.
    /// </summary>
    public static void Initialize(string tag)
    {
        if (_initialized) return;
        _initialized = true;
        _tag = string.IsNullOrWhiteSpace(tag) ? "app" : tag;
        try
        {
            string? dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            RotateIfNeeded();

            Console.SetOut(new TeeTextWriter(Console.Out));
            Console.SetError(new TeeTextWriter(Console.Error));

            AppendLine($"──── log started ({tag}) ────");
        }
        catch { /* logging must never crash startup */ }
    }

    /// <summary>Return up to <paramref name="count"/> of the most recent log lines.</summary>
    public static IReadOnlyList<string> ReadLastLines(int count)
    {
        var tail = new List<string>();
        if (count <= 0) return tail;
        try
        {
            string path = LogFilePath;
            if (!File.Exists(path)) return tail;

            // Open shared so we can read while the file is being written.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var ring = new Queue<string>(count);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (ring.Count >= count) ring.Dequeue();
                ring.Enqueue(line);
            }
            tail.AddRange(ring);
        }
        catch { /* best effort */ }
        return tail;
    }

    private static void AppendLine(string rawLine)
    {
        if (!_initialized) return;
        try
        {
            string clean = AnsiPattern.Replace(rawLine, string.Empty);
            string stamped = clean.Length == 0
                ? string.Empty
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}",
                    DateTime.Now, _tag, clean);
            lock (_fileGate)
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, stamped + Environment.NewLine);
            }
        }
        catch { /* swallow — never throw from logging */ }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogFilePath);
            if (fi.Exists && fi.Length > MaxLogBytes)
            {
                string previous = LogFilePath + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(LogFilePath, previous);
            }
        }
        catch { /* rotation is best effort */ }
    }

    /// <summary>
    /// TextWriter that forwards to the real console while buffering characters
    /// into whole lines and appending each completed line to the log file.
    /// </summary>
    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly StringBuilder _buffer = new();
        private readonly object _bufGate = new();

        public TeeTextWriter(TextWriter inner) => _inner = inner;

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            lock (_bufGate)
            {
                if (value == '\n') FlushBuffer();
                else if (value != '\r') _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
            if (string.IsNullOrEmpty(value)) return;
            lock (_bufGate)
            {
                foreach (char c in value)
                {
                    if (c == '\n') FlushBuffer();
                    else if (c != '\r') _buffer.Append(c);
                }
            }
        }

        public override void Flush() => _inner.Flush();

        // Caller must hold _bufGate.
        private void FlushBuffer()
        {
            string line = _buffer.ToString();
            _buffer.Clear();
            FileLogger.AppendLine(line);
        }
    }
}
