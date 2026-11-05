using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("屏幕遮罩")]
[assembly: AssemblyDescription("WinUI 3 亮色屏幕遮罩")]
[assembly: AssemblyVersion("2.4.2.0")]

internal static class Launcher
{
    private const string Digest = "PAYLOAD_DIGEST";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCurtain", "WinUI");
            string destination = Path.Combine(root, "2.4.2-" + Digest.Substring(0, 12));
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
                        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenCurtain.Payload"))
                        using (ZipArchive zip = new ZipArchive(resource, ZipArchiveMode.Read))
                        {
                            foreach (ZipArchiveEntry entry in zip.Entries)
                            {
                                string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("组件路径无效。");
                                if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                using (Stream source = entry.Open())
                                using (FileStream file = File.Create(target)) source.CopyTo(file);
                            }
                        }
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
            using (Process app = Process.Start(start)) { app.WaitForExit(); return app.ExitCode; }
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
