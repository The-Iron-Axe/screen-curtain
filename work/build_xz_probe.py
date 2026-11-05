from pathlib import Path
import argparse, base64, hashlib, re, subprocess
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(); parser.add_argument('--codec',choices=['xz','lzms'],default='xz'); parser.add_argument('--payload'); parser.add_argument('--lab',default=str(root/'ExtremeLab')); parser.add_argument('--cache-compression',default='LZX'); parser.add_argument('--output-name'); args=parser.parse_args()
lab=Path(args.lab).resolve()
manifest=(lab/'manifest.scm').read_text(encoding='utf-8').splitlines()
version=manifest[0].split('\t')[3]
assembly=ET.parse(root/'WinUI/ScreenCurtain.WinUI.csproj').findtext('PropertyGroup/AssemblyName')+'.exe'
manifest[0]=manifest[0].replace('XZ-LZMA2','LZMS-BLOCKS' if args.codec=='lzms' else 'XZ-LZMA2')+'\t'+base64.b64encode(assembly.encode()).decode()
data=('\n'.join(manifest)+'\n').encode()
manifest_file=lab/(args.codec+'-probe-manifest.scm'); manifest_file.write_bytes(data)
payload=lab/(args.payload or ('lzms-8m.bin' if args.codec=='lzms' else 'xz-9-bcj.xz'))
vendor=root/'CompressionVendor/xz-5.6.4'
resources={'ScreenCurtain.Payload':payload,'ScreenCurtain.Manifest':manifest_file}
if args.codec=='xz': resources.update({
 'ScreenCurtain.XzDecoder':vendor/'xzdec.exe','ScreenCurtain.LzmaLibrary':vendor/'liblzma.dll',
 'ScreenCurtain.VcRuntime':vendor/'vcruntime140.dll','ScreenCurtain.XzLicense':vendor/'COPYING','ScreenCurtain.XzLicense0':vendor/'COPYING.0BSD'})
source=(root/'ExtremeLauncher.template.cs').read_text(encoding='utf-8')
replacements={'__ASSEMBLY_VERSION__':version+'.0','__VERSION__':version,'__PAYLOAD_DIGEST__':hashlib.sha256(payload.read_bytes()).hexdigest(),
 '__MANIFEST_DIGEST__':hashlib.sha256(data).hexdigest(),'__CACHE_COMPRESSION__':args.cache_compression}
replacements['__ARCHIVE_FORMAT__']='LZMS-BLOCKS' if args.codec=='lzms' else 'XZ-LZMA2'
for a,b in replacements.items(): source=source.replace(a,b)
source=source.replace('args[0] == "--self-test" ||', 'args[0] == "--self-test-launcher-only" || args[0] == "--self-test" ||', 1)
source=source.replace('            var start = new ProcessStartInfo(CheckedPath(destination, manifest.Executable))',
    '            if (args[0] == "--self-test-launcher-only") { File.WriteAllText(args[1], "PASS: cache prepared and validated"); return 0; }\n'
    '            var start = new ProcessStartInfo(CheckedPath(destination, manifest.Executable))', 1)
src=lab/(args.codec+'-ProbeLauncher.cs'); src.write_text(source,encoding='utf-8-sig')
compiler='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
exe=lab/(args.output_name or (args.codec+'-probe.exe'))
subprocess.run([compiler,'/nologo','/target:winexe','/platform:x64','/optimize+','/reference:System.Windows.Forms.dll',
 '/reference:System.Drawing.dll','/reference:System.Core.dll',f'/win32manifest:{root / "ScreenCurtain.manifest"}',
 f'/win32icon:{root / "ScreenCurtain.ico"}',*(['/define:LZMS'] if args.codec=='lzms' else []),
 *[f'/resource:{p},{n}' for n,p in resources.items()],f'/out:{exe}',str(src)],check=True)
print(f'{args.codec} probe: {exe.stat().st_size / 1024**2:.2f} MiB')
