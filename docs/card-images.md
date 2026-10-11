# Your own card images

Campfire Cards 0.6.1 can load a PNG for any existing card. The image fills the illustration window; the mod still draws the card name, rarity, benefit and drawback. The image keeps its proportions and fits inside the window.

1. Close PEAK. Find the installed `CampfireCards.dll`. With a mod manager, open your active profile's folder and look under `BepInEx/plugins` for the Campfire Cards folder.
2. Inside the folder containing the DLL, open or create `art/cards`.
3. Save your image there as `<Key>.png`. Use the exact filename from [card-image-map.csv](../design/editable/card-image-map.csv), including capitalization. Restart PEAK after adding or replacing an image.

For example:

```text
CampfireCards/
  CampfireCards.dll
  art/
    cards/
      GeckoGrip.png       # Piton Fingers
      BonePlating.png     # BingBong
      SecondWind.png      # Nic of Time
```

The filename uses the card's **stable Key**, not its display name. This lets you rename a card without renaming its image. The updated spreadsheet includes the same Key column, and the ZIP includes `art/cards/card-image-map.csv`.

Use a **1024 × 576 PNG** as a starting point. The window is roughly 16:9. Transparent backgrounds work. Other proportions fit with space around them; they are not stretched. Keep each dimension at **2048 pixels or less** and each file at **8 MiB or less**. Export ordinary static PNGs; draw just the illustration rather than a full card with baked-in text.

Cards without custom images keep their original animated art. A rejected image also falls back to the original and writes one warning to the BepInEx log. If an image does not appear, check the folder, stable Key, `.png` extension and limits above, then restart PEAK.

Images are local cosmetics: other players see their own installed images. Share the PNGs if you want matching art. Replacing art does not create a new card or change its gameplay effects.

For repository builds, place PNGs in `assets/card-images/`. `python tools/package.py` includes them under `plugins/CampfireCards/art/cards/`. It also packages the instructions and filename map. Unity image rendering still needs a live check in PEAK; the loader compiles against the available game references and its header/size validation is unit tested.
