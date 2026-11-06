using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace ScreenCurtain
{
    internal static class OverlayStabilityTests
    {
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        private sealed class FullscreenSurface : Forms.Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }
        private sealed class Messages : Forms.NativeWindow, IDisposable
        {
            internal int PositionChanges, StyleChanges, BackgroundErases;
            internal Messages(IntPtr h) { AssignHandle(h); }
            internal void Reset() { PositionChanges = StyleChanges = BackgroundErases = 0; }
            protected override void WndProc(ref Forms.Message message)
            {
                if (message.Msg == 0x0047) PositionChanges++;
                if (message.Msg == 0x007d) StyleChanges++;
                if (message.Msg == 0x0014) BackgroundErases++;
                base.WndProc(ref message);
            }
            public void Dispose() { ReleaseHandle(); }
        }
        private static bool Above(IntPtr upper, IntPtr lower)
        {
            for (IntPtr h = GetWindow(lower, 3); h != IntPtr.Zero; h = GetWindow(h, 3)) if (h == upper) return true;
            return false;
        }
        internal static async Task RunAsync(Action<bool, string> check)
        {
            var screen = Forms.SystemInformation.VirtualScreen;
            var origin = new Point(screen.Right + 4000, screen.Bottom + 4000);
            var rectangle = Geometry.RectangleZone(new Point(0, 0), new Point(199, 159), origin);
            using (var wheelHook = new TemporarySpotlightWheel(delta => { }))
                check(wheelHook.Available, "Temporary spotlight can install the mouse-wheel hook and release it after use");
            using (var spotlight = new SpotlightForm(new Rectangle(origin, new Size(400, 300)), rectangle))
            {
                spotlight.Show();
                IntPtr region = Native.CreateRectRgn(0, 0, 0, 0);
                try
                {
                    check(Native.GetWindowRgn(spotlight.Handle, region) != 0 && !Native.PtInRegion(region, 50, 50) && Native.PtInRegion(region, 250, 200),
                        "Spotlight excludes its selected opening while covering the rest of the screen");
                    check((Native.GetStyle(spotlight.Handle) & Native.WS_EX_TRANSPARENT) != 0 &&
                        Native.GetLayeredWindowAttributes(spotlight.Handle, out uint key, out byte alpha, out uint flags) && alpha > 0 && alpha < 255,
                        "Spotlight is translucent and permits mouse interaction through the dimmed area");
                    var shifted = MainWindow.FollowingOpening(new Size(80, 60), new Point(origin.X + 250, origin.Y + 180));
                    spotlight.UpdateOpening(shifted);
                    check(Native.GetWindowRgn(spotlight.Handle, region) != 0 && Native.PtInRegion(region, 50, 50) && !Native.PtInRegion(region, 250, 180) &&
                        shifted.Bounds.Size == new Size(80, 60), "Following spotlight moves its opening to the cursor while retaining dimensions");
                    var negative = MainWindow.FollowingOpening(new Size(81, 61), new Point(-500, -300));
                    check(negative.Bounds == new Rectangle(-540, -330, 81, 61), "Following spotlight centers correctly on negative-coordinate monitors");
                    var circle = MainWindow.FollowingOpening(new Size(100, 100), new Point(origin.X + 250, origin.Y + 180), true);
                    spotlight.UpdateOpening(circle);
                    check(Native.GetWindowRgn(spotlight.Handle, region) != 0 && !Native.PtInRegion(region, 250, 180) &&
                        Native.PtInRegion(region, 201, 131) && circle.Curve == CurveShape.Ellipse,
                        "Circular following spotlight leaves its center clear while covering its bounding-box corners");
                }
                finally { Native.DeleteObject(region); }
            }
            var polygon = new Zone { Polygon = true, ColorRgb = 0x176be8, OpacityPercent = 50, BorderEnabled = true, BorderWidth = 4, BorderColorRgb = 0xeeeeee, BorderStyle = 1,
                Points = new List<Point> { origin, new Point(origin.X + 179, origin.Y), new Point(origin.X + 90, origin.Y + 139) } };
            using (var initial = new MaskForm(rectangle, true, () => { }, () => { }, () => { }))
            {
                initial.SetMaskVisible(true);
                check((Native.GetStyle(initial.Handle) & Native.WS_EX_TRANSPARENT) != 0, "New visible masks immediately inherit enabled pass-through");
                Native.SetStyle(initial.Handle, Native.GetStyle(initial.Handle) & ~Native.WS_EX_TRANSPARENT);
                initial.SetClickThrough(true);
                check((Native.GetStyle(initial.Handle) & Native.WS_EX_TRANSPARENT) != 0, "Refreshing the same pass-through setting repairs a lost native style");
                initial.SetClickThrough(false); initial.SetMaskVisible(false); initial.SetMaskVisible(true);
                check((Native.GetStyle(initial.Handle) & Native.WS_EX_TRANSPARENT) == 0, "Disabled pass-through remains disabled after hide and show");
            }
            using (var surface = new FullscreenSurface { AutoScaleMode = Forms.AutoScaleMode.None, FormBorderStyle = Forms.FormBorderStyle.None,
                ShowInTaskbar = false, TopMost = true, StartPosition = Forms.FormStartPosition.Manual,
                Bounds = new Rectangle(origin, screen.Size), BackColor = Color.White })
            using (var bottom = new MaskForm(rectangle, false, () => { }, () => { }, () => { }))
            using (var top = new MaskForm(polygon, false, () => { }, () => { }, () => { }))
            {
                surface.Show(); bottom.SetMaskVisible(true); top.SetMaskVisible(true);
                var group = new[] { top.Handle, bottom.Handle };
                OverlayOrder.Ensure(group); await Task.Delay(100);
                IntPtr foreground = Native.GetForegroundWindow();
                using (var bottomMessages = new Messages(bottom.Handle))
                using (var topMessages = new Messages(top.Handle))
                {
                    // Reproduce the old keeper: every pass temporarily reverses overlapping masks.
                    for (int i = 0; i < 4; i++) { Native.Raise(bottom.Handle); Native.Raise(top.Handle); }
                    check(bottomMessages.PositionChanges + topMessages.PositionChanges >= 8,
                        "The previous unconditional keep-on-top loop repeatedly changes overlapping native windows' positions in Z order");
                    bottomMessages.Reset(); topMessages.Reset();
                    bool repaired = false;
                    for (int i = 0; i < 20; i++)
                    {
                        surface.BackColor = i % 2 == 0 ? Color.White : Color.Black; surface.Update();
                        repaired |= OverlayOrder.Ensure(group);
                        bottom.SetClickThrough(false); top.SetClickThrough(false);
                        bottom.ApplyAppearance(rectangle.ColorRgb, rectangle.OpacityPercent); top.ApplyAppearance(polygon.ColorRgb, polygon.OpacityPercent);
                        top.ApplyBorder(BorderSettings.FromZone(polygon));
                        await Task.Delay(10);
                    }
                    check(!repaired && bottomMessages.PositionChanges == 0 && topMessages.PositionChanges == 0,
                        "Animating a full-screen-sized surface underneath overlapping masks does not reorder stable overlay windows");
                    check(bottomMessages.StyleChanges == 0 && topMessages.StyleChanges == 0,
                        "Unchanged pass-through and appearance settings leave native window styles untouched");
                    check(bottomMessages.BackgroundErases == 0 && topMessages.BackgroundErases == 0,
                        "Stable overlays do not erase their backgrounds while the full-screen surface changes");
                    for (int i = 0; i < 4; i++)
                    {
                        Native.Raise(surface.Handle);
                        if (i == 0) check(!OverlayOrder.Ensure(group, Environment.ProcessId) && Above(surface.Handle, top.Handle),
                            "The application's own native popup windows are not pushed underneath overlays");
                        check(OverlayOrder.Ensure(group) && Above(top.Handle, bottom.Handle) && Above(bottom.Handle, surface.Handle),
                            "A full-screen window coming above the masks is repaired in one stable group, pass " + (i + 1));
                        bottomMessages.Reset(); topMessages.Reset();
                        check(!OverlayOrder.Ensure(group) && bottomMessages.PositionChanges == 0 && topMessages.PositionChanges == 0,
                            "A repaired group is not reordered again, pass " + (i + 1));
                    }
                    bottomMessages.Reset(); topMessages.Reset();
                    top.SetClickThrough(true);
                    int styles = topMessages.StyleChanges;
                    top.SetClickThrough(true);
                    Native.GetLayeredWindowAttributes(top.Handle, out uint key, out byte alpha, out uint flags);
                    check((Native.GetStyle(top.Handle) & Native.WS_EX_TRANSPARENT) != 0 && alpha == 128 &&
                        topMessages.StyleChanges == styles && topMessages.PositionChanges == 0,
                        "Changing pass-through preserves alpha and Z order without a frame refresh or repeated style writes");
                    check(Native.GetForegroundWindow() == foreground && top.CurrentColorRgb == 0x176be8 &&
                        top.CurrentBorder.Equals(BorderSettings.FromZone(polygon)) && top.Visible && bottom.Visible,
                        "Full-screen recovery preserves focus, colors, border settings, and mask visibility");
                    Native.SetWindowPos(top.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0213);
                    check(OverlayOrder.Ensure(group) && (Native.GetStyle(top.Handle) & 8) != 0 && Above(top.Handle, bottom.Handle),
                        "A mask whose native topmost state was removed is restored without hiding it");
                    top.ApplyAppearance(polygon.ColorRgb, 0); bottom.SetMaskVisible(false);
                    check(!top.Visible && !bottom.Visible && !OverlayOrder.Ensure(group),
                        "Maintenance never shows hidden or zero-opacity masks");
                }
            }
        }
    }
}
