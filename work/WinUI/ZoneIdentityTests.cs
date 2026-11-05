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
    internal static class ZoneIdentityTests
    {
        internal static void CheckNumbering(MainWindow window, P origin, Action<bool, string> check)
        {
            Zone Rectangle() { return Geometry.RectangleZone(P.Empty, new P(99, 79), origin); }
            var first = Rectangle(); var second = Rectangle();
            window.AddZone(first); window.AddZone(second);
            check(first.Number == 1 && second.Number == 2, "An empty panel starts zone numbering at one");
            var removedThird = Rectangle(); window.AddZone(removedThird);
            check(first.Number == 1 && second.Number == 2 && removedThird.Number == 3, "Existing zones one and two are followed by a newly created zone three");
            window.RemoveZone(removedThird); var third = Rectangle(); window.AddZone(third);
            check(third.Number == 3, "Deleting the highest-numbered zone lets the next new zone follow the current maximum instead of historical IDs");
            window.RemoveZone(second); var fourth = Rectangle(); window.AddZone(fourth);
            check(first.Number == 1 && third.Number == 3 && fourth.Number == 4, "Existing zones one and three are followed by four without filling the gap or renumbering existing zones");
            window.RemoveZone(fourth);
            window.AddImportedZones(new[] { Rectangle(), Rectangle() });
            check(window.Zones.Select(z => z.Number).SequenceEqual(new[] { 1, 3, 4, 5 }), "Batch import starts above the current maximum and assigns consecutive IDs");
            window.RemoveZone(window.Zones.Last()); var drawn = Rectangle(); window.AddDrawnZone(drawn);
            check(drawn.Number == 5 && window.Zones.Select(z => z.Number).SequenceEqual(new[] { 1, 3, 4, 5 }), "Newly drawn shapes use the current maximum after deleting an imported final zone");
            window.RemoveZone(first); var sixth = Rectangle(); window.AddZone(sixth);
            check(third.Number == 3 && drawn.Number == 5 && sixth.Number == 6, "Removing a lower-numbered zone preserves existing identities and continues above the highest remaining ID");
            foreach (var zone in window.Zones.ToArray()) window.RemoveZone(zone);
            var afterDelete = Rectangle(); window.AddZone(afterDelete);
            check(afterDelete.Number == 1, "Deleting the last zone resets the next manually created zone to one");
            window.ClearZones(); var afterClear = Rectangle(); window.AddZone(afterClear);
            check(afterClear.Number == 1, "Clear All resets numbering before the next manually created zone");
            window.ClearZones();
            window.AddImportedZones(new[] { Rectangle(), Rectangle() });
            check(window.Zones.Select(z => z.Number).SequenceEqual(new[] { 1, 2 }), "A batch imported into a cleared panel starts at one and increments within the batch");
            foreach (var zone in window.Zones.ToArray()) window.RemoveZone(zone);
            window.AddImportedZones(new[] { Rectangle() });
            check(window.Zones.Single().Number == 1, "Import also restarts at one after individually deleting every zone");
            window.ClearZones();
        }

        internal static async Task CheckRenameAsync(MainWindow window, Zone rect, Zone polygon, Action<bool, string> check, string directory)
        {
            string original = rect.Name; var points = rect.Points.ToArray(); int number = rect.Number;
            var style = ZoneStyle.FromZone(rect); var mask = window.Masks[rect];
            var task = window.OpenRenameAsync(rect); await Task.Delay(180);
            var editor = window.RenameEditor;
            check(editor != null && editor.Input.Text == rect.DisplayName && editor.Dialog.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Light,
                "Rename opens a light dialog prefilled with the current zone name");
            window.BeginSelection(true); window.BeginGeometryEdit(rect);
            var nested = window.OpenAppearanceAsync(rect); var nestedRename = window.OpenRenameAsync(polygon);
            check(!window.IsSelecting && window.Editor == null && nested.IsCompleted && nestedRename.IsCompleted && window.RenameEditor == editor,
                "Renaming prevents nested drawing, geometry editing, appearance dialogs, and another rename");
            editor.Input.Text = "临时名称"; await Task.Delay(60);
            check(rect.Name == original, "Typing a name leaves the saved zone unchanged until confirmation");
            await SelfTest.CaptureAsync(editor.Dialog, Path.Combine(directory, "winui-rename.png"));
            editor.CompleteForTesting(false); await task;
            check(rect.Name == original, "Cancel rename preserves the original name");

            task = window.OpenRenameAsync(rect); await Task.Delay(120); editor = window.RenameEditor;
            editor.Input.Text = "   "; await Task.Delay(60);
            check(!editor.Dialog.IsPrimaryButtonEnabled, "An empty or whitespace-only name cannot be confirmed");
            const string custom = "字幕 & 片头 <A>";
            editor.Input.Text = "  " + custom + "  "; await Task.Delay(60);
            editor.CompleteForTesting(true); await task;
            var title = ControllerTests.Descendants(window.CardScroll).OfType<TextBlock>().Single(t => t.Name == "ZoneName" + rect.Number);
            check(rect.Name == custom && rect.DisplayName == custom && title.Text == custom &&
                rect.Number == number && rect.Points.SequenceEqual(points) && ZoneStyle.FromZone(rect).Equals(style) && window.Masks[rect] == mask && mask.Visible,
                "Confirm trims and displays the custom name without changing ID, geometry, appearance, or the live mask");

            window.ToggleMaster(); window.SetEnabled(polygon, false);
            task = window.OpenRenameAsync(polygon); await Task.Delay(120); editor = window.RenameEditor;
            string longName = new string('长', RenameEditor.MaxNameLength);
            editor.Input.Text = longName; await Task.Delay(60); editor.CompleteForTesting(true); await task;
            check(polygon.Name == longName && !window.Master && !polygon.Enabled && window.Masks.Values.All(m => !m.Visible),
                "A maximum-length polygon name keeps the master and independent visibility switches unchanged");
            window.SetEnabled(polygon, true); window.ToggleMaster();

            task = window.OpenRenameAsync(rect); await Task.Delay(120);
            window.RenameEditor.Input.Text = "未确认的改名";
            Native.SendMessage(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero); await task;
            check(rect.Name == custom && window.RenameEditor == null && !window.AppWindow.IsVisible,
                "Closing the panel cancels an unconfirmed rename before hiding to the tray");
            window.ShowController(); await Task.Delay(100);

            string xml;
            using (var writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, new Settings { Zones = new List<Zone>(window.Zones) }); xml = writer.ToString(); }
            Settings restored;
            using (var reader = new StringReader(xml)) restored = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
            check(restored.Zones[0].Name == custom && restored.Zones[1].Name == longName &&
                restored.Zones.Zip(window.Zones).All(pair => pair.First.Number == pair.Second.Number && pair.First.Points.SequenceEqual(pair.Second.Points)),
                "Configuration round-trips custom names including XML punctuation while preserving IDs and vertices");
        }
    }
}
