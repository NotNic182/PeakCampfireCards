# Your card editing files

Use **card-editing.xlsx** if you have Excel, Google Sheets or LibreOffice. Its **Cards** tab contains all 64
existing names, benefit descriptions, drawback descriptions and flavor lines, copied exactly from the mod.

Edit the blue columns: **name**, **benefit**, **drawback**, **flavor**, **requested_effects**. For an effect change,
write what you want in plain English in requested_effects, including its numbers and when it triggers. Keep
**id** and **key** unchanged so the card can be matched to its code when you return the file. Leave a drawback
empty to display the game's standard no-drawback message.

**Current code** contains the actual modifiers, ability names and parameters. **notes** flags existing
limitations. In particular, Bing Bong's Baggage is present in the source but ships disabled: its extra pocket
does not work. **New ideas** starts empty for you to add concepts; no experimental card has been added.

**card-editing.csv** holds the same catalog in a plain spreadsheet format. For renamed/reworded cards, the
import tool validates the stable identities and produces a separate JSON review copy. Gameplay changes and
new ideas need implementation; a text change never silently changes stats.

See [the effect investigation](../../docs/effect-feasibility.md) before picking new mechanics. The intended
game version is PEAK **2.6.b**. Runtime tests on that version are still needed for new physics effects.
