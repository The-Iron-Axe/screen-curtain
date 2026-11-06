using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace ScreenCurtain
{
    internal static class SelfTest
    {
        internal static async Task RunNumberingAsync(MainWindow window, string reportPath)
        {
            var results = new List<string>();
            Action<bool, string> check = (ok, label) => { if (!ok) throw new InvalidOperationException(label); results.Add("PASS " + label); File.WriteAllLines(reportPath, results); };
            try
            {
                await Task.Delay(150);
                var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
                var origin = new System.Drawing.Point(desktop.Right + 800, desktop.Bottom + 800);
                ZoneIdentityTests.CheckNumbering(window, origin, check);
                Zone Rectangle() { return Geometry.RectangleZone(System.Drawing.Point.Empty, new System.Drawing.Point(99, 79), origin); }
                var first = Rectangle(); var middle = Rectangle(); var last = Rectangle();
                window.AddDrawnZone(first); window.AddDrawnZone(middle); window.AddDrawnZone(last); window.RemoveZone(middle);
                first.Name = "自定义名称";
                string xml;
                using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings { Zones = window.Zones.ToList() }); xml = writer.ToString(); }
                Settings restored;
                using (var reader = new StringReader(xml)) restored = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
                check(restored.Zones.Select(z => z.Number).SequenceEqual(new[] { 1, 3 }) && restored.Zones[0].Name == first.Name,
                    "Configuration preserves current IDs, gaps, and custom names without a historical counter");
                window.Zones.Reverse(); var following = Rectangle(); window.AddDrawnZone(following);
                check(following.Number == 4 && first.Number == 1 && last.Number == 3, "Unsorted existing zones still allocate above the maximum without renumbering them");
                window.ClearZones();
                results.Add("RESULT: " + results.Count + " numbering checks passed"); File.WriteAllLines(reportPath, results);
            }
            catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(reportPath, results); Environment.ExitCode = 1; }
            finally { window.RequestExit(); }
        }
        internal static async Task RunAsync(MainWindow window, string reportPath)
        {
            var results = new List<string>();
            Action<bool, string> check = (ok, label) => { if (!ok) throw new InvalidOperationException(label); results.Add("PASS " + label); File.WriteAllLines(reportPath, results); };
            try
            {
                await Task.Delay(350);
                check(typeof(Window).Assembly.GetName().Name == "Microsoft.WinUI", "Main window uses the native WinUI 3 framework");
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var bundled = new[] { "coreclr.dll", "Microsoft.ui.xaml.dll" };
                    check(new[] { typeof(object).Assembly.Location, typeof(Window).Assembly.Location, typeof(System.Windows.Forms.Form).Assembly.Location }
                        .All(path => path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) &&
                        bundled.All(name => process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(module => string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase) &&
                            module.FileName.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))),
                        "Runtime and UI modules load from the bundled app without requiring installed runtimes");
                }
                check(window.Root.ActualTheme == ElementTheme.Light, "Main interface forces the light theme");
                CheckPolygonClosure(check, Path.GetDirectoryName(reportPath));
                CheckVertexEditing(check, Path.GetDirectoryName(reportPath));
                TransferTests.RunCodec(check, Path.GetDirectoryName(reportPath));
                BorderTests.RunDrawing(check, Path.GetDirectoryName(reportPath));
                ShapeTests.CheckGeometry(check, Path.GetDirectoryName(reportPath));
                await OverlayStabilityTests.RunAsync(check);
                var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
                var outside = new System.Drawing.Point(desktop.Right + 800, desktop.Bottom + 800);
                ZoneIdentityTests.CheckNumbering(window, outside, check);
                var rect = Geometry.RectangleZone(new System.Drawing.Point(0, 0), new System.Drawing.Point(159, 119), outside);
                var polygon = new Zone { Polygon = true, ColorRgb = 0x186fe5, OpacityPercent = 65,
                    Points = new List<System.Drawing.Point> { outside, new System.Drawing.Point(outside.X + 159, outside.Y), new System.Drawing.Point(outside.X, outside.Y + 119) } };
                window.AddZone(rect); window.AddZone(polygon); await Task.Delay(100);
                check(window.Masks.Values.All(m => m.Visible), "Both rectangle and polygon masks are visible");
                window.BeginTemporarySpotlight();
                check(window.TemporarySpotlightCount > 0 && window.Masks.Values.All(m => !m.Visible),
                    "Temporary spotlight hides normal masks while preserving them");
                int diameter = window.TemporaryDiameter;
                window.ResizeTemporarySpotlight(120);
                check(window.TemporaryDiameter == diameter + 30, "Wheel input enlarges the temporary spotlight");
                window.ResizeTemporarySpotlight(-120);
                check(window.TemporaryDiameter == diameter, "Reverse wheel restores the previous spotlight diameter");
                window.EndTemporarySpotlight();
                check(window.TemporarySpotlightCount == 0 && window.Masks.Values.All(m => m.Visible),
                    "Releasing temporary spotlight restores the prior masks without changing their settings");
                check(Alpha(window.Masks[rect]) == 255 && Alpha(window.Masks[polygon]) == 166, "Native masks retain independent opacity values");
                IntPtr region = Native.CreateRectRgn(0, 0, 0, 0);
                try
                {
                    check(Native.GetWindowRgn(window.Masks[polygon].Handle, region) != 0 && Native.PtInRegion(region, 10, 10) && !Native.PtInRegion(region, 150, 110),
                        "Polygon window covers only its actual shape");
                }
                finally { Native.DeleteObject(region); }
                var mask = window.Masks[rect];
                window.ToggleMaster(); check(window.Masks.Values.All(m => !m.Visible), "Master switch hides all masks");
                window.ToggleMaster(); check(window.Masks.Values.All(m => m.Visible), "Master switch restores masks");
                Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(60);
                check(window.Masks.Values.All(m => !m.Visible), "Native hotkey message reaches the WinUI dispatcher");
                Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(60);
                window.SetEnabled(polygon, false); check(!window.Masks[polygon].Visible && mask.Visible, "Independent region switch");
                window.SetEnabled(polygon, true);
                await ControllerTests.CheckNormalPanelAsync(window, check);
                await ControllerTests.CheckTrayAsync(window, check);
                await ControllerTests.CheckShortcutsAsync(window, check, Path.GetDirectoryName(reportPath), "normal");
                await CaptureAsync(window.Root, Path.Combine(Path.GetDirectoryName(reportPath), "winui-main.png"));
                var task = window.OpenAppearanceAsync(rect); await Task.Delay(250);
                check(window.Editor != null && window.Editor.Dialog.ActualTheme == ElementTheme.Light, "Appearance settings use a native light ContentDialog");
                check(window.Editor.Picker is ColorPicker, "Appearance settings use the WinUI ColorPicker");
                Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(2), IntPtr.Zero); await Task.Delay(30);
                check(!window.IsSelecting && window.Editor != null, "Editing prevents accidental selection reentry");
                window.BeginGeometryEdit(rect);
                check(!window.IsSelecting && window.Editor != null, "Appearance editing prevents entering vertex editing");
                window.Editor.Picker.Color = Windows.UI.Color.FromArgb(255, 220, 75, 50);
                window.Editor.OpacitySlider.Value = 50;
                check(mask.CurrentColorRgb == 0xdc4b32 && Alpha(mask) == 128 && rect.ColorRgb == 0, "Color picker and slider preview without committing");
                window.PassThrough.IsOn = true;
                check(Alpha(mask) == 128 && (Native.GetStyle(mask.Handle) & Native.WS_EX_TRANSPARENT) != 0, "Mouse pass-through preserves the selected appearance");
                window.PassThrough.IsOn = false;
                window.Editor.OpacityNumber.Value = 0;
                check(!mask.Visible && window.Editor.OpacitySlider.Value == 0, "Zero opacity hides the mask and synchronizes controls");
                window.Editor.OpacityNumber.Value = 65;
                check(mask.Visible && Alpha(mask) == 166, "Numeric input restores the mask at its chosen opacity");
                await Task.Delay(160);
                await CaptureAsync(window.Editor.Dialog, Path.Combine(Path.GetDirectoryName(reportPath), "winui-appearance.png"));
                window.Editor.Hide(); await task;
                check(rect.ColorRgb == 0 && rect.OpacityPercent == 100 && Alpha(mask) == 255, "Cancel restores the original appearance");
                task = window.OpenAppearanceAsync(rect); await Task.Delay(120);
                window.Editor.SetValues(0x227755, 50); window.Editor.CompleteForTesting(true); await task;
                check(rect.ColorRgb == 0x227755 && rect.OpacityPercent == 50 && Alpha(mask) == 128, "Confirm saves an independent appearance");
                await ZoneIdentityTests.CheckRenameAsync(window, rect, polygon, check, Path.GetDirectoryName(reportPath));
                CheckGeometryCommit(window, rect, polygon, check);
                CheckEditWorkflow(window, rect, polygon, check);
                await TransferTests.RunDialogsAsync(window, check, Path.GetDirectoryName(reportPath));
                await BorderTests.RunDialogsAsync(window, rect, polygon, check, Path.GetDirectoryName(reportPath));
                string xml;
                using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings { Zones = window.Zones.ToList() }); xml = writer.ToString(); }
                Settings saved;
                using (var reader = new StringReader(xml)) saved = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
                check(saved.Zones[0].ColorRgb == rect.ColorRgb && saved.Zones[0].OpacityPercent == 50 && saved.Zones[1].Polygon &&
                    saved.Zones[0].Points.SequenceEqual(rect.Points) && saved.Zones[1].Points.SequenceEqual(polygon.Points), "Existing XML format round-trips edited vertices and appearance");
                check(saved.Zones.Zip(window.Zones).All(pair => BorderSettings.FromZone(pair.First).Equals(BorderSettings.FromZone(pair.Second))),
                    "XML persistence restores every zone's border visibility, width, color, and style");
                var oldXml = new System.Xml.XmlDocument(); oldXml.LoadXml(xml);
                foreach (var field in new[] { "Name", "ColorRgb", "OpacityPercent", "BorderEnabled", "BorderWidth", "BorderColorRgb", "BorderStyle", "Curve", "CornerPercent" }) { var nodes = oldXml.GetElementsByTagName(field); while (nodes.Count > 0) nodes[0].ParentNode.RemoveChild(nodes[0]); }
                using (var reader = new StringReader(oldXml.OuterXml)) saved = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
                check(saved.Zones.All(z => z.OpacityPercent == 100 && z.ColorRgb == 0), "Older settings default to fully opaque black");
                check(saved.Zones.All(z => !z.BorderEnabled && z.BorderWidth == 2 && z.BorderColorRgb == BorderSettings.DefaultColor && z.BorderStyle == 0),
                    "Older settings keep their geometry and start with borders disabled");
                check(saved.Zones.All(z => string.IsNullOrEmpty(z.Name) && z.DisplayName == "区域 " + z.Number),
                    "Older configurations without names keep their existing numbers and default display names");
                await StyleTests.RunAsync(window, check);
                check(saved.Zones.All(z => z.Curve == CurveShape.None), "Legacy configurations retain rectangle and polygon geometry without curve fields");
                await ShapeTests.CheckLiveAsync(window, check, Path.GetDirectoryName(reportPath));
                await DefaultStyleTests.RunAsync(window, check, Path.GetDirectoryName(reportPath));
                await ControllerTests.CheckCardLayoutAsync(window, check, Path.GetDirectoryName(reportPath));
                double scale = window.Root.XamlRoot.RasterizationScale;
                window.AppWindow.Resize(window.MinimumWindowSize);
                await Task.Delay(180); window.Root.UpdateLayout();
                check(window.Root.ActualTheme == ElementTheme.Light && window.Root.ActualWidth > 300, "Light layout remains active at minimum window size");
                await CaptureAsync(window.Root, Path.Combine(Path.GetDirectoryName(reportPath), "winui-main-min.png"));
                await ControllerTests.CheckShortcutsAsync(window, check, Path.GetDirectoryName(reportPath), "minimum");
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(520 * scale), window.MinimumWindowSize.Height));
                await Task.Delay(100);
                var savedBorder = BorderSettings.FromZone(rect);
                var draft = window.OpenBorderAsync(rect); await Task.Delay(100);
                window.BorderEditor.SetValues(new BorderSettings(false, 50, 0xffcc00, 2)); window.Shutdown(); await draft;
                check(mask.IsDisposed && rect.ColorRgb == 0x227755 && rect.OpacityPercent == 50 && BorderSettings.FromZone(rect).Equals(savedBorder),
                    "Shutdown discards unconfirmed border edits and releases overlays");
                window.RequestExit();
                check(window.ExitRequested && window.ControllerClosed, "Explicit Exit still closes the window and completes shutdown");
                results.Add("RESULT: " + results.Count + " checks passed"); File.WriteAllLines(reportPath, results);
            }
            catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(reportPath, results); Environment.ExitCode = 1; }
            finally { window.RequestExit(); }
        }
        private static void CheckPolygonClosure(Action<bool, string> check, string directory)
        {
            var bounds = new System.Drawing.Rectangle(-1920, -1080, 3840, 2160);
            var first = new System.Drawing.Point(100, 180);
            using (var selector = new SelectionForm(true, bounds))
            {
                ClickSelector(selector, first.X, first.Y);
                MoveSelector(selector, first.X + 1, first.Y + 1);
                check(!selector.SnappedToStart, "One vertex cannot trigger closure snapping");
                ClickSelector(selector, 420, 180);
                MoveSelector(selector, first.X + 1, first.Y + 1);
                check(!selector.SnappedToStart, "Two vertices cannot trigger closure snapping");
                ClickSelector(selector, 300, 420);
                int radius = selector.StartSnapRadius;
                MoveSelector(selector, first.X + radius + 1, first.Y);
                check(!selector.SnappedToStart && selector.PreviewCursor.X == first.X + radius + 1, "Cursor outside the snap radius remains free");
                MoveSelector(selector, first.X + radius, first.Y);
                check(selector.SnappedToStart && selector.PreviewCursor == first && selector.Result == null, "Snap boundary previews the exact first point without completing on hover");
                MoveSelector(selector, first.X + radius, first.Y + radius);
                check(!selector.SnappedToStart, "Snap radius uses circular distance, not a square hit area");
                // Mouse-down must recompute snapping even without a preceding mouse-move.
                ClickSelector(selector, first.X + 2, first.Y + 2);
                check(selector.Result != null && selector.Result.Points.Count == 3 && selector.Result.Points.Distinct().Count() == 3 &&
                    selector.Result.Points[0] == new System.Drawing.Point(bounds.X + first.X, bounds.Y + first.Y) && Geometry.Valid(selector.Result),
                    "Click near start closes without a duplicate vertex and preserves negative desktop coordinates");
            }
            using (var selector = new SelectionForm(true, bounds))
            {
                ClickSelector(selector, 100, 180); ClickSelector(selector, 250, 180); ClickSelector(selector, 400, 180);
                MoveSelector(selector, 102, 182);
                check(!selector.SnappedToStart && selector.Result == null, "Collinear vertices do not advertise valid closure");
            }
            using (var selector = new SelectionForm(true, bounds))
            {
                ClickSelector(selector, 100, 180); ClickSelector(selector, 420, 180); ClickSelector(selector, 300, 420);
                MoveSelector(selector, 102, 182); KeySelector(selector, System.Windows.Forms.Keys.Back);
                check(!selector.SnappedToStart && selector.PreviewCursor == new System.Drawing.Point(102, 182), "Undo immediately clears a no-longer-valid snap");
                KeySelector(selector, System.Windows.Forms.Keys.Enter);
                check(selector.Result == null, "Enter still rejects polygons with too few vertices");
                ClickSelector(selector, 300, 420); KeySelector(selector, System.Windows.Forms.Keys.Enter);
                check(selector.Result != null && selector.Result.Points.Count == 3, "Enter completion remains available after undo and redraw");
            }
            using (var selector = new SelectionForm(true, bounds))
            {
                ClickSelector(selector, 100, 180); ClickSelector(selector, 420, 180); ClickSelector(selector, 420, 420);
                ClickSelector(selector, 100, 420);
                Native.SendMessage(selector.Handle, 0x0203, new IntPtr(1), MousePosition(100, 420));
                check(selector.Result != null && selector.Result.Points.Count == 4, "Double-click completion remains available away from the start");
            }
            using (var selector = new SelectionForm(true, new System.Drawing.Rectangle(0, 0, 960, 540)))
            {
                ClickSelector(selector, 180, 200); ClickSelector(selector, 660, 200); ClickSelector(selector, 540, 440);
                MoveSelector(selector, 182, 202);
                using (var bitmap = new System.Drawing.Bitmap(960, 540))
                {
                    selector.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, 960, 540));
                    bitmap.Save(Path.Combine(directory, "polygon-snap-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                KeySelector(selector, System.Windows.Forms.Keys.Escape);
                check(selector.Result == null && selector.DialogResult == System.Windows.Forms.DialogResult.Cancel, "Escape still cancels while snapped to start");
            }
            using (var selector = new SelectionForm(false, bounds))
            {
                Native.SendMessage(selector.Handle, 0x0201, new IntPtr(1), MousePosition(100, 180));
                MoveSelector(selector, 420, 420);
                Native.SendMessage(selector.Handle, 0x0202, IntPtr.Zero, MousePosition(420, 420));
                check(selector.Result != null && !selector.Result.Polygon && Geometry.Valid(selector.Result), "Rectangle drag remains unaffected by polygon snapping");
            }
        }
        private static void CheckVertexEditing(Action<bool, string> check, string directory)
        {
            var bounds = new System.Drawing.Rectangle(-1920, -1080, 3840, 2160);
            var source = new Zone { Number = 7, Polygon = true, Enabled = false, ColorRgb = 0x3377ee, OpacityPercent = 0,
                Points = new List<System.Drawing.Point> { new System.Drawing.Point(bounds.X + 100, bounds.Y + 200),
                    new System.Drawing.Point(bounds.X + 420, bounds.Y + 200), new System.Drawing.Point(bounds.X + 300, bounds.Y + 500) } };
            var original = source.Points.ToList();
            using (var editor = new ZoneEditForm(source, bounds))
            {
                DragVertex(editor, 260, 300, 300, 330);
                check(editor.Draft.Points.SequenceEqual(original.Select(p => new System.Drawing.Point(p.X + 40, p.Y + 30))) && source.Points.SequenceEqual(original),
                    "Dragging inside a polygon moves all vertices without changing the source");
                KeySelector(editor, System.Windows.Forms.Keys.Left); KeySelector(editor, System.Windows.Forms.Keys.Down);
                check(editor.Draft.Points.SequenceEqual(original.Select(p => new System.Drawing.Point(p.X + 39, p.Y + 31))),
                    "Arrow keys move the entire polygon by one pixel");
                KeySelector(editor, System.Windows.Forms.Keys.Enter);
                check(editor.Result.Points.SequenceEqual(original.Select(p => new System.Drawing.Point(p.X + 39, p.Y + 31))) &&
                    editor.Result.ColorRgb == source.ColorRgb && editor.Result.OpacityPercent == source.OpacityPercent && editor.Result.Enabled == source.Enabled,
                    "Saving a moved zone preserves its appearance and enabled state");
            }
            using (var editor = new ZoneEditForm(source, bounds))
            {
                DragVertex(editor, 260, 300, 1, 1);
                check(editor.Draft.Points.Min(p => p.X) == bounds.X, "Moving is clamped at the virtual desktop edge without distorting geometry");
                var reset = editor.ResetButtonBounds; ClickSelector(editor, reset.X + reset.Width / 2, reset.Y + reset.Height / 2);
                check(editor.Draft.Points.SequenceEqual(original), "Reset restores a moved shape");
                KeySelector(editor, System.Windows.Forms.Keys.Right); KeySelector(editor, System.Windows.Forms.Keys.Escape);
                check(editor.Result == null && source.Points.SequenceEqual(original), "Cancel discards a keyboard move");
            }
            using (var editor = new ZoneEditForm(source, bounds))
            {
                check(editor.Draft.Points.SequenceEqual(original) && editor.Draft.Number == 7 && !editor.Draft.Enabled && editor.Draft.OpacityPercent == 0,
                    "Vertex editor loads existing geometry even for a disabled transparent zone");
                Native.SendMessage(editor.Handle, 0x0201, new IntPtr(1), MousePosition(105, 205));
                MoveSelector(editor, 170, 230);
                check(editor.IsDragging && editor.Draft.Points[0] == new System.Drawing.Point(bounds.X + 165, bounds.Y + 225) && source.Points.SequenceEqual(original),
                    "Dragging a polygon vertex previews a separate draft without jumping to the cursor");
                Native.SendMessage(editor.Handle, 0x0202, IntPtr.Zero, MousePosition(185, 245));
                check(!editor.IsDragging && editor.Draft.Points[0] == new System.Drawing.Point(bounds.X + 180, bounds.Y + 240) && editor.Draft.Points.Skip(1).SequenceEqual(original.Skip(1)),
                    "Mouse release applies the final point and keeps the other polygon vertices fixed");
                KeySelector(editor, System.Windows.Forms.Keys.Escape);
                check(editor.Result == null && source.Points.SequenceEqual(original), "Cancel discards all vertex changes");
            }
            using (var editor = new ZoneEditForm(source, bounds))
            {
                DragVertex(editor, 300, 500, 240, 440);
                KeySelector(editor, System.Windows.Forms.Keys.Enter);
                check(editor.Result != null && editor.Result.Points[2] == new System.Drawing.Point(bounds.X + 240, bounds.Y + 440) &&
                    editor.Result.ColorRgb == source.ColorRgb && editor.Result.OpacityPercent == 0 && source.Points.SequenceEqual(original),
                    "Enter returns the edited polygon with its original appearance and no premature persistence");
            }
            using (var editor = new ZoneEditForm(source, bounds))
            {
                DragVertex(editor, 300, 500, 240, 440);
                var reset = editor.ResetButtonBounds;
                ClickSelector(editor, reset.X + reset.Width / 2, reset.Y + reset.Height / 2);
                check(editor.Draft.Points.SequenceEqual(original), "Restore original resets all edited vertices");
                var save = editor.SaveButtonBounds;
                ClickSelector(editor, save.X + save.Width / 2, save.Y + save.Height / 2);
                check(editor.Result != null && editor.Result.Points.SequenceEqual(original), "Floating Save button confirms the current geometry");
            }
            using (var editor = new ZoneEditForm(source, bounds))
            {
                DragVertex(editor, 300, 500, 250, 200);
                check(editor.Draft.Points.SequenceEqual(original), "A drag that collapses the polygon is rolled back");
                Native.SendMessage(editor.Handle, 0x0204, new IntPtr(2), MousePosition(10, 10));
                check(editor.Result == null && editor.DialogResult == System.Windows.Forms.DialogResult.Cancel, "Right-click cancels vertex editing");
            }
            var rect = Geometry.RectangleZone(new System.Drawing.Point(100, 200), new System.Drawing.Point(420, 500), bounds.Location);
            var rectOriginal = rect.Points.ToList();
            using (var editor = new ZoneEditForm(rect, bounds))
            {
                DragVertex(editor, 100, 200, 140, 240);
                var draft = editor.Draft;
                check(!draft.Polygon && draft.Points[2] == rectOriginal[2] && draft.Points[0].Y == draft.Points[1].Y &&
                    draft.Points[0].X == draft.Points[3].X && draft.Bounds.Width == 281 && draft.Bounds.Height == 261,
                    "Rectangle corner drag keeps the opposite corner anchored and edges axis-aligned");
                DragVertex(editor, 140, 240, 500, 600);
                check(Geometry.Valid(editor.Draft) && editor.Draft.Points[2] == rectOriginal[2] && editor.Draft.Bounds.Width == 81,
                    "Rectangle corners may cross the opposite corner without breaking the shape");
                KeySelector(editor, System.Windows.Forms.Keys.Enter);
            }
            using (var editor = new ZoneEditForm(rect, bounds))
            {
                DragVertex(editor, 100, 200, 420, 500);
                check(editor.Draft.Points.SequenceEqual(rectOriginal), "A zero-size rectangle drag is rolled back");
                Native.SendMessage(editor.Handle, 0x0201, new IntPtr(1), MousePosition(100, 200));
                Native.SendMessage(editor.Handle, 0x0202, IntPtr.Zero, MousePosition(-50, -80));
                check(editor.Draft.Points[0] == bounds.Location, "Dragging beyond the virtual desktop clamps to its physical edge");
                editor.Close();
                check(editor.Result == null && rect.Points.SequenceEqual(rectOriginal), "Closing the editor discards unconfirmed changes");
            }
            var primary = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var previewBounds = new System.Drawing.Rectangle(primary.X, primary.Y, 960, 600);
            var previewZone = new Zone { Number = 3, Polygon = true, ColorRgb = 0x2266cc, OpacityPercent = 50,
                Points = new List<System.Drawing.Point> { new System.Drawing.Point(primary.X + 180, primary.Y + 200),
                    new System.Drawing.Point(primary.X + 660, primary.Y + 200), new System.Drawing.Point(primary.X + 540, primary.Y + 440) } };
            using (var editor = new ZoneEditForm(previewZone, previewBounds))
            using (var bitmap = new System.Drawing.Bitmap(960, 600))
            {
                MoveSelector(editor, 180, 200);
                editor.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, 960, 600));
                bitmap.Save(Path.Combine(directory, "vertex-edit-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void CheckGeometryCommit(MainWindow window, Zone rect, Zone polygon, Action<bool, string> check)
        {
            var oldBounds = rect.Bounds;
            var draft = Geometry.RectangleZone(System.Drawing.Point.Empty, new System.Drawing.Point(199, 149), new System.Drawing.Point(oldBounds.X - 20, oldBounds.Y - 30));
            int number = rect.Number;
            var nativeMask = window.Masks[rect];
            check(window.CommitGeometry(rect, draft) && rect.Number == number && rect.ColorRgb == 0x227755 && rect.OpacityPercent == 50 &&
                window.Masks[rect] == nativeMask && nativeMask.Bounds == rect.Bounds && Alpha(nativeMask) == 128,
                "Saving rectangle vertices updates the native mask while preserving zone identity and appearance");
            var oldPolygon = polygon.Points.ToList();
            var anchor = oldPolygon[0];
            draft = new Zone { Polygon = true, Points = new List<System.Drawing.Point> { anchor,
                new System.Drawing.Point(anchor.X + 199, anchor.Y), new System.Drawing.Point(anchor.X + 100, anchor.Y + 159) } };
            window.SetEnabled(polygon, false); window.ToggleMaster(); window.PassThrough.IsOn = true;
            check(window.CommitGeometry(polygon, draft) && !window.Master && !polygon.Enabled && window.Masks.Values.All(m => !m.Visible) &&
                polygon.ColorRgb == 0x186fe5 && polygon.OpacityPercent == 65 && Alpha(window.Masks[polygon]) == 166 &&
                (Native.GetStyle(window.Masks[polygon].Handle) & Native.WS_EX_TRANSPARENT) != 0,
                "Saving polygon vertices retains master switch, independent switch, opacity, color, and pass-through");
            IntPtr region = Native.CreateRectRgn(0, 0, 0, 0);
            try
            {
                check(Native.GetWindowRgn(window.Masks[polygon].Handle, region) != 0 && Native.PtInRegion(region, 90, 30) && !Native.PtInRegion(region, 10, 110),
                    "Saving polygon vertices rebuilds the native window region to the new outline");
            }
            finally { Native.DeleteObject(region); }
            var committed = polygon.Points.ToList();
            draft.Points[0] = System.Drawing.Point.Empty;
            check(polygon.Points.SequenceEqual(committed) && !window.CommitGeometry(polygon, new Zone { Polygon = true }) && polygon.Points.SequenceEqual(committed),
                "Committed vertices do not share draft storage and invalid edits cannot replace the saved shape");
            window.PassThrough.IsOn = false; window.SetEnabled(polygon, true); window.ToggleMaster();
        }
        private static void CheckEditWorkflow(MainWindow window, Zone zone, Zone other, Action<bool, string> check)
        {
            foreach (bool confirm in new[] { false, true })
            {
                bool entered = false;
                Exception failure = null;
                var original = zone.Points.ToList();
                using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    timer.Tick += (s, e) =>
                    {
                        var editor = System.Windows.Forms.Application.OpenForms.OfType<ZoneEditForm>().FirstOrDefault();
                        if (editor == null) return;
                        timer.Stop();
                        try
                        {
                            entered = true;
                            check(window.IsSelecting && !window.AppWindow.IsVisible && window.Masks.Values.All(m => !m.Visible),
                                "Edit entry hides the controller and masks while showing the existing vertices");
                            window.BeginSelection(true); window.BeginGeometryEdit(other);
                            var appearance = window.OpenAppearanceAsync(zone);
                            check(window.IsSelecting && window.Editor == null && appearance.IsCompleted &&
                                System.Windows.Forms.Application.OpenForms.OfType<ZoneEditForm>().Count() == 1,
                                "Active vertex editing prevents nested selection, shape editing, and appearance dialogs");
                            int x = original[0].X - editor.Left, y = original[0].Y - editor.Top;
                            DragVertex(editor, x, y, x + 12, y + 16);
                            check(zone.Points.SequenceEqual(original), "Modal vertex preview leaves the saved zone untouched");
                            KeySelector(editor, confirm ? System.Windows.Forms.Keys.Enter : System.Windows.Forms.Keys.Escape);
                        }
                        catch (Exception ex) { failure = ex; editor.Close(); }
                    };
                    timer.Start(); window.BeginGeometryEdit(zone); timer.Stop();
                }
                if (failure != null) throw failure;
                check(entered && !window.IsSelecting && window.AppWindow.IsVisible && window.Masks.Values.All(m => m.Visible) &&
                    (confirm ? !zone.Points.SequenceEqual(original) : zone.Points.SequenceEqual(original)),
                    confirm ? "Modal Save updates the existing zone and restores the controller and masks" : "Modal Cancel restores the original zone, controller, and masks");
            }
        }
        private static void DragVertex(System.Windows.Forms.Form editor, int x, int y, int endX, int endY)
        {
            Native.SendMessage(editor.Handle, 0x0201, new IntPtr(1), MousePosition(x, y));
            MoveSelector(editor, endX, endY);
            Native.SendMessage(editor.Handle, 0x0202, IntPtr.Zero, MousePosition(endX, endY));
        }
        private static IntPtr MousePosition(int x, int y) { return new IntPtr((y << 16) | (x & 0xffff)); }
        private static void MoveSelector(System.Windows.Forms.Form selector, int x, int y)
        {
            Native.SendMessage(selector.Handle, 0x0200, IntPtr.Zero, MousePosition(x, y));
        }
        private static void ClickSelector(System.Windows.Forms.Form selector, int x, int y)
        {
            Native.SendMessage(selector.Handle, 0x0201, new IntPtr(1), MousePosition(x, y));
            if (!selector.IsDisposed) Native.SendMessage(selector.Handle, 0x0202, IntPtr.Zero, MousePosition(x, y));
        }
        internal static void KeySelector(System.Windows.Forms.Form selector, System.Windows.Forms.Keys key)
        {
            var message = System.Windows.Forms.Message.Create(selector.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
            // Exercise the form's actual command handler without inheriting the human user's Ctrl/Alt/Shift state.
            var handler = selector.GetType().GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            handler.Invoke(selector, new object[] { message, key });
        }
        private static byte Alpha(MaskForm mask)
        {
            uint key, flags; byte alpha;
            if (!Native.GetLayeredWindowAttributes(mask.Handle, out key, out alpha, out flags)) throw new InvalidOperationException("Cannot read native opacity");
            return alpha;
        }
        internal static async Task CaptureAsync(UIElement element, string path)
        {
            var bitmap = new RenderTargetBitmap();
            for (int attempt = 0; ; attempt++)
            {
                try { await bitmap.RenderAsync(element); break; }
                catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x80004004) && attempt < 2)
                {
                    await Task.Delay(160);
                    if (element is FrameworkElement framework) framework.UpdateLayout();
                    bitmap = new RenderTargetBitmap();
                }
            }
            var buffer = await bitmap.GetPixelsAsync();
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
            var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, buffer.ToArray());
                await encoder.FlushAsync();
            }
        }
    }
}
