"""Preflight: lay every sheet over the others and list every unfilled cell and every unresolved reference.
Run: python -I tools/preflight.py [--release]   (from the project root). Exit code 1 when anything is wrong.
--release also requires systems.testedBy to be filled and no PLACEHOLDER cards."""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGN = os.path.join(ROOT, 'design')
SRC = os.path.join(ROOT, 'src', 'CampfireCards')
RELEASE = '--release' in sys.argv
problems = []
warnings = []


def load(name):
    with open(os.path.join(DESIGN, name), encoding='utf-8') as f:
        return json.load(f)


def problem(msg):
    problems.append(msg)


def check_cells(sheet_name, sheet, allow_empty=()):
    cols = list(sheet['_columns'].keys())
    allow = set(allow_empty) | set(sheet.get('_optionalEmpty', []))
    for i, row in enumerate(sheet['rows']):
        label = '%s row %d (%s)' % (sheet_name, i, row.get('id', row.get('name', row.get('code', '?'))))
        for c in cols:
            if c not in row:
                problem('%s: missing column %s' % (label, c))
                continue
            v = row[c]
            empty = v is None or (isinstance(v, str) and v.strip() == '') or (isinstance(v, (list, dict)) and len(v) == 0)
            if empty and c not in allow:
                problem('%s: unfilled cell %s' % (label, c))
        for c in row:
            if c not in cols:
                problem('%s: unknown column %s' % (label, c))


def read_tree(base, exts):
    out = []
    for dp, dn, fn in os.walk(base):
        if any(part in ('obj', 'bin', '__pycache__') for part in dp.replace('\\', '/').split('/')):
            continue
        for f in fn:
            if f.endswith(exts):
                out.append(os.path.join(dp, f))
    return out


def src_text():
    chunks = []
    for path in read_tree(SRC, ('.cs',)):
        if os.sep + 'Generated' + os.sep in path:
            continue
        with open(path, encoding='utf-8') as fh:
            chunks.append(fh.read())
    return '\n'.join(chunks)


def gen_text():
    chunks = []
    g = os.path.join(SRC, 'Generated')
    if os.path.isdir(g):
        for f in os.listdir(g):
            if f.endswith('.cs'):
                with open(os.path.join(g, f), encoding='utf-8') as fh:
                    chunks.append(fh.read())
    return '\n'.join(chunks)


def has_call(name, src):
    return re.search(r'\b' + re.escape(name) + r'\s*\(', src) is not None


