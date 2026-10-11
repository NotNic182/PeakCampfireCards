# Add card art

Save PNG illustrations here when building from the repository. The packager copies them beside the plugin to `art/cards/`.

For an installed mod, put your PNGs in `art/cards/` beside `CampfireCards.dll` instead. Name each file with its stable card Key; see `card-image-map.csv`. Example: `GeckoGrip.png` changes the illustration for **Piton Fingers**.

Restart PEAK after changes. Recommended: 1024 × 576; maximum: 2048 pixels per dimension, 8 MiB per file. Transparency is supported. Missing or rejected PNGs use the original animated art.

Full instructions are in `docs/card-images.md` in the plugin folder or the repository.
