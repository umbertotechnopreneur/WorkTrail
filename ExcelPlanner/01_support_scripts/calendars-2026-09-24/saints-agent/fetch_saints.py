# SPDX-License-Identifier: MIT
"""Fetch pinned sources or build the informational Italian saints CSV offline."""

import argparse
import calendar
import csv
import datetime as dt
import hashlib
import io
import json
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
RAW = ROOT / "raw"
ENDPOINT = "https://query.wikidata.org/sparql"
AGENT = "WorkTrailSaintsDataset/1.0 (one-off informational open-data extraction)"
REPOSITORY = "Liturgical-Calendar/LiturgicalCalendarAPI"
COMMIT = "1bb2b7c503a701a9713b2f881795afe46044af3b"
DATA_PREFIX = "jsondata/sourcedata/rite/roman/"
AS_OF = "2026-09-24"
YEAR = 2026
EXCLUDED = {
    "Presentation": "Festa del Signore; fuori dal sottoinsieme santi e ricorrenze mariane",
    "Annunciation": "Festa del Signore; fuori dal sottoinsieme santi e ricorrenze mariane",
    "Transfiguration": "Festa del Signore; fuori dal sottoinsieme santi e ricorrenze mariane",
    "ExaltationCross": "Festa della Croce; fuori dal sottoinsieme santi e ricorrenze mariane",
    "NameJesus": "Festa del Signore; fuori dal sottoinsieme santi e ricorrenze mariane",
    "AllSouls": "Commemorazione dei defunti; non identifica santi",
    "DedicationStMaryMajor": "Dedicazione di una basilica; fuori dal sottoinsieme",
    "DedicationLateran": "Dedicazione di una basilica; fuori dal sottoinsieme",
    "DedicationStsPeterPaul": "Dedicazione di basiliche; fuori dal sottoinsieme",
}


def litcal_paths():
    paths = {"LICENSE-Apache-2.0.txt": "LICENSE"}
    for edition, locale in (("1970", "it"), ("2002", "it"), ("2008", "it"), ("IT_1983", "it_IT")):
        folder = DATA_PREFIX + f"missals/propriumdesanctis_{edition}/"
        paths[f"missal-{edition}.json"] = folder + f"propriumdesanctis_{edition}.json"
        paths[f"missal-{edition}-it.json"] = folder + f"i18n/{locale}.json"
    paths["decrees.json"] = DATA_PREFIX + "decrees/decrees.json"
    paths["decrees-it.json"] = DATA_PREFIX + "decrees/i18n/it.json"
    paths["CalendarHandler.php"] = "src/Handlers/CalendarHandler.php"
    return paths


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def fetch_wikidata():
    RAW.mkdir(exist_ok=True)
    query = (ROOT / "wikidata-saints.rq").read_text(encoding="utf-8")
    url = ENDPOINT + "?" + urllib.parse.urlencode({"query": query, "format": "json"})
    request = urllib.request.Request(url, headers={"User-Agent": AGENT, "Accept": "application/sparql-results+json"})
    metadata = {"requested_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(), "endpoint": ENDPOINT,
                "query": "wikidata-saints.rq", "query_sha256": hashlib.sha256(query.encode()).hexdigest(),
                "retry_policy": "one request, no automatic retries; respect Retry-After before another manual attempt"}
    try:
        with urllib.request.urlopen(request, timeout=55) as response:
            payload = response.read()
            metadata.update({"status": response.status, "content_type": response.headers.get("Content-Type"),
                             "sha256": hashlib.sha256(payload).hexdigest(), "bytes": len(payload)})
        data = json.loads(payload)
        metadata["bindings"] = len(data["results"]["bindings"])
        (RAW / "wikidata-saints.json").write_bytes(payload)
        save_json(RAW / "wikidata-request.json", metadata)
        print(json.dumps(metadata, ensure_ascii=False))
    except urllib.error.HTTPError as error:
        metadata.update({"status": error.code, "retry_after": error.headers.get("Retry-After"), "error": str(error)})
        save_json(RAW / "wikidata-request.json", metadata)
        raise SystemExit(json.dumps(metadata, ensure_ascii=False))
    except (TimeoutError, urllib.error.URLError) as error:
        metadata["error"] = str(error)
        save_json(RAW / "wikidata-request.json", metadata)
        raise SystemExit(json.dumps(metadata, ensure_ascii=False))


