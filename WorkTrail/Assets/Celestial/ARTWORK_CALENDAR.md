# Calendar artwork provenance

These images are decorative thumbnails for the astronomical agenda. They are not records of a specific celebration, photographs, portraits, or claims about historical appearance. The holiday art uses broad visual associations with each country. The saint art uses symbolic attributes, not an approved or exhaustive catalogue of religious iconography.

## Holiday countries

The 17 `Artwork/holiday-<lowercase ISO 3166-1 alpha-2>-v1.png` files were each generated separately with built-in ImageGen on 2026-09-24. The existing project image `Artwork/moonset-v1.png` was used only as a reference for the restrained cinematic navy, ivory and gold style. No third-party image was used. The original outputs were copied without pixel edits. Each final image is a 1254 × 1254 32-bit RGBA PNG with genuine transparent alpha.

The shared prompt requested a single centered, realistic decorative symbol for a country holiday event in WorkTrail's dark frosted-acrylic astronomical agenda. It specified a square composition readable at 48–96 pixels, generous transparent margins, navy shadows, warm ivory/gold light, and no text, literal flags, badges, frames, UI or watermarks. The country-specific subjects were:

| Code | Subject in the ImageGen prompt |
| --- | --- |
| AU | Golden wattle and eucalyptus against a subtle southern sky |
| BD | White water lily with crimson dawn light |
| CA | Crimson maple leaf edged by ivory frost |
| CN | Red-and-gold traditional hanging lantern |
| DE | Brandenburg Gate silhouette with black-red-gold light accents |
| FR | Golden fleur-de-lis ornament with tricolor silk glow |
| IN | Brass diya lamp with marigold petals |
| IT | Roman marble arch, laurel branch and muted green/red ribbon |
| JP | Sakura branch and ivory rising-sun disc |
| KH | Angkor-inspired lotus tower |
| KR | Traditional lantern with restrained red-blue accents |
| LA | Dok champa flower and warm lantern glow |
| NZ | Silver fern and faint southern-sky glints |
| PL | White eagle feather and red/ivory ribbon |
| SG | Vanda Miss Joaquim orchid |
| US | Ivory-gold star and subtle red/blue ribbon |
| VN | Red silk lantern with a small ivory lotus motif |

These icons indicate the selected country; they do not identify the particular holiday or imply that every holiday in that country uses the illustrated symbol.

## Saints and related calendar entries

There is one file per `eventKey` in `WorkTrail.Core/Data/celestial-calendar.json`, named `Artwork/saint-<exact eventKey>-v1.png`. This includes the calendar's Marian and collective entries. The source data contains 211 distinct keys.

Three entries were generated individually with built-in ImageGen on 2026-09-24, using `moonset-v1.png` only as a style reference:

| File | Individual prompt subject |
| --- | --- |
| `saint-StFrancisAssisi-v1.png` | Symbolic Franciscan friar in a brown habit with a small bird; not a claimed likeness |
| `saint-StAgnes-v1.png` | Symbolic young Christian woman with a white lamb and palm; not a claimed likeness |
| `saint-OwnerOurLadyOfMercy-v1.png` | Symbolic Marian figure with a protective blue mantle; not a claimed likeness |

Their prompts specified reverent painterly realism, a centered bust or figure, ivory/gold light with navy shadow, true alpha transparency, and no names, text, badges or watermarks. These three original outputs are 1254 × 1254 32-bit RGBA PNGs.

The other 208 entries were drawn locally by [`scripts/Generate-SaintArtwork.ps1`](../../../scripts/Generate-SaintArtwork.ps1) from each entry's exact `eventKey` and Latin name. The script chooses a large foreground symbol from explicit name/title matches where available. For other entries it chooses a neutral decorative emblem from a stable hash of the key. It also draws a name-derived monogram and a key-seeded constellation, so every file is a distinct composition. The script renders at 512 px and downsamples to a 256 × 256 32-bit RGBA PNG for 60 px agenda cards. The script preserves existing files, including the three individual ImageGen illustrations. Run it from the repository root with PowerShell 7:

```powershell
pwsh -NoProfile -File ./scripts/Generate-SaintArtwork.ps1
```

The deterministic emblems share a visual grammar. Some motifs are decorative rather than traditional attributes of a named saint; do not describe them as canonically accurate. The three ImageGen portraits are more detailed than the emblems. All 228 new PNGs were checked for their expected names, square dimensions, 32-bit RGBA pixel format, transparent corners, and distinct SHA-256 hashes. A 60 px contact sheet was inspected against a dark agenda-like background.
