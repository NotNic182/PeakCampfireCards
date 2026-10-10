"""Package the built plugin for Melty and (optionally) install it into the local PEAK for testing.
Usage: python -I tools/package.py [--install]
Produces build/PeakRounds-<version>.zip with layout plugins/PeakRounds/... (recipe maps plugins/ -> {game}/BepInEx/plugins/PeakRounds)."""
import hashlib, json, os, re, shutil, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(ROOT, 'build', 'bin')
LIB = os.path.join(ROOT, 'lib')
PEAK = os.environ.get('PEAK_DIR', r'C:\Program Files (x86)\Steam\steamapps\common\PEAK')

csproj = open(os.path.join(ROOT, 'src', 'PeakRounds', 'PeakRounds.csproj'), encoding='utf-8').read()
VERSION = re.search(r'<Version>([^<]+)</Version>', csproj).group(1)

FILES = [
    (os.path.join(BIN, 'PeakRounds.dll'), 'PeakRounds.dll'),
    (os.path.join(LIB, 'AssetsTools.NET.dll'), 'AssetsTools.NET.dll'),
    (os.path.join(LIB, 'AssetsTools.NET.MonoCecil.dll'), 'AssetsTools.NET.MonoCecil.dll'),
    (os.path.join(LIB, 'NVorbis.dll'), 'NVorbis.dll'),
    (os.path.join(LIB, 'classdata.tpk'), 'classdata.tpk'),
    (os.path.join(ROOT, 'README.md'), 'README.md'),
    (os.path.join(ROOT, 'LICENSES.txt'), 'LICENSES.txt'),
]


def main():
    for src, _ in FILES:
        if not os.path.exists(src):
            print('missing', src); sys.exit(1)
    out = os.path.join(ROOT, 'build', f'PeakRounds-{VERSION}.zip')
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        for src, name in FILES:
            z.write(src, 'plugins/PeakRounds/' + name)
    size = os.path.getsize(out)
    sha = hashlib.sha256(open(out, 'rb').read()).hexdigest()
    entries = [f'plugins/PeakRounds/{n}' for _, n in FILES]
    meta = {'file': os.path.basename(out), 'version': VERSION, 'size': size, 'sha256': sha, 'entries': entries}
    json.dump(meta, open(os.path.join(ROOT, 'build', 'package.json'), 'w'), indent=1)
    print(json.dumps(meta, indent=1))
    if '--install' in sys.argv:
        dst = os.path.join(PEAK, 'BepInEx', 'plugins', 'PeakRounds')
        os.makedirs(dst, exist_ok=True)
        for src, name in FILES:
            shutil.copy2(src, os.path.join(dst, name))
        pdb = os.path.join(BIN, 'PeakRounds.pdb')
        if os.path.exists(pdb):
            shutil.copy2(pdb, os.path.join(dst, 'PeakRounds.pdb'))
        print('installed to', dst)


if __name__ == '__main__':
    main()
