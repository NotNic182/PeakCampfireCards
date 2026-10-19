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
# channels a card uses but that cannot be applied on the current game (documented blockers, README "Known limitations")
KNOWN_UNAPPLIED = {'invSlots'}


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
    conditions = load('conditions.json')
    ui = load('ui.json'); systems = load('systems.json'); art = load('art.json'); sounds = load('sounds.json')

    # 1. every cell filled
    check_cells('stats', stats)
    check_cells('conditions', conditions)
    check_cells('cards', cards)
    check_cells('hooks', hooks)
    check_cells('net', net)
    check_cells('ui', ui, allow_empty=('text',))
    check_cells('art', art)
    check_cells('sounds', sounds)
    check_cells('systems', systems, allow_empty=() if RELEASE else ('testedBy',))

    stat_ids = {r['id']: r for r in stats['rows']}
    cond_ids = {r['id'] for r in conditions['rows']}
    art_ids = {r['id'] for r in art['rows']}
    sound_ids = {r['id'] for r in sounds['rows']}
    src = src_text()

    # 2. stats (channels): every channel is applied somewhere in src
    for r in stats['rows']:
        if r['op'] not in ('mul', 'add'):
            problem('stats %s: op must be mul or add' % r['id'])
        if r.get('better') not in ('lower', 'higher'):
            problem('stats %s: better must be lower or higher' % r['id'])
        for col in ('more', 'less'):
            if '{0}' not in (r.get(col) or ''):
                problem('stats %s: %s must be in-game wording with {0} for the amount' % (r['id'], col))
        if '{' in (r.get('never') or ''):
            problem('stats %s: never takes no amount' % r['id'])
        if r.get('never') and r['op'] != 'mul':
            problem('stats %s: never only applies to mul channels' % r['id'])
        if r['capGroup'] not in ('none', 'staminaCost', 'maxStamina'):
            problem('stats %s: unknown capGroup %s' % (r['id'], r['capGroup']))
        if ('Channel.' + r['id']) not in src:
            msg = 'stats %s: channel is never read in src (cards using it would do nothing)' % r['id']
            (warnings if r['id'] in KNOWN_UNAPPLIED else problems).append(msg)

    # 2a. the UI font and its license ship with the mod
    for key in ('font', 'fontLicense'):
        name = ui['canvas'].get(key)
        if not name or not os.path.exists(os.path.join(ROOT, 'assets', 'fonts', name)):
            problem('ui canvas.%s: assets/fonts/%s is missing' % (key, name))
        elif ("'" + name + "'") not in open(os.path.join(ROOT, 'tools', 'package.py'), encoding='utf-8').read():
            problem('ui canvas.%s: tools/package.py does not ship %s' % (key, name))

    # 2b. conditions: every condition is evaluated in src
    for r in conditions['rows']:
        if ('Condition.' + r['id']) not in src:
            problem('conditions %s: never evaluated in src' % r['id'])
        if not r.get('shown'):
            problem('conditions %s: shown (in-game wording) is empty' % r['id'])

    # 3. cards
    rarities = ['common', 'uncommon', 'rare', 'legendary', 'mythic']
    for k in rarities:
        if k not in cards['rarityWeights']:
            problem('cards: rarityWeights has no %s' % k)
        if k not in cards['rarityColors'] or len(cards['rarityColors'][k]) != 4:
            problem('cards: rarityColors %s must be [r,g,b,a]' % k)
    tiers = cards.get('difficultyStrength', {})
    for k in ('tenderfootPeak', 'ascent1to3', 'ascent4to6', 'ascent7to8'):
        v = tiers.get(k)
        if not isinstance(v, (int, float)) or not (0.05 <= v <= 1.5):
            problem('cards: difficultyStrength.%s must be a number from 0.05 to 1.5 (got %r)' % (k, v))
    if not (1 <= cards['offerCount'] <= 5):
        problem('cards: offerCount %s unreasonable' % cards['offerCount'])
    for tid, th in cards['themes'].items():
        for c in ('frame', 'bg', 'accent'):
            if c not in th or len(th[c]) != 4:
                problem('cards theme %s: %s must be [r,g,b,a]' % (tid, c))
    seen_ids, seen_names, seen_keys, placeholders = set(), set(), set(), 0
    counts = {k: 0 for k in rarities}
    for r in cards['rows']:
        label = r.get('key', r.get('id'))
        if r['id'] in seen_ids or not (1 <= r['id'] <= 255):
            problem('cards %s: id %s duplicate or out of byte range' % (label, r['id']))
        seen_ids.add(r['id'])
        for col, seen in (('name', seen_names), ('key', seen_keys)):
            if r[col] in seen:
                problem('cards %s: duplicate %s' % (label, col))
            seen.add(r[col])
        if not re.match(r'^[A-Z][A-Za-z0-9]*$', r['key']):
            problem('cards %s: key must be PascalCase letters and digits' % label)
        if r['name'].startswith('PLACEHOLDER'):
            placeholders += 1
        if r['rarity'] not in rarities:
            problem('cards %s: rarity %s unknown' % (label, r['rarity']))
        else:
            counts[r['rarity']] += 1
        if r['rarity'] == 'mythic' and r.get('negative'):
            problem('cards %s: a Mythic has no drawback' % label)
        # a card with no drawback text (every Mythic, and normal cards without a catch) may only move channels the
        # helpful way (stats.json better); a hidden drawback would contradict what the card says
        if not r.get('negative'):
            for i, m in enumerate(r.get('mods') or []):
                if not (isinstance(m, list) and len(m) == 5) or m[0] not in stat_ids or m[1] == 'set':
                    continue
                ch, op, val = m[0], m[1], m[2]
                higher = stat_ids[ch].get('better') == 'higher'
                neutral = 1 if op == 'mul' else 0
                if (higher and val < neutral) or (not higher and val > neutral):
                    problem('cards %s mod %d: no drawback is shown, but %s %s %s hurts' % (label, i, ch, op, val))
        if r['category'] == 'team' and not r['multiplayerOnly']:
            problem('cards %s: team cards are multiplayer-only' % label)
        if r['theme'] not in cards['themes']:
            problem('cards %s: theme %s is not in themes' % (label, r['theme']))
        if r['art'] not in art_ids:
            problem('cards %s: art %s is not an art.json motif' % (label, r['art']))
        for i, m in enumerate(r.get('mods') or []):
            where = 'cards %s mod %d' % (label, i)
            if not (isinstance(m, list) and len(m) == 5):
                problem('%s: must be [channel, op, value, when, target]' % where)
                continue
            ch, op, val, when, target = m
            if ch not in stat_ids:
                problem('%s: channel %s is not a stats.json id' % (where, ch))
            elif op not in ('set', stat_ids[ch]['op']):
                problem('%s: op %s does not match channel %s (%s)' % (where, op, ch, stat_ids[ch]['op']))
            if isinstance(val, bool) or not isinstance(val, (int, float)):
                problem('%s: value must be a number' % where)
            elif op == 'mul' and val < 0:
                problem('%s: a multiplier cannot be negative' % where)
            if when not in cond_ids:
                problem('%s: condition %s is not a conditions.json id' % (where, when))
            if target not in ('self', 'team', 'teamAndSelf'):
                problem('%s: target %s unknown' % (where, target))
            if op == 'set' and r['rarity'] != 'mythic':
                problem('%s: only Mythics may set a channel outright' % where)
        if r.get('ability'):
            if not re.search(r'\bclass\s+Ability_%s\b' % re.escape(r['ability']), src):
                problem('cards %s: ability class Ability_%s not found in src' % (label, r['ability']))
        elif not r.get('mods'):
            problem('cards %s: has neither modifiers nor an ability (it would do nothing)' % label)
        if r['drawsCards'] and 'bonusCards' not in (r.get('params') or {}):
            problem('cards %s: card-draw card needs params.bonusCards' % label)
        for k, v in (r.get('params') or {}).items():
            if isinstance(v, bool) or not isinstance(v, (int, float)):
                problem('cards %s: param %s must be a number' % (label, k))
        if r.get('ability') == 'CampfireSpawn':
            item = (r.get('params') or {}).get('item', -1)
            if not (0 <= item < len(cards['spawnItems'])):
                problem('cards %s: params.item does not index spawnItems' % label)
    expected = {'common': 24, 'uncommon': 20, 'rare': 15, 'legendary': 3, 'mythic': 2}
    if len(cards['rows']) != 64:
        problem('cards: %d cards, expected 64' % len(cards['rows']))
    for k, n in expected.items():
        if counts.get(k) != n:
            problem('cards: %d %s cards, expected %d' % (counts.get(k, 0), k, n))
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
    for r in hooks['rows']:
        if r['kind'] == 'harmony':
            if not r['prefixArgs'] and not r['postfixArgs']:
                problem('hooks %s: neither prefixArgs nor postfixArgs' % r['id'])
            if r['prefixArgs'] and not has_call(r['handler'] + 'Prefix', src):
                problem('hooks %s: handler %sPrefix not found in src' % (r['id'], r['handler']))
            if r['postfixArgs'] and not has_call(r['handler'], src):
                problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))
            if r.get('finalizerArgs') and not has_call(r['handler'] + 'Finalizer', src):
                problem('hooks %s: handler %sFinalizer not found in src' % (r['id'], r['handler']))
        else:
            if r['targetMember'].split('.')[-1] not in src:
                problem('hooks %s: event %s is never subscribed in src' % (r['id'], r['targetMember']))
            if not has_call(r['handler'], src):
                problem('hooks %s: handler %s not found in src' % (r['id'], r['handler']))

    # 7. net
    codes = set(); allowed_types = {'byte', 'bool', 'int', 'float', 'double', 'string', 'byte[]', 'int[]'}
    for r in net['rows']:
        if r['code'] in codes or not (90 <= r['code'] <= 119):
            problem('net %s: code %s duplicate or outside 90..119' % (r['name'], r['code']))
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
        for k, v in r['consts'].items():
            if isinstance(v, str) and v.startswith('@') and v[1:] not in ui.get('palette', {}):
                problem('ui %s.%s: colour role %s is not in the palette' % (r['id'], k, v))

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
