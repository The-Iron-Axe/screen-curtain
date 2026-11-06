using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Forms = System.Windows.Forms;

namespace ScreenCurtain
{
    internal static class ShapeTests
    {
        private static IntPtr Position(int x, int y) { return new IntPtr((y << 16) | (x & 0xffff)); }
        private static void Drag(Forms.Form form, Point start, Point end)
        {
            Native.SendMessage(form.Handle, 0x0201, new IntPtr(1), Position(start.X, start.Y));
            Native.SendMessage(form.Handle, 0x0200, new IntPtr(1), Position(end.X, end.Y));
            Native.SendMessage(form.Handle, 0x0202, IntPtr.Zero, Position(end.X, end.Y));
        }
        private static void Key(Forms.Form form, Forms.Keys key)
        {
            SelfTest.KeySelector(form, key);
        }
        internal static void CheckGeometry(Action<bool, string> check, string directory)
        {
            var preciseRect = PreciseShape.Create(SelectionShape.Rectangle, 300, 200, 1);
            var preciseCircle = PreciseShape.Create(SelectionShape.Ellipse, 75, 1, 1);
            var preciseTriangle = PreciseShape.Create(SelectionShape.Triangle, 300, 400, 500);
            check(preciseRect.Bounds.Size == new Size(300, 200) && preciseCircle.Bounds.Size == new Size(150, 150) && preciseCircle.Curve == CurveShape.Ellipse,
                "Precise rectangle uses pixel width and height and circle uses coordinate radius");
            double Distance(Point a, Point b) { return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)); }
            check(Math.Abs(Distance(preciseTriangle.Points[2], preciseTriangle.Points[1]) - 300) < 1 &&
                Math.Abs(Distance(preciseTriangle.Points[0], preciseTriangle.Points[2]) - 400) < 1 &&
                Math.Abs(Distance(preciseTriangle.Points[0], preciseTriangle.Points[1]) - 500) < 1,
                "Precise triangle respects all three side lengths within pixel rounding");
            bool invalidSides = false;
            try { PreciseShape.Create(SelectionShape.Triangle, 10, 2, 3); } catch (ArgumentException) { invalidSides = true; }
            check(invalidSides && PreciseShape.Place(preciseRect, new Point(-100, -50)).Bounds.Location == new Point(-100, -50),
                "Impossible triangles are rejected and precise shapes place correctly at negative coordinates");
            var screen = new Rectangle(-1920, -1080, 3840, 2160);
            using (var circular = new SelectionForm(SelectionShape.Ellipse, screen, true))
            {
                Drag(circular, new Point(100, 100), new Point(300, 220));
                check(circular.Result != null && circular.Result.Curve == CurveShape.Ellipse && circular.Result.Bounds.Width == circular.Result.Bounds.Height,
                    "Circular spotlight selection keeps equal width and height without holding Shift");
            }
            using (var picker = new SelectionForm(SelectionShape.Rectangle, screen))
            using (var timer = new Forms.Timer { Interval = 100 })
            {
                timer.Tick += (sender, e) => {
                    var dialog = Forms.Application.OpenForms.Cast<Forms.Form>().FirstOrDefault(f => f.Text == "输入精确尺寸");
                    if (dialog == null) return;
                    timer.Stop();
                    var fields = dialog.Controls.OfType<Forms.NumericUpDown>().ToArray(); fields[0].Value = 300; fields[1].Value = 200;
                    ((Forms.Button)dialog.AcceptButton).PerformClick();
                };
                timer.Start(); Key(picker, Forms.Keys.S); timer.Stop();
                Native.SendMessage(picker.Handle, 0x0200, IntPtr.Zero, Position(200, 300));
                Native.SendMessage(picker.Handle, 0x0201, new IntPtr(1), Position(200, 300));
                check(picker.Result != null && picker.Result.Bounds == new Rectangle(-1720, -780, 300, 200),
                    "Precise-size dialog applies dimensions to the clicked position on a negative-origin desktop");
            }
            foreach (var kind in new[] { SelectionShape.Ellipse, SelectionShape.Triangle, SelectionShape.RoundedRectangle })
                using (var picker = new SelectionForm(kind, screen))
                {
                    Drag(picker, new Point(360, 280), new Point(120, 100)); var zone = picker.Result;
                    check(zone != null && Geometry.Valid(zone) && zone.Bounds == new Rectangle(-1800, -980, 241, 181) &&
                        (kind == SelectionShape.Triangle ? zone.Polygon && zone.Points.Count == 3 : zone.Curve != CurveShape.None && !zone.Polygon),
                        "Reverse dragging creates the requested shape on a negative-origin desktop: " + kind);
                }
            var circle = ShapeGeometry.FromDrag(SelectionShape.Ellipse, new Point(400, 320), new Point(120, 100), screen.Location, true);
            check(circle.Curve == CurveShape.Ellipse && circle.Bounds.Width == 221 && circle.Bounds.Height == 221,
                "Shift-constrained ellipse creation produces a true circle in reverse drag directions");
            foreach (var kind in new[] { SelectionShape.Ellipse, SelectionShape.RoundedRectangle })
            {
                var zone = ShapeGeometry.FromDrag(kind, Point.Empty, new Point(239, 159), Point.Empty);
                using (var path = ShapeGeometry.Path(zone, Point.Empty))
                    check(!path.IsVisible(0, 0) && path.IsVisible(120, 80), "Curved geometry excludes box corners and retains its center: " + kind);
                foreach (var mode in new[] { CoordinateMode.Pixels, CoordinateMode.ScreenPercent })
                {
                    var outline = ShapeGeometry.ExportPoints(zone);
                    var imported = ShapeTextCodec.Parse(ShapeTextCodec.Serialize(new[] { zone }, mode, screen), mode, screen).Single();
                    check(outline.Count >= 8 && outline.Count <= 256 && imported.Polygon && imported.Curve == CurveShape.None &&
                        imported.Points.SequenceEqual(outline) && imported.Bounds == zone.Bounds,
                        "Curve TXT round-trips its polygon approximation with matching bounds: " + kind + " " + mode);
                }
                var tiny = ShapeGeometry.FromDrag(kind, Point.Empty, new Point(2, 2), Point.Empty);
                check(ShapeTextCodec.Parse(ShapeTextCodec.Serialize(new[] { tiny }, CoordinateMode.Pixels, screen), CoordinateMode.Pixels, screen).Count == 1,
                    "The smallest valid curved shape also exports and imports: " + kind);
            }
            using (var sheet = new Bitmap(780, 460))
            using (var graphics = Graphics.FromImage(sheet))
            using (var label = new Font("Microsoft YaHei UI", 11))
            {
                Color background = Color.FromArgb(238, 243, 249); graphics.Clear(background); int index = 0;
                foreach (var kind in new[] { SelectionShape.Ellipse, SelectionShape.Triangle, SelectionShape.RoundedRectangle })
                    for (int style = 0; style < 2; style++)
                    {
                        int x = index % 3 * 260 + 20, y = index / 3 * 220 + 20;
                        var zone = ShapeGeometry.FromDrag(kind, Point.Empty, new Point(219, 149), new Point(x, y));
                        using (var path = ShapeGeometry.Path(zone, Point.Empty)) graphics.FillPath(Brushes.Black, path);
                        MaskBorderRenderer.Draw(graphics, zone, Point.Empty, new BorderSettings(true, 6, 0xee2233, style));
                        int red = 0;
                        for (int yy = y; yy < y + 150; yy++) for (int xx = x; xx < x + 220; xx++)
                        { Color pixel = sheet.GetPixel(xx, yy); if (pixel.R > 180 && pixel.G < 90 && pixel.B < 100) red++; }
                        check(sheet.GetPixel(x, y).ToArgb() == background.ToArgb() && sheet.GetPixel(x + 110, y + 90).ToArgb() == Color.Black.ToArgb() && red > 50,
                            "A curved or triangular border leaves exterior corners clear and the interior fill intact: " + kind + " " + style);
                        graphics.DrawString(ShapeGeometry.Label(zone) + (style == 0 ? " · 实线" : " · 虚线"), label, Brushes.Black, x, y + 160); index++;
                    }
                sheet.Save(Path.Combine(directory, "new-shapes.png"), ImageFormat.Png);
            }
        }
        internal static async Task CheckLiveAsync(MainWindow window, Action<bool, string> check, string directory)
        {
            var desktop = Forms.SystemInformation.VirtualScreen; Point origin = new Point(desktop.Right + 1000, desktop.Bottom + 1000);
            var ellipse = ShapeGeometry.FromDrag(SelectionShape.Ellipse, Point.Empty, new Point(199, 119), origin);
            var rounded = ShapeGeometry.FromDrag(SelectionShape.RoundedRectangle, Point.Empty, new Point(199, 119), origin);
            var triangle = ShapeGeometry.FromDrag(SelectionShape.Triangle, Point.Empty, new Point(199, 119), origin);
            ellipse.ColorRgb = 0x3377aa; ellipse.OpacityPercent = 50; ellipse.BorderEnabled = true; ellipse.BorderWidth = 7; ellipse.BorderStyle = 3;
            foreach (var zone in new[] { ellipse, rounded, triangle }) window.AddZone(zone); await Task.Delay(120);
            foreach (var zone in new[] { ellipse, rounded, triangle })
            {
                var mask = window.Masks[zone]; IntPtr region = Native.CreateRectRgn(0, 0, 0, 0);
                try
                {
                    check(mask.Visible && Native.GetWindowRgn(mask.Handle, region) != 0 && !Native.PtInRegion(region, 0, 0) && Native.PtInRegion(region, 100, 65),
                        "The live mask follows the actual curved or triangular outline: " + ShapeGeometry.Label(zone));
                }
                finally { Native.DeleteObject(region); }
            }
            var original = rounded.Points.ToArray();
            using (var editor = new ZoneEditForm(rounded, new Rectangle(origin.X - 100, origin.Y - 100, 1000, 700)))
            {
                editor.Show();
                check(editor.RadiusInput.Visible && editor.RadiusInput.Width > 60 && editor.RadiusInput.Top > 0 && editor.RadiusInput.Bottom < editor.ClientSize.Height,
                    "Rounded editing exposes an accessible radius input inside the visible toolbar");
                editor.RadiusInput.Value = 40; Drag(editor, new Point(100, 100), new Point(80, 70));
                check(editor.Draft.Curve == CurveShape.RoundedRectangle && editor.Draft.CornerPercent == 40 && editor.Draft.Bounds.Width == 220 &&
                    rounded.CornerPercent == 15 && rounded.Points.SequenceEqual(original), "Editing curved bounding handles and radius previews without changing the saved zone");
                using (var image = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(image, editor.ClientRectangle); image.Save(Path.Combine(directory, "rounded-editor.png"), ImageFormat.Png); }
                Key(editor, Forms.Keys.Escape);
                check(editor.Result == null && rounded.CornerPercent == 15, "Cancel curve editing discards radius and size changes");
            }
            using (var editor = new ZoneEditForm(rounded, new Rectangle(origin.X - 100, origin.Y - 100, 1000, 700)))
            {
                editor.RadiusInput.Value = 50; Key(editor, Forms.Keys.Enter); int number = rounded.Number; var handle = window.Masks[rounded].Handle;
                check(window.CommitGeometry(rounded, editor.Result) && rounded.CornerPercent == 50 && rounded.Curve == CurveShape.RoundedRectangle &&
                    rounded.Number == number && window.Masks[rounded].Handle == handle, "Confirm saves the rounded radius while preserving zone and native window identity");
            }
            using (var editor = new ZoneEditForm(ellipse, new Rectangle(origin.X - 100, origin.Y - 100, 1000, 700)))
            {
                Drag(editor, new Point(100, 100), new Point(80, 70)); Key(editor, Forms.Keys.Enter);
                check(editor.Result != null && window.CommitGeometry(ellipse, editor.Result) && ellipse.Curve == CurveShape.Ellipse &&
                    ellipse.Bounds.Width == 220 && ellipse.Bounds.Height == 150, "Dragging ellipse bounding handles saves the new size while retaining the curve");
            }
            window.ShowController(); await Task.Delay(100);
            var points = rounded.Points.ToArray();
            check(window.CopyStyle(ellipse) && window.PasteStyle(rounded) && rounded.Curve == CurveShape.RoundedRectangle && rounded.CornerPercent == 50 &&
                rounded.Points.SequenceEqual(points) && rounded.OpacityPercent == 50 && rounded.BorderStyle == 3,
                "Cross-shape style paste preserves curved target geometry and its corner radius");
            bool pass = window.PassThrough.IsOn;
            window.ToggleMaster(); window.ToggleMaster(); window.PassThrough.IsOn = !pass; window.PassThrough.IsOn = pass;
            check(window.Masks[rounded].Visible && window.Masks[rounded].CurrentOpacityPercent == 50 && rounded.Curve == CurveShape.RoundedRectangle,
                "Master and pass-through switches retain curved geometry and opacity");
            window.Masks[ellipse].ApplyAppearance(ellipse.ColorRgb, 0);
            check(!window.Masks[ellipse].Visible, "Zero-opacity curves hide their windows and do not intercept input");
            window.Masks[ellipse].ApplyAppearance(ellipse.ColorRgb, 100);
            check(window.Masks[ellipse].Visible, "Increasing curved-mask opacity restores its window");
            string xml;
            using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings { Zones = new List<Zone> { ellipse, rounded, triangle } }); xml = writer.ToString(); }
            Settings saved;
            using (var reader = new StringReader(xml)) saved = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            check(saved.Zones[0].Curve == CurveShape.Ellipse && saved.Zones[1].Curve == CurveShape.RoundedRectangle && saved.Zones[1].CornerPercent == 50 &&
                saved.Zones[2].Polygon && saved.Zones[2].Points.Count == 3 && saved.Zones.All(Geometry.Valid), "XML restores exact curve types, radius, and triangular geometry");
            var export = window.OpenTransferAsync(false); await Task.Delay(150);
            check(window.Transfer.OutputText != null && ShapeTextCodec.Parse(window.Transfer.OutputText, CoordinateMode.Pixels, desktop).Count == window.Zones.Count,
                "Old and new shapes export together without losing any shape");
            window.Transfer.CompleteForTesting(false); await export;
            foreach (var zone in new[] { ellipse, rounded, triangle }) window.RemoveZone(zone);
        }
    }
}
