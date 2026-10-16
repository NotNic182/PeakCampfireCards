#!/usr/bin/env python3
"""The card list as one editable text file, and the tool that puts the edits back into the mod.

  python tools/card_text.py export                 writes design/editable/card-list.txt from design/cards.json
  python tools/card_text.py apply <edited.txt>     writes the edits into design/cards.json (then regenerate and build)
  python tools/card_text.py apply <edited.txt> --check   only reports what would change

One block per card, grouped by rarity: name, rarity, benefit, drawback and flavor exactly as shown
in game, plus a reference list of what the card actually does. Only the labelled lines are applied;
numbers live in the card sheet (tools/card_sheet.py). The parser is deliberately strict: an edit it
does not understand is a hard NOT APPLIED error, never a silent drop.
"""
import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from card_sheet import (CARDS, CONDITIONS, PERCENT_CHANGE, RARITIES, STATS,
                        load, number_rows, save_cards, text)

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'design' / 'editable' / 'card-list.txt'
FIELDS = ('name', 'rarity', 'benefit', 'drawback', 'flavor')
NONE = '(none)'
RULE = '=' * 78

CARD_HEAD = re.compile(r'^#(\d+)\s*(?:[-–—]\s*(.*))?$')
FIELD = re.compile(r'^(name|rarity|benefit|drawback|flavor)\s*:\s*(.*)$', re.IGNORECASE)
BANNER = re.compile(r'^\s*(' + '|'.join(RARITIES) + r')\s*(\(\d+\s*cards?\))?\s*$', re.IGNORECASE)
REFERENCE = re.compile(r'^\s+(effects\b.*|\.\s.*)$')       # the "effects" header and its ". " bullets
NONE_RE = re.compile(r'\(?\s*none\s*\)?', re.IGNORECASE)   # (none), (None), none...

HOW_TO_EDIT = """\
 HOW TO EDIT
   - Change anything on the name / rarity / benefit / drawback / flavor lines.
     Keep each one on a single line; a wrapped second line is an error.
   - The "#N - Name" line above each card is just a heading: rename a card on
     its name: line (the heading may stay stale, or change it to match).
   - rarity is one of: Common, Uncommon, Rare, Legendary, Mythic. The card's
     own "rarity:" line is what counts; moving a block under another section
     banner does nothing by itself (and the tool will tell you so).
   - Write (none) on a drawback or flavor line the card should not have.
   - Keep {key} in key-press cards: the game fills in the real key.
   - The indented "effects" lines show what the card actually does. They are
     reference only and are ignored; to change a number, ask for it in words.
   - Do not change the "#N" card numbers, and do not add or remove cards.
   - Send the file back and it is applied straight into the mod."""


def effect_lines(card, stats, conditions):
    lines = []
    for ref, effect, when, who, value, unit in number_rows(card, stats, conditions):
        shown = f'{value:+g}' if unit == PERCENT_CHANGE else f'{value:g}'
        line = f'{effect}: {shown} {unit}'.rstrip()
        extras = ', '.join(x for x in (who, when) if x)
        lines.append(f'     . {line}' + (f'  ({extras})' if extras else ''))
    return lines or ['     . (text only: no numbers)']


# ---------------------------------------------------------------------------------------------- export
def export(path=OUTPUT):
    sheet = load(CARDS)
    stats = {r['id']: r for r in load(STATS)['rows']}
    conditions = {r['id']: r['shown'] for r in load(CONDITIONS)['rows']}

    for c in sheet['rows']:
        for field in ('name', 'rarity', 'positive', 'negative', 'flavor'):
            if '\n' in str(c.get(field) or ''):
                raise ValueError(f'card #{c["id"]} {c["name"]}: the {field} text contains a line break, '
                                 'which this one-line-per-field file cannot hold - use tools/card_sheet.py')

    out = [f'CAMPFIRE CARDS - CARD LIST ({len(sheet["rows"])} cards)', RULE, '', HOW_TO_EDIT, '']
    for rarity in RARITIES:
        cards = sorted((c for c in sheet['rows'] if c['rarity'] == rarity), key=lambda c: c['name'].lower())
        out += ['', RULE, f' {rarity.upper()}  ({len(cards)} cards)', RULE]
        for c in cards:
            out += ['', f'#{c["id"]} - {c["name"]}',
                    f'name:     {c["name"]}',
                    f'rarity:   {c["rarity"].capitalize()}',
                    f'benefit:  {c["positive"]}',
                    f'drawback: {c["negative"] or NONE}',
                    f'flavor:   {c.get("flavor") or NONE}',
                    '   effects (reference only):']
            out += effect_lines(c, stats, conditions)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('\n'.join(out) + '\n', encoding='utf-8')
    return path


