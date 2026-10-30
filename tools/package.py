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
    (os.path.join(ROOT, 'design', 'editable', 'card-sheet.xlsx'), 'design/editable/card-sheet.xlsx'),
    (os.path.join(ROOT, 'design', 'editable', 'README.md'), 'design/editable/README.md'),
    (os.path.join(ROOT, 'docs', 'effect-feasibility.md'), 'docs/effect-feasibility.md'),
    (os.path.join(ROOT, 'docs', 'research', 'peak-api-2.5.json'), 'docs/research/peak-api-2.5.json'),
    (os.path.join(ROOT, 'docs', 'research', 'peak-api-redesign.json'), 'docs/research/peak-api-redesign.json'),
    (os.path.join(ROOT, 'docs', 'research', 'card-edit-review.json'), 'docs/research/card-edit-review.json'),
    (os.path.join(ROOT, 'docs', 'card-edit-review.md'), 'docs/card-edit-review.md'),
    (os.path.join(ROOT, 'design', 'editable', 'card-edit-review.xlsx'), 'design/editable/card-edit-review.xlsx'),
    (os.path.join(ROOT, 'design', 'reviews', 'card-editing-submitted.xlsx'), 'design/reviews/card-editing-submitted.xlsx'),
    (os.path.join(ROOT, 'docs', 'card-images.md'), 'docs/card-images.md'),
    (os.path.join(ROOT, 'docs', 'card-text-and-playtest.md'), 'docs/card-text-and-playtest.md'),
    (os.path.join(ROOT, 'docs', 'card-interactions.md'), 'docs/card-interactions.md'),
    (os.path.join(ROOT, 'docs', 'research', 'card-copy-review.json'), 'docs/research/card-copy-review.json'),
    (os.path.join(ROOT, 'assets', 'logo', 'CampfireLogo.png'), 'art/CampfireLogo.png'),
    (os.path.join(ROOT, 'assets', 'card-images', 'README.md'), 'art/cards/README.md'),
    (os.path.join(ROOT, 'design', 'editable', 'card-image-map.csv'), 'art/cards/card-image-map.csv'),
    (os.path.join(ROOT, 'design', 'editable', 'card-image-map.csv'), 'design/editable/card-image-map.csv'),
]

# Optional artwork stays beside the DLL; files are named with stable card keys.
for name in sorted(os.listdir(os.path.join(ROOT, 'assets', 'card-images'))):
    if name.endswith('.png'):
        FILES.append((os.path.join(ROOT, 'assets', 'card-images', name), 'art/cards/' + name))


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
            target = os.path.join(dst, name)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copy2(src, target)
        pdb = os.path.join(BIN, 'CampfireCards.pdb')
        if os.path.exists(pdb):
            shutil.copy2(pdb, os.path.join(dst, 'CampfireCards.pdb'))
        print('installed to', dst)


if __name__ == '__main__':
    main()
