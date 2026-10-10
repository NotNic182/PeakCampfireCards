"""Preflight: lay every sheet over the others and list every unfilled cell and every unresolved reference.
Run: python -I tools/preflight.py [--release]   (from the project root). Exit code 1 when anything is wrong.
--release also requires systems.testedBy to be filled."""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGN = os.path.join(ROOT, 'design')
SRC = os.path.join(ROOT, 'src', 'PeakRounds')
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
        label = '%s row %d (%s)' % (sheet_name, i, row.get('id', row.get('rounds', row.get('name', row.get('code', '?')))))
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


def src_text():
    chunks = []
    if os.path.isdir(SRC):
        for dp, dn, fn in os.walk(SRC):
            if 'Generated' in dp or os.sep + 'obj' in dp or os.sep + 'bin' in dp:
                continue
            for f in fn:
                if f.endswith('.cs'):
                    with open(os.path.join(dp, f), encoding='utf-8') as fh:
                        chunks.append(fh.read())
    return '\n'.join(chunks)


def main():
    stats = load('stats.json'); cards = load('cards.json'); hooks = load('hooks.json'); net = load('net.json')
    ui = load('ui.json'); ra = load('rounds_assets.json'); systems = load('systems.json'); pool = load('rounds_pool.json')

    # 1. every cell filled
    check_cells('stats', stats, allow_empty=('displayNegative',))
    check_cells('cards', cards, allow_empty=('effects',))
    check_cells('hooks', hooks)
    check_cells('net', net)
    check_cells('ui', ui, allow_empty=('text',))
    check_cells('rounds_assets', ra)
    check_cells('systems', systems, allow_empty=() if RELEASE else ('testedBy',))

    stat_ids = {r['id']: r for r in stats['rows']}
    hook_ids = {r['id'] for r in hooks['rows']}
    pool_names = {p['rounds']: p for p in pool['pool']}
    textures = set(pool['textures']); clips = set(pool['audioClips'])

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

    # 3. cards -> stats, pool
    seen_ids = set(); seen_names = set(); in_deck = 0
    for r in cards['rows']:
        if r['id'] in seen_ids or not (1 <= r['id'] <= 255):
            problem('cards %s: id %s duplicate or out of byte range' % (r['rounds'], r['id']))
        seen_ids.add(r['id'])
        if r['rounds'] in seen_names:
            problem('cards %s: duplicate rounds name' % r['rounds'])
        seen_names.add(r['rounds'])
        if r['rounds'] not in pool_names:
            problem('cards %s: not a card in the installed ROUNDS pool (rounds_pool.json)' % r['rounds'])
        if r['inDeck']:
            in_deck += 1
            if not r['effects']:
                problem('cards %s: inDeck but no effects' % r['rounds'])
        elif r['effects']:
            problem('cards %s: out of the deck but has effects' % r['rounds'])
        for k, v in r['effects'].items():
            if k not in stat_ids:
                problem('cards %s: effect %s is not a stats.json id' % (r['rounds'], k))
                continue
            sd = stat_ids[k]
            if sd['kind'] == 'flag':
                if v is not True:
                    problem('cards %s: flag %s must be true' % (r['rounds'], k))
            else:
                if isinstance(v, bool) or not isinstance(v, (int, float)):
                    problem('cards %s: effect %s must be a number' % (r['rounds'], k))
                elif not (sd['min'] <= v <= sd['max']):
                    problem('cards %s: effect %s=%s outside [%s, %s]' % (r['rounds'], k, v, sd['min'], sd['max']))
    for name in pool_names:
        if name not in seen_names:
            problem('cards: ROUNDS pool card %s has no row (add it, inDeck true or false)' % name)
    if cards['handSize'] < 2 or cards['handSize'] > 7:
        problem('cards: handSize %s unreasonable' % cards['handSize'])

    # 4. hooks: harmony rows have a prefix or postfix; handlers exist in src
    src = src_text()
    for r in hooks['rows']:
        if r['kind'] == 'harmony':
            if not r['prefixArgs'] and not r['postfixArgs']:
                problem('hooks %s: neither prefixArgs nor postfixArgs' % r['id'])
            if src:
                if r['prefixArgs'] and not re.search(r'\b%sPrefix\s*\(' % re.escape(r['handler']), src):
                    problem('hooks %s: handler %sPrefix not found in src' % (r['id'], r['handler']))
                if r['postfixArgs'] and not re.search(r'\b%s\s*\(' % re.escape(r['handler']), src):
                    problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))
        else:
            if src and r['targetMember'].split('.')[-1] not in src:
                problem('hooks %s: event %s is never subscribed in src' % (r['id'], r['targetMember']))
            if src and not re.search(r'\b%s\s*\(' % re.escape(r['handler']), src):
                problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))

    # 5. net
    codes = set(); allowed_types = {'byte', 'bool', 'int', 'float', 'double', 'string', 'byte[]', 'int[]'}
    for r in net['rows']:
        if r['code'] in codes or not (90 <= r['code'] <= 99):
            problem('net %s: code %s duplicate or outside 90..99' % (r['name'], r['code']))
        codes.add(r['code'])
        for f in r['fields']:
            if f['type'] not in allowed_types:
                problem('net %s: field %s has unsupported type %s' % (r['name'], f['name'], f['type']))
        if src and not re.search(r'\b%s\s*\(' % re.escape(r['handler']), src):
            problem('net %s: handler %s not found in src' % (r['name'], r['handler']))

    # 6. ui
    ui_ids = {r['id'] for r in ui['rows']}
    for r in ui['rows']:
        if r['parent'] != 'root' and r['parent'] not in ui_ids:
            problem('ui %s: parent %s unknown' % (r['id'], r['parent']))
        if r['soundOn'] != 'none' and r['soundOn'] not in ra['audioKeys']:
            problem('ui %s: soundOn %s is not an audioKeys entry' % (r['id'], r['soundOn']))
        if src and ('Ui_%s' % r['id']) not in src:
            problem('ui %s: Ui_%s is never used in src' % (r['id'], r['id']))

    # 7. rounds assets -> pool
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from gen import expand_names
    ra_ids = {r['id'] for r in ra['rows']}
    for k, v in ra['audioKeys'].items():
        if v not in ra_ids:
            problem('rounds_assets audioKeys %s -> %s unknown row' % (k, v))
    for r in ra['rows']:
        if r['kind'] == 'audio':
            names = expand_names(r['name'])
            if not names:
                problem('rounds_assets %s: audio name pattern %s does not expand' % (r['id'], r['name']))
            for n in names:
                if n not in clips:
                    problem('rounds_assets %s: clip %s not in the installed ROUNDS' % (r['id'], n))
        if r['kind'] == 'texture':
            for n in expand_names(r['name']):
                if n not in textures:
                    problem('rounds_assets %s: texture %s not in the installed ROUNDS' % (r['id'], n))
        if src and ('RA_%s' % r['id']) not in src:
            problem('rounds_assets %s: RA_%s is never used in src' % (r['id'], r['id']))
    if ra['install']['unityVersion'] != pool['unityVersion']:
        problem('rounds_assets install.unityVersion %s != pool %s' % (ra['install']['unityVersion'], pool['unityVersion']))

    # 8. systems -> src classes
    sys_ids = {r['id'] for r in systems['rows']}
    for r in systems['rows']:
        for d in [x.strip() for x in r["dependsOn"].split(",") if x.strip() and x.strip() != "-"]:
            if d not in sys_ids:
                problem('systems %s: dependsOn %s unknown' % (r['id'], d))
        for cls in re.findall(r'PeakRounds(?:\.[A-Za-z]+)*\.([A-Za-z0-9_]+)', r['class']):
            if src and not re.search(r'\b(class|struct)\s+%s\b' % cls, src + '\n' + gen_text()):
                problem('systems %s: class %s not found in src' % (r['id'], cls))

    print('cards in deck: %d of %d pool cards' % (in_deck, len(pool_names)))
    for w in warnings:
        print('WARN', w)
    if problems:
        print('PREFLIGHT: %d problem(s)' % len(problems))
        for p in problems:
            print(' -', p)
        sys.exit(1)
    print('PREFLIGHT CLEAN' + (' (release)' if RELEASE else ''))


def gen_text():
    g = os.path.join(SRC, 'Generated')
    chunks = []
    if os.path.isdir(g):
        for f in os.listdir(g):
            if f.endswith('.cs'):
                with open(os.path.join(g, f), encoding='utf-8') as fh:
                    chunks.append(fh.read())
    return '\n'.join(chunks)


if __name__ == '__main__':
    main()
