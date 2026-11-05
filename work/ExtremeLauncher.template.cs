using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("屏幕遮罩")]
[assembly: AssemblyDescription("WinUI 3 亮色屏幕遮罩")]
[assembly: AssemblyVersion("__ASSEMBLY_VERSION__")]

internal static class Launcher
{
    private const string Version = "__VERSION__";
    private const string PayloadDigest = "__PAYLOAD_DIGEST__";
    private const string ManifestDigest = "__MANIFEST_DIGEST__";
    private static readonly string CacheCompression = "__CACHE_COMPRESSION__";
    private const string ArchiveFormat = "__ARCHIVE_FORMAT__";
    private sealed class Entry { public string Path, Digest; public long Length; }
    private sealed class Manifest { public string Executable; public readonly List<Entry> Entries = new List<Entry>(); }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            bool testing = args.Length == 2 && (args[0] == "--self-test" || args[0] == "--self-test-numbering" || args[0] == "--self-test-resources");
            string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCurtain", "WinUI");
            string testRoot = testing ? Environment.GetEnvironmentVariable("SCREENCURTAIN_TEST_CACHE_ROOT") : null;
            if (!string.IsNullOrWhiteSpace(testRoot)) root = System.IO.Path.GetFullPath(testRoot);
            Directory.CreateDirectory(root);
            string name = SafeVersion(Version) + "-" + PayloadDigest.Substring(0, 12) + "-" + ManifestDigest.Substring(0, 8);
            string destination = System.IO.Path.Combine(root, name);
            Manifest manifest = ReadManifest();
            using (Mutex mutex = new Mutex(false, "Local\\ScreenCurtain.Prepare.WinUI.v2"))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromMinutes(10)); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("另一个启动进程仍在准备组件，请稍后重试。");
                    CleanupAbandonedStages(root);
                    if (!CacheValid(destination, manifest)) Prepare(root, destination, manifest, testing);
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
            var start = new ProcessStartInfo(CheckedPath(destination, manifest.Executable))
            {
                WorkingDirectory = destination, UseShellExecute = false,
                Arguments = string.Join(" ", args.Select(Quote).ToArray())
            };
            using (Process app = Process.Start(start))
            {
                if (testing) { app.WaitForExit(); return app.ExitCode; }
                return 0;
            }
        }
        catch (Exception error)
        {
            if (args.Length == 2 && args[0].StartsWith("--self-test", StringComparison.Ordinal))
            {
                try { File.WriteAllText(args[1] + ".launcher-error.txt", error.ToString()); } catch { }
            }
            else MessageBox.Show("启动失败：" + error.Message, "屏幕遮罩", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static string SafeVersion(string version)
    {
        string value = new string(version.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_').ToArray());
        if (value.Length == 0 || value == "." || value == "..") throw new IOException("版本信息无效。");
        return value;
    }
    private static byte[] ResourceBytes(string name)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (source == null) throw new IOException("缺少程序组件：" + name);
            using (var buffer = new MemoryStream()) { source.CopyTo(buffer); return buffer.ToArray(); }
        }
    }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
    private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    private static string CheckedPath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || System.IO.Path.IsPathRooted(relative)) throw new IOException("组件路径无效。");
        string prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("组件路径无效。");
        return full;
    }
    private static Manifest ReadManifest()
    {
        byte[] bytes = ResourceBytes("ScreenCurtain.Manifest");
        if (Hash(bytes) != ManifestDigest) throw new IOException("程序组件清单校验失败。");
        var manifest = new Manifest();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = new StringReader(Encoding.UTF8.GetString(bytes)))
        {
            string header = reader.ReadLine(); string[] fields = (header ?? "").Split('\t');
            if (fields.Length != 5 || fields[0] != "SCMANIFEST" || fields[1] != "1" || fields[2] != ArchiveFormat || fields[3] != Version)
                throw new IOException("程序组件清单格式不受支持。");
            manifest.Executable = Encoding.UTF8.GetString(Convert.FromBase64String(fields[4]));
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                fields = line.Split('\t'); long length;
                if (fields.Length != 3 || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0 || fields[1].Length != 64)
                    throw new IOException("程序组件清单内容无效。");
                var entry = new Entry { Length = length, Digest = fields[1], Path = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2])) };
                CheckedPath(System.IO.Path.GetTempPath(), entry.Path);
                if (!seen.Add(entry.Path.Replace('/', '\\'))) throw new IOException("组件路径重复。");
                manifest.Entries.Add(entry);
            }
        }
        if (!seen.Contains(manifest.Executable.Replace('/', '\\'))) throw new IOException("组件清单缺少启动程序。");
        return manifest;
    }
    private static bool CacheValid(string directory, Manifest manifest)
    {
        try
        {
            string marker = System.IO.Path.Combine(directory, ".ready");
            if (!File.Exists(marker) || File.ReadAllText(marker) != PayloadDigest + "\n" + ManifestDigest) return false;
            foreach (Entry entry in manifest.Entries)
            {
                string path = CheckedPath(directory, entry.Path);
                if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) return false;
                using (var sha = SHA256.Create()) using (var file = File.OpenRead(path))
                    if (Hex(sha.ComputeHash(file)) != entry.Digest) return false;
            }
            return true;
        }
        catch { return false; }
    }
    private static void CopyResource(string resource, string path)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
        {
            if (source == null) throw new IOException("缺少程序组件：" + resource);
            using (FileStream output = File.Create(path)) source.CopyTo(output);
        }
    }
    private static void Prepare(string root, string destination, Manifest manifest, bool testing)
    {
        string stage = CheckedPath(root, ".screen-curtain-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        File.WriteAllText(System.IO.Path.Combine(stage, ".stage-owner"), "screen-curtain-stage-v1\n" + PayloadDigest);
        string payload = System.IO.Path.Combine(stage, ".payload.xz");
        try
        {
            CopyResource("ScreenCurtain.Payload", payload);
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(payload))
                if (Hex(sha.ComputeHash(file)) != PayloadDigest) throw new IOException("程序组件校验失败，请重新获取 EXE。");
#if LZMS
            using (Stream input = new LzmsStream(payload)) ExtractFiles(input, stage, manifest);
#else
            string decoderFolder = System.IO.Path.Combine(stage, ".decoder");
            Directory.CreateDirectory(decoderFolder);
            string decoder = System.IO.Path.Combine(decoderFolder, "xzdec.exe");
            CopyResource("ScreenCurtain.XzDecoder", decoder);
            CopyResource("ScreenCurtain.LzmaLibrary", System.IO.Path.Combine(decoderFolder, "liblzma.dll"));
            CopyResource("ScreenCurtain.VcRuntime", System.IO.Path.Combine(decoderFolder, "vcruntime140.dll"));
            var info = new ProcessStartInfo(decoder, Quote(payload)) { WorkingDirectory = decoderFolder, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            string decoderError = "";
            using (Process process = Process.Start(info))
            {
                var stderr = new Thread(() => { decoderError = process.StandardError.ReadToEnd(); }); stderr.IsBackground = true; stderr.Start();
                try
                {
                    ExtractFiles(process.StandardOutput.BaseStream, stage, manifest);
                    process.WaitForExit(); stderr.Join();
                    if (process.ExitCode != 0) throw new IOException("组件解压失败：" + decoderError);
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } stderr.Join(5000); }
            }
            Directory.Delete(decoderFolder, true);
            string notice = System.IO.Path.Combine(stage, "ThirdParty", "XZ"); Directory.CreateDirectory(notice);
            if (!File.Exists(System.IO.Path.Combine(notice, "COPYING"))) CopyResource("ScreenCurtain.XzLicense", System.IO.Path.Combine(notice, "COPYING"));
            if (!File.Exists(System.IO.Path.Combine(notice, "COPYING.0BSD"))) CopyResource("ScreenCurtain.XzLicense0", System.IO.Path.Combine(notice, "COPYING.0BSD"));
#endif
            File.Delete(payload);
            if (!(testing && Environment.GetEnvironmentVariable("SCREENCURTAIN_TEST_NO_COMPACT") == "1")) TryCompact(stage);
            File.WriteAllText(System.IO.Path.Combine(stage, ".ready"), PayloadDigest + "\n" + ManifestDigest);
            string saved = null;
            if (Directory.Exists(destination))
            {
                saved = destination + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(destination, saved);
            }
            try { Directory.Move(stage, destination); }
            catch { if (saved != null && Directory.Exists(saved) && !Directory.Exists(destination)) Directory.Move(saved, destination); throw; }
            File.Delete(System.IO.Path.Combine(destination, ".stage-owner"));
            if (saved != null)
            {
                try
                {
                    string prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                    string full = System.IO.Path.GetFullPath(saved);
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && full.StartsWith(System.IO.Path.GetFullPath(destination) + ".previous-", StringComparison.OrdinalIgnoreCase)) Directory.Delete(full, true);
                }
                catch { /* A previous running process may still hold a component; leave the cache for recovery. */ }
            }
        }
        finally
        {
            // Only the uniquely created staging directory bearing our marker is eligible for cleanup.
            if (Directory.Exists(stage) && File.Exists(System.IO.Path.Combine(stage, ".stage-owner")))
                try { Directory.Delete(stage, true); } catch { }
        }
    }
    private static void ExtractFiles(Stream input, string stage, Manifest manifest)
    {
        byte[] buffer = new byte[1024 * 1024];
        foreach (Entry entry in manifest.Entries)
        {
            string target = CheckedPath(stage, entry.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            using (var sha = SHA256.Create()) using (var output = File.Create(target))
            {
                long remaining = entry.Length;
                while (remaining > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0) throw new EndOfStreamException("组件包不完整。");
                    output.Write(buffer, 0, read); sha.TransformBlock(buffer, 0, read, buffer, 0); remaining -= read;
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                if (Hex(sha.Hash) != entry.Digest) throw new IOException("组件校验失败：" + entry.Path);
            }
        }
        if (input.ReadByte() != -1) throw new IOException("组件包包含多余数据。");
    }
#if LZMS
    private sealed class LzmsStream : Stream
    {
        [DllImport("cabinet.dll", SetLastError = true)] private static extern bool CreateDecompressor(uint algorithm, IntPtr allocator, out IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)] private static extern bool Decompress(IntPtr handle, byte[] input, UIntPtr inputSize, byte[] output, UIntPtr outputSize, out UIntPtr actualSize);
        [DllImport("cabinet.dll", SetLastError = true)] private static extern bool CloseDecompressor(IntPtr handle);
        private readonly BinaryReader source; private IntPtr handle; private byte[] current = new byte[0]; private int offset;
        public LzmsStream(string path)
        {
            source = new BinaryReader(File.OpenRead(path));
            if (!CreateDecompressor(5, IntPtr.Zero, out handle)) { source.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        public override int Read(byte[] buffer, int start, int count)
        {
            if (count == 0) return 0;
            if (offset == current.Length)
            {
                if (source.BaseStream.Position == source.BaseStream.Length) return 0;
                if (source.BaseStream.Length - source.BaseStream.Position < 8) throw new EndOfStreamException("压缩块标题不完整。");
                uint uncompressed = source.ReadUInt32(), compressed = source.ReadUInt32();
                if (uncompressed == 0 || uncompressed > int.MaxValue || compressed == 0 || compressed > int.MaxValue || compressed > source.BaseStream.Length - source.BaseStream.Position)
                    throw new IOException("压缩块大小无效。");
                byte[] data = source.ReadBytes((int)compressed); current = new byte[(int)uncompressed]; UIntPtr actual;
                if (!Decompress(handle, data, (UIntPtr)compressed, current, (UIntPtr)uncompressed, out actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (actual.ToUInt64() != uncompressed) throw new IOException("压缩块长度无效。");
                offset = 0;
            }
            int copied = Math.Min(count, current.Length - offset); Buffer.BlockCopy(current, offset, buffer, start, copied); offset += copied; return copied;
        }
        protected override void Dispose(bool disposing) { if (handle != IntPtr.Zero) { CloseDecompressor(handle); handle = IntPtr.Zero; } if (disposing) source.Dispose(); base.Dispose(disposing); }
        public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
#endif
    private static void CleanupAbandonedStages(string root)
    {
        string prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        foreach (string candidate in Directory.GetDirectories(root, ".screen-curtain-stage-*"))
        {
            try
            {
                string full = System.IO.Path.GetFullPath(candidate);
                string owner = System.IO.Path.Combine(full, ".stage-owner");
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(owner) &&
                    File.ReadAllText(owner).StartsWith("screen-curtain-stage-v1\n", StringComparison.Ordinal)) Directory.Delete(full, true);
            }
            catch { /* A decoder left briefly running after interruption may still hold a file. */ }
        }
    }
    private static void TryCompact(string stage)
    {
        if (CacheCompression == "none") return;
        try
        {
            string compact = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "compact.exe");
            var info = new ProcessStartInfo(compact, "/C /I /Q /EXE:" + CacheCompression + " /S:" + Quote(stage))
                { WorkingDirectory = stage, UseShellExecute = false, CreateNoWindow = true };
            using (Process process = Process.Start(info)) process.WaitForExit();
            // Compression is optional; Windows may leave some or all files uncompressed.
        }
        catch (Exception) { }
    }
    private static string Quote(string value)
    {
        var output = new StringBuilder("\""); int slashes = 0;
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
