from pathlib import Path
import argparse,hashlib,re,subprocess,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(); parser.add_argument('--lab',default=str(root/'ExtremeLab')); args=parser.parse_args()
lab=Path(args.lab).resolve(); payload=lab/'benchmark-lzx.cab'
source=(root/'CompressionVendor/LegacyLzxLauncher.cs').read_text(encoding='utf-8-sig')
project=ET.parse(root/'WinUI/ScreenCurtain.WinUI.csproj')
version=project.findtext('PropertyGroup/Version')
entry=project.findtext('PropertyGroup/AssemblyName')+'.exe'
parts=re.match(r'^(\d+)\.(\d+)\.(\d+)',version)
if not parts: raise RuntimeError('Project version must start with major.minor.patch.')
source=re.sub(r'AssemblyVersion\("[\d.]+"\)',f'AssemblyVersion("{".".join(parts.groups())}.0")',source)
source=re.sub(r'Path.Combine\(root, "[\d.]+-"',f'Path.Combine(root, "{version}-"',source)
old_ready='''bool ready = File.Exists(marker) && File.ReadAllText(marker) == Digest &&
                        File.Exists(Path.Combine(destination, "ScreenCurtain.WinUI.exe")) &&
                        File.Exists(Path.Combine(destination, "Microsoft.UI.Xaml.dll")) &&
                        File.Exists(Path.Combine(destination, "ScreenCurtain.WinUI.pri")) &&
                        File.Exists(Path.Combine(destination, "App.xbf"));'''
new_ready='bool ready = File.Exists(marker) && File.ReadAllText(marker) == Digest && File.Exists(Path.Combine(destination, "'+entry+'"));'
if old_ready not in source: raise RuntimeError('Legacy cache validation template has changed.')
source=source.replace(old_ready,new_ready).replace('Path.Combine(destination, "ScreenCurtain.WinUI.exe")','Path.Combine(destination, "'+entry+'")')
digest=hashlib.sha256(payload.read_bytes()).hexdigest()
source=re.sub(r'private const string Digest = "[a-f0-9]+";',f'private const string Digest = "{digest}";',source)
needle='string destination = Path.Combine(root, '
patch='string testRoot = args.Length == 2 && args[0].StartsWith("--self-test", StringComparison.Ordinal) ? Environment.GetEnvironmentVariable("SCREENCURTAIN_TEST_CACHE_ROOT") : null;\n            if (!string.IsNullOrWhiteSpace(testRoot)) root = Path.GetFullPath(testRoot);\n            '
if needle not in source: raise RuntimeError('Legacy launcher template has changed.')
source=source.replace(needle,patch+needle)
src=lab/'lzx-ProbeLauncher.cs'; src.write_text(source,encoding='utf-8-sig')
exe=lab/'lzx-probe.exe'
subprocess.run(['C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe','/nologo','/target:winexe','/platform:x64','/optimize+',
 '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll','/reference:System.IO.Compression.dll',
 '/reference:System.IO.Compression.FileSystem.dll',f'/win32manifest:{root / "ScreenCurtain.manifest"}',f'/win32icon:{root / "ScreenCurtain.ico"}',
 f'/resource:{payload},ScreenCurtain.Payload',f'/out:{exe}',str(src)],check=True)
print(f'LZX probe: {exe.stat().st_size/1024**2:.2f} MiB')
