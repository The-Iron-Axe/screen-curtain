using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace ScreenCurtain
{
    // Repair an obstructed overlay group as one transaction. An already-correct group is left alone.
    internal static class OverlayOrder
    {
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr batch);

        internal static bool Ensure(IReadOnlyList<IntPtr> topToBottom, int ignoreProcessId = 0)
        {
            var windows = topToBottom.Where(h => h != IntPtr.Zero && IsWindowVisible(h)).Distinct().ToArray();
            if (windows.Length == 0) return false;
            var ranks = windows.Select((h, index) => (h, index)).ToDictionary(v => v.h, v => v.index);
            var above = new List<(IntPtr Handle, Rectangle Bounds)>();
            bool repair = false;
            int remaining = windows.Length;
            var visited = new HashSet<IntPtr>();
            for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && visited.Add(h); h = GetWindow(h, 2))
            {
                bool ours = ranks.TryGetValue(h, out int rank);
                if (!ours && (!IsWindowVisible(h) || IsIconic(h))) continue;
                if (!ours && ignoreProcessId != 0)
                {
                    GetWindowThreadProcessId(h, out uint processId);
                    if (processId == ignoreProcessId) continue; // Keep this app's pickers and flyouts above their owner.
                }
                if (Native.GetWindowRect(h, out Native.RECT r))
                {
                    var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (!ours && (bounds.IsEmpty || (DwmGetWindowAttribute(h, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0))) continue;
                    if (ours)
                    {
                        if ((Native.GetStyle(h) & 8) == 0) repair = true; // WS_EX_TOPMOST
                        foreach (var other in above)
                            if (other.Bounds.IntersectsWith(bounds) && (!ranks.TryGetValue(other.Handle, out int otherRank) || otherRank > rank))
                            { repair = true; break; }
                    }
                    above.Add((h, bounds));
                }
                if (ours && --remaining == 0) break;
            }
            if (!repair || remaining != 0) return false;
            IntPtr batch = BeginDeferWindowPos(windows.Length);
            if (batch == IntPtr.Zero) return false;
            IntPtr after = Native.Topmost;
            foreach (IntPtr h in windows)
            {
                // No movement, resize, activation, frame refresh, or owner reordering.
                batch = DeferWindowPos(batch, h, after, 0, 0, 0, 0, 0x0213);
                if (batch == IntPtr.Zero) return false;
                after = h;
            }
            return EndDeferWindowPos(batch);
        }
    }

    internal sealed class WindowOrderWatcher : IDisposable
    {
        private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private readonly WinEventProc callback;
        private readonly List<IntPtr> hooks = new List<IntPtr>();
        internal WindowOrderWatcher(Action changed)
        {
            callback = (hook, kind, window, objectId, childId, thread, time) =>
            {
                if (kind == 3 || kind == 0x8004 || (objectId == 0 && childId == 0)) changed();
            };
            foreach (var range in new[] { (3u, 3u), (0x8002u, 0x8004u), (0x800bu, 0x800bu) })
            {
                // Out-of-context delivery, skipping this app's own reordering and painting.
                IntPtr hook = SetWinEventHook(range.Item1, range.Item2, IntPtr.Zero, callback, 0, 0, 2);
                if (hook != IntPtr.Zero) hooks.Add(hook);
            }
        }
        public void Dispose()
        {
            foreach (IntPtr hook in hooks) UnhookWinEvent(hook);
            hooks.Clear(); GC.KeepAlive(callback);
        }
    }
}
