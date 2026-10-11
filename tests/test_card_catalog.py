import copy
import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('card_catalog', ROOT / 'tools' / 'card_catalog.py')
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)


class CardCatalogTests(unittest.TestCase):
    def setUp(self):
        self.sheet = json.loads(catalog.SOURCE.read_text())
        self.rows = catalog.catalog(self.sheet)

    def test_all_cards_round_trip_including_quotes_commas_and_line_breaks(self):
        self.rows[0]['name'] = 'Nic\'s "new", card'
        self.rows[0]['benefit'] = 'First line\nSecond line'
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / 'cards.csv'
            catalog.write_csv(self.rows, path)
            with path.open(encoding='utf-8-sig', newline='') as f:
                read = list(csv.DictReader(f))
        result, suggestions = catalog.import_text(self.sheet, read)
        self.assertEqual(64, len(result['rows']))
        self.assertEqual(self.rows[0]['name'], result['rows'][0]['name'])
        self.assertEqual(self.rows[0]['benefit'], result['rows'][0]['positive'])
        self.assertEqual(self.sheet['rows'][0]['mods'], result['rows'][0]['mods'])
        self.assertEqual([('SporeLungs', next(r for r in self.rows if r['key'] == 'SporeLungs')['requested_effects'])], suggestions)

    def test_unchanged_catalog_does_not_change_design_or_gameplay(self):
        result, _ = catalog.import_text(self.sheet, self.rows)
        self.assertEqual(self.sheet, result)

    def test_duplicate_missing_or_renamed_identifiers_are_rejected(self):
        for change in ('duplicate', 'missing', 'rename', 'unknown'):
            rows = copy.deepcopy(self.rows)
            if change == 'duplicate': rows.append(rows[0])
            if change == 'missing': rows.pop()
            if change == 'rename': rows[0]['key'] = 'NewKey'
            if change == 'unknown': rows[0]['id'] = 200
            with self.subTest(change=change), self.assertRaises(ValueError):
                catalog.import_text(self.sheet, rows)

    def test_gameplay_requests_are_reported_and_never_silently_applied(self):
        self.rows[0]['requested_effects'] = 'Half size, double stamina, fragile to falling rocks'
        result, suggestions = catalog.import_text(self.sheet, self.rows)
        self.assertEqual(self.sheet, result)
        self.assertIn((self.rows[0]['key'], self.rows[0]['requested_effects']), suggestions)
        self.assertIn('SporeLungs', [key for key, _ in suggestions])

    def test_redesigned_inventory_card_reports_upgrade_behavior(self):
        pack_rat = next(r for r in self.rows if r['key'] == 'PackRat')
        self.assertEqual('yes', pack_rat['enabled_by_default'])
        self.assertIn('previous Enabled=false is preserved', pack_rat['notes'])


if __name__ == '__main__': unittest.main()