def fetch_litcal():
    RAW.mkdir(exist_ok=True)
    previous = json.loads((RAW / "sources.json").read_text(encoding="utf-8")) if (RAW / "sources.json").exists() else {"files": []}
    previous_by_url = {entry["url"]: entry for entry in previous["files"]}
    sources = []
    for filename, remote_path in litcal_paths().items():
        url = f"https://raw.githubusercontent.com/{REPOSITORY}/{COMMIT}/{remote_path}"
        prior = previous_by_url.get(url)
        if prior and (RAW / filename).exists():
            payload = (RAW / filename).read_bytes()
            if hashlib.sha256(payload).hexdigest() != prior["sha256"]:
                raise ValueError(f"Cached source changed: {filename}; refusing to overwrite it")
            sources.append(prior)
            continue
        request = urllib.request.Request(url, headers={"User-Agent": AGENT})
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = response.read()
        if filename.endswith(".json"):
            json.loads(payload)
        (RAW / filename).write_bytes(payload)
        sources.append({"file": f"raw/{filename}", "url": url,
                        "browse_url": f"https://github.com/{REPOSITORY}/blob/{COMMIT}/{remote_path}",
                        "retrieved_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
                        "sha256": hashlib.sha256(payload).hexdigest(), "bytes": len(payload),
                        "license": "Apache-2.0"})
        print(f"Saved {filename}: {len(payload)} bytes", flush=True)
    save_json(RAW / "sources.json", {"repository": REPOSITORY, "commit": COMMIT, "files": sources})


def read_json(filename):
    return json.loads((RAW / filename).read_text(encoding="utf-8"))


