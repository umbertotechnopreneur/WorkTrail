# SPDX-License-Identifier: MIT
# VBWR B
#
# Project: WorkTrail
# Repository: https://github.com/umbertotechnopreneur/WorkTrail
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
#
# VBWR E

"""Extend the manifest with the approved Europe, USA, Australia, and Russia city batch."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path


CITY_UPDATES = (
    # Europe: missing sovereign-state capitals in the GeoNames Europe country set.
    ("andorra-la-vella", 3041563, "Casa de la Vall and Sant Esteve Church", "north"),
    ("tirana", 3183875, "Et'hem Bey Mosque and Clock Tower", "north"),
    ("sarajevo", 3191281, "Vijećnica and the Miljacka riverfront", "north"),
    ("bern", 2661552, "Zytglogge clock tower and Federal Palace", "north"),
    ("tallinn", 588409, "St. Olaf's Church and the medieval Old Town", "north"),
    ("helsinki", 658225, "Helsinki Cathedral and Senate Square", "north"),
    ("athens", 264371, "the Acropolis and Parthenon", "north"),
    ("zagreb", 3186886, "St. Mark's Church and Zagreb Cathedral", "north"),
    ("reykjavik", 3413829, "Hallgrímskirkja and Harpa Concert Hall", "north"),
    ("vaduz", 3042030, "Vaduz Castle and Cathedral of St. Florin", "north"),
    ("vilnius", 593116, "Gediminas Tower and Vilnius Cathedral", "north"),
    ("luxembourg", 2960316, "Grand Ducal Palace and Adolphe Bridge", "north"),
    ("riga", 456172, "House of the Black Heads and Freedom Monument", "north"),
    ("monaco", 2993458, "Prince's Palace and Monte Carlo Old Town", "north"),
    ("chisinau", 618426, "Triumphal Arch and Nativity Cathedral", "north"),
    ("podgorica", 3193044, "Millennium Bridge and Morača riverbank", "north"),
    ("skopje", 785842, "Stone Bridge and Kale Fortress", "north"),
    ("valletta", 2562305, "Upper Barrakka Gardens and fortified limestone skyline", "north"),
    ("amsterdam", 2759794, "Westerkerk and gabled canal houses", "north"),
    ("lisbon", 2267057, "Belém Tower and the 25 de Abril Bridge", "north"),
    ("ljubljana", 3196359, "Dragon Bridge and Ljubljana Castle", "north"),
    ("bratislava", 3060972, "Bratislava Castle and the UFO Bridge", "north"),
    ("san-marino", 3168070, "Guaita Tower on Mount Titano", "north"),
    ("vatican-city", 3164670, "St. Peter's Basilica and its colonnade", "north"),
    # USA: largest ten-city set, with New York already present.
    ("los-angeles", 5368361, "Griffith Observatory, palm silhouettes, and the downtown skyline", "north"),
    ("chicago", 4887398, "Willis Tower and the Chicago River skyline", "north"),
    ("houston", 4699066, "Houston downtown and the Williams Tower", "north"),
    ("phoenix", 5308655, "Camelback Mountain and the low desert city skyline", "north"),
    ("philadelphia", 4560349, "Philadelphia City Hall and Independence Hall", "north"),
    ("san-antonio", 4726206, "Tower of the Americas and River Walk architecture", "north"),
    ("san-diego", 5391811, "Cabrillo National Monument and the Coronado Bridge", "north"),
    ("dallas", 4684888, "Reunion Tower and the downtown skyline", "north"),
    ("san-jose", 5392171, "San José City Hall and a low Silicon Valley skyline", "north"),
    # Australia: all state/territory capitals plus Gold Coast and Newcastle, with Sydney already present.
    ("melbourne", 2158177, "Flinders Street Station and the Eureka Tower", "south"),
    ("brisbane", 2174003, "Story Bridge and the Brisbane skyline", "south"),
    ("perth", 2063523, "the Bell Tower and Elizabeth Quay skyline", "south"),
    ("adelaide", 2078025, "Adelaide Oval and North Terrace architecture", "south"),
    ("gold-coast", 2165087, "Surfers Paradise high-rises and coastal palms", "south"),
    ("newcastle-australia", 2155472, "Fort Scratchley and Newcastle East skyline", "south"),
    ("canberra", 2172517, "Parliament House and Black Mountain Tower", "south"),
    ("hobart", 2163355, "Salamanca Place and Mount Wellington", "south"),
    ("darwin", 2073124, "Darwin Esplanade and tropical palms", "south"),
    # Russia: largest cities after Moscow and Saint Petersburg, which are already present.
    ("novosibirsk", 1496747, "Novosibirsk Opera and Ballet Theatre", "north"),
    ("yekaterinburg", 1486209, "Church on the Blood and the modern skyline", "north"),
    ("kazan", 551487, "Kul Sharif Mosque and the Kazan Kremlin", "north"),
    ("nizhny-novgorod", 520555, "Nizhny Novgorod Kremlin and the Volga hillside", "north"),
    ("chelyabinsk", 1508291, "Chelyabinsk State Historical Museum and the city skyline", "north"),
    ("krasnoyarsk", 1502026, "Paraskeva Pyatnitsa Chapel and the Yenisei-side skyline", "north"),
    ("samara", 499099, "Samara Space Museum and the Volga embankment", "north"),
    ("ufa", 479561, "Salavat Yulayev Monument and the Belaya riverbank", "north"),
)

CAPITAL_IDS = {
    "andorra-la-vella", "tirana", "sarajevo", "bern", "tallinn", "helsinki",
    "athens", "zagreb", "reykjavik", "vaduz", "vilnius", "luxembourg", "riga",
    "monaco", "chisinau", "podgorica", "skopje", "valletta", "amsterdam", "lisbon",
    "ljubljana", "bratislava", "san-marino", "vatican-city", "canberra",
}

COUNTRY_NAMES = {
    "AD": "Andorra", "AL": "Albania", "AT": "Austria", "AU": "Australia", "BA": "Bosnia and Herzegovina",
    "CH": "Switzerland", "EE": "Estonia", "FI": "Finland", "GR": "Greece", "HR": "Croatia", "IN": "India", "IS": "Iceland",
    "LI": "Liechtenstein", "LT": "Lithuania", "LU": "Luxembourg", "LV": "Latvia", "MC": "Monaco", "MD": "Moldova",
    "ME": "Montenegro", "MK": "North Macedonia", "MT": "Malta", "NL": "Netherlands", "PT": "Portugal", "RU": "Russia",
    "SM": "San Marino", "SK": "Slovakia", "SI": "Slovenia", "US": "United States", "VA": "Vatican City",
}

# Vatican City's population is present in GeoNames country data but its capital is below
# the cities500 population threshold. Keep the same GeoNames identifier and published data.
SMALL_CAPITAL_OVERRIDES = {
    3164670: {
        "name": "Vatican City",
        "countryCode": "VA",
        "latitude": 41.902916,
        "longitude": 12.453389,
        "population": 921,
        "timeZoneId": "Europe/Rome",
    },
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_geonames(cities_zip: Path) -> dict[int, dict[str, object]]:
    rows: dict[int, dict[str, object]] = {}
    with zipfile.ZipFile(cities_zip) as archive:
        with archive.open("cities500.txt") as source:
            for raw_line in source:
                fields = raw_line.decode("utf-8").rstrip("\n").split("\t")
                if len(fields) != 19:
                    raise ValueError("Unexpected GeoNames cities500 row shape.")
                rows[int(fields[0])] = {
                    "name": fields[1],
                    "countryCode": fields[8],
                    "countryName": fields[8],
                    "latitude": float(fields[4]),
                    "longitude": float(fields[5]),
                    "population": int(fields[14]),
                    "timeZoneId": fields[17],
                }
    return rows


def update(manifest_path: Path, cities_zip: Path, master_root: Path, *, dry_run: bool) -> None:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    cities = manifest["cities"]
    additional = manifest["additionalCatalogCities"]
    reviewed = manifest["reviewedMasters"]
    expected_ids = {item[0] for item in CITY_UPDATES}
    if len(expected_ids) != len(CITY_UPDATES):
        raise ValueError("Catalog expansion contains duplicate city ids.")
    existing_ids = {item["cityId"] for item in cities}
    duplicate_ids = expected_ids & existing_ids
    if duplicate_ids:
        raise ValueError(f"Catalog expansion contains existing ids: {sorted(duplicate_ids)}")

    geonames = read_geonames(cities_zip)
    additions: list[dict[str, object]] = []
    prompt_records: list[dict[str, object]] = []
    master_records: list[dict[str, str]] = []
    for city_id, geoname_id, landmark, hemisphere in CITY_UPDATES:
        row = geonames.get(geoname_id) or SMALL_CAPITAL_OVERRIDES.get(geoname_id)
        if row is None:
            raise ValueError(f"GeoNames cities500 lacks {city_id} ({geoname_id}).")
        country_code = row["countryCode"]
        country_name = COUNTRY_NAMES.get(country_code)
        if country_name is None:
            raise ValueError(f"Country {country_code} is not represented by the existing catalog.")
        additions.append({
            "cityId": city_id,
            "geonameId": geoname_id,
            "name": row["name"],
            "countryCode": country_code,
            "countryName": country_name,
            "latitude": row["latitude"],
            "longitude": row["longitude"],
            "population": row["population"],
            "timeZoneId": row["timeZoneId"],
            "isCapital": city_id in CAPITAL_IDS,
            "hemisphere": hemisphere,
        })
        prompt_records.append({
            "cityId": city_id,
            "displayName": row["name"],
            "landmarks": [landmark, "low local urban mass and authentic regional vegetation"],
            "seasonalMode": "meteorological",
            "summerPalette": "clear local daylight, restrained watercolor saturation, fresh seasonal foliage",
            "summerCues": "seasonally plausible clear weather and local greenery",
            "winterPalette": "cooler muted watercolor tones with locally plausible seasonal texture",
            "winterCues": "seasonally plausible low light; never add falling snow unless appropriate",
            "uncertain": False,
        })
        for season in ("summer", "winter"):
            file_name = f"{city_id}-{season}.png"
            master = master_root / file_name
            if not master.is_file():
                raise FileNotFoundError(f"Missing reviewed master: {master}")
            master_records.append({
                "cityId": city_id,
                "season": season,
                "fileName": file_name,
                "sha256": sha256(master),
            })

    if dry_run:
        print(json.dumps({"cityCount": len(additions), "assetCount": len(master_records), "valid": True}, indent=2))
        return

    cities.extend(prompt_records)
    additional.extend(additions)
    reviewed.extend(master_records)
    manifest["assetCountExpected"] = len(cities) * 2
    if len(reviewed) != manifest["assetCountExpected"]:
        raise ValueError("Reviewed-master count is not aligned with the two-season city catalog.")
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"cityCount": len(cities), "assetCount": manifest["assetCountExpected"], "updated": str(manifest_path)}, indent=2))


def main() -> None:
    repository_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, default=repository_root / "design/world-clocks/watercolor/generation-manifest-v1.json")
    parser.add_argument("--cities-zip", type=Path, required=True)
    parser.add_argument("--masters", type=Path, default=repository_root / "design/world-clocks/watercolor/masters-v1")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    update(args.manifest.resolve(), args.cities_zip.resolve(), args.masters.resolve(), dry_run=args.dry_run)


if __name__ == "__main__":
    main()
