from pathlib import Path
import argparse,json,subprocess
import time

root = Path(__file__).resolve().parent
parser=argparse.ArgumentParser(); parser.add_argument('--publish',default=str(root/'StylePublish')); parser.add_argument('--lab',default=str(root/'ExtremeLab')); args=parser.parse_args()
lab=Path(args.lab).resolve(); lab.mkdir(parents=True,exist_ok=True)
publish=Path(args.publish).resolve()
profile=json.loads((root/'compression-profile.json').read_text(encoding='utf-8'))
excluded={extension.lower() for extension in profile.get('excluded_extensions', [])}
files = sorted((f for f in publish.rglob('*') if f.is_file() and f.suffix.lower() not in excluded),
    key=lambda f:(f.suffix.lower(),f.name.lower(),f.relative_to(publish).as_posix().lower()))
ddf = lab / 'benchmark-lzx.ddf'
lines = ['.OPTION EXPLICIT', '.Set Cabinet=ON', '.Set Compress=ON', '.Set CompressionType=LZX',
         '.Set CompressionMemory=21', '.Set MaxDiskSize=0', '.Set MaxCabinetSize=0',
         '.Set FolderSizeThreshold=0', '.Set CabinetNameTemplate=benchmark-lzx.cab',
         f'.Set DiskDirectoryTemplate="{lab}"', f'.Set RptFileName="{lab / "benchmark-lzx.rpt"}"',
         f'.Set InfFileName="{lab / "benchmark-lzx.inf"}"']
lines += [f'"{f}" "{f.relative_to(publish)}"' for f in files]
ddf.write_text('\n'.join(lines), encoding='mbcs')
start = time.perf_counter()
with (lab / 'benchmark-lzx-build.txt').open('w') as log:
    subprocess.run(['makecab.exe', '/f', str(ddf)], stdout=log, stderr=subprocess.STDOUT, check=True)
print(f'{len(files)} files; {(lab / "benchmark-lzx.cab").stat().st_size / 1024**2:.2f} MiB; {time.perf_counter()-start:.1f} seconds')
