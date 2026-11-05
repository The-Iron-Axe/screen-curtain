using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

[assembly: AssemblyTitle("屏幕遮罩")]
[assembly: AssemblyDescription("WinUI 3 亮色屏幕遮罩")]
[assembly: AssemblyVersion("2.7.2.0")]

internal static class Launcher
{
    private delegate uint CabinetCallback(IntPtr context, uint notification, IntPtr parameter1, IntPtr parameter2);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupIterateCabinet(string cabinet, uint reserved, CabinetCallback callback, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CabinetEntry
    {
        public IntPtr Name;
        public uint Size, Error;
        public ushort DosDate, DosTime, Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FullTargetName;
    }
    private const string Digest = "0825ee1335448de485d62c8f3455b798a20747f7294d47fe62aa3f58c6bf1e41";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCurtain", "WinUI");
            string destination = Path.Combine(root, "2.7.2-" + Digest.Substring(0, 12));
            string marker = Path.Combine(destination, ".ready");
            using (Mutex preparation = new Mutex(false, "Local\\ScreenCurtain.Prepare.WinUI.v2"))
            {
                bool acquired = false;
                try
                {
                    try { acquired = preparation.WaitOne(TimeSpan.FromSeconds(90)); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("另一个启动进程仍在准备组件，请稍后重试。");
                    bool ready = File.Exists(marker) && File.ReadAllText(marker) == Digest &&
                        File.Exists(Path.Combine(destination, "ScreenCurtain.WinUI.exe")) &&
                        File.Exists(Path.Combine(destination, "Microsoft.UI.Xaml.dll")) &&
                        File.Exists(Path.Combine(destination, "ScreenCurtain.WinUI.pri")) &&
                        File.Exists(Path.Combine(destination, "App.xbf"));
                    if (!ready)
                    {
                        Directory.CreateDirectory(destination);
                        string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        string cabinet = Path.Combine(destination, ".payload.cab");
                        try
                        {
                            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenCurtain.Payload"))
                            using (FileStream file = File.Create(cabinet)) resource.CopyTo(file);
                            using (SHA256 sha = SHA256.Create())
                            using (FileStream file = File.OpenRead(cabinet))
                            {
                                string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                                if (actual != Digest) throw new IOException("程序组件校验失败，请重新获取 EXE。");
                            }
                            Exception extractionError = null;
                            int targetOffset = Marshal.OffsetOf(typeof(CabinetEntry), "FullTargetName").ToInt32();
                            CabinetCallback callback = (context, notification, p1, p2) =>
                            {
                                try
                                {
                                    if (notification == 0x11)
                                    {
                                        var entry = (CabinetEntry)Marshal.PtrToStructure(p1, typeof(CabinetEntry));
                                        string name = Marshal.PtrToStringUni(entry.Name);
                                        string target = Path.GetFullPath(Path.Combine(destination, name));
                                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("组件路径无效。");
                                        if (target.Length >= 260) throw new PathTooLongException("组件缓存路径过长。");
                                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                                        char[] characters = (target + "\0").ToCharArray();
                                        Marshal.Copy(characters, 0, IntPtr.Add(p1, targetOffset), characters.Length);
                                        return 1; // FILEOP_DOIT
                                    }
                                    if (notification == 0x12) throw new IOException("程序组件包不完整。");
                                    if (notification == 0x13)
                                    {
                                        int error = Marshal.ReadInt32(p1, IntPtr.Size * 2);
                                        if (error != 0) throw new Win32Exception(error);
                                    }
                                    return 0;
                                }
                                catch (Exception error) { extractionError = error; return notification == 0x11 ? 0u : 30u; }
                            };
                            bool extracted = SetupIterateCabinet(cabinet, 0, callback, IntPtr.Zero);
                            int cabinetError = Marshal.GetLastWin32Error(); GC.KeepAlive(callback);
                            if (extractionError != null) throw extractionError;
                            if (!extracted) throw new Win32Exception(cabinetError);
                        }
                        finally { if (File.Exists(cabinet)) File.Delete(cabinet); }
                        File.WriteAllText(marker, Digest);
                    }
                }
                finally { if (acquired) preparation.ReleaseMutex(); }
            }
            var start = new ProcessStartInfo(Path.Combine(destination, "ScreenCurtain.WinUI.exe"))
            {
                WorkingDirectory = destination, UseShellExecute = false,
                Arguments = string.Join(" ", args.Select(Quote).ToArray())
            };
            using (Process app = Process.Start(start))
            {
                // Tests need an exit code; normal use needs no permanently resident bootstrap process.
                if (args.Any(arg => arg.StartsWith("--self-test", StringComparison.Ordinal))) { app.WaitForExit(); return app.ExitCode; }
                return 0;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show("启动失败：" + error.Message, "屏幕遮罩", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static string Quote(string value)
    {
        var output = new StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"') output.Append('\\', slashes * 2 + 1).Append('"');
            else output.Append('\\', slashes).Append(ch);
            slashes = 0;
        }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }
}
