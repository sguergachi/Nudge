namespace NudgeCore;

internal static class KWinScripts
{
    internal const string MetadataJson = @"{
    ""KPackageStructure"": ""KWin/Script"",
    ""KPlugin"": {
        ""Id"": ""nudge-window-tracker"",
        ""Name"": ""Nudge Window Tracker"",
        ""Description"": ""Publishes the active window's app id and title to org.nudge.WindowTracker on the session bus, for the Nudge productivity tracker. Invisible: no UI."",
        ""Version"": ""1.2"",
        ""EnabledByDefault"": true,
        ""ServiceTypes"": [""KWin/Script""]
    },
    ""X-Plasma-API"": ""javascript"",
    ""X-Plasma-MainScript"": ""code/main.js""
}
";

    internal const string MainJs = @"// Auto-installed by Nudge. Publishes active window info via D-Bus.
// Fully passive: listens to windowActivated + captionChanged + fullScreenChanged.
var _trackedWin = null;

function publish() {
    try {
        var w = workspace.activeWindow;
        if (w !== _trackedWin) {
            if (_trackedWin) {
                try { _trackedWin.captionChanged.disconnect(publish); } catch (e) {}
                try { _trackedWin.fullScreenChanged.disconnect(publish); } catch (e) {}
            }
            _trackedWin = w;
            if (w) {
                try { w.captionChanged.connect(publish); } catch (e) {}
                try { w.fullScreenChanged.connect(publish); } catch (e) {}
            }
        }
        var cls = (w && w.resourceClass) ? w.resourceClass.toString() : """";
        var title = (w && w.caption) ? w.caption.toString() : """";
        var fs = (w && w.fullScreen) ? 1 : 0;
        
        // Use callDBus for background IPC.
        callDBus(""org.nudge.WindowTracker"", ""/"", ""org.nudge.WindowTracker"", ""Update"", cls, title, fs);
    } catch (e) {
        console.error(""nudge-window-tracker error: "" + e);
    }
}

if (workspace.windowActivated !== undefined) {
    workspace.windowActivated.connect(publish);
}
publish();
";
}
