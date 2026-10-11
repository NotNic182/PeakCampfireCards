#!/usr/bin/env python3
"""Export the live design sheet for editing; import text into a separate review copy.

CSV uses only the standard library. Pass --xlsx to also create a formatted workbook
(requires openpyxl). Requested mechanics always need a code review/implementation.
"""
import argparse
import copy
import csv
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'design' / 'cards.json'
VERSION = re.search(r'<Version>([^<]+)</Version>', (ROOT / 'src/CampfireCards/CampfireCards.csproj').read_text()).group(1)
FIELDS = ['id', 'key', 'rarity', 'category', 'enabled_by_default', 'name', 'benefit',
          'drawback', 'flavor', 'requested_effects', 'notes', 'current_modifiers',
          'current_ability', 'current_parameters']
TEXT_FIELDS = {'name': 'name', 'benefit': 'positive', 'drawback': 'negative', 'flavor': 'flavor'}
NOTES = {
    'PackRat': 'Now halves total weight and blocks new items in the third pocket; existing items remain retrievable. Enabled by default on a fresh config; previous Enabled=false is preserved.',
    'DoubleDeal': 'Two bonus cards and one permanent random penalty. The selected penalty is shown in Current Build and retained in state sync.',
    'SporeLungs': 'Requested Mandrake/Zombie immunity plus next-biome zombie spawn is pending. The existing spores resistance remains; see docs/card-edit-review.md.',
    'BonePlating': 'Native timed Infinite Stamina, Invincibility or Low Gravity. Low Gravity is floating, not free flight. Ghost time pauses the ten-minute clock.',
    'SecondWind': 'Manual V; below 20 stamina, restore 25, five-minute cooldown. Key configurable.',
    'EmergencyPurge': 'Manual K; clear 20 from the worst Poison/Spores/Drowsy status at 35+, once per biome. Key configurable.',
    'BloodBattery': 'Manual B; spend 15 Injury for 60 bonus stamina, once per biome. Key configurable.',
    'LuckyLunch': 'Preserved food stays in hand; no peel is spawned for the preserved use.',
    'EfficientMedic': 'Item healing only; healing amulet and healing dart are not scaled.',
    'FieldMedic': 'Item healing only; healing amulet and healing dart are not scaled.',
    'CleansingRitual': 'Medical items only (not ordinary food).',
    'ScavengersInstinct': 'Screen labels through terrain, not object outlines.',
    'HerosArrival': 'Book of Bones / Scout Effigy revives are not detected for this reward.',
    'Thornbreaker': 'New thorns only; one remaining thorn is rounded up and is not removed.',
    'EternalCampfire': 'The revive is for a nearby passed-out scout; host migration does not preserve used revive charges.',
}


def catalog(sheet):
    rows = []
    for c in sheet['rows']:
        row = {field: '' for field in FIELDS}
        row.update(id=c['id'], key=c['key'], rarity=c['rarity'], category=c['category'],
                   enabled_by_default='yes',
                   name=c['name'], benefit=c['positive'], drawback=c['negative'],
                   flavor=c.get('flavor', ''), notes=NOTES.get(c['key'], ''),
                   current_modifiers=json.dumps(c['mods'], ensure_ascii=False),
                   current_ability=c['ability'], current_parameters=json.dumps(c['params'], ensure_ascii=False))
        if c['key'] == 'SporeLungs':
            row['requested_effects'] = 'Pending paired replacement: Mandrakes and Zombies do not affect you; a Zombie spawns somewhere random in the next biome.'
        rows.append(row)
    return rows


