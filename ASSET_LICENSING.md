# Asset Licensing and Provenance

This record maps non-code assets in the WorkTrail repository. It is a release
review aid, not legal advice, and does not establish ownership by itself.

## MIT-licensed material

Project-authored software and documentation are licensed under the root
[MIT License](LICENSE), unless a file-specific notice says otherwise. This
includes project-authored scripts, Markdown documentation, and metadata used to
build or describe assets; it does not automatically change the license of the
images or data those files process.

## WorkTrail Brand Assets

The WorkTrail name, wordmarks, logos, app icons, and branded marketing artwork
are outside the MIT grant and are governed by
[`TRADEMARKS.md`](TRADEMARKS.md). The following visual files are treated as
WorkTrail Brand Assets:

- PNG artwork under `design/branding/`, including the Recall Timeline and
  Atomic Nuke banners and the icon reference;
- WorkTrail logo, icon, splash-screen, Store, lock-screen, badge, square, and
  wide-logo image derivatives under `WorkTrail/Assets/`; and
- `WorkTrail/Assets/WorkTrailIcon.ico`.

This classification defines license scope only. It does not replace source and
rights verification for each asset. Forks and independently published builds
must replace these assets unless separate permission has been granted.

## World-clock data and artwork

`WorkTrail/Assets/WorldClocks/` contains GeoNames-derived city data, licensed
under CC BY 4.0, together with WorkTrail-directed Urban Wash city artwork and
atmosphere overlays. The project artwork is outside the repository's MIT grant;
it is not a Wikimedia derivative set. Exact source manifests, transformations,
provenance, and checksums are distributed in
[`ATTRIBUTION.md`](WorkTrail/Assets/WorldClocks/ATTRIBUTION.md),
[`ATTRIBUTION.json`](WorkTrail/Assets/WorldClocks/ATTRIBUTION.json), and the
adjacent provenance records. Preserve the GeoNames attribution when
redistributing the catalog data.

`WorkTrail/Assets/WorldClocks/ThirdParty/OpenWeather/ow_logo.svg` is the official
OpenWeather provider mark included solely for visible linked weather
attribution. The exact official source and SHA-256 are recorded in
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) and
[`IP_PROVENANCE.md`](IP_PROVENANCE.md). The mark and optional OpenWeather
observations are third-party provider material outside the repository MIT grant
and outside WorkTrail Brand Assets. A person supplying an OpenWeather API key is
responsible for the terms of the selected provider plan.

Third-party package assets and embedded web code retain the terms recorded in
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) and its linked generated
notice bundle.

## Celestial photographs

`WorkTrail/Assets/Celestial/` contains public-domain NASA photographs of the
Sun (SDO/AIA) and Moon (Apollo 11). They are third-party observations, outside
the repository MIT grant and outside WorkTrail Brand Assets. Preserve the
NASA and SDO science-team credits, source links, and usage guidance recorded in
[`Celestial/PROVENANCE.md`](WorkTrail/Assets/Celestial/PROVENANCE.md).

## First-party AI-generated asset record

The decorative planetary/event atlas, transparent horizon and meteor illustration in
`WorkTrail/Assets/Celestial/Artwork/`, and the twelve informational zodiac glyphs in
`WorkTrail/Assets/Celestial/Zodiac/`, were generated at the owner's request for the
celestial windows. They are illustrative interface artwork, not NASA observations
or live sky data. Generation prompts, unmodified output dimensions and checksums
are recorded in the adjacent `PROVENANCE.md` files. These records do not change
the separate licensing of existing photographs, geographic textures or branding.

The astronomical agenda's country-holiday and saint illustrations in the same
artwork directory are also first-party decorative assets, outside the MIT code
grant. The country set and three named saint illustrations were AI-generated at
the owner's request; the remaining saint symbols are generated from project-
authored drawing code and stable calendar keys. The adjacent artwork provenance
records their distinct methods, dimensions and hashes. None is a historical
portrait, government emblem, or liturgical source image.


The project owner has confirmed that the first-party WorkTrail visual assets
listed in
[`AI_ASSET_PROVENANCE.md`](design/branding/AI_ASSET_PROVENANCE.md) were created
with AI-assisted workflows run under his control, were selected and reviewed by
him, and are authorized for publication in the public source repository and
official WorkTrail binaries. That dated declaration covers the application
identity family, Atomic Nuke and Recall Timeline artwork, the screenshot
placeholder, the original world-clock watercolor
pilot, the manifest-defined Urban Wash catalog, and its 11 atmosphere overlays.

The declaration does not alter the separate source and license records for
GeoNames, packages, fonts, or other third-party material. The exact Urban Wash
masters, runtime derivatives, overlays, prompts, transformations, and checksums
remain bound to their adjacent manifests and provenance records.

## Publication status

On 2026-08-30 the project owner explicitly authorized public publication of the
generated Urban Wash city artwork and atmosphere overlays after reviewing the
selected style and confirming the applicable ImageGen publication scope. These
assets remain reserved WorkTrail project artwork outside the repository's MIT
grant; publication does not grant downstream trademark or brand rights.