def main():
    stats = load('stats.json'); cards = load('cards.json'); hooks = load('hooks.json'); net = load('net.json')
    ui = load('ui.json'); systems = load('systems.json'); art = load('art.json'); sounds = load('sounds.json')

    # 1. every cell filled
    check_cells('stats', stats, allow_empty=('displayNegative',))
    check_cells('cards', cards)
    check_cells('hooks', hooks)
    check_cells('net', net)
    check_cells('ui', ui, allow_empty=('text',))
    check_cells('art', art)
    check_cells('sounds', sounds)
    check_cells('systems', systems, allow_empty=() if RELEASE else ('testedBy',))

    stat_ids = {r['id']: r for r in stats['rows']}
    hook_ids = {r['id'] for r in hooks['rows']}
    art_ids = {r['id'] for r in art['rows']}
    sound_ids = {r['id'] for r in sounds['rows']}

    # 2. stats -> hooks
    for r in stats['rows']:
        if r['kind'] == 'flag' and r['displayNegative'] != '':
            problem('stats %s: a flag has a displayNegative' % r['id'])
        if r['kind'] == 'float' and r['displayNegative'] == '' and r['min'] < 0:
            problem('stats %s: can go negative but has no displayNegative' % r['id'])
        av = r['appliesVia']
        if av.startswith('hook:') and av[5:] not in hook_ids:
            problem('stats %s: appliesVia %s does not resolve to a hooks.json id' % (r['id'], av))
        if av not in ('effects', 'tick') and not av.startswith('hook:'):
            problem('stats %s: unknown appliesVia %s' % (r['id'], av))

    # 3. cards -> stats, themes, art
    for k in ('common', 'uncommon', 'rare'):
        if k not in cards['rarityWeights']:
            problem('cards: rarityWeights has no %s' % k)
    for tid, th in cards['themes'].items():
        for c in ('frame', 'bg', 'accent'):
            if c not in th or len(th[c]) != 4:
                problem('cards theme %s: %s must be [r,g,b,a]' % (tid, c))
    seen_ids, seen_names, placeholders = set(), set(), 0
    for r in cards['rows']:
        label = r.get('name', r.get('id'))
        if r['id'] in seen_ids or not (1 <= r['id'] <= 255):
            problem('cards %s: id %s duplicate or out of byte range' % (label, r['id']))
        seen_ids.add(r['id'])
        if r['name'] in seen_names:
            problem('cards %s: duplicate name' % label)
        seen_names.add(r['name'])
        if r['name'].startswith('PLACEHOLDER'):
            placeholders += 1
        if r['rarity'] not in cards['rarityWeights']:
            problem('cards %s: rarity %s unknown' % (label, r['rarity']))
        if r['theme'] not in cards['themes']:
            problem('cards %s: theme %s is not in themes' % (label, r['theme']))
        if r['art'] not in art_ids:
            problem('cards %s: art %s is not an art.json motif' % (label, r['art']))
        if not isinstance(r['unique'], bool):
            problem('cards %s: unique must be true or false' % label)
        for k, v in r['effects'].items():
            if k not in stat_ids:
                problem('cards %s: effect %s is not a stats.json id' % (label, k))
                continue
            sd = stat_ids[k]
            if sd['kind'] == 'flag':
                if v is not True:
                    problem('cards %s: flag %s must be true' % (label, k))
            elif isinstance(v, bool) or not isinstance(v, (int, float)):
                problem('cards %s: effect %s must be a number' % (label, k))
            elif not (sd['min'] <= v <= sd['max']):
                problem('cards %s: effect %s=%s outside [%s, %s]' % (label, k, v, sd['min'], sd['max']))
    if len(cards['rows']) < cards['handSize']:
        problem('cards: only %d cards for a hand of %d' % (len(cards['rows']), cards['handSize']))
    if not (2 <= cards['handSize'] <= 7):
        problem('cards: handSize %s unreasonable' % cards['handSize'])
    if placeholders:
        (problems if RELEASE else warnings).append('cards: %d PLACEHOLDER card(s) still in the deck' % placeholders)

    # 4. art
    for r in art['rows']:
        for i, l in enumerate(r['layers']):
            where = 'art %s layer %d' % (r['id'], i)
            for key in ('shape', 'color', 'size', 'pos', 'rot', 'anim', 'amp', 'speed', 'phase', 'alpha'):
                if key not in l:
                    problem('%s: missing %s' % (where, key))
            if l.get('shape') not in art['shapes']:
                problem('%s: unknown shape %s' % (where, l.get('shape')))
            if l.get('color') not in art['colors']:
                problem('%s: unknown color %s' % (where, l.get('color')))
            if l.get('anim') not in art['anims']:
                problem('%s: unknown anim %s' % (where, l.get('anim')))
            if not (0 <= l.get('alpha', 1) <= 1):
                problem('%s: alpha outside 0..1' % where)
            if min(l.get('size', [0, 0])) <= 0:
                problem('%s: size must be positive' % where)

    # 5. sounds
    for r in sounds['rows']:
        if r['kind'] not in ('tick', 'chime', 'whoosh'):
            problem('sounds %s: unknown kind %s' % (r['id'], r['kind']))
        if r['kind'] == 'whoosh' and len(r['freqs']) != 2:
            problem('sounds %s: whoosh needs two freqs' % r['id'])
        if not (0 < r['duration'] <= 5) or not (0 < r['volume'] <= 1):
            problem('sounds %s: duration or volume out of range' % r['id'])

    # 6. hooks: harmony rows have a prefix or postfix; handlers exist in src
    src = src_text()
    for r in hooks['rows']:
        if r['kind'] == 'harmony':
            if not r['prefixArgs'] and not r['postfixArgs']:
                problem('hooks %s: neither prefixArgs nor postfixArgs' % r['id'])
            if r['prefixArgs'] and not has_call(r['handler'] + 'Prefix', src):
                problem('hooks %s: handler %sPrefix not found in src' % (r['id'], r['handler']))
            if r['postfixArgs'] and not has_call(r['handler'], src):
                problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))
        else:
            if r['targetMember'].split('.')[-1] not in src:
                problem('hooks %s: event %s is never subscribed in src' % (r['id'], r['targetMember']))
            if not has_call(r['handler'], src):
                problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))

    # 7. net
    codes = set(); allowed_types = {'byte', 'bool', 'int', 'float', 'double', 'string', 'byte[]', 'int[]'}
    for r in net['rows']:
        if r['code'] in codes or not (90 <= r['code'] <= 99):
            problem('net %s: code %s duplicate or outside 90..99' % (r['name'], r['code']))
        codes.add(r['code'])
        for f in r['fields']:
            if f['type'] not in allowed_types:
                problem('net %s: field %s has unsupported type %s' % (r['name'], f['name'], f['type']))
        if not has_call(r['handler'], src):
            problem('net %s: handler %s not found in src' % (r['name'], r['handler']))

    # 8. ui
    ui_ids = {r['id'] for r in ui['rows']}
    for r in ui['rows']:
        if r['parent'] != 'root' and r['parent'] not in ui_ids:
            problem('ui %s: parent %s unknown' % (r['id'], r['parent']))
        if r['soundOn'] != 'none' and r['soundOn'] not in sound_ids:
            problem('ui %s: soundOn %s is not a sounds.json id' % (r['id'], r['soundOn']))
        if ('Ui_%s' % r['id']) not in src:
            problem('ui %s: Ui_%s is never used in src' % (r['id'], r['id']))

    # 9. systems -> src classes
    sys_ids = {r['id'] for r in systems['rows']}
    every = src + '\n' + gen_text()
    for r in systems['rows']:
        for d in [x.strip() for x in r['dependsOn'].split(',') if x.strip() and x.strip() != '-']:
            if d not in sys_ids:
                problem('systems %s: dependsOn %s unknown' % (r['id'], d))
        for cls in re.findall(r'CampfireCards(?:\.[A-Za-z]+)*\.([A-Za-z0-9_]+)', r['class']):
            if not re.search(r'\b(class|struct)\s+%s\b' % cls, every):
                problem('systems %s: class %s not found in src' % (r['id'], cls))

    # 10. standalone PEAK mod: no other game is named anywhere in the project
    other = ''.join(['R', 'O', 'U', 'N', 'D', 'S'])
    pat = re.compile(r'\b' + other + r'\b', re.IGNORECASE)
    files = [os.path.join(ROOT, 'README.md')]
    for base in ('design', 'src', 'tools'):
        files += read_tree(os.path.join(ROOT, base), ('.json', '.cs', '.py', '.sh', '.ps1', '.md', '.csproj'))
    for path in files:
        with open(path, encoding='utf-8', errors='replace') as fh:
            for n, line in enumerate(fh, 1):
                if pat.search(line):
                    problem('%s:%d mentions another game' % (os.path.relpath(path, ROOT), n))

    print('cards in deck: %d (%d placeholder)' % (len(cards['rows']), placeholders))
    for w in warnings:
        print('WARN', w)
    if problems:
        print('PREFLIGHT: %d problem(s)' % len(problems))
        for p in problems:
            print(' -', p)
        sys.exit(1)
    print('PREFLIGHT CLEAN' + (' (release)' if RELEASE else ''))


if __name__ == '__main__':
    main()
