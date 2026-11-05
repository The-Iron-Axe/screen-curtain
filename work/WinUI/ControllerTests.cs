using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ScreenCurtain
{
    internal static class ControllerTests
    {
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        private sealed class PassiveWindow : System.Windows.Forms.Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }
        internal static async Task CheckNormalPanelAsync(MainWindow window, Action<bool, string> check)
        {
            bool IsNormal() { return window.AppWindow.Presenter is OverlappedPresenter presenter &&
                !presenter.IsAlwaysOnTop && (Native.GetStyle(window.Handle) & 8) == 0; }
            bool Above(IntPtr upper, IntPtr lower)
            {
                for (IntPtr h = GetWindow(lower, 3); h != IntPtr.Zero; h = GetWindow(h, 3))
                    if (h == upper) return true;
                return false;
            }
            check(IsNormal(), "The control panel starts as a normal, non-topmost window");
            var position = window.AppWindow.Position; var size = window.AppWindow.Size;
            using (var cover = new PassiveWindow { ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = new System.Drawing.Rectangle(position.X, position.Y, size.Width, size.Height) })
            {
                cover.Show();
                Native.SetWindowPos(cover.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0213);
                await Task.Delay(60);
                check(Above(cover.Handle, window.Handle), "An ordinary window can cover the control panel");
                for (int i = 0; i < 3; i++) window.MaintainOverlayOrder();
                check(IsNormal() && Above(cover.Handle, window.Handle), "Background overlay maintenance does not raise or pin the control panel");
                window.ToggleMaster(); window.ToggleMaster();
                bool pass = window.PassThrough.IsOn;
                window.PassThrough.IsOn = !pass; window.PassThrough.IsOn = pass;
                check(IsNormal() && Above(cover.Handle, window.Handle) && window.Masks.Values.All(m => m.Visible && (Native.GetStyle(m.Handle) & 8) != 0),
                    "Master and pass-through switches keep the panel normal and the masks topmost");
                var mask = window.Masks[window.Zones[0]];
                Native.SetWindowPos(mask.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0213);
                await Task.Delay(100);
                window.MaintainOverlayOrder();
                if ((Native.GetStyle(mask.Handle) & 8) == 0) { await Task.Delay(100); window.MaintainOverlayOrder(); }
                bool maskTopmost = (Native.GetStyle(mask.Handle) & 8) != 0, panelNormal = IsNormal(), coverAbove = Above(cover.Handle, window.Handle);
                check(maskTopmost && panelNormal && coverAbove,
                    "Repairing a mask's lost topmost state leaves the control panel behind an ordinary window (mask=" + maskTopmost + ", panel=" + panelNormal + ", cover=" + coverAbove + ")");
            }
        }
        internal static async Task CheckTrayAsync(MainWindow window, Action<bool, string> check)
        {
            var masks = window.Masks.Values.ToArray(); int zones = window.Zones.Count;
            Native.SendMessage(window.Handle, 0x0112, new IntPtr(0xf020), IntPtr.Zero); await Task.Delay(150);
            check(IsIconic(window.Handle) && IsWindowVisible(window.Handle) && window.AppWindow.IsShownInSwitchers &&
                !window.ControllerClosed && !window.ExitRequested && masks.All(m => m.Visible),
                "The native Minimize command keeps the control panel on the taskbar and leaves masks running");
            window.UpdateLayoutConstraints(); window.MaintainOverlayOrder(); await Task.Delay(150);
            check(IsIconic(window.Handle) && IsWindowVisible(window.Handle),
                "Background layout and overlay maintenance leave the panel minimized instead of hiding or restoring it");
            Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(60);
            check(IsIconic(window.Handle) && IsWindowVisible(window.Handle) && masks.All(m => !m.Visible),
                "Global mask hotkeys work while the panel stays minimized on the taskbar");
            Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(60);
            Native.SendMessage(window.Handle, 0x0112, new IntPtr(0xf120), IntPtr.Zero); await Task.Delay(150);
            check(!IsIconic(window.Handle) && IsWindowVisible(window.Handle) && (Native.GetStyle(window.Handle) & 8) == 0 && masks.All(m => m.Visible),
                "The taskbar Restore command returns the same ordinary control panel without changing masks");
            Native.SendMessage(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(80);
            check(!window.AppWindow.IsVisible && !IsWindowVisible(window.Handle) && !window.ControllerClosed && !window.ExitRequested &&
                masks.All(m => !m.IsDisposed && m.Visible) && window.Zones.Count == zones,
                "Closing the controller hides it to the tray while keeping masks and zones alive");
            Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(80);
            check(!window.AppWindow.IsVisible && masks.All(m => !m.Visible), "Global hotkeys continue to work while the controller is in the tray");
            Native.SendMessage(window.Handle, Native.WM_HOTKEY, new IntPtr(1), IntPtr.Zero); await Task.Delay(80);
            window.ShowController(); await Task.Delay(60);
            check(window.AppWindow.IsVisible && masks.All(m => m.Visible) && !window.ControllerClosed,
                "The tray's show-controller action restores the same window and masks");
            check(window.AppWindow.Presenter is OverlappedPresenter presenter && !presenter.IsAlwaysOnTop && (Native.GetStyle(window.Handle) & 8) == 0,
                "Restoring the panel from the tray preserves its normal, non-topmost state");
            Native.SendMessage(window.Handle, 0x0112, new IntPtr(0xf060), IntPtr.Zero); await Task.Delay(80);
            check(!window.AppWindow.IsVisible && !window.ControllerClosed && masks.All(m => m.Visible),
                "Native caption Close command also hides the panel instead of exiting");
            window.ShowController(); await Task.Delay(60);
            var zone = window.Zones[0]; var saved = BorderSettings.FromZone(zone);
            var editing = window.OpenBorderAsync(zone); await Task.Delay(100);
            var draft = new BorderSettings(true, 19, 0xff22aa, 4);
            window.BorderEditor.SetValues(draft);
            Native.SendMessage(window.Handle, 0x0112, new IntPtr(0xf020), IntPtr.Zero); await Task.Delay(150);
            check(IsIconic(window.Handle) && IsWindowVisible(window.Handle) && window.BorderEditor != null &&
                window.Masks[zone].CurrentBorder.Equals(draft) && BorderSettings.FromZone(zone).Equals(saved) && masks.All(m => m.Visible),
                "Minimizing during a settings preview preserves the draft and the live masks without saving or cancelling");
            Native.SendMessage(window.Handle, 0x0112, new IntPtr(0xf120), IntPtr.Zero); await Task.Delay(150);
            Native.SendMessage(window.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero); await editing; await Task.Delay(60);
            check(!window.AppWindow.IsVisible && window.BorderEditor == null && window.Masks[zone].CurrentBorder.Equals(saved) &&
                BorderSettings.FromZone(zone).Equals(saved) && masks.All(m => m.Visible),
                "Closing during a settings preview cancels the draft and keeps the original masks running");
            window.ShowController(); await Task.Delay(60);
        }
        internal static async Task CheckShortcutsAsync(MainWindow window, Action<bool, string> check, string directory, string size)
        {
            window.Shortcuts.IsExpanded = false; window.Root.UpdateLayout(); await Task.Delay(200);
            var header = Descendants(window.Shortcuts).OfType<TextBlock>().First(t => t.Text == "快捷键");
            Rect Before() { return header.TransformToVisual(window.Root).TransformBounds(new Rect(0, 0, header.ActualWidth, header.ActualHeight)); }
            Rect before = Before();
            window.Shortcuts.IsExpanded = true; await Task.Delay(300); window.Root.UpdateLayout();
            Rect after = Before();
            var content = (FrameworkElement)window.Shortcuts.Content;
            Rect detail = content.TransformToVisual(window.Root).TransformBounds(new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            check(Math.Abs(before.X - after.X) <= 1 && Math.Abs(before.Y - after.Y) <= 1,
                "Shortcut button stays at the same position when expanded at " + size + " window size (delta " + (after.Y - before.Y).ToString("0.0") + " DIP)");
            check(detail.Top < after.Top && detail.Bottom <= after.Top + 2 && after.Bottom < window.Root.ActualHeight,
                "Shortcut details expand above the fixed header at " + size + " window size");
            await SelfTest.CaptureAsync(window.Root, Path.Combine(directory, "shortcuts-" + size + "-expanded.png"));
            window.Shortcuts.IsExpanded = false; await Task.Delay(250); window.Root.UpdateLayout();
            check(Math.Abs(Before().Y - before.Y) <= 1, "Collapsing shortcuts leaves the button anchored at " + size + " window size");
        }
        internal static async Task CheckCardLayoutAsync(MainWindow window, Action<bool, string> check, string directory)
        {
            double scale = window.Root.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(window.MinimumWindowSize.Width, window.MinimumWindowSize.Height));
            await Task.Delay(220); window.Root.UpdateLayout(); window.UpdateLayoutConstraints(); await Task.Delay(150); window.Root.UpdateLayout();
            Rect Bounds(FrameworkElement element) { return element.TransformToVisual(window.Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
            void CheckButtons(string label)
            {
                foreach (var zone in window.Zones)
                {
                    Button Find(string prefix) { return Descendants(window.CardScroll).OfType<Button>().Single(b => b.Name == prefix + zone.Number); }
                    Rect look = Bounds(Find("Appearance")), border = Bounds(Find("Border")), edit = Bounds(Find("Edit")), copy = Bounds(Find("Copy")), paste = Bounds(Find("Paste"));
                    check(Math.Abs(look.Left - border.Left) < 1 && Math.Abs(look.Right - border.Right) < 1 &&
                        edit.Left >= look.Right + 5 && Math.Abs(edit.Top - look.Top) < 1 &&
                        copy.Left >= border.Right + 5 && paste.Left > copy.Right && Math.Abs(copy.Top - border.Top) < 1,
                        "Settings buttons align in one column with vertex editing and copy/paste on the right: " + label + " zone " + zone.Number);
                    foreach (var button in new[] { Find("Appearance"), Find("Border"), Find("Edit"), Find("Copy"), Find("Paste") })
                    {
                        Rect outer = Bounds(button);
                        check(Descendants(button).OfType<TextBlock>().All(t => { Rect text = Bounds(t); return text.Left >= outer.Left && text.Right <= outer.Right + 1 && text.Top >= outer.Top && text.Bottom <= outer.Bottom + 1; }),
                            "Card button text fits its bounds: " + label + " " + button.Name);
                    }
                }
            }
            CheckButtons("minimum width");
            int minHeight = window.MinimumWindowSize.Height;
            window.Shortcuts.IsExpanded = true; await Task.Delay(300); window.Root.UpdateLayout();
            check(window.UsesPageScrolling || window.CardScroll.ViewportHeight + 1 >= window.RequiredCardHeight,
                "At minimum size the expanded shortcuts leave room for a complete measured card (viewport " + window.CardScroll.ViewportHeight.ToString("0.0") + ", card " + window.RequiredCardHeight.ToString("0.0") + " DIP)");
            check(window.MinimumWindowSize.Height == minHeight, "Minimum height reserves expanded shortcuts even while collapsed");
            check(window.CardScroll.ScrollableHeight > 0 && window.PageScroll.ScrollableHeight < 1,
                "Multiple zones scroll inside the list at the normal minimum size");
            await SelfTest.CaptureAsync(window.Root, Path.Combine(directory, "style-cards-minimum.png"));
            window.Shortcuts.IsExpanded = false;
            var labels = Descendants(window.Root).OfType<TextBlock>().Select(t => (Text: t, Size: t.FontSize)).ToArray();
            foreach (var label in labels) label.Text.FontSize = label.Size * 1.25;
            await Task.Delay(180); window.Root.UpdateLayout(); window.UpdateLayoutConstraints(); await Task.Delay(180); window.Root.UpdateLayout();
            CheckButtons("125 percent text");
            check(window.RequiredWindowHeight >= minHeight / scale - 20 &&
                (window.UsesPageScrolling || window.CardScroll.ViewportHeight + 1 >= window.RequiredCardHeight),
                "Increasing text size recalculates the minimum or enables whole-page scrolling");
            await SelfTest.CaptureAsync(window.Root, Path.Combine(directory, "style-cards-large-text.png"));
            foreach (var label in labels) label.Text.FontSize = label.Size;
            window.TestWorkAreaHeight = 600; window.Root.UpdateLayout(); window.UpdateLayoutConstraints(); await Task.Delay(250); window.Root.UpdateLayout();
            await SelfTest.CaptureAsync(window.Root, Path.Combine(directory, "style-cards-small-screen.png"));
            check(window.UsesPageScrolling && window.PageScroll.ScrollableHeight > 0 && window.CardScroll.ViewportHeight + 1 >= window.RequiredCardHeight &&
                window.AppWindow.Size.Height <= 600 * scale + 1, "Small work areas keep the window on screen and expose the full page through scrolling (page " +
                window.PageScroll.ScrollableHeight.ToString("0.0") + ", viewport " + window.CardScroll.ViewportHeight.ToString("0.0") + ", card " +
                window.RequiredCardHeight.ToString("0.0") + ", window " + window.AppWindow.Size.Height + ", scale " + scale + ")");
            window.PageScroll.ChangeView(null, window.PageScroll.ScrollableHeight, null, true); await Task.Delay(120); window.Root.UpdateLayout();
            Rect shortcuts = Bounds(window.Shortcuts);
            check(shortcuts.Bottom <= window.Root.ActualHeight + 1 && shortcuts.Top >= 48,
                "Whole-page scrolling reaches the bottom controls without clipping");
            await SelfTest.CaptureAsync(window.Root, Path.Combine(directory, "style-cards-small-screen.png"));
            window.TestWorkAreaHeight = null; window.UpdateLayoutConstraints(); await Task.Delay(180); window.Root.UpdateLayout();
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(520 * scale), window.MinimumWindowSize.Height));
            await Task.Delay(180); window.Root.UpdateLayout();
        }
        internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i); yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
