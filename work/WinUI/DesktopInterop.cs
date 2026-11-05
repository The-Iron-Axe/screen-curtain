using System;
using System.Runtime.InteropServices;

namespace ScreenCurtain
{
    internal static class DesktopInterop
    {
        internal delegate IntPtr SubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] internal struct P { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMax { public P Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    }
}
