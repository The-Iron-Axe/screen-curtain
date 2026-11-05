using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using P = System.Drawing.Point;

namespace ScreenCurtain
{
    internal static class ResourceTests
    {
        internal static async Task RunAsync(MainWindow window, string reportPath)
        {
            var results = new List<string>();
            void Check(bool ok, string label)
            {
                if (!ok) throw new InvalidOperationException(label);
                results.Add("PASS " + label); File.WriteAllLines(reportPath, results);
            }
            void Measure(string phase)
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                results.Add($"MEMORY {phase}: working={process.WorkingSet64 / 1048576.0:F2} MiB, private={process.PrivateMemorySize64 / 1048576.0:F2} MiB, managed={GC.GetTotalMemory(false) / 1048576.0:F2} MiB, cards={window.CachedCardCount}, created={window.CardCreationCount}");
                File.WriteAllLines(reportPath, results);
            }
            Border Card(Zone zone) { return ControllerTests.Descendants(window.CardScroll).OfType<Border>().Single(c => c.Name == "ZoneCard" + zone.Number); }
            try
            {
                await Task.Delay(2500); Measure("startup");
                Check(window.ResourceCleanupCount > 0, "Startup releases transient resources once the UI is idle");
                var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
                var origin = new P(desktop.Right + 800, desktop.Bottom + 800);
                Zone Rectangle(int i) { return Geometry.RectangleZone(P.Empty, new P(99, 79), new P(origin.X + i * 110, origin.Y)); }
                var first = Rectangle(0); window.AddZone(first);
                var original = Card(first); int created = window.CardCreationCount;
                var second = Rectangle(1); window.AddZone(second);
                Check(ReferenceEquals(original, Card(first)) && window.CardCreationCount == created + 1,
                    "Adding a zone creates only its own card and reuses existing native controls");
                window.CopyStyle(first); created = window.CardCreationCount;
                for (int i = 0; i < 40; i++) window.PasteStyle(second);
                Check(window.CardCreationCount == created && ReferenceEquals(original, Card(first)),
                    "Repeatedly applying an unchanged style allocates no replacement cards");
                first.ColorRgb = 0x3377aa; first.OpacityPercent = 50; first.BorderEnabled = true; first.BorderWidth = 8;
                window.CopyStyle(first); window.PasteStyle(second);
                // Both direct source changes and target changes are recognized; unrelated cards remain reusable.
                created = window.CardCreationCount; var unchanged = Card(first);
                second.ColorRgb = 0xcc9955; window.CopyStyle(second); window.PasteStyle(second);
                Check(window.CardCreationCount == created + 1 && ReferenceEquals(unchanged, Card(first)),
                    "Changing one zone's style replaces only the affected card");
                var points = second.Points.ToArray(); var style = ZoneStyle.FromZone(second);
                window.SetEnabled(second, false); window.ReleaseIdleResources(); await Task.Delay(150);
                Check(!second.Enabled && second.Points.SequenceEqual(points) && ZoneStyle.FromZone(second).Equals(style) && !window.Masks[second].Visible,
                    "Idle cleanup preserves geometry, styles and independent visibility");
                window.RemoveZone(first);
                Check(window.CachedCardCount == 1 && window.Zones.Count == 1,
                    "Deleting a zone removes its card and control references from all live caches");
                window.ClearZones();
                Check(window.CachedCardCount == 0, "Clearing zones releases the bounded card cache");
                var imported = Enumerable.Range(0, 30).Select(Rectangle).ToArray();
                window.AddImportedZones(imported); Measure("30 zones");
                Check(window.CachedCardCount == 30 && window.Zones.Count == 30, "Batch import caches exactly one card per live zone");
                var keep = window.Zones[0]; var keepCard = Card(keep); created = window.CardCreationCount;
                foreach (var zone in window.Zones.Skip(1).ToArray()) window.RemoveZone(zone);
                Check(window.CardCreationCount == created && window.CachedCardCount == 1 && ReferenceEquals(keepCard, Card(keep)),
                    "Deleting 29 zones retains the surviving card without rebuilding the list");
                window.ClearZones(); window.ReleaseIdleResources(); await Task.Delay(800); window.ReleaseIdleResources();
                await Task.Delay(200); Measure("after clearing 30 zones");
                var previewZone = Rectangle(0); window.AddZone(previewZone);
                var closedEditors = new List<WeakReference>();
                var closedPickers = new List<WeakReference>();
                for (int i = 0; i < 12; i++)
                {
                    var task = window.OpenAppearanceAsync(previewZone); await Task.Delay(100);
                    Check(window.Editor != null, "Repeated appearance dialog opens, cycle " + (i + 1));
                    closedEditors.Add(new WeakReference(window.Editor)); closedPickers.Add(new WeakReference(window.Editor.Picker));
                    window.Editor.SetValues(0x225588, 50); window.Editor.CompleteForTesting(false); await task;
                }
                Check(previewZone.ColorRgb == 0 && previewZone.OpacityPercent == 100 && window.Editor == null,
                    "Repeated dialog cancellation preserves the saved appearance");
                window.ReleaseIdleResources(); await Task.Delay(1000); Measure("after 12 dialogs");
                window.ReleaseIdleResources(); await Task.Delay(1500);
                results.Add("LIFETIME editors=" + closedEditors.Count(r => r.IsAlive) + ", pickers=" + closedPickers.Count(r => r.IsAlive));
                Check(closedEditors.All(r => !r.IsAlive) && closedPickers.All(r => !r.IsAlive),
                    "Closed appearance editors and their native color-picker wrappers are collectable");
                Measure("settled after 12 dialogs");
                for (int i = 0; i < 12; i++)
                {
                    var task = window.OpenAppearanceAsync(previewZone); await Task.Delay(100);
                    window.Editor.CompleteForTesting(false); await task;
                }
                window.ReleaseIdleResources(); await Task.Delay(1000); window.ReleaseIdleResources(); await Task.Delay(1000);
                Measure("after 24 dialogs");
                window.HideToTray(); await Task.Delay(2500);
                Check(!window.AppWindow.IsVisible && window.Masks[previewZone].Visible,
                    "Tray cleanup leaves enabled masks active");
                window.ShowController(); await Task.Delay(200);
                Check(window.AppWindow.IsVisible && window.Zones.Count == 1,
                    "Restoring the panel after cleanup preserves the same zones");
                window.ClearZones();
                results.Add("RESULT: " + results.Count(line => line.StartsWith("PASS ")) + " resource checks passed");
                File.WriteAllLines(reportPath, results);
            }
            catch (Exception error) { results.Add("FAIL " + error); File.WriteAllLines(reportPath, results); Environment.ExitCode = 1; }
            finally { window.RequestExit(); }
        }
    }
}
