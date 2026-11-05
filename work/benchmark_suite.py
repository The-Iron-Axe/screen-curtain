from pathlib import Path
import argparse, datetime, json, shutil, subprocess, sys, xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(); parser.add_argument('--smoke',action='store_true'); args=parser.parse_args()
lab_root=(root/'ExtremeLab').resolve(); lab_root.mkdir(exist_ok=True)
run_id=datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
lab=lab_root/(('suite-smoke-' if args.smoke else 'benchmark-run-')+run_id); lab.mkdir()
def run(*args):
    print('Running:', ' '.join(map(str,args)), flush=True)
    result=subprocess.run(list(map(str,args)),cwd=root.parent,capture_output=True,text=True,errors='replace')
    if result.returncode: raise RuntimeError(result.stdout+'\n'+result.stderr)
    if result.stdout: print(result.stdout, end='', flush=True)
    return result.stdout

# Rebuild first so the comparison includes every component of the current source tree.
publish=lab/('benchmark-publish-'+run_id)
if args.smoke:
    publish=lab/'publish'; publish.mkdir()
    entry=ET.parse(root/'WinUI/ScreenCurtain.WinUI.csproj').findtext('PropertyGroup/AssemblyName')+'.exe'
    stub=lab/'stub.cs'; stub.write_text('using System; using System.IO; class Stub { static int Main(string[] a) { if (a.Length == 2) File.WriteAllText(a[1], "RESULT: 13 numbering checks passed"); return 0; } }',encoding='utf-8-sig')
    run('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe','/nologo','/target:winexe','/platform:x64',f'/out:{publish/entry}',stub)
    (publish/'sample.dll').write_bytes((b'example payload and executable data '*1000))
    for name in ['Microsoft.UI.Xaml.dll',entry[:-4]+'.pri','App.xbf']: (publish/name).write_bytes(b'smoke-test component')
else: run('dotnet','publish',root/'WinUI/ScreenCurtain.WinUI.csproj','-c','Release','-o',publish)
metrics=[]
for arguments in [ ['prepare'], ['xz','--preset','9'], ['xz','--preset','9','--bcj'],
                   ['xz','--preset','9','--bcj','--extreme'], ['xz','--preset','9','--bcj','--dict-mib','128'], ['lzms'], ['disk'] ]:
    output=run(sys.executable,root/'extreme_benchmark.py',*arguments,'--publish',publish,'--lab',lab)
    for line in output.splitlines():
        if line.startswith('{'): metrics.append(json.loads(line))
run(sys.executable,root/'benchmark_cab.py','--publish',publish,'--lab',lab)
run(sys.executable,root/'build_lzx_probe.py','--lab',lab)
run(sys.executable,root/'build_xz_probe.py','--codec','lzms','--lab',lab)
run(sys.executable,root/'build_xz_probe.py','--codec','xz','--payload','xz-9-bcj-dict128.xz','--lab',lab)
powershell='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
for codec in ['lzx','lzms','xz']:
    cache=lab/('bench-'+run_id+'-'+codec)
    for phase in ['cold','warm']:
        tag=run_id+'-'+codec+'-'+phase
        run(powershell,'-NoProfile','-File',root/'benchmark_launcher.ps1','-Executable',lab/(codec+'-probe.exe'),
            '-CacheRoot',cache,'-Tag',tag,'-NoCompact')
        metrics.append(json.loads((root/'ExtremeLab'/(tag+'-measurement.json')).read_text(encoding='utf-8-sig')))
report=lab_root/('benchmark-'+run_id+'.json')
report.write_text(json.dumps(metrics,indent=2,ensure_ascii=False),encoding='utf-8')
resolved=lab.resolve()
if resolved.parent != lab_root or not resolved.name.startswith(('suite-smoke-', 'benchmark-run-')):
    raise RuntimeError('Refusing to remove an unexpected benchmark directory.')
shutil.rmtree(resolved)
print('Saved comparison:',report)