# ---------------------------------------------------------------------------------------------- apply
def parse(path):
    """The edited file back as {card id: block}. Strict: any line it does not recognize is an error."""
    cards, cur, section = {}, None, None
    for lineno, raw in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
        line = raw.rstrip()
        m = CARD_HEAD.match(line)
        if m:
            cid = int(m.group(1))
            if cid in cards:
                raise ValueError(f'line {lineno}: card #{cid} appears twice')
            cur = cards[cid] = {'fields': {}, 'title': (m.group(2) or '').strip(), 'section': section}
            continue
        m = FIELD.match(line)
        if m:
            if cur is None:
                raise ValueError(f'line {lineno}: a "{m.group(1)}:" line before any card number (#N)')
            field = m.group(1).lower()
            if field in cur['fields']:
                raise ValueError(f'line {lineno}: card #{list(cards)[-1]} has two "{field}:" lines')
            cur['fields'][field] = m.group(2).strip()
            continue
        if not line or line.startswith('='):
            continue
        m = BANNER.match(line)
        if m:
            section = m.group(1).lower()
            continue
        if cur is None or REFERENCE.match(line):
            continue
        raise ValueError(f'line {lineno}: unrecognized line {line!r} - keep each field on its single labelled line')
    return cards


def check_placeholders(where, old, new):
    if '{key}' in old and '{key}' not in new:
        raise ValueError(f'{where}: the text lost its {{key}} placeholder - keep {{key}} where the key '
                         'should appear (the game fills in the real key)')
    for token in re.findall(r'\{[^{}]*\}', new):
        if token != '{key}':
            raise ValueError(f'{where}: unknown placeholder {token} - only {{key}} is filled in by the game')


def apply(path, check=False):
    sheet = load(CARDS)
    by_id = {c['id']: c for c in sheet['rows']}
    parsed = parse(path)

    unknown = set(parsed) - set(by_id)
    if unknown:
        raise ValueError('there is no card ' + ', '.join(f'#{i}' for i in sorted(unknown)))
    missing = set(by_id) - set(parsed)
    if missing:
        raise ValueError('missing card numbers ' + ', '.join(f'#{i}' for i in sorted(missing)))

    changes, rarity_changed, renamed = [], False, False
    for cid, block in sorted(parsed.items()):
        where = f'card #{cid}'
        fields = block['fields']
        lost = [f for f in FIELDS if f not in fields]
        if lost:
            raise ValueError(f'{where}: missing the {", ".join(lost)} line(s)')
        c = by_id[cid]
        name, benefit = text(fields['name']), text(fields['benefit'])
        if not name or not benefit or NONE_RE.fullmatch(name) or NONE_RE.fullmatch(benefit):
            raise ValueError(f'{where}: the name and benefit cannot be empty')
        if block['title'] and block['title'] not in (name, c['name']):
            raise ValueError(f'{where}: the heading says "{block["title"]}" but the name: line says "{name}" - '
                             'rename the card on its name: line (the "#N - Name" line is just a heading)')
        rarity = text(fields['rarity']).lower()
        if rarity not in RARITIES:
            raise ValueError(f'{where}: rarity "{fields["rarity"]}" must be one of '
                             + ', '.join(x.capitalize() for x in RARITIES))
        if block['section'] and block['section'] != rarity and rarity == c['rarity']:
            raise ValueError(f'{where}: the block sits under the {block["section"].upper()} banner but its '
                             f'rarity: line still says {rarity.capitalize()} - the rarity: line is what counts, '
                             'so change that line (moving a block does nothing by itself)')
        blank = lambda v: '' if NONE_RE.fullmatch(v) else v
        new = {'name': name, 'rarity': rarity, 'positive': benefit,
               'negative': blank(text(fields['drawback'])), 'flavor': blank(text(fields['flavor']))}
        for field in ('positive', 'negative'):
            check_placeholders(where, c[field], new[field])
        for field, value in new.items():
            if c.get(field, '') != value:
                changes.append(f'#{cid} {c["name"]}: {field} "{c.get(field, "")}" -> "{value}"')
                rarity_changed |= field == 'rarity'
                renamed |= field == 'name'
                c[field] = value

    notes = []
    if rarity_changed:
        notes.append('a rarity changed: update the per-rarity counts pinned in tools/preflight.py before building')
    if renamed:
        notes.append('a card was renamed: tools/gen.py rewrites its README row by matching the old text, '
                     'and warns if it cannot - check its output')
    if not check and changes:
        save_cards(sheet)
    return changes, notes


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='action', required=True)
    exp = sub.add_parser('export')
    exp.add_argument('--output', type=Path, default=OUTPUT)
    app = sub.add_parser('apply')
    app.add_argument('input', type=Path)
    app.add_argument('--check', action='store_true', help='report the changes without writing them')
    args = parser.parse_args()
    if args.action == 'export':
        print('Wrote', export(args.output))
        return
    try:
        changes, notes = apply(args.input, args.check)
    except ValueError as e:
        print('NOT APPLIED:', e)
        sys.exit(1)
    print(f'{len(changes)} change(s){" (check only, nothing written)" if args.check else ""}:')
    for c in changes:
        print('  ' + c)
    for n in notes:
        print('NOTE: ' + n)
    if changes and not args.check:
        print('Next: python tools/gen.py, then tools/build.sh')


if __name__ == '__main__':
    main()
