"""Package the built plugin and (optionally) install it into the local PEAK for testing.
Usage: python -I tools/package.py [--install]
Produces build/CampfireCards-<version>.zip with layout plugins/CampfireCards/...."""
import hashlib, json, os, re, shutil, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(ROOT, 'build', 'bin')
PEAK = os.environ.get('PEAK_DIR', r'C:\Program Files (x86)\Steam\steamapps\common\PEAK')

csproj = open(os.path.join(ROOT, 'src', 'CampfireCards', 'CampfireCards.csproj'), encoding='utf-8').read()
VERSION = re.search(r'<Version>([^<]+)</Version>', csproj).group(1)

FILES = [
    (os.path.join(BIN, 'CampfireCards.dll'), 'CampfireCards.dll'),
    (os.path.join(ROOT, 'README.md'), 'README.md'),
    (os.path.join(ROOT, 'LICENSE'), 'LICENSE'),
    (os.path.join(ROOT, 'assets', 'fonts', 'DarumadropOne-Regular.ttf'), 'DarumadropOne-Regular.ttf'),
    (os.path.join(ROOT, 'assets', 'fonts', 'DarumadropOne-OFL.txt'), 'DarumadropOne-OFL.txt'),
]


def main():
    for src, _ in FILES:
        if not os.path.exists(src):
            print('missing', src); sys.exit(1)
    out = os.path.join(ROOT, 'build', f'CampfireCards-{VERSION}.zip')
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        for src, name in FILES:
            z.write(src, 'plugins/CampfireCards/' + name)
    size = os.path.getsize(out)
    sha = hashlib.sha256(open(out, 'rb').read()).hexdigest()
    entries = [f'plugins/CampfireCards/{n}' for _, n in FILES]
    meta = {'file': os.path.basename(out), 'version': VERSION, 'size': size, 'sha256': sha, 'entries': entries}
    json.dump(meta, open(os.path.join(ROOT, 'build', 'package.json'), 'w'), indent=1)
    print(json.dumps(meta, indent=1))
    if '--install' in sys.argv:
        dst = os.path.join(PEAK, 'BepInEx', 'plugins', 'CampfireCards')
        os.makedirs(dst, exist_ok=True)
        for src, name in FILES:
            shutil.copy2(src, os.path.join(dst, name))
        pdb = os.path.join(BIN, 'CampfireCards.pdb')
        if os.path.exists(pdb):
            shutil.copy2(pdb, os.path.join(dst, 'CampfireCards.pdb'))
        print('installed to', dst)


if __name__ == '__main__':
    main()
