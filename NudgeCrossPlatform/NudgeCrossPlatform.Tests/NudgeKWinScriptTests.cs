using System.Linq;
using System.Text.Json;
using NudgeCore;
using Xunit;

namespace NudgeCrossPlatform.Tests
{
    public sealed class NudgeKWinScriptTests
    {
        [Fact]
        public void KWinMetadataJson_IsValidJsonAndHasRequiredFields()
        {
            // Verify MetadataJson is valid JSON
            var doc = JsonDocument.Parse(NudgeCoreLogic.KWinMetadataJson);
            var root = doc.RootElement;

            // KWin 6 requires KPackageStructure to be KWin/Script
            Assert.True(root.TryGetProperty("KPackageStructure", out var kps));
            Assert.Equal("KWin/Script", kps.GetString());

            // Check KPlugin metadata
            Assert.True(root.TryGetProperty("KPlugin", out var kplugin));
            Assert.True(kplugin.TryGetProperty("Id", out var id));
            Assert.Equal("nudge-window-tracker", id.GetString());
            
            Assert.True(kplugin.TryGetProperty("ServiceTypes", out var serviceTypes));
            Assert.Contains("KWin/Script", serviceTypes.EnumerateArray().Select(s => s.GetString()));

            // Check entry point
            Assert.True(root.TryGetProperty("X-Plasma-MainScript", out var mainScript));
            Assert.Equal("code/main.js", mainScript.GetString());
        }

        [Fact]
        public void KWinMainJs_IsNotEmptyAndContainsRequiredLogic()
        {
            var js = NudgeCoreLogic.KWinMainJs;
            Assert.False(string.IsNullOrWhiteSpace(js));

            // Should use callDBus for passive background IPC (no crosshair/UI)
            Assert.Contains("callDBus", js);
            Assert.Contains("org.nudge.WindowTracker", js);
            
            // Should be passive (connect to events)
            Assert.Contains("workspace.windowActivated.connect", js);
            
            // Should NOT use queryWindowInfo or anything that might trigger UI/crosshair
            Assert.DoesNotContain("queryWindowInfo", js);
            
            // Should collect resourceClass and caption
            Assert.Contains("resourceClass", js);
            Assert.Contains("caption", js);
        }
    }
}
