# Third-Party Notices

TrackMeUp's project-authored software and documentation are governed by the
[MIT License](LICENSE), unless a file states otherwise. The TrackMeUp name and
brand assets are governed separately by [the trademark and brand policy](TRADEMARKS.md).
Each third-party component, data set, or asset retains its own license terms;
this inventory does not relicense third-party material as MIT.

This file inventories the direct `PackageReference` dependencies declared in
tracked `.csproj` files and distributed world-clock data/media assets.

- NuGet scope: direct dependencies declared in tracked project files as of 2026-09-17. Transitive NuGet packages are not included yet.
- Source of truth: repository `*.csproj` files, official NuGet package metadata, and installed package license/notice files.
- Special cases: packages that publish a bundled license file instead of a NuGet SPDX expression are called out explicitly below.
- Binary-release boundary: before distributing a self-contained installer, generate and package notices for the complete published runtime closure, including transitive NuGet/runtime/native components. This source inventory alone is not a complete binary-distribution notice bundle.

## Summary

- Runtime dependencies: 26 unique packages, plus the pinned LibreHardwareMonitor source build below.
- Build-only hardware source generator: Microsoft.Windows.CsWin32 0.3.269 (MIT).
- Test-only dependencies: 4 unique packages.
- Open-source licenses observed: `MIT`, `Apache-2.0`, `MPL-2.0`, `LGPL-2.1` (upstream embedded PawnIO modules), plus the separately recorded Creative Commons/public-domain asset terms.
- Additional Microsoft package terms observed: `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`.

## Runtime Dependencies

