using System;
using System.Collections.Generic;
using NudgeCore;

namespace NudgeTray;

internal static class LiveAIState
{
    private static readonly object _lock = new();
    private static readonly List<MLLiveEvent> _events = new(capacity: 210);
    private static readonly string _historyFile;

    static LiveAIState()
    {
        _historyFile = System.IO.Path.Combine(PlatformConfig.DataDirectory, "prediction_history.json");
        LoadFromDisk();
    }

    /// <summary>
    /// Unix timestamp (seconds UTC) of the next scheduled ML check.
    /// Emitted by nudge.cs as "MLNEXT:{ts}" at startup and after each cycle.
    /// 0 = not yet received.
    /// </summary>
    public static long NextCheckAt;
    /// <summary>Most-recent foreground app name; updated in real time via APPFOCUS: lines.</summary>
    public static volatile string CurrentApp = "";
    /// <summary>Window title / domain detail for the current app (tab-separated second field of APPFOCUS).</summary>
    public static volatile string CurrentDetail = "";
    /// <summary>Latest sensor fusion snapshot (HARVEST: lines), updated every 2s.</summary>
    public static volatile HarvestSignal? LastHarvest;

    public static void Add(MLLiveEvent evt)
    {
        lock (_lock)
        {
            _events.Add(evt);
            if (_events.Count > 200)
                _events.RemoveAt(0);
        }
        SaveToDisk();
    }

    /// <summary>Updates the matching event with the user's response and correctness.</summary>
    public static void UpdateResponse(long t, bool response)
    {
        lock (_lock)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                if (_events[i].T == t)
                {
                    _events[i].UserResponse = response;
                    _events[i].AiCorrect = !response;
                    break;
                }
            }
        }
        SaveToDisk();
    }

    /// <summary>Returns a snapshot of recent events, oldest first.</summary>
    public static IReadOnlyList<MLLiveEvent> GetRecent()
    {
        lock (_lock)
        {
            return _events.ToArray();
        }
    }

    public static MLLiveEvent? Latest
    {
        get
        {
            lock (_lock)
            {
                return _events.Count > 0 ? _events[_events.Count - 1] : null;
            }
        }
    }

    private static void LoadFromDisk()
    {
        try
        {
            if (!System.IO.File.Exists(_historyFile)) return;
            var json = System.IO.File.ReadAllText(_historyFile);
            var loaded = System.Text.Json.JsonSerializer.Deserialize(json, NudgeJsonContext.Default.ListMLLiveEvent);
            if (loaded == null) return;
            lock (_lock)
            {
                _events.Clear();
                int start = Math.Max(0, loaded.Count - 200);
                for (int i = start; i < loaded.Count; i++)
                    _events.Add(loaded[i]);
            }
        }
        catch { }
    }

    private static void SaveToDisk()
    {
        try
        {
            List<MLLiveEvent> snapshot;
            lock (_lock) { snapshot = new List<MLLiveEvent>(_events); }
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot, NudgeJsonContext.Default.ListMLLiveEvent);
            System.IO.File.WriteAllText(_historyFile, json);
        }
        catch { }
    }
}
