# Editing the cards

Open **card-sheet.xlsx** in Excel, Google Sheets or LibreOffice. Edit the blue cells, save as .xlsx, and send it
back: it is applied straight into the mod.

- **Cards** tab: each card's name, rarity, benefit, drawback and flavor, exactly as the card shows them in game
  (benefit in green, drawback in red, no labels). Leave Drawback empty for a card with no drawback. Keep `{key}` in
  key-press cards: the game fills in the player's key.
- **Numbers** tab: every number a card uses, in plain units. "% change": +40 means 40% more, -12 means 12% less, -100
  means none at all. Points: 25 is a quarter of the stamina bar or a status bar. Seconds and metres are what they say.
- **Difficulty** tab: how strong benefits are on each difficulty, in percent. The cards are written for Ascent 7–8
  (100%); easier difficulties shrink every benefit to that share, and above 100% amplifies them. Drawbacks and
  Mythics stay at 100%.
- The **#** column matches each row to its card: don't change it, and don't add or remove rows.
- The card text is what players read; the Numbers tab is what the game does. When you change a number, change the
  text too. Applying the sheet warns when a card's text still shows the old number.

For the mod's maintainer:

```bash
python tools/card_sheet.py export                      # write card-sheet.xlsx from design/cards.json
python tools/card_sheet.py apply edited.xlsx --check   # show what would change
python tools/card_sheet.py apply edited.xlsx           # write it into design/cards.json, then gen + build
```

Applying refuses impossible values (below -100%, negative seconds, a rarity that doesn't exist) before writing
anything, and raises the card balance version when a number changes so saved configs pick up the new numbers once.

To change an illustration, use **card-image-map.csv** for each card's PNG filename and see
[the image instructions](../../docs/card-images.md).

The older files here (card-editing.xlsx/.csv, card-edit-review.xlsx) are the history of the 0.6.0 review pass.