def build():
    """Rebuild only inside this script's directory, without any network access."""
    snapshot = read_json("sources.json")
    if snapshot["commit"] != COMMIT or snapshot["repository"] != REPOSITORY:
        raise ValueError("Unexpected upstream snapshot")
    sources = {Path(entry["file"]).name: entry for entry in snapshot["files"]}
    for filename, entry in sources.items():
        if hashlib.sha256((RAW / filename).read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError(f"Source checksum mismatch: {filename}")

    def source_refs(*filenames):
        return [sources[filename]["browse_url"] for filename in filenames]

    records = {}
    excluded = []
    changes = []
    for edition in ("1970", "2002", "2008"):
        filename = f"missal-{edition}.json"
        names_file = f"missal-{edition}-it.json"
        names = read_json(names_file)
        for row in read_json(filename):
            key = row["event_key"]
            if key in records:
                raise ValueError(f"Duplicate event key between missals: {key}")
            if key in EXCLUDED:
                excluded.append({"event_key": key, "reason": EXCLUDED[key], "file": filename})
                continue
            name = names[key]
            if not isinstance(name, str) or not name.strip():
                raise ValueError(f"Missing Italian label: {key}")
            dt.date(2000, row["month"], row["day"])
            records[key] = {
                "event_key": key, "Month": row["month"], "Day": row["day"], "Name": name,
                "source_files": [f"raw/{filename}", f"raw/{names_file}"],
                "sources": source_refs(filename, names_file), "license": "Apache-2.0",
            }

    # Apply fixed-date additions and explicit Italian name updates, never mobile dates.
    decree_names = read_json("decrees-it.json")
    for row in read_json("decrees.json"):
        event, metadata = row["liturgical_event"], row["metadata"]
        key, action = event["event_key"], metadata["action"]
        if metadata.get("since_year", 0) > YEAR or metadata.get("until_year", 9999) < YEAR:
            excluded.append({"event_key": key, "reason": "Decreto fuori dal periodo della snapshot"})
            continue
        if action == "createNew":
            if event.get("type") == "mobile" or "strtotime" in event:
                excluded.append({"event_key": key, "reason": "Ricorrenza mobile: non rappresentabile da Month/Day"})
                continue
            if event.get("type") != "fixed" or key in records:
                raise ValueError(f"Unsupported or duplicate decree: {key}")
            dt.date(2000, event["month"], event["day"])
            records[key] = {
                "event_key": key, "Month": event["month"], "Day": event["day"], "Name": decree_names[key],
                "source_files": ["raw/decrees.json", "raw/decrees-it.json"],
                "sources": source_refs("decrees.json", "decrees-it.json"), "license": "Apache-2.0",
            }
            changes.append({"event_key": key, "action": "added_from_decree", "since_year": metadata["since_year"]})
        elif action == "makeDoctor" or (action == "setProperty" and metadata["property"] == "name"):
            previous_name = records[key]["Name"]
            records[key]["Name"] = decree_names[key]
            records[key]["source_files"] += ["raw/decrees.json", "raw/decrees-it.json"]
            records[key]["sources"] += source_refs("decrees.json", "decrees-it.json")
            changes.append({"event_key": key, "action": "updated_name", "from": previous_name, "to": decree_names[key]})
        elif action == "setProperty" and metadata["property"] == "grade":
            changes.append({"event_key": key, "action": "grade_not_exported", "reason": "Informational names/dates only"})
        else:
            raise ValueError(f"Unhandled decree action: {key}: {action}")

    # The 1970 JSON retains the historical date; upstream applies this fixed transfer in PHP.
    php = (RAW / "CalendarHandler.php").read_text(encoding="utf-8")
    marker = "has been transferred from Dec. 12 to Aug. 12 since the year 2002"
    if marker not in php:
        raise ValueError("Upstream evidence for the permanent Chantal transfer is missing")
    chantal = records["StJaneFrancesDeChantal"]
    if (chantal["Month"], chantal["Day"]) != (12, 12):
        raise ValueError("Review the Chantal adjustment; the upstream base date has changed")
    chantal.update({"Month": 8, "Day": 12})
    chantal["sources"] += source_refs("CalendarHandler.php")
    chantal["source_files"].append("raw/CalendarHandler.php")
    changes.append({"event_key": "StJaneFrancesDeChantal", "action": "permanent_date_transfer",
                    "from": "12-12", "to": "08-12", "since_year": 2002,
                    "evidence": "raw/CalendarHandler.php, transfer text at line 3395"})

    examples = read_json("user-examples.json")
    for row in examples["records"]:
        key = row["event_key"]
        if key in records:
            if (records[key]["Month"], records[key]["Day"]) != (row["Month"], row["Day"]):
                raise ValueError(f"Owner example date conflicts with upstream: {key}")
            previous_name = records[key]["Name"]
            records[key]["Name"] = row["Name"]
            records[key]["sources"].append(row["Source"])
            records[key]["source_files"].append("raw/user-examples.json")
            records[key]["owner_supplied_label"] = True
            changes.append({"event_key": key, "action": "preserved_owner_spelling", "from": previous_name, "to": row["Name"]})
        else:
            records[key] = {"event_key": key, "Month": row["Month"], "Day": row["Day"], "Name": row["Name"],
                            "source_files": ["raw/user-examples.json"], "sources": [row["Source"]],
                            "license": "Owner-supplied name/date/link fact; Vatican News page not relicensed"}

    by_date = {}
    for record in records.values():
        name = record["Name"]
        if not isinstance(name, str) or not name.strip() or any(c in name for c in "\r\n;"):
            raise ValueError(f"Unsafe or ambiguous name: {record['event_key']}")
        if name.startswith(("=", "+", "-", "@")):
            raise ValueError("Formula-like label is not supported")
        by_date.setdefault((record["Month"], record["Day"]), []).append(record)
    output_rows, provenance = [], []
    for (month, day), date_records in sorted(by_date.items()):
        date_records.sort(key=lambda record: (record["Name"].casefold(), record["event_key"]))
        names = [record["Name"] for record in date_records]
        if len(names) != len(set(names)):
            raise ValueError(f"Duplicate display name on {month}/{day}")
        refs = sorted({url for record in date_records for url in record["sources"]})
        output_rows.append([month, day, "; ".join(names), "; ".join(refs)])
        provenance.append({"Month": month, "Day": day, "entries": date_records})

    buffer = io.StringIO(newline="")
    writer = csv.writer(buffer, delimiter=";", quotechar='"', quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
    writer.writerow(["Month", "Day", "Name", "Source"])
    writer.writerows(output_rows)
    encoded = buffer.getvalue().encode("utf-8-sig")
    # Round-trip the exact exported bytes before publishing the generated CSV.
    parsed = list(csv.reader(io.StringIO(encoded.decode("utf-8-sig")), delimiter=";"))
    expected = [[str(value) for value in row] for row in output_rows]
    if parsed[0] != ["Month", "Day", "Name", "Source"] or parsed[1:] != expected:
        raise ValueError("CSV round-trip mismatch")
    if any(len(row) != 4 for row in parsed) or not encoded.startswith(b"\xef\xbb\xbf"):
        raise ValueError("Invalid CSV format")

    all_dates = {(month, day) for month in range(1, 13) for day in range(1, calendar.monthrange(2000, month)[1] + 1)}
    gaps = sorted(all_dates - set(by_date))
    coverage = {
        "covered_dates": len(by_date), "possible_recurring_dates": 366,
        "missing_dates_count": len(gaps), "missing_dates_mm_dd": [f"{month:02d}-{day:02d}" for month, day in gaps],
        "covered_dates_in_2026": len(by_date) - int((2, 29) in by_date),
        "possible_dates_in_2026": 365, "february_29_covered": (2, 29) in by_date,
        "calendar_entries": len(records), "dates_with_multiple_entries": sum(len(rows) > 1 for rows in by_date.values()),
        "months": [{"Month": month, "covered": sum(m == month for m, _ in by_date),
                    "possible": calendar.monthrange(2000, month)[1]} for month in range(1, 13)],
    }
    manifest = {
        "dataset": "Saints.csv", "as_of_date": AS_OF, "language": "it",
        "purpose": "Santi e ricorrenze mariane informative, indipendenti dalle chiusure lavorative",
        "scope": "Sottoinsieme a data fissa del santorale romano generale, con due esempi del proprietario",
        "not_full_martyrology": True, "encoding": "UTF-8 BOM", "delimiter": ";",
        "headers": ["Month", "Day", "Name", "Source"], "name_joiner": "; ", "source_joiner": "; ",
        "one_row_per_covered_month_day": True, "coverage": coverage,
        "upstream": snapshot,
        "used_litcal_editions": ["1970", "2002", "2008"],
        "inspected_but_not_used": ["raw/missal-IT_1983.json", "raw/missal-IT_1983-it.json"],
        "inspected_but_not_used_reason": "Italian 1983 records duplicate general-calendar entries; not treated as extra saints or current national rules",
        "license": {"bulk_data": "Apache-2.0", "retained_license": "raw/LICENSE-Apache-2.0.txt",
                    "attribution": "Liturgical-Calendar/LiturgicalCalendarAPI, John R. D'Orazio and contributors",
                    "seed_facts": examples["license_note"], "wikidata": "No Wikidata data in this CSV"},
        "changes_from_raw": changes, "excluded_records": excluded,
        "wikidata_attempt": read_json("wikidata-request.json"),
        "limitations": [
            "Date senza dati assenti dal CSV; l'assenza non significa che non esistano santi per quel giorno.",
            "Non e un martirologio completo ne un calendario liturgico ufficiale italiano.",
            "Non determina festivita civili, chiusure, giorni lavorativi, precetti o orari.",
            "Date ricorrenti nominali: non applica precedenze, soppressioni domenicali o trasferimenti annuali.",
            "Esclude ricorrenze mobili, calendari diocesani/locali e varianti di altre tradizioni.",
            "Include commemorazioni collettive, angeli e ricorrenze mariane; una voce non equivale sempre a una persona.",
            "Le etichette italiane e i dati possono contenere imperfezioni della fonte; non sono stati corretti per congettura.",
            "Lo snapshot e fissato alla revisione indicata, non si aggiorna automaticamente.",
        ],
        "output_sha256": hashlib.sha256(encoded).hexdigest(),
        "local_input_sha256": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
                               for name in ["fetch_saints.py", "wikidata-saints.rq", "raw/user-examples.json"]},
        "validation": {"csv_roundtrip": "passed", "source_hashes": "passed", "valid_month_day_pairs": "passed",
                       "unique_month_day_pairs": "passed", "exact_header_and_bom": "passed",
                       "all_labels_present_in_italian_sources_or_owner_examples": "passed"},
    }
    (ROOT / "Saints.csv").write_bytes(encoded)
    save_json(ROOT / "provenance.json", provenance)
    save_json(ROOT / "manifest.json", manifest)
    print(json.dumps({"output": str(ROOT / "Saints.csv"), "coverage": coverage, "sha256": manifest["output_sha256"]}, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--fetch-wikidata", action="store_true", help="One network request; never retries.")
    actions.add_argument("--fetch-litcal", action="store_true", help="Download pinned Apache-2.0 sources, once each.")
    actions.add_argument("--build", action="store_true", help="Rebuild Saints.csv and manifests offline from verified raw sources.")
    args = parser.parse_args()
    if args.fetch_wikidata:
        fetch_wikidata()
    elif args.fetch_litcal:
        fetch_litcal()
    elif args.build:
        build()
