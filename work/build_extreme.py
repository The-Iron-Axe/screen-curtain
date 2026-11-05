from pathlib import Path
import shutil, subprocess, sys, uuid

root=Path(__file__).resolve().parent
build=(root/'ExtremeBuild').resolve(); build.mkdir(exist_ok=True)
publish=build/('publish-'+uuid.uuid4().hex)
publish.mkdir()
owner=build/(publish.name+'.owner'); owner.write_text('screen-curtain-build-v1',encoding='ascii')
try:
    subprocess.run(['dotnet','publish',str(root/'WinUI/ScreenCurtain.WinUI.csproj'),'-c','Release','-o',str(publish)],check=True)
    subprocess.run([sys.executable,str(root/'package_extreme.py'),'--publish',str(publish)],check=True)
except Exception:
    print('Build output retained for diagnosis:',publish,flush=True)
    raise
else:
    resolved=publish.resolve()
    if resolved.parent != build or not resolved.name.startswith('publish-') or owner.read_text(encoding='ascii')!='screen-curtain-build-v1':
        raise RuntimeError('Refusing to remove an unexpected build directory.')
    shutil.rmtree(resolved)
    owner.unlink()
