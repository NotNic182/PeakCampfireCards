import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('card_text', ROOT / 'tools' / 'card_text.py')
text_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(text_tool)
card_sheet = text_tool.card_sheet if hasattr(text_tool, 'card_sheet') else __import__('card_sheet')


class CardTextTests(unittest.TestCase):
    """Export the text card list, edit it like a Notepad user would, and apply it to a temporary cards.json."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        d = Path(self.tmp.name)
        self.cards, self.txt = d / 'cards.json', d / 'card-list.txt'
        shutil.copy(text_tool.CARDS, self.cards)
        self._saved = (text_tool.CARDS, card_sheet.CARDS)
        text_tool.CARDS = card_sheet.CARDS = self.cards
        text_tool.export(self.txt)

    def tearDown(self):
        text_tool.CARDS, card_sheet.CARDS = self._saved
        self.tmp.cleanup()

    def edit(self, old, new, notepad=True):
        """Replace once, then save the way Windows Notepad would: BOM + CRLF."""
        src = self.txt.read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        self.assertIn(old, src)
        src = src.replace(old, new, 1)
        if notepad:
            self.txt.write_bytes(b'\xef\xbb\xbf' + src.replace('\n', '\r\n').encode('utf-8'))
        else:
            self.txt.write_text(src, encoding='utf-8')

    def card(self, cid):
        return next(c for c in json.loads(self.cards.read_text(encoding='utf-8'))['rows'] if c['id'] == cid)

    def test_unchanged_file_changes_nothing_even_saved_by_notepad(self):
        self.edit('CAMPFIRE CARDS', 'CAMPFIRE CARDS')   # no-op edit, but re-saved with BOM + CRLF
        changes, notes = text_tool.apply(self.txt)
        self.assertEqual(([], []), (changes, notes))

    def test_name_text_and_none_edits_apply(self):
        self.edit('name:     To The Bone', 'name:     Sticky Fingers')
        self.edit('drawback: Take 5% more fall Injury.', 'drawback: (None)')
        changes, notes = text_tool.apply(self.txt)
        self.assertEqual(2, len(changes))
        self.assertEqual('Sticky Fingers', self.card(3)['name'])
        self.assertEqual('', self.card(2)['negative'])
        raw = self.cards.read_bytes()
        self.assertEqual(0, raw.count(b'\r\n'), 'cards.json must stay pure LF')

    def test_rarity_edit_applies_and_notes_the_preflight_pin(self):
        self.edit('rarity:   Common\nbenefit:  Jump 30% higher.', 'rarity:   Rare\nbenefit:  Jump 30% higher.')
        changes, notes = text_tool.apply(self.txt)
        self.assertEqual('rare', self.card(2)['rarity'])
        self.assertTrue(any('preflight' in n for n in notes))

    def test_indented_continuation_is_an_error_not_a_silent_truncation(self):
        for cont in ('\n          higher.', '\n\thigher.', '\nhigher.'):
            with self.subTest(cont=cont):
                text_tool.export(self.txt)
                self.edit('benefit:  Jump 30% higher.', 'benefit:  Jump 30%' + cont)
                with self.assertRaisesRegex(ValueError, 'unrecognized line'):
                    text_tool.apply(self.txt)

    def test_block_moved_under_another_banner_is_an_error_unless_the_rarity_line_agrees(self):
        src = self.txt.read_text(encoding='utf-8-sig').replace('\r\n', '\n')
        start = src.index('#3 - To The Bone')
        end = src.index('\n\n#', start)
        block = src[start:end]
        moved = src.replace('\n' + block, '').replace(' RARE  (14 cards)\n' + '=' * 78,
                                                      ' RARE  (14 cards)\n' + '=' * 78 + '\n\n' + block)
        self.txt.write_text(moved, encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'moving a block does nothing'):
            text_tool.apply(self.txt)
        self.txt.write_text(moved.replace('name:     To The Bone\nrarity:   Common',
                                          'name:     To The Bone\nrarity:   Rare'), encoding='utf-8')
        changes, notes = text_tool.apply(self.txt)
        self.assertEqual('rare', self.card(3)['rarity'])

    def test_renaming_only_the_heading_is_an_error(self):
        self.edit('#3 - To The Bone', '#3 - Crimp Master')
        with self.assertRaisesRegex(ValueError, 'name: line'):
            text_tool.apply(self.txt)

    def test_losing_the_key_placeholder_is_an_error(self):
        src = self.txt.read_text(encoding='utf-8-sig')
        line = next(l for l in src.splitlines() if l.startswith('benefit:') and '{key}' in l)
        self.edit(line, line.replace('{key}', 'the ability key'))
        with self.assertRaisesRegex(ValueError, 'placeholder'):
            text_tool.apply(self.txt)

    def test_deleted_card_bad_rarity_and_duplicate_number_are_errors(self):
        self.edit('#3 - To The Bone', '#2 - To The Bone')
        with self.assertRaisesRegex(ValueError, 'appears twice'):
            text_tool.apply(self.txt)
        text_tool.export(self.txt)
        self.edit('rarity:   Mythic', 'rarity:   Mythical')
        with self.assertRaisesRegex(ValueError, 'must be one of'):
            text_tool.apply(self.txt)

    def test_export_refuses_a_card_text_with_an_embedded_line_break(self):
        data = json.loads(self.cards.read_text(encoding='utf-8'))
        data['rows'][0]['positive'] = 'Line one.\nLine two.'
        self.cards.write_text(json.dumps(data), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'line break'):
            text_tool.export(self.txt)


if __name__ == '__main__':
    unittest.main()
