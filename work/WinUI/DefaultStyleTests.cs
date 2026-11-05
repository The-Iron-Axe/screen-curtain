using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI.Xaml.Controls;
using P = System.Drawing.Point;

namespace ScreenCurtain
{
    internal static class DefaultStyleTests
    {
        internal static async Task RunAsync(MainWindow window, Action<bool, string> check, string directory)
        {
            var initial = window.DefaultStyle; var factory = ZoneStyle.FromZone(new Zone());
            var existing = window.Zones.ToArray(); var originals = existing.Select(ZoneStyle.FromZone).ToArray();
            bool ExistingUnchanged() { return existing.Select((z, i) => ZoneStyle.FromZone(z).Equals(originals[i]) &&
                window.Masks[z].CurrentColorRgb == originals[i].ColorRgb && window.Masks[z].CurrentOpacityPercent == originals[i].OpacityPercent &&
                window.Masks[z].CurrentBorder.Equals(originals[i].Border)).All(ok => ok); }
            check(initial.Equals(factory), "Missing default-style configuration retains opaque black with the border disabled");
            var task = window.OpenDefaultAppearanceAsync(); await Task.Delay(180);
            window.BeginSelection(true); var nested = window.OpenDefaultBorderAsync();
            check(window.Editor != null && !window.IsSelecting && nested.IsCompleted && window.BorderEditor == null,
                "Editing default appearance prevents selection and nested settings dialogs");
            window.Editor.SetValues(0x8844cc, 35);
            check(window.DefaultStyle.Equals(initial) && ExistingUnchanged(), "Default appearance preview changes neither saved defaults nor existing live masks");
            window.Editor.CompleteForTesting(false); await task;
            check(window.DefaultStyle.Equals(initial), "Cancelling default appearance discards its draft");

            task = window.OpenDefaultAppearanceAsync(); await Task.Delay(120);
            window.Editor.SetValues(0x8844cc, 35); await Task.Delay(100);
            await SelfTest.CaptureAsync(window.Editor.Dialog, Path.Combine(directory, "default-appearance.png"));
            window.Editor.CompleteForTesting(true); await task;
            check(window.DefaultStyle.ColorRgb == 0x8844cc && window.DefaultStyle.OpacityPercent == 35 && window.DefaultStyle.Border.Equals(initial.Border) && ExistingUnchanged(),
                "Saving default appearance retains default borders and every existing mask's independent style");
            var border = new BorderSettings(true, 7, 0xffaa33, 4);
            task = window.OpenDefaultBorderAsync(); await Task.Delay(120); window.BorderEditor.SetValues(border);
            check(window.DefaultStyle.Border.Equals(initial.Border) && ExistingUnchanged(), "Default-border preview does not update saved defaults or existing windows");
            await Task.Delay(120); await SelfTest.CaptureAsync(window.BorderEditor.Dialog, Path.Combine(directory, "default-border.png"));
            window.BorderEditor.CompleteForTesting(true); await task;
            var custom = window.DefaultStyle;
            check(custom.Border.Equals(border) && custom.ColorRgb == 0x8844cc && custom.OpacityPercent == 35 && ExistingUnchanged(),
                "Saving default borders preserves default color and opacity without applying to existing zones");

            task = window.OpenDefaultAppearanceAsync(); await Task.Delay(120); window.Editor.ResetValues();
            check(window.Editor.ColorRgb == 0 && window.Editor.OpacityPercent == 100 && window.DefaultStyle.Equals(custom),
                "Restoring initial default appearance stages opaque black until confirmed");
            window.Editor.CompleteForTesting(false); await task;
            task = window.OpenDefaultBorderAsync(); await Task.Delay(120); window.BorderEditor.ResetValues();
            check(window.BorderEditor.Value.Equals(factory.Border) && window.DefaultStyle.Equals(custom),
                "Restoring initial default borders is also a cancellable draft");
            window.BorderEditor.CompleteForTesting(false); await task;

            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            var origin = new P(desktop.Right + 900, desktop.Bottom + 900);
            var created = new List<Zone>();
            foreach (var shape in new[] { SelectionShape.Rectangle, SelectionShape.Ellipse, SelectionShape.Triangle, SelectionShape.RoundedRectangle })
            {
                var zone = ShapeGeometry.FromDrag(shape, P.Empty, new P(159, 119), origin);
                var points = zone.Points.ToArray(); var curve = zone.Curve; int radius = zone.CornerPercent;
                zone.Name = "默认样式测试"; window.AddDrawnZone(zone); created.Add(zone);
                check(ZoneStyle.FromZone(zone).Equals(custom) && zone.Points.SequenceEqual(points) && zone.Curve == curve && zone.CornerPercent == radius &&
                    zone.Name == "默认样式测试" && window.Masks[zone].Visible && window.Masks[zone].CurrentOpacityPercent == 35,
                    "New drawn masks inherit default appearance and borders while retaining geometry: " + shape);
            }
            var polygon = new Zone { Polygon = true, Points = new List<P> { origin, new P(origin.X + 180, origin.Y), new P(origin.X + 120, origin.Y + 150), new P(origin.X, origin.Y + 80) } };
            var circle = ShapeGeometry.FromDrag(SelectionShape.Ellipse, P.Empty, new P(130, 100), origin, true);
            window.AddDrawnZone(polygon); window.AddDrawnZone(circle); created.Add(polygon); created.Add(circle);
            check(ZoneStyle.FromZone(polygon).Equals(custom) && ZoneStyle.FromZone(circle).Equals(custom) && circle.Bounds.Width == circle.Bounds.Height && polygon.Polygon,
                "Polygon and circle selection also inherit the same defaults without changing shape");

            foreach (var mode in new[] { CoordinateMode.Pixels, CoordinateMode.ScreenPercent })
            {
                task = window.OpenTransferAsync(true); await Task.Delay(130);
                window.Transfer.ModePicker.SelectedIndex = mode == CoordinateMode.Pixels ? 0 : 1;
                var screen = ((CoordinateScreen)window.Transfer.ScreenPicker.SelectedItem).Bounds;
                string text = ShapeTextCodec.Serialize(new[] { polygon }, mode, screen);
                if (mode == CoordinateMode.Pixels) window.Transfer.LoadText(text, "默认样式测试.txt"); else window.Transfer.TextPreview.Text = text;
                await Task.Delay(60); window.Transfer.CompleteForTesting(true); await task;
                var imported = window.Zones.Last(); created.Add(imported);
                check(ZoneStyle.FromZone(imported).Equals(custom) && imported.Points.SequenceEqual(polygon.Points) && imported.Enabled,
                    "Imported geometry inherits saved defaults from both file and direct-input paths: " + mode);
            }
            var target = created[0]; var targetStyle = ZoneStyle.FromZone(target);
            task = window.OpenAppearanceAsync(target); await Task.Delay(120); window.Editor.SetValues(0, 100); window.Editor.CompleteForTesting(true); await task;
            task = window.OpenAppearanceAsync(target); await Task.Delay(120); window.Editor.ResetValues();
            check(window.Editor.ColorRgb == custom.ColorRgb && window.Editor.OpacityPercent == custom.OpacityPercent && target.ColorRgb == 0,
                "Using default appearance on a zone previews the user's saved defaults without immediately saving");
            window.Editor.CompleteForTesting(false); await task;
            check(target.ColorRgb == 0 && target.OpacityPercent == 100 && window.DefaultStyle.Equals(custom), "Cancel restores the individual appearance and preserves global defaults");
            targetStyle.ApplyTo(target); window.Masks[target].ApplyAppearance(target.ColorRgb, target.OpacityPercent);
            task = window.OpenBorderAsync(target); await Task.Delay(120); window.BorderEditor.SetValues(factory.Border); window.BorderEditor.CompleteForTesting(true); await task;
            task = window.OpenBorderAsync(target); await Task.Delay(120); window.BorderEditor.ResetValues(); window.BorderEditor.CompleteForTesting(true); await task;
            check(BorderSettings.FromZone(target).Equals(custom.Border) && window.DefaultStyle.Equals(custom), "Using default borders applies them only to the confirmed target zone");

            string xml;
            using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings {
                DefaultStyle = DefaultStyleSettings.FromStyle(custom), Zones = existing.ToList() }); xml = writer.ToString(); }
            Settings restored;
            using (var reader = new StringReader(xml)) restored = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            check(ZoneStyle.FromZone(restored.DefaultStyle.ToZone()).Equals(custom) && restored.Zones.Zip(existing).All(pair =>
                pair.First.Number == pair.Second.Number && pair.First.Name == pair.Second.Name && pair.First.Points.SequenceEqual(pair.Second.Points)),
                "Configuration round-trips all default-style fields separately from existing names, IDs, and vertices");
            var legacy = new System.Xml.XmlDocument(); legacy.LoadXml(xml); var node = legacy.DocumentElement.SelectSingleNode("DefaultStyle"); node.ParentNode.RemoveChild(node);
            using (var reader = new StringReader(legacy.OuterXml)) restored = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            check(ZoneStyle.FromZone(restored.DefaultStyle.ToZone()).Equals(factory) && restored.Zones.Select(ZoneStyle.FromZone).SequenceEqual(originals),
                "Old configurations get factory defaults without replacing independent zone styles");
            var invalid = new DefaultStyleSettings { ColorRgb = -1, OpacityPercent = 999, BorderEnabled = true, BorderWidth = 999, BorderColorRgb = -1, BorderStyle = 999 };
            var normalized = ZoneStyle.FromZone(invalid.ToZone());
            check(normalized.ColorRgb == 0 && normalized.OpacityPercent == 100 && normalized.Border.Width == 50 && normalized.Border.ColorRgb == 0 && normalized.Border.Style == 0,
                "Malformed saved default-style values normalize to supported ranges");

            window.UpdateDefaultStyle(ZoneStyle.FromZone(new Zone { OpacityPercent = 0 }));
            var zero = Geometry.RectangleZone(P.Empty, new P(100, 80), origin); window.AddDrawnZone(zero); created.Add(zero);
            check(zero.OpacityPercent == 0 && !window.Masks[zero].Visible && !zero.BorderEnabled, "Zero-opacity defaults create hidden, nonblocking windows with the chosen border state");
            window.UpdateDefaultStyle(custom);
            check(zero.OpacityPercent == 0 && !window.Masks[zero].Visible && ExistingUnchanged(), "Changing defaults again leaves previously created masks unchanged");
            task = window.OpenDefaultBorderAsync(); await Task.Delay(120); window.BorderEditor.SetValues(factory.Border);
            Native.SendMessage(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero); await task;
            check(window.DefaultStyle.Equals(custom) && window.BorderEditor == null && !window.AppWindow.IsVisible,
                "Closing the panel cancels an unconfirmed default-style edit before hiding to the tray");
            window.ShowController();
            foreach (var zone in created) window.RemoveZone(zone);
            window.UpdateDefaultStyle(initial); window.RefreshMasks(); await Task.Delay(120);
        }
    }
}
