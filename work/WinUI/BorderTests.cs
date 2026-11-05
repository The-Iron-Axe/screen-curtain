using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using P = System.Drawing.Point;

namespace ScreenCurtain
{
    internal static class BorderTests
    {
        internal static void RunDrawing(Action<bool, string> check, string directory)
        {
            var defaults = BorderSettings.FromZone(new Zone());
            check(!defaults.Enabled && defaults.Width == 2 && defaults.ColorRgb == BorderSettings.DefaultColor && defaults.Style == 0,
                "New zones default to a disabled blue 2px solid border");
            check(new BorderSettings(true, 0, -20, 99).Width == 1 && new BorderSettings(true, 999, 0, -1).Width == 50 &&
                new BorderSettings(true, 2, -20, 99).ColorRgb == 0 && new BorderSettings(true, 2, 0, 99).Style == 0,
                "Invalid border settings are normalized to supported ranges");
            var rect = Geometry.RectangleZone(P.Empty, new P(239, 159), new P(-1920, -1080));
            var origin = new P(-1940, -1100);
            using (var solid = Render(rect, origin, new BorderSettings(true, 6, 0xee2233, 0)))
            {
                check(Red(solid.GetPixel(120, 22)) && Red(solid.GetPixel(120, 24)) && Red(solid.GetPixel(258, 100)) &&
                    solid.GetPixel(120, 30).ToArgb() == Color.Black.ToArgb() && solid.GetPixel(120, 19).ToArgb() == Color.White.ToArgb(),
                    "Rectangle border is painted inward at the requested thickness without expanding the zone");
            }
            var triangle = new Zone { Polygon = true, Points = new List<P> { new P(-1920, -1080), new P(-1680, -1080), new P(-1920, -920) } };
            using (var image = Render(triangle, origin, new BorderSettings(true, 6, 0xee2233, 0)))
            {
                check(Red(image.GetPixel(130, 104)) && image.GetPixel(70, 70).ToArgb() == Color.Black.ToArgb() &&
                    image.GetPixel(220, 160).ToArgb() == Color.White.ToArgb(),
                    "Polygon border follows diagonal edges and retains negative desktop coordinates");
            }
            var concave = new Zone { Polygon = true, Points = new List<P> { new P(0, 0), new P(239, 0), new P(239, 50), new P(70, 50), new P(70, 159), new P(0, 159) } };
            using (var image = Render(concave, new P(-20, -20), new BorderSettings(true, 10, 0xee2233, 0)))
            {
                check(Red(image.GetPixel(85, 110)) && image.GetPixel(100, 110).ToArgb() == Color.White.ToArgb() && image.GetPixel(50, 110).ToArgb() == Color.Black.ToArgb(),
                    "Thick borders stay inside concave polygon corners");
            }
            var signatures = new List<string>();
            using (var sheet = new Bitmap(640, 660))
            using (var graphics = Graphics.FromImage(sheet))
            using (var label = new Font("Microsoft YaHei UI", 10))
            {
                graphics.Clear(Color.FromArgb(238, 243, 249));
                for (int style = 0; style < 5; style++)
                {
                    using (var image = Render(rect, origin, new BorderSettings(true, 4, 0xee2233, style)))
                    {
                        signatures.Add(string.Concat(Enumerable.Range(40, 180).Select(x => Red(image.GetPixel(x, 22)) ? "1" : "0")));
                        int x = style % 2 * 320, y = style / 2 * 220;
                        graphics.DrawImageUnscaled(image, x, y); graphics.DrawString(BorderSettings.StyleNames[style], label, Brushes.Black, x + 20, y + 194);
                    }
                }
                sheet.Save(Path.Combine(directory, "border-styles.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            check(signatures.Distinct().Count() == 5 && signatures[0].All(c => c == '1') && signatures.Skip(1).All(s => s.Contains('0') && s.Contains('1')),
                "Solid, dashed, dotted, dash-dot, and dash-dot-dot borders render distinct visible patterns");
            using (var image = Render(rect, origin, new BorderSettings(false, 50, 0xee2233, 0)))
                check(image.GetPixel(120, 22).ToArgb() == Color.Black.ToArgb(), "Disabled border leaves the original fill untouched");
        }
        private static bool Red(Color c) { return c.R > 180 && c.G < 90 && c.B < 100; }
        private static Bitmap Render(Zone zone, P origin, BorderSettings value)
        {
            var image = new Bitmap(320, 220);
            using (var graphics = Graphics.FromImage(image))
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                graphics.Clear(Color.White);
                if (zone.Polygon) path.AddPolygon(zone.Points.Select(p => new P(p.X - origin.X, p.Y - origin.Y)).ToArray());
                else { var r = zone.Bounds; r.Offset(-origin.X, -origin.Y); path.AddRectangle(r); }
                graphics.FillPath(Brushes.Black, path); MaskBorderRenderer.Draw(graphics, zone, origin, value);
            }
            return image;
        }
        internal static async Task RunDialogsAsync(MainWindow window, Zone rect, Zone polygon, Action<bool, string> check, string directory)
        {
            var mask = window.Masks[rect];
            var task = window.OpenBorderAsync(rect); await Task.Delay(150);
            var editor = window.BorderEditor;
            check(editor != null && editor.Dialog.ActualTheme == ElementTheme.Light && !editor.EnabledSwitch.IsOn && editor.WidthNumber.Value == 2,
                "Each zone opens a native light border dialog with its own settings");
            window.BeginSelection(true); window.BeginGeometryEdit(polygon);
            var appearance = window.OpenAppearanceAsync(rect); var transfer = window.OpenTransferAsync(true); var nested = window.OpenBorderAsync(polygon);
            check(!window.IsSelecting && window.Editor == null && window.Transfer == null && appearance.IsCompleted && transfer.IsCompleted && nested.IsCompleted && window.BorderEditor == editor,
                "Border editing prevents nested drawing and settings dialogs");
            editor.EnabledSwitch.IsOn = true; editor.WidthSlider.Value = 6; editor.WidthNumber.Value = 9; editor.StylePicker.SelectedIndex = 3;
            editor.Picker.Color = Windows.UI.Color.FromArgb(255, 35, 220, 130);
            check(mask.CurrentBorder.Enabled && mask.CurrentBorder.Width == 9 && mask.CurrentBorder.ColorRgb == 0x23dc82 && mask.CurrentBorder.Style == 3 &&
                editor.WidthSlider.Value == 9 && !rect.BorderEnabled && !window.Masks[polygon].CurrentBorder.Enabled,
                "Border switch, width inputs, style, and color preview only the chosen zone without committing");
            editor.EnabledSwitch.IsOn = false; editor.EnabledSwitch.IsOn = true;
            check(mask.CurrentBorder.Width == 9 && mask.CurrentBorder.Style == 3 && mask.CurrentBorder.ColorRgb == 0x23dc82,
                "Border visibility toggle preserves its chosen appearance");
            await Task.Delay(100); await SelfTest.CaptureAsync(editor.Dialog, Path.Combine(directory, "winui-border.png"));
            editor.Hide(); await task;
            check(!rect.BorderEnabled && !mask.CurrentBorder.Enabled && mask.CurrentBorder.Width == 2, "Cancel restores the exact original border settings");
            task = window.OpenBorderAsync(rect); await Task.Delay(100);
            window.BorderEditor.SetValues(new BorderSettings(true, 6, 0xee2233, 0)); window.BorderEditor.CompleteForTesting(true); await task;
            task = window.OpenBorderAsync(polygon); await Task.Delay(100);
            window.BorderEditor.SetValues(new BorderSettings(true, 9, 0x2266ee, 4)); window.BorderEditor.CompleteForTesting(true); await task;
            var rectBorder = BorderSettings.FromZone(rect); var polygonBorder = BorderSettings.FromZone(polygon);
            check(rectBorder.Enabled && rectBorder.Width == 6 && rectBorder.ColorRgb == 0xee2233 && polygonBorder.Width == 9 && polygonBorder.Style == 4 &&
                mask.CurrentBorder.Equals(rectBorder) && window.Masks[polygon].CurrentBorder.Equals(polygonBorder),
                "Confirm saves independent border colors, widths, and line styles for multiple shapes");
            using (var image = new Bitmap(mask.Width, mask.Height))
            {
                mask.DrawToBitmap(image, new Rectangle(0, 0, mask.Width, mask.Height));
                check(Red(image.GetPixel(2, mask.Height / 2)), "Native mask paint includes the configured border");
            }
            window.PassThrough.IsOn = true; window.ToggleMaster(); window.ToggleMaster(); window.SetEnabled(rect, false); window.SetEnabled(rect, true);
            check(mask.CurrentBorder.Equals(rectBorder) && window.Masks[polygon].CurrentBorder.Equals(polygonBorder) && mask.Visible &&
                (Native.GetStyle(mask.Handle) & Native.WS_EX_TRANSPARENT) != 0, "Mouse pass-through, master switch, and region switch retain border settings");
            window.PassThrough.IsOn = false;
            mask.ApplyAppearance(rect.ColorRgb, 0); mask.ApplyBorder(rectBorder);
            check(!mask.Visible, "Zero opacity hides the border together with the fill");
            mask.ApplyAppearance(rect.ColorRgb, rect.OpacityPercent);
            uint key, flags; byte alpha;
            Native.GetLayeredWindowAttributes(mask.Handle, out key, out alpha, out flags);
            check(mask.Visible && alpha == Appearance.Alpha(rect.OpacityPercent) && mask.CurrentBorder.Equals(rectBorder),
                "Restoring opacity also restores the border without resetting window alpha");
            var bounds = rect.Bounds;
            var draft = Geometry.RectangleZone(P.Empty, new P(bounds.Width + 19, bounds.Height + 9), bounds.Location);
            check(window.CommitGeometry(rect, draft) && mask.CurrentBorder.Equals(rectBorder) && BorderSettings.FromZone(rect).Equals(rectBorder) && mask.Bounds == rect.Bounds,
                "Editing vertices resizes the border with the shape and preserves its settings");
            using (var edit = new ZoneEditForm(rect, System.Windows.Forms.SystemInformation.VirtualScreen))
                check(BorderSettings.FromZone(edit.Draft).Equals(rectBorder), "Vertex-edit drafts retain border settings");
        }
    }
}
