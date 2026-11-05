using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using P = System.Drawing.Point;
using R = System.Drawing.Rectangle;

namespace ScreenCurtain
{
    internal static class TransferTests
    {
        internal static void RunCodec(Action<bool, string> check, string directory)
        {
            var hd = new R(0, 0, 1920, 1080);
            const string triangle = "[0,0],[100,0],[0,100]";
            var parsed = ShapeTextCodec.Parse("\ufeff\r\n [100,100], [100,300], [500,300], [500,100]\r\n\n[-100,-50],[80,-50],[0,120],[-100,-50]\n", CoordinateMode.Pixels, hd);
            check(parsed.Count == 2 && !parsed[0].Polygon && parsed[0].Points[0] == new P(100, 100) && parsed[0].Points[1] == new P(500, 100) &&
                parsed[1].Polygon && parsed[1].Points.Count == 3 && parsed[1].Points[0] == new P(-100, -50),
                "Pixel TXT accepts multiple shapes, blank lines, negative coordinates, and a repeated closing vertex");
            check(ShapeTextCodec.Parse("[0,0],[100,100],[100,0],[0,100]", CoordinateMode.Pixels, hd)[0].Polygon,
                "Crossed rectangle corners retain their polygon outline rather than becoming a rectangle");
            var percent = ShapeTextCodec.Parse("[0%,0%],[50%,0%],[50%,50%],[0%,50%]", CoordinateMode.ScreenPercent, hd)[0];
            check(!percent.Polygon && percent.Points[2] == new P(960, 540), "Percentage coordinates convert against 1920 by 1080 screen dimensions");
            var fractions = ShapeTextCodec.Parse("[25,12.5],[75,12.5],[50,62.5]", CoordinateMode.ScreenPercent, hd)[0];
            check(fractions.Points[0] == new P(480, 135) && fractions.Points[2] == new P(960, 675),
                "Unmarked numeric percentages and decimal percentages are accepted in percentage mode");
            var uhd = ShapeTextCodec.Parse("[25%,12.5%],[75%,12.5%],[50%,62.5%]", CoordinateMode.ScreenPercent, new R(0, 0, 3840, 2160))[0];
            check(uhd.Points.Zip(fractions.Points).All(pair => pair.First.X == 2 * pair.Second.X && pair.First.Y == 2 * pair.Second.Y),
                "Percentage import scales the same shape to a different screen resolution");
            var leftMonitor = new R(-1920, -1080, 1920, 1080);
            var fitted = TransferDialog.FitReference(leftMonitor, 2);
            check(fitted == new R(-1635, -1080, 1350, 1080), "5:4 reference fits a 16:9 monitor with centered side margins and a negative origin");
            var letterbox = TransferDialog.FitReference(new R(0, 0, 1280, 1024), 1);
            check(letterbox == new R(0, 152, 1280, 720), "16:9 reference fits a 5:4 monitor with centered top and bottom margins");
            var pixelTriangle = ShapeTextCodec.Parse("[0,0],[720,0],[0,576]", CoordinateMode.Pixels, hd);
            var scaledPixels = ShapeTextCodec.ScalePixels(pixelTriangle, new System.Drawing.Size(720, 576), fitted)[0];
            check(scaledPixels.Points.SequenceEqual(new[] { new P(-1635, -1080), new P(-285, -1080), new P(-1635, 0) }),
                "Pixel coordinates scale uniformly into a centered reference on a negative-origin monitor");
            var centered = ShapeTextCodec.Parse("[0%,0%],[100%,0%],[0%,100%]", CoordinateMode.ScreenPercent, fitted)[0];
            check(centered.Points[0] == new P(-1635, -1080) && centered.Points[1] == new P(-285, -1080) &&
                ShapeTextCodec.Parse(ShapeTextCodec.Serialize(new[] { centered }, CoordinateMode.ScreenPercent, fitted), CoordinateMode.ScreenPercent, fitted)[0].Points.SequenceEqual(centered.Points),
                "Fitted reference import and export preserve coordinates and aspect ratio");
            var shifted = ShapeTextCodec.Parse("[25%,12.5%],[75%,12.5%],[50%,62.5%]", CoordinateMode.ScreenPercent, leftMonitor)[0];
            check(shifted.Points[0] == new P(-1440, -945), "Percentage coordinates respect the selected monitor's negative desktop origin");
            string pixels = ShapeTextCodec.Serialize(parsed, CoordinateMode.Pixels, hd);
            var restored = ShapeTextCodec.Parse(pixels, CoordinateMode.Pixels, hd);
            check(pixels.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length == 2 &&
                restored.Zip(parsed).All(pair => pair.First.Polygon == pair.Second.Polygon && pair.First.Points.SequenceEqual(pair.Second.Points)),
                "Pixel export contains only one coordinate list per line and imports without geometry loss");
            var arbitrary = new Zone { Polygon = true, Points = new List<P> { new P(-333, -222), new P(1919, 1079), new P(987, -99) } };
            string ratioText = ShapeTextCodec.Serialize(new[] { arbitrary }, CoordinateMode.ScreenPercent, hd);
            check(ratioText.Contains("%") && ShapeTextCodec.Parse(ratioText, CoordinateMode.ScreenPercent, hd)[0].Points.SequenceEqual(arbitrary.Points),
                "Percentage export preserves sub-percent precision, edge pixels, and positions outside the reference screen");
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                string invariant = ShapeTextCodec.Serialize(new[] { arbitrary }, CoordinateMode.ScreenPercent, hd);
                check(invariant == ratioText && ShapeTextCodec.Parse(invariant, CoordinateMode.ScreenPercent, hd)[0].Points.SequenceEqual(arbitrary.Points),
                    "TXT decimal punctuation is independent of Windows number formatting");
            }
            finally { CultureInfo.CurrentCulture = culture; }
            check(Error("\n" + triangle + "\n[0,0],oops", CoordinateMode.Pixels, hd).Contains("第 3 行"), "Import errors report the actual TXT line number including blank lines");
            check(new[] { triangle + ",", triangle + " junk", "[0,0];[100,0];[0,100]", "[1,2],[3,4]", "[0,0],[10,10],[20,20]", "" }
                .All(text => Error(text, CoordinateMode.Pixels, hd).Length > 0), "Malformed rows and zero-area shapes cannot be partially imported");
            check(Error("[0%,0%],[50%,0%],[0%,50%]", CoordinateMode.Pixels, hd).Contains("屏幕比例") &&
                Error("[0.5,0],[100,0],[0,100]", CoordinateMode.Pixels, hd).Contains("整数"), "Pixel mode rejects percentage markers and fractional pixels with an explanation");
            check(Error("[999999999999999999999999999999,0],[100,0],[0,100]", CoordinateMode.Pixels, hd).Length > 0 &&
                Error("[99999999999999999%,0%],[50%,0%],[0%,50%]", CoordinateMode.ScreenPercent, hd).Length > 0 &&
                Error("[0%,0%],[0.000001%,0%],[0%,0.000001%]", CoordinateMode.ScreenPercent, hd).Length > 0,
                "Overflowing numbers and percentage shapes that collapse after rounding are rejected");
            var circle = Enumerable.Range(0, 256).Select(i => new P((int)Math.Round(1000 + 800 * Math.Cos(i * Math.PI / 128)),
                (int)Math.Round(1000 + 800 * Math.Sin(i * Math.PI / 128)))).ToList();
            string vertices = string.Join(",", circle.Select(p => "[" + p.X + "," + p.Y + "]"));
            check(ShapeTextCodec.Parse(vertices + "," + vertices.Split(']')[0] + "]", CoordinateMode.Pixels, hd)[0].Points.Count == 256 &&
                Error(vertices + ",[123,456]", CoordinateMode.Pixels, hd).Length > 0 &&
                Error(string.Join("\n", Enumerable.Repeat(triangle, 101)), CoordinateMode.Pixels, hd).Length > 0,
                "TXT enforces vertex and shape limits while accepting a repeated closing point at the limit");
            string path = Path.Combine(directory, "shape transfer test.txt");
            ShapeTextCodec.WriteFile(path, pixels); ShapeTextCodec.WriteFile(path, ratioText);
            check(ShapeTextCodec.ReadFile(path) == ratioText, "TXT export writes UTF-8 and safely replaces an existing file");
            File.WriteAllText(path, pixels, System.Text.Encoding.Unicode);
            check(ShapeTextCodec.ReadFile(path) == pixels, "TXT import also accepts UTF-16 files with a byte order mark");
        }
        private static string Error(string text, CoordinateMode mode, R screen)
        {
            try { ShapeTextCodec.Parse(text, mode, screen); return ""; } catch (FormatException ex) { return ex.Message; }
        }
        internal static async Task RunDialogsAsync(MainWindow window, Action<bool, string> check, string directory)
        {
            var existing = window.Zones.ToArray(); int count = existing.Length;
            var task = window.OpenTransferAsync(true); await Task.Delay(150);
            var transfer = window.Transfer;
            check(transfer != null && transfer.Dialog.ActualTheme == ElementTheme.Light && !transfer.Dialog.IsPrimaryButtonEnabled && window.Masks.Values.All(m => !m.Visible),
                "Import uses a light WinUI dialog and temporarily hides masks without altering the master switch");
            check(!transfer.TextPreview.IsReadOnly && transfer.TextPreview.Visibility == Visibility.Visible && transfer.TextPreview.AcceptsReturn,
                "Import offers an editable multiline coordinate field immediately without choosing a file");
            await SelfTest.CaptureAsync(transfer.Dialog, Path.Combine(directory, "winui-import-empty.png"));
            window.BeginSelection(true); window.BeginGeometryEdit(existing[0]);
            var blocked = window.OpenAppearanceAsync(existing[0]);
            check(!window.IsSelecting && window.Editor == null && blocked.IsCompleted && window.Transfer == transfer,
                "Transfer dialogs prevent drawing or editing reentry");
            transfer.TextPreview.Text = "[10%,10%],[50%,10%],[30%,60%]";
            await Task.Delay(60);
            check(!transfer.Dialog.IsPrimaryButtonEnabled && transfer.Status.Severity == InfoBarSeverity.Error, "Import preview catches a mismatched coordinate mode");
            transfer.ModePicker.SelectedIndex = 1;
            var basis = ((CoordinateScreen)transfer.ScreenPicker.SelectedItem).Bounds;
            check(transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == 1 && transfer.ParsedZones[0].Points[0] ==
                new P(basis.X + (int)Math.Round(basis.Width * 0.1, MidpointRounding.AwayFromZero), basis.Y + (int)Math.Round(basis.Height * 0.1, MidpointRounding.AwayFromZero)),
                "Changing to percentage mode reinterprets directly entered coordinates against the chosen physical screen");
            transfer.ModePicker.SelectedIndex = 0;
            transfer.TextPreview.Text = "[0,0],[720,0],[0,576]";
            transfer.PixelScaling.IsChecked = true; transfer.RatioPicker.SelectedIndex = 2;
            var fit = TransferDialog.FitReference(((CoordinateScreen)transfer.ScreenPicker.SelectedItem).Bounds, 2);
            check(transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones[0].Points[0] == fit.Location &&
                transfer.ParsedZones[0].Points[1].X == fit.Right && transfer.ParsedZones[0].Points[2].Y == fit.Bottom,
                "Pixel import optionally converts coordinates from the selected reference resolution");
            transfer.PixelScaling.IsChecked = false; transfer.RatioPicker.SelectedIndex = 0; transfer.ModePicker.SelectedIndex = 1;
            await Task.Delay(300); window.Root.UpdateLayout();
            await SelfTest.CaptureAsync(transfer.Dialog, Path.Combine(directory, "winui-import.png"));
            transfer.LoadText("[0%,0%],[50%,0%],[0%,50%]\ninvalid", "错误测试.txt");
            check(!transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == 0 && transfer.Status.Message.Contains("第 2 行"),
                "An invalid later row disables the whole import instead of leaving earlier shapes ready to commit");
            string corrected = "[0%,0%],[50%,0%],[0%,50%]\r\n[60%,10%],[80%,10%],[80%,30%],[60%,30%]";
            transfer.TextPreview.Text = corrected;
            await Task.Delay(60);
            check(transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == 2 && transfer.ParsedZones[0].Polygon && !transfer.ParsedZones[1].Polygon &&
                transfer.TextPreview.Text.Replace("\r\n", "\n").Replace('\r', '\n') == corrected.Replace("\r\n", "\n") && window.Zones.Count == count,
                "Editing loaded file contents fixes errors and previews multiple shapes without changing the draft or saved zones");
            transfer.TextPreview.Text = " \r\n ";
            await Task.Delay(60);
            check(!transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == 0 && !transfer.Status.IsOpen && transfer.TextPreview.Visibility == Visibility.Visible,
                "Clearing the input removes stale parsed shapes while keeping the editor available");
            transfer.ModePicker.SelectedIndex = 0;
            string longInput = new string(' ', 12020) + "[0,0],[100,0],[0,100]";
            transfer.LoadText(longInput, "完整内容测试.txt");
            check(transfer.TextPreview.Text == longInput && transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == 1,
                "Loading a file keeps all editable coordinates beyond the former preview truncation limit");
            transfer.CompleteForTesting(false); await task;
            check(window.Zones.Count == count && window.Master && window.Masks.Values.All(m => m.Visible), "Cancel import restores masks and preserves every existing zone");
            window.ToggleMaster(); window.SetEnabled(existing[1], false);
            task = window.OpenTransferAsync(true); await Task.Delay(100); transfer = window.Transfer;
            check(transfer.TextPreview.Text.Length == 0 && !transfer.Dialog.IsPrimaryButtonEnabled,
                "Reopening import starts with an empty draft instead of a cancelled draft");
            string input = ShapeTextCodec.Serialize(existing, CoordinateMode.Pixels, basis);
            transfer.TextPreview.Text = input;
            await Task.Delay(60);
            check(transfer.Dialog.IsPrimaryButtonEnabled && transfer.ParsedZones.Count == count && transfer.ParsedZones.Zip(existing).All(pair => pair.First.Points.SequenceEqual(pair.Second.Points)),
                "Directly entered pixel coordinates parse every line and enable import without a TXT file");
            var draft = transfer.ParsedZones;
            transfer.CompleteForTesting(true); await task;
            var added = window.Zones.Skip(count).ToArray();
            check(added.Length == count && window.Zones.Take(count).SequenceEqual(existing) && added.Zip(existing).All(pair => pair.First.Points.SequenceEqual(pair.Second.Points)) &&
                added.All(z => z.ColorRgb == 0 && z.OpacityPercent == 100 && z.Enabled) && window.Zones.Select(z => z.Number).Distinct().Count() == window.Zones.Count,
                "Confirm appends separately editable shapes with unique IDs and black opaque defaults while keeping existing zones");
            check(!window.Master && !existing[1].Enabled && window.Masks.Values.All(m => !m.Visible), "Import preserves the existing master and individual visibility switches");
            draft[0].Points[0] = P.Empty;
            check(added[0].Points[0] != P.Empty, "Imported geometry does not share mutable storage with the preview");
            bool rejected = false;
            try { window.AddImportedZones(new List<Zone> { new Zone() }); } catch (FormatException) { rejected = true; }
            check(rejected && window.Zones.Count == count + added.Length, "Invalid import batches cannot change the existing collection");
            foreach (var zone in added) window.RemoveZone(zone);
            window.SetEnabled(existing[1], true); window.ToggleMaster();
            var limited = new TransferDialog(true, Enumerable.Repeat(existing[0], ShapeTextCodec.MaxShapes - 1), window.Root.XamlRoot, window.Handle);
            var limitedTask = limited.ShowAsync(); await Task.Delay(150);
            limited.TextPreview.Text = input;
            await Task.Delay(60);
            check(!limited.Dialog.IsPrimaryButtonEnabled && limited.ParsedZones.Count == 0 && limited.Status.Message.Contains("当前还可添加 1 个选区"),
                "Direct input obeys the remaining zone limit without allowing a partial import");
            limited.CompleteForTesting(false); await limitedTask;
            task = window.OpenTransferAsync(false); await Task.Delay(100); transfer = window.Transfer;
            check(transfer.TextPreview.IsReadOnly && transfer.OutputText == ShapeTextCodec.Serialize(existing, CoordinateMode.Pixels, basis), "Export preview stays read-only and includes all existing shapes in pixel mode");
            transfer.ModePicker.SelectedIndex = 1;
            check(transfer.OutputText.Contains("%") && transfer.Dialog.IsPrimaryButtonEnabled, "Export mode switch generates percentage TXT without touching saved geometry");
            transfer.CompleteForTesting(false); await task;
            check(window.Zones.Count == count && window.Masks.Values.All(m => m.Visible), "Cancel export returns to the original masks");
        }
    }
}
