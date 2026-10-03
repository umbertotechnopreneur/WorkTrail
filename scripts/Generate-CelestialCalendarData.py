# SPDX-License-Identifier: MIT
"""Build WorkTrail's offline agenda JSON from pinned holiday and sanctoral sources."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import sys
import urllib.request


START = dt.date(2026, 1, 1)
END = dt.date(2028, 3, 31)
LITCAL_REPOSITORY = "Liturgical-Calendar/LiturgicalCalendarAPI"
LITCAL_COMMIT = "1bb2b7c503a701a9713b2f881795afe46044af3b"
LITCAL_PREFIX = "jsondata/sourcedata/rite/roman"
COUNTRIES = {
    "AU": ("Australia", "australia"),
    "BD": ("Bangladesh", "bangladesh"),
    "CA": ("Canada", "canada"),
    "CN": ("China", "china"),
    "DE": ("Germany", "germany"),
    "FR": ("France", "france"),
    "IN": ("India", "india"),
    "IT": ("Italy", "italy"),
    "JP": ("Japan", "japan"),
    "KH": ("Cambodia", "cambodia"),
    "KR": ("South Korea", "south_korea"),
    "LA": ("Laos", "laos"),
    "NZ": ("New Zealand", "new_zealand"),
    "PL": ("Poland", "poland"),
    "SG": ("Singapore", "singapore"),
    "US": ("United States", "united_states"),
    "VN": ("Vietnam", "vietnam"),
}


def latin_url(edition: str) -> str:
    if edition == "decrees":
        path = f"{LITCAL_PREFIX}/decrees/i18n/la.json"
    else:
        path = f"{LITCAL_PREFIX}/missals/propriumdesanctis_{edition}/i18n/la.json"
    return f"https://raw.githubusercontent.com/{LITCAL_REPOSITORY}/{LITCAL_COMMIT}/{path}"


def load_latin(edition: str, cache: Path, refresh: bool) -> dict[str, str]:
    path = cache / f"litcal-{edition}-la.json"
    if refresh:
        request = urllib.request.Request(latin_url(edition), headers={
            "User-Agent": "WorkTrail-Agenda-Asset-Generator/1.0",
            "Accept": "application/json",
        })
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = response.read()
        labels = json.loads(payload)
        if not isinstance(labels, dict) or not labels:
            raise ValueError(f"The Latin {edition} source is empty or invalid")
        cache.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
    elif not path.is_file():
        raise FileNotFoundError(f"Missing pinned Latin source {path}; use --refresh-latin once")
    else:
        payload = path.read_bytes()
        labels = json.loads(payload)
    if not isinstance(labels, dict) or not labels or not all(
        isinstance(key, str) and isinstance(value, str) and value.strip()
        for key, value in labels.items()
    ):
        raise ValueError(f"Invalid Latin {edition} labels")
    return labels


def build_saints(source: Path, cache: Path, refresh: bool) -> tuple[list[dict], dict]:
    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    provenance = json.loads((source / "provenance.json").read_text(encoding="utf-8"))
    if (manifest["language"] != "it" or manifest["upstream"]["commit"] != LITCAL_COMMIT
            or manifest["coverage"]["calendar_entries"] != 211):
        raise ValueError("Unexpected saints snapshot; review source coverage before generating assets")
    latin = {edition: load_latin(edition, cache, refresh)
             for edition in ("1970", "2002", "2008", "decrees")}
    entries: list[dict] = []
    seen: set[str] = set()
    for day in provenance:
        month, number = day["Month"], day["Day"]
        dt.date(2000, month, number)
        for record in day["entries"]:
            key = record["event_key"]
            if key in seen:
                raise ValueError(f"Duplicate saint event key: {key}")
            seen.add(key)
            if (record["Month"], record["Day"]) != (month, number):
                raise ValueError(f"Conflicting saint date: {key}")
            if key == "OwnerOurLadyOfMercy":
                # The source calendar has no Latin label for this owner-supplied local example.
                name = "Beatæ Mariæ Virginis de Mercede"
                source_url = "https://www.vatican.va/content/john-paul-ii/la/apost_letters/1982/documents/hf_jp-ii_apl_19820203_cultum-sanctorum.html"
            else:
                source_files = record["source_files"]
                edition = "decrees" if "raw/decrees-it.json" in source_files else next(
                    (item for item in ("1970", "2002", "2008")
                     if f"raw/missal-{item}-it.json" in source_files), None)
                if edition is None or key not in latin[edition]:
                    raise ValueError(f"No source-backed Latin label for {key}")
                name = latin[edition][key]
                source_url = latin_url(edition)
            entries.append({"month": month, "day": number, "eventKey": key,
                            "nameLatin": name, "sourceUrl": source_url})
    if len(entries) != 211 or len({(item["month"], item["day"]) for item in entries}) != 191:
        raise ValueError("The Latin saints output does not match the reviewed source coverage")
    entries.sort(key=lambda item: (item["month"], item["day"], item["eventKey"]))
    return entries, {"source": LITCAL_REPOSITORY, "revision": LITCAL_COMMIT,
                     "scope": "Fixed-date General Roman Calendar selection; not a complete martyrology",
                     "datesCovered": 191, "entries": len(entries)}


def build_holidays(holidays_module) -> tuple[list[dict], list[dict]]:
    if holidays_module.__version__ != "0.105":
        raise ValueError(f"Expected holidays 0.105, got {holidays_module.__version__}")
    rows: list[dict] = []
    countries: list[dict] = []
    for code, (name, module) in COUNTRIES.items():
        categories = ("public", "government") if code == "CA" else ("public",)
        calendar = holidays_module.country_holidays(
            code, years=(2026, 2027, 2028), observed=True, expand=False,
            language="it" if code == "IT" else "en_US", categories=categories)
        source = f"https://github.com/vacanza/holidays/blob/v0.105/holidays/countries/{module}.py"
        countries.append({"code": code, "name": name, "sourceUrl": source,
                          "scope": "Federal baseline" if code == "CA" else "National baseline"})
        for day, label in sorted(calendar.items()):
            if not START <= day <= END:
                continue
            estimated = any(part in label.casefold() for part in ("estimated", "dự kiến"))
            quality = "provisional" if code == "CN" and day.year >= 2027 else (
                "estimated" if estimated else "rule_based")
            rows.append({"date": day.isoformat(), "country": code, "name": label,
                         "kind": "holiday", "quality": quality, "sourceUrl": source})
        for day in sorted(calendar.weekend_workdays):
            if not START <= day <= END:
                continue
            if day in calendar:
                raise ValueError(f"A date is both a holiday and a make-up workday: {code} {day}")
            rows.append({"date": day.isoformat(), "country": code,
                         "name": "Official make-up workday", "kind": "workday",
                         "quality": "provisional" if code == "CN" and day.year >= 2027 else "rule_based",
                         "sourceUrl": source})
        if not all(any(row["country"] == code and row["date"].startswith(str(year))
                       and row["kind"] == "holiday" for row in rows) for year in (2026, 2027, 2028)):
            raise ValueError(f"Missing holiday coverage for {code}")
    rows.sort(key=lambda item: (item["date"], item["country"], item["kind"], item["name"]))
    keys = [(row["date"], row["country"], row["kind"], row["name"]) for row in rows]
    if len(set(keys)) != len(keys):
        raise ValueError("Duplicate holiday entry")
    return rows, countries


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--saints-root", type=Path, required=True)
    parser.add_argument("--vendor", type=Path, help="Directory containing the pinned holidays 0.105 package")
    parser.add_argument("--latin-cache", type=Path, required=True)
    parser.add_argument("--refresh-latin", action="store_true", help="Fetch Latin labels at the pinned revision")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.vendor:
        sys.path.insert(0, str(args.vendor.resolve()))
    import holidays  # noqa: PLC0415 - the pinned source path is selected above.

    saints, saints_source = build_saints(args.saints_root, args.latin_cache, args.refresh_latin)
    holidays_rows, countries = build_holidays(holidays)
    output = {
        "schemaVersion": 1,
        "coverageStart": START.isoformat(),
        "coverageEnd": END.isoformat(),
        "holidayLibrary": f"holidays {holidays.__version__}",
        "holidayScope": "Rule-based national baseline; subdivisions excluded; provisional dates require review",
        "saintsSource": saints_source,
        "countries": countries,
        "holidays": holidays_rows,
        "saints": saints,
    }
    serialized = (json.dumps(output, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(serialized)
    print(json.dumps({"output": str(args.output), "bytes": len(serialized),
                      "sha256": hashlib.sha256(serialized).hexdigest(),
                      "countries": len(countries), "holidays": len(holidays_rows),
                      "saints": len(saints)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