def write_csv(rows, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(rows)


def write_workbook(rows, path):
    from openpyxl import Workbook
    from openpyxl.styles import Alignment, Font, PatternFill, Protection
    wb = Workbook()
    guide = wb.active; guide.title = 'Read me'
    instructions = [
        ('Campfire Cards — edit and send back', f'PEAK target: 2.6.b. Source: design/cards.json, mod {VERSION}. All 64 cards are included. See card-edit-review.xlsx for your submitted requests and their implementation status.'),
        ('Edit the blue columns on Cards', 'Name, Benefit, Drawback, Flavor, Requested effects. Existing text is copied exactly from the mod.'),
        ('Changing gameplay', 'Write mechanics in plain English in Requested effects, including numbers, trigger, duration, and drawback. Text edits alone do not change gameplay.'),
        ('Keep these unchanged', 'ID and Key identify the card for reimport. Rarity, Category and Enabled by default are reference fields.'),
        ('No drawback', 'Leave Drawback blank; the game shows its standard no-drawback message.'),
        ('Ability key placeholder', 'Keep {key} in manual card text: the game replaces it with your configured binding. Defaults: Nic of Time V, Discount Cure-All K, Bootleg Scoutmaster Bugle B, Budget Checkpoint Flag N.'),
        ('Your own images', 'Save <Key>.png in art/cards beside CampfireCards.dll. See card-image-map.csv and docs/card-images.md. Restart PEAK after changing images.'),
        ('Current implementation', 'Notes flag known limitations. Current code shows the actual modifiers, ability handler and parameters. Existing Enabled settings are preserved during upgrade.'),
        ('Numbers in the code', 'Multiplier 0.8 = 20% less, 1.3 = 30% more. Stamina/status amounts 0.1 = 10 game units; cooldowns/delays are seconds, radii metres, chance 0.65 = 65%.'),
        ('New ideas', 'Use New ideas for new card concepts. See docs/effect-feasibility.md for supported effects and what still needs a playtest.'),
        ('Return the file', 'Save as .xlsx and send it back. CSV is also provided for Excel, Sheets, LibreOffice or a text editor.'),
    ]
    for row in instructions: guide.append(row)
    cards = wb.create_sheet('Cards')
    card_fields = FIELDS[:11]
    cards.append(card_fields)
    for row in rows: cards.append([row[f] for f in card_fields])
    current = wb.create_sheet('Current code')
    current_fields = ['id', 'key', 'current_modifiers', 'current_ability', 'current_parameters']
    current.append(current_fields)
    for row in rows: current.append([row[f] for f in current_fields])
    ideas = wb.create_sheet('New ideas')
    ideas.append(['name', 'benefit', 'drawback', 'requested_effects', 'rarity', 'notes'])
    for _ in range(12): ideas.append([''] * 6)
    editable = {'name', 'benefit', 'drawback', 'flavor', 'requested_effects'}
    for ws in (cards, current, ideas):
        ws.freeze_panes = 'F2' if ws == cards else 'C2'
        ws.auto_filter.ref = ws.dimensions
        ws.row_dimensions[1].height = 30
        for cell in ws[1]:
            cell.fill = PatternFill('solid', fgColor='173D45'); cell.font = Font(color='FFFFFF', bold=True)
            cell.alignment = Alignment(wrap_text=True, vertical='center')
        for cells in ws.iter_rows(min_row=2):
            ws.row_dimensions[cells[0].row].height = 100 if ws == cards else 65
            for cell in cells:
                field = ws.cell(1, cell.column).value
                cell.alignment = Alignment(wrap_text=True, vertical='top')
                cell.protection = Protection(locked=False)
                cell.fill = PatternFill('solid', fgColor='E5F3FD' if ws == cards and field in editable else 'F3F6F5')
                if isinstance(cell.value, str): cell.data_type = 's' # keep user text literal, even if it starts with =
        for col in ws.columns:
            field = col[0].value
            ws.column_dimensions[col[0].column_letter].width = {
                'id': 7, 'key': 25, 'rarity': 13, 'category': 14, 'enabled_by_default': 18,
                'name': 30, 'benefit': 52, 'drawback': 44, 'flavor': 34,
                'requested_effects': 56, 'notes': 50, 'current_modifiers': 80,
                'current_ability': 24, 'current_parameters': 66,
            }.get(field, 35)
    # Prevent IDs being silently reformatted; reference values remain easy to copy.
    for ws in (cards, current):
        for cell in ws['B'][1:]: cell.number_format = '@'
    guide.column_dimensions['A'].width = 33; guide.column_dimensions['B'].width = 105
    for row in guide:
        guide.row_dimensions[row[0].row].height = 48
        row[0].font = Font(bold=True, color='173D45')
        for cell in row: cell.alignment = Alignment(wrap_text=True, vertical='top')
    wb.save(path)


def import_text(sheet, rows):
    """Validate every identity before changing any text; gameplay suggestions are never applied as code."""
    by_id = {str(c['id']): c for c in sheet['rows']}
    seen = set(); suggestions = []
    result = copy.deepcopy(sheet)
    updated = {str(c['id']): c for c in result['rows']}
    for row in rows:
        identity = str(row['id'])
        if identity in seen or identity not in by_id:
            raise ValueError('Duplicate or unknown card id: ' + identity)
        seen.add(identity)
        if row['key'] != by_id[identity]['key']:
            raise ValueError('Key changed for card ' + identity)
        for field in ('rarity', 'category'):
            if row[field] != by_id[identity][field]:
                raise ValueError(field + ' changed for card ' + identity + '; requires design review')
        if not row['name'].strip() or not row['benefit'].strip():
            raise ValueError('Name and benefit cannot be empty for card ' + identity)
        for field, target in TEXT_FIELDS.items(): updated[identity][target] = row[field]
        if row.get('requested_effects', '').strip(): suggestions.append((row['key'], row['requested_effects']))
    if seen != set(by_id): raise ValueError('Missing cards: ' + ', '.join(sorted(set(by_id) - seen)))
    return result, suggestions


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    export = sub.add_parser('export')
    export.add_argument('--output-dir', type=Path, default=ROOT / 'design' / 'editable')
    export.add_argument('--xlsx', action='store_true')
    imp = sub.add_parser('import-text', help='write text edits to a separate JSON review copy')
    imp.add_argument('input', type=Path, help='the edited CSV or XLSX workbook')
    imp.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sheet = json.loads(SOURCE.read_text(encoding='utf-8'))
    if args.action == 'export':
        rows = catalog(sheet); write_csv(rows, args.output_dir / 'card-editing.csv')
        with (args.output_dir / 'card-image-map.csv').open('w', encoding='utf-8-sig', newline='') as f:
            writer = csv.writer(f)
            writer.writerow(['id', 'key', 'filename', 'name'])
            writer.writerows([c['id'], c['key'], c['key'] + '.png', c['name']] for c in sheet['rows'])
        if args.xlsx: write_workbook(rows, args.output_dir / 'card-editing.xlsx')
        print('Exported %d cards to %s' % (len(rows), args.output_dir))
    else:
        if args.output.resolve() == SOURCE.resolve(): parser.error('Output must be a separate review copy, not design/cards.json')
        if args.input.suffix.lower() == '.xlsx':
            from openpyxl import load_workbook
            ws = load_workbook(args.input)['Cards']
            if any(c.data_type == 'f' for row in ws for c in row): parser.error('Cards must contain plain text, not formulas')
            values = list(ws.values); headers = values[0]
            rows = [{k: '' if v is None else str(v) for k, v in zip(headers, row)} for row in values[1:] if any(v is not None for v in row)]
        else:
            with args.input.open(encoding='utf-8-sig', newline='') as f: rows = list(csv.DictReader(f))
        result, suggestions = import_text(sheet, rows)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print('Wrote a text-only review copy to', args.output)
        for key, requested in suggestions: print('Needs gameplay implementation:', key, requested)


if __name__ == '__main__': main()