| Package | Badges | License / terms | Used by |
| --- | --- | --- | --- |
| [CosineKitty.AstronomyEngine 2.1.19](https://www.nuget.org/packages/CosineKitty.AstronomyEngine/2.1.19) | NuGet | [MIT](https://github.com/cosinekitty/astronomy/blob/v2.1.19/LICENSE) | TrackMeUp.Core; shared solar/lunar/planet ephemerides, rise/set, twilight, lunar quarters and seasons |
| [Lucene.Net](https://www.nuget.org/packages/Lucene.Net/4.8.0-beta00018) | [![NuGet](https://img.shields.io/nuget/v/Lucene.Net?label=NuGet)](https://www.nuget.org/packages/Lucene.Net/4.8.0-beta00018) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) (embedded LICENSE.txt) | TrackMeUp.Search |
| [Lucene.Net.Analysis.Common](https://www.nuget.org/packages/Lucene.Net.Analysis.Common/4.8.0-beta00018) | [![NuGet](https://img.shields.io/nuget/v/Lucene.Net.Analysis.Common?label=NuGet)](https://www.nuget.org/packages/Lucene.Net.Analysis.Common/4.8.0-beta00018) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) (embedded LICENSE.txt) | TrackMeUp.Search |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.Data.Sqlite?label=NuGet)](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core |
| [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.Extensions.DependencyInjection?label=NuGet)](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp |
| [Microsoft.Extensions.Logging](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.Extensions.Logging?label=NuGet)](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp |
| [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.Extensions.Logging.Abstractions?label=NuGet)](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.28000.2526) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.Windows.SDK.BuildTools?label=NuGet)](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.28000.2526) [![License](https://img.shields.io/badge/license-Microsoft%20Windows%20SDK%20license%20terms-blue)](https://aka.ms/WinSDKLicenseURL) | [Microsoft Windows SDK license terms](https://aka.ms/WinSDKLicenseURL) (nuspec licenseUrl) | TrackMeUp |
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.3.1) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.WindowsAppSDK?label=NuGet)](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.3.1) [![License](https://img.shields.io/badge/license-Microsoft%20Software%20License%20Terms-blue)](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.3.1) | [Microsoft Software License Terms](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.3.1) (bundled license.txt) | TrackMeUp |
| [Sentry.Extensions.Logging](https://www.nuget.org/packages/Sentry.Extensions.Logging/6.9.0) | [![NuGet](https://img.shields.io/nuget/v/Sentry.Extensions.Logging?label=NuGet)](https://www.nuget.org/packages/Sentry.Extensions.Logging/6.9.0) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp |
| [Serilog](https://www.nuget.org/packages/Serilog/4.4.0) | [![NuGet](https://img.shields.io/nuget/v/Serilog?label=NuGet)](https://www.nuget.org/packages/Serilog/4.4.0) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp |
| [Serilog.Extensions.Logging](https://www.nuget.org/packages/Serilog.Extensions.Logging/10.0.0) | [![NuGet](https://img.shields.io/nuget/v/Serilog.Extensions.Logging?label=NuGet)](https://www.nuget.org/packages/Serilog.Extensions.Logging/10.0.0) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp |
| [Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console/6.1.1) | [![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.Console?label=NuGet)](https://www.nuget.org/packages/Serilog.Sinks.Console/6.1.1) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | [![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.File?label=NuGet)](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp |
| [SGP.NET 1.5.0](https://www.nuget.org/packages/SGP.NET/1.5.0) | NuGet | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core; satellite propagation from public orbital elements |
| [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.151.0) | [![NuGet](https://img.shields.io/nuget/v/SkiaSharp?label=NuGet)](https://www.nuget.org/packages/SkiaSharp/4.151.0) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core |
| [SkiaSharp.NativeAssets.Win32](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/4.151.0) | [![NuGet](https://img.shields.io/nuget/v/SkiaSharp.NativeAssets.Win32?label=NuGet)](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/4.151.0) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core |
| [Spectre.Console](https://www.nuget.org/packages/Spectre.Console/0.57.2) | [![NuGet](https://img.shields.io/nuget/v/Spectre.Console?label=NuGet)](https://www.nuget.org/packages/Spectre.Console/0.57.2) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Cli |
| [SQLitePCLRaw.lib.e_sqlite3](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12) | [![NuGet](https://img.shields.io/nuget/v/SQLitePCLRaw.lib.e_sqlite3?label=NuGet)](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp.Core |
| [System.Drawing.Common](https://www.nuget.org/packages/System.Drawing.Common/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/System.Drawing.Common?label=NuGet)](https://www.nuget.org/packages/System.Drawing.Common/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Core |
| [System.Management](https://www.nuget.org/packages/System.Management/10.0.10) | [![NuGet](https://img.shields.io/nuget/v/System.Management?label=NuGet)](https://www.nuget.org/packages/System.Management/10.0.10) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | LibreHardwareMonitor source build |
| [DiskInfoToolkit 1.1.2](https://www.nuget.org/packages/DiskInfoToolkit/1.1.2) | NuGet | [MPL-2.0](https://licenses.nuget.org/MPL-2.0) | LibreHardwareMonitor source build |
| [HidSharp 2.6.4](https://www.nuget.org/packages/HidSharp/2.6.4) | NuGet | Apache-2.0 (package LICENSE.txt) | LibreHardwareMonitor source build |
| [Mono.Posix.NETStandard 1.0.0](https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0) | NuGet | [Package license terms](https://go.microsoft.com/fwlink/?linkid=869050) (legacy licenseUrl, no SPDX expression) | LibreHardwareMonitor source build |
| [System.IO.FileSystem.AccessControl 5.0.0](https://www.nuget.org/packages/System.IO.FileSystem.AccessControl/5.0.0) | NuGet | [MIT](https://licenses.nuget.org/MIT) | LibreHardwareMonitor source build |
| [System.IO.Ports 10.0.3](https://www.nuget.org/packages/System.IO.Ports/10.0.3) | NuGet | [MIT](https://licenses.nuget.org/MIT) | LibreHardwareMonitor source build |

## Offline national holidays and Latin sanctoral selection

`WorkTrail.Core/Data/celestial-calendar.json` bundles 565 national-calendar rows for
17 countries from 2026-01-01 through 2028-03-31. They were generated with
[holidays 0.105](https://github.com/vacanza/holidays/tree/v0.105) (MIT license),
using national or federal baselines rather than subdivision calendars. They are
rule-based data, not official government feeds; future lunar and bridge dates
may change. The asset marks estimated and provisional entries, and the agenda
does not present make-up workdays as holidays.

The same JSON contains 211 Latin fixed-date sanctoral entries covering 191
distinct recurring dates. Their calendar keys and Latin labels come from the
[Liturgical Calendar API](https://github.com/Liturgical-Calendar/LiturgicalCalendarAPI)
at revision `1bb2b7c503a701a9713b2f881795afe46044af3b` (Apache-2.0),
except for the owner-supplied Our Lady of Mercy example, whose Latin title is
supported by the [Vatican text](https://www.vatican.va/content/john-paul-ii/la/apost_letters/1982/documents/hf_jp-ii_apl_19820203_cultum-sanctorum.html).
This is a selected General Roman Calendar subset, not a complete martyrology
or a computed movable liturgical calendar. `scripts/Generate-CelestialCalendarData.py`
records the pinned source and quality rules used to build the JSON.

## Celestial star catalog subset

`TrackMeUp.Core/Data/sky-catalog.json` includes 83 J2000 ICRS stellar coordinates and available V-band
magnitudes retrieved from the [SIMBAD astronomical database, CDS, Strasbourg](https://simbad.cds.unistra.fr/)
on 2026-09-19 through its [TAP service](https://simbad.cds.unistra.fr/simbad/sim-tap).
This small selection of scientific facts is attributed to SIMBAD and its original
catalog references; it does not relicense the SIMBAD database. SIMBAD reference:
Wenger et al. (2000), Astronomy & Astrophysics Supplement 143, 9,
[2000A&AS..143....9W](https://simbad.cds.unistra.fr/simbad/sim-ref?bibcode=2000A%26AS..143....9W).

The selection covers Orion, Cassiopeia, the Big Dipper in Ursa Major, the Little
Dipper in Ursa Minor, Crux and twelve zodiac constellation figures. The seven
Little Dipper stars use the SIMBAD identifiers `* alf UMi`, `* bet UMi`, `* gam UMi`,
`* del UMi`, `* eps UMi`, `* zet UMi` and `* eta UMi`; their coordinates and available
V magnitudes were queried from the TAP `basic` and `allfluxes` tables on the same date.
Missing system-level V magnitudes, including Mizar, Acrux and Eta Ursae Minoris,
remain absent. Coordinates are rounded to six decimal degrees. Proper
motion and stellar aberration are omitted in this schematic bright-star view;
precession, nutation and standard atmospheric refraction are applied. The
17 connecting figures are project-authored schematic line segments, not IAU
constellation boundaries. Astronomy Engine's MIT-licensed algorithms calculate
the dynamic solar-system and event data; no event is inferred from a decorative image.

The sky catalog records ISS (NORAD 25544) and the Chinese Tiangong/CSS Tianhe
(NORAD 48274) identities. Current orbital elements are requested from
[CelesTrak's public GP STATIONS group](https://celestrak.org/NORAD/documentation/gp-data-formats.php)
subject to its [usage policy](https://celestrak.org/usage-policy.php). They are
not bundled, redistributed or a source of historical orbital positions. SGP.NET
propagates recent elements for an approximate local-sky marker; this does not
establish optical visibility or a predicted pass. The 24 project-authored
decorative sky keyframes and glow colors are stored separately in
`TrackMeUp.Core/Data/sky-palette.json`.

## Curated annual meteor shower data and celestial event definitions

`TrackMeUp.Core/Data/meteor-showers.json` contains eight selected activity periods
and peak solar longitudes from the International Meteor Organization's
[2026 Meteor Shower Calendar, Table 5](https://www.imo.net/files/meteor-shower/cal2026.pdf):
Quadrantids, April Lyrids, eta-Aquariids, Perseids, Orionids, Leonids, Geminids and
Ursids. This small selection of factual catalog parameters is attributed to IMO
and the calendar's contributing observers; the calendar itself is not
redistributed or relicensed. The upstream site's maintenance state can affect
availability of the source link.

Solar longitudes are evaluated in the catalog's J2000 ecliptic frame. Each entry
is an approximate recurring annual peak, not a year-specific forecast,
outburst prediction or promise of visibility. Fixed activity month/day ranges
are approximate and may change as meteor streams evolve. Runtime presentation
must preserve the expected/approximate distinction. The application uses this
catalog parametrically across its supported 1900–2100 request range; historical
or future activity is not asserted to have been observed or predicted by IMO.

Moon/planet conjunctions are computed by Astronomy Engine as equality of
geocentric ecliptic longitude, with geocentric angular separation reported at
that instant. They are not necessarily the instant of minimum separation and
do not establish local observability. Tropical zodiac signs are twelve equal
30-degree sectors measured from the March equinox, distinct from the
[IAU's astronomical constellation boundaries](https://iauarchive.eso.org/public/themes/constellations/).
They are informational coordinate labels, not astrological predictions.

## Hardware telemetry source and optional driver

The isolated `TrackMeUp.Hardware` helper compiles LibreHardwareMonitor 0.9.6 from
upstream commit `3d331e3370efb858411f19511373eff65a218701`, under MPL-2.0. The build
downloads only the pinned SHA-256-verified source archive into the project's
ignored `obj` directory. It replaces memory discovery with OS usage counters,
omits RAM SPD sources and RAMSPDToolkit, gates low-level driver access behind
explicit advanced-mode consent, and tracks fresh sensor assignments. Without
the driver, AMD CPU load uses LibreHardwareMonitor's own GenericCpu service.
The exact source changes are in
`scripts/Restore-LibreHardwareMonitorSource.ps1` and
`TrackMeUp.Hardware/LibreHardwareMonitor/MemoryGroup.cs`.

The helper distribution includes the upstream MPL license, upstream third-party
notices (including the LGPL-2.1 notice for embedded PawnIO modules), the changed
upstream source files and the modification script under `HardwareLicenses`.
Original source: [pinned LibreHardwareMonitor source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701).
The [integration notice](TrackMeUp.Hardware/LibreHardwareMonitor/NOTICE.md) records
the complete source pin and modifications. First-party collector, IPC and
application integration code remains MIT-licensed.

TrackMeUp x64 includes the unmodified [official signed PawnIO 2.2.0 installer](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0).
The official binary edition is proprietary; the author explicitly permits
[redistribution of the installer](https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules#licensing-considerations).
Its version, source URL, and SHA-256 are pinned in `TrackMeUp.Hardware/PawnIO/distribution.json`;
the adjacent `NOTICE.md` accompanies the bundled installer. Builds verify its hash
and signature without executing it. The signed-release installation helper and
the explicit advanced-sensor action install it with Windows administrator consent
when needed. Existing equal/newer installations are preserved; app removal does
not uninstall this shared system component. ARM64 sensor collection remains unsupported.
The separately published PawnIO source retains its
[GPL license and IOCTL exception](https://github.com/namazso/PawnIO#license).

## Test-Only Dependencies

| Package | Badges | License / terms | Used by |
| --- | --- | --- | --- |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1) | [![NuGet](https://img.shields.io/nuget/v/Microsoft.NET.Test.Sdk?label=NuGet)](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Cli.Tests, TrackMeUp.Core.Tests, TrackMeUp.Ocr.Tests, TrackMeUp.Presentation.Tests, TrackMeUp.Search.Tests |
| [Spectre.Console.Testing](https://www.nuget.org/packages/Spectre.Console.Testing/0.57.2) | [![NuGet](https://img.shields.io/nuget/v/Spectre.Console.Testing?label=NuGet)](https://www.nuget.org/packages/Spectre.Console.Testing/0.57.2) [![License](https://img.shields.io/badge/license-MIT-green)](https://licenses.nuget.org/MIT) | [MIT](https://licenses.nuget.org/MIT) | TrackMeUp.Cli.Tests |
| [xunit](https://www.nuget.org/packages/xunit/2.9.3) | [![NuGet](https://img.shields.io/nuget/v/xunit?label=NuGet)](https://www.nuget.org/packages/xunit/2.9.3) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp.Cli.Tests, TrackMeUp.Core.Tests, TrackMeUp.Ocr.Tests, TrackMeUp.Presentation.Tests, TrackMeUp.Search.Tests |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | [![NuGet](https://img.shields.io/nuget/v/xunit.runner.visualstudio?label=NuGet)](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) [![License](https://img.shields.io/badge/license-Apache-2.0-orange)](https://licenses.nuget.org/Apache-2.0) | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) | TrackMeUp.Cli.Tests, TrackMeUp.Core.Tests, TrackMeUp.Ocr.Tests, TrackMeUp.Presentation.Tests, TrackMeUp.Search.Tests |

## Distributed world-clock data, weather, and media

- The Sun and Moon reference photographs in `TrackMeUp/Assets/Celestial/` are NASA public-domain U.S. government imagery. Sun credit: NASA/SDO; courtesy of NASA/SDO and the AIA, EVE, and HMI science teams. Moon credit: NASA, Apollo 11. The source pages, exact download URLs, SHA-256 values, framing, and NASA/SDO usage guidance are recorded in [`Celestial/PROVENANCE.md`](TrackMeUp/Assets/Celestial/PROVENANCE.md). Preserve the credits; NASA does not endorse TrackMeUp. These third-party photographs are outside the repository MIT grant and outside TrackMeUp Brand Assets.
- World-clock city coordinates, population, and IANA time zones are derived from [GeoNames cities500](https://download.geonames.org/export/dump/), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
- Optional current observations are supplied directly by OpenWeather under the API key and plan selected by the person running TrackMeUp. OpenWeather data is provider material, is not covered by the repository MIT License, and remains subject to the provider's applicable service, data, attribution, redistribution, and plan terms. The application preserves visible linked provider attribution whenever that weather is shown.
- `TrackMeUp/Assets/WorldClocks/ThirdParty/OpenWeather/ow_logo.svg` is the official OpenWeather attribution mark downloaded from [OpenWeather's published SVG](https://openweathermap.org/payload/api/media/file/ow_logo.svg). Its SHA-256 is `fd0ad613ebcdb5f013df98bf75603c83fe1f3f0a5f677118b99557da8ac9281c`. The mark is third-party provider artwork, not TrackMeUp-authored material, TrackMeUp Brand Assets, or MIT-licensed repository artwork; preserve it unchanged with its attribution when redistributing weather-enabled binaries.
- The bundled seasonal Urban Wash skyline files and composable atmosphere overlays are TrackMeUp-directed project artwork, not third-party Wikimedia derivatives. They are outside the repository MIT grant and were explicitly authorized by the project owner for public publication on 2026-08-30, as recorded in `TrackMeUp/Assets/WorldClocks/PROVENANCE.md`, `TrackMeUp/Assets/WorldClocks/Overlays/PROVENANCE.md`, and `ASSET_LICENSING.md`. Exact skyline SHA-256 values are distributed in `ATTRIBUTION.md`, `ATTRIBUTION.json`, and the SQLite catalog.

Other repository artwork is mapped in [`ASSET_LICENSING.md`](ASSET_LICENSING.md).
That record distinguishes owner-attested first-party assets from third-party
media while preserving the separate license scope of each category.

## Notes

- The three `Lucene.Net*` packages publish an embedded `LICENSE.txt`; this inventory resolves them as `Apache-2.0` from that bundled file.
- `Microsoft.WindowsAppSDK` publishes a bundled `license.txt` with Microsoft software license terms, not an OSS SPDX expression.
- `Microsoft.Windows.SDK.BuildTools` does not publish an SPDX license expression in NuGet metadata; this inventory links the package's official Windows SDK license URL.
