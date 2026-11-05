using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI.Xaml.Controls;

namespace ScreenCurtain
{
    internal static class StyleTests
    {
        internal static async Task RunAsync(MainWindow window, Action<bool, string> check)
        {
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            var origin = new System.Drawing.Point(desktop.Right + 900, desktop.Bottom + 900);
            Zone Rectangle() { return Geometry.RectangleZone(new System.Drawing.Point(0, 0), new System.Drawing.Point(179, 139), origin); }
            var source = new Zone { Polygon = true, ColorRgb = 0x3478ad, OpacityPercent = 50, BorderEnabled = true, BorderWidth = 7, BorderColorRgb = 0xf2a431, BorderStyle = 4,
                Points = new List<System.Drawing.Point> { origin, new System.Drawing.Point(origin.X + 179, origin.Y), new System.Drawing.Point(origin.X + 90, origin.Y + 139) } };
            var target = Rectangle(); target.Enabled = false;
            window.AddZone(source); window.AddZone(target); await Task.Delay(80);
            var pastes = ControllerTests.Descendants(window.CardScroll).OfType<Button>().Where(b => b.Name.StartsWith("Paste")).ToArray();
            check(pastes.Length == window.Zones.Count && pastes.All(b => !b.IsEnabled) && !window.PasteStyle(target),
                "Paste is disabled on every card and makes no changes before any style is copied");
            var points = target.Points.ToArray(); int number = target.Number;
            check(window.CopyStyle(source) && pastes.All(b => b.IsEnabled), "Copying enables paste on every existing card");
            source.ColorRgb = 0xffffff; source.OpacityPercent = 100; source.BorderEnabled = false; source.BorderWidth = 1;
            window.RemoveZone(source);
            check(window.PasteStyle(target) && target.ColorRgb == 0x3478ad && target.OpacityPercent == 50 &&
                target.BorderEnabled && target.BorderWidth == 7 && target.BorderColorRgb == 0xf2a431 && target.BorderStyle == 4,
                "Pasting uses an independent snapshot after changing and deleting the source polygon");
            check(!target.Polygon && !target.Enabled && target.Number == number && target.Points.SequenceEqual(points) && !window.Masks[target].Visible,
                "Polygon-to-rectangle paste preserves target geometry, number, type, and independent visibility");
            window.SetEnabled(target, true);
            Native.GetLayeredWindowAttributes(window.Masks[target].Handle, out uint key, out byte alpha, out uint flags);
            check(window.Masks[target].Visible && window.Masks[target].CurrentColorRgb == 0x3478ad && alpha == 128 &&
                window.Masks[target].CurrentBorder.Equals(BorderSettings.FromZone(target)), "Pasted appearance and border reach the live native window at 50 percent");
            check(window.PasteStyle(target) && window.PasteStyle(target) && target.Points.SequenceEqual(points), "The same snapshot can be pasted repeatedly");
            window.ToggleMaster();
            check(window.PasteStyle(target) && !window.Masks[target].Visible && !window.Master,
                "Pasting while the master switch is off never re-enables a mask");
            window.ToggleMaster();
            var rectangle = Rectangle(); rectangle.ColorRgb = 0xcc4488; rectangle.OpacityPercent = 0;
            rectangle.BorderEnabled = false; rectangle.BorderWidth = 30; rectangle.BorderColorRgb = 0x124578; rectangle.BorderStyle = 1;
            var polygon = new Zone { Polygon = true, Points = new List<System.Drawing.Point> {
                origin, new System.Drawing.Point(origin.X + 100, origin.Y + 10), new System.Drawing.Point(origin.X + 40, origin.Y + 120) } };
            window.AddZone(rectangle); window.AddZone(polygon);
            var polygonPoints = polygon.Points.ToArray();
            check(window.CopyStyle(rectangle) && window.PasteStyle(polygon) && polygon.Polygon && polygon.Points.SequenceEqual(polygonPoints) &&
                polygon.ColorRgb == 0xcc4488 && polygon.OpacityPercent == 0 && !polygon.BorderEnabled && polygon.BorderWidth == 30 &&
                polygon.BorderColorRgb == 0x124578 && polygon.BorderStyle == 1 && !window.Masks[polygon].Visible,
                "Rectangle-to-polygon paste copies zero opacity and all disabled-border settings without changing vertices");
            window.PassThrough.IsOn = !window.PassThrough.IsOn; window.ToggleMaster(); window.ToggleMaster();
            check(!window.Masks[polygon].Visible && polygon.OpacityPercent == 0 && polygon.BorderWidth == 30,
                "Pass-through and master switches preserve a pasted zero-opacity style and keep it hidden");
            rectangle.OpacityPercent = 100; rectangle.ColorRgb = 0x5566aa;
            check(window.CopyStyle(rectangle) && window.PasteStyle(polygon) && window.Masks[polygon].Visible,
                "A new copy replaces the previous snapshot and restores a pasted mask at 100 percent");
            var draft = window.OpenAppearanceAsync(rectangle); await Task.Delay(100);
            window.Editor.SetValues(0x00ff00, 25);
            check(!window.CopyStyle(rectangle) && !window.PasteStyle(polygon) && rectangle.ColorRgb == 0x5566aa,
                "An unconfirmed appearance preview cannot overwrite or paste the copied style");
            window.Editor.Hide(); await draft;
            string xml;
            using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings { Zones = new List<Zone> { polygon } }); xml = writer.ToString(); }
            Settings restored;
            using (var reader = new StringReader(xml)) restored = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            check(restored.Zones[0].ColorRgb == 0x5566aa && restored.Zones[0].OpacityPercent == 100 &&
                BorderSettings.FromZone(restored.Zones[0]).Equals(BorderSettings.FromZone(polygon)) && restored.Zones[0].Points.SequenceEqual(polygonPoints) &&
                !xml.Contains("copiedStyle"), "Pasted styles round-trip through the existing configuration without persisting the internal clipboard");
            window.RemoveZone(target); window.RemoveZone(rectangle); window.RemoveZone(polygon);
            await Task.Delay(100);
        }
    }
}
