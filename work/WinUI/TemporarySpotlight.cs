using System;
using System.Runtime.InteropServices;

namespace ScreenCurtain
{
    internal sealed class TemporarySpotlightWheel : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct MouseHookData
        {
            internal DesktopInterop.P Point;
            internal uint MouseData, Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        private readonly HookProc callback;
        private readonly Action<int> onWheel;
        private IntPtr hook;
        internal bool Available { get { return hook != IntPtr.Zero; } }
        internal static bool ShortcutHeld() { return Down(0x11) && Down(0x12) && Down(0x20); }
        private static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal TemporarySpotlightWheel(Action<int> action)
        {
            onWheel = action; callback = Process; hook = SetWindowsHookEx(14, callback, IntPtr.Zero, 0);
        }
        private IntPtr Process(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == 0x020A && ShortcutHeld())
            {
                var input = Marshal.PtrToStructure<MouseHookData>(data);
                int delta = unchecked((short)(input.MouseData >> 16));
                if (delta != 0) { onWheel(delta); return new IntPtr(1); }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    }
}
