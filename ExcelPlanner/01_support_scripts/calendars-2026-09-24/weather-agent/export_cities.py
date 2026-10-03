#!/usr/bin/env python3
"""Extend a four-column city CSV using only the local canonical SQLite catalog."""

import argparse
import csv
from datetime import datetime, timezone
import hashlib
import io
import json
import math
import os
from pathlib import Path
import re
import sqlite3
import sys
import tempfile


ORIGINAL_HEADERS = ["Code", "City", "WindowsId", "IanaId"]
HEADERS = ORIGINAL_HEADERS + ["Latitude", "Longitude", "CountryCode"]
EXPECTED_ROWS = 206
STAGING = Path(__file__).resolve().parent
OUTPUT = STAGING / "Cities.csv"
MANIFEST = STAGING / "cities-manifest.json"


class ValidationFailure(Exception):
    """A fixed diagnostic code; no raw file contents or credentials."""


def require(condition, message):
    if not condition:
        raise ValidationFailure(message)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def parse_csv(raw, headers):
    text = raw.decode("utf-8-sig")
    reader = csv.DictReader(io.StringIO(text, newline=""), delimiter=";")
    require(reader.fieldnames == headers, "CSV_HEADER_MISMATCH")
    rows = list(reader)
    require(len(rows) == EXPECTED_ROWS, "CSV_ROW_COUNT_MISMATCH")
    require(all(set(row) == set(headers) and all(v is not None for v in row.values())
                for row in rows), "CSV_MALFORMED_ROW")
    codes = [row["Code"] for row in rows]
    require(all(code and code == code.strip() for code in codes), "INVALID_CODE")
    require(len(set(codes)) == len(codes), "DUPLICATE_CODE")
    return rows


def validate_coordinate(value, lower, upper):
    require(not isinstance(value, bool) and isinstance(value, (int, float)),
            "NON_NUMERIC_COORDINATE")
    require(math.isfinite(value) and lower <= value <= upper, "COORDINATE_OUT_OF_RANGE")


def enrich(rows, catalog):
    # SQLite read-only URI rejects writes and never creates a missing database.
    connection = sqlite3.connect(catalog.as_uri() + "?mode=ro", uri=True)
    try:
        connection.execute("PRAGMA query_only=ON")
        connection.row_factory = sqlite3.Row
        columns = {row["name"] for row in connection.execute("PRAGMA table_info(city)")}
        require({"id", "latitude", "longitude", "country_code"}.issubset(columns),
                "CATALOG_SCHEMA_MISMATCH")
        connection.execute("BEGIN")
        result = []
        for original in rows:
            # Exact, case-sensitive Code -> city.id; no name or geocoding fallback.
            matches = connection.execute(
                "SELECT id, latitude, longitude, country_code FROM city "
                "WHERE id COLLATE BINARY = ? LIMIT 2", (original["Code"],),
            ).fetchall()
            require(len(matches) == 1, "CITY_CODE_MISSING_OR_AMBIGUOUS")
            match = matches[0]
            latitude, longitude, country = match["latitude"], match["longitude"], match["country_code"]
            validate_coordinate(latitude, -90, 90)
            validate_coordinate(longitude, -180, 180)
            require(isinstance(country, str) and re.fullmatch(r"[A-Z]{2}", country) is not None,
                    "INVALID_COUNTRY_CODE")
            # Copy every original field verbatim, including city labels and time zones.
            result.append({**original, "Latitude": str(latitude),
                           "Longitude": str(longitude), "CountryCode": country})
        return result
    finally:
        connection.close()


def verify_rows(original, extended, expected):
    require([row["Code"] for row in original] == [row["Code"] for row in extended],
            "ORDERED_CODES_CHANGED")
    require(all([row[column] for column in ORIGINAL_HEADERS]
                == [out[column] for column in ORIGINAL_HEADERS]
                for row, out in zip(original, extended)), "ORIGINAL_VALUES_CHANGED")
    require(extended == expected, "OUTPUT_CATALOG_VALUES_CHANGED")
    for row in extended:
        validate_coordinate(float(row["Latitude"]), -90, 90)
        validate_coordinate(float(row["Longitude"]), -180, 180)
        require(re.fullmatch(r"[A-Z]{2}", row["CountryCode"]) is not None,
                "INVALID_OUTPUT_COUNTRY_CODE")


def atomic_write(path, raw):
    temp_path = None
    try:
        with tempfile.NamedTemporaryFile(mode="wb", prefix=".cities-", suffix=".tmp",
                                         dir=STAGING, delete=False) as stream:
            temp_path = Path(stream.name)
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp_path, path)
    finally:
        if temp_path is not None and temp_path.exists():
            temp_path.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--check-only", action="store_true",
                        help="Validate all inputs and matches without writing files.")
    parser.add_argument("--verify-output", action="store_true",
                        help="Validate existing staged CSV and manifest without writes.")
    args = parser.parse_args()
    require(not (args.check_only and args.verify_output), "INCOMPATIBLE_MODES")
    source, catalog = args.source.resolve(strict=True), args.catalog.resolve(strict=True)
    require(source != OUTPUT and catalog not in (OUTPUT, MANIFEST), "INPUT_OUTPUT_PATH_COLLISION")
    source_raw, catalog_raw = source.read_bytes(), catalog.read_bytes()
    source_hash, catalog_hash = digest(source_raw), digest(catalog_raw)
    original = parse_csv(source_raw, ORIGINAL_HEADERS)
    extended = enrich(original, catalog)
    buffer = io.StringIO(newline="")
    writer = csv.DictWriter(buffer, fieldnames=HEADERS, delimiter=";", lineterminator="\r\n")
    writer.writeheader()
    writer.writerows(extended)
    output_raw = buffer.getvalue().encode("utf-8-sig")
    verify_rows(original, parse_csv(output_raw, HEADERS), extended)
    require(source_hash == digest(source.read_bytes()), "SOURCE_CHANGED_DURING_READ")
    require(catalog_hash == digest(catalog.read_bytes()), "CATALOG_CHANGED_DURING_READ")
    summary = {"rows": EXPECTED_ROWS, "unique_codes": len({row["Code"] for row in extended}),
               "headers": HEADERS, "ordered_codes_preserved": True,
               "original_four_columns_preserved": True, "coordinates_valid": True,
               "country_codes_valid": True, "missing_codes": 0, "ambiguous_codes": 0,
               "validation_errors": 0, "api_calls": 0}
    if args.check_only:
        print(json.dumps({"mode": "check_only", **summary}))
        return
    if args.verify_output:
        saved_raw = OUTPUT.read_bytes()
        verify_rows(original, parse_csv(saved_raw, HEADERS), extended)
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        require(manifest["source_sha256"] == source_hash
                and manifest["catalog_sha256"] == catalog_hash
                and manifest["output_sha256"] == digest(saved_raw), "MANIFEST_HASH_MISMATCH")
        require(all(manifest[key] == value for key, value in summary.items()),
                "MANIFEST_SUMMARY_MISMATCH")
        print(json.dumps({"mode": "verify_output", "verified": True, **summary}))
        return
    manifest = {
        "generated_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S"),
        "output_file": OUTPUT.name, "source_headers": ORIGINAL_HEADERS, **summary,
        "source_sha256": source_hash, "catalog_sha256": catalog_hash,
        "output_sha256": digest(output_raw),
        "encoding": "utf-8-sig", "delimiter": ";", "line_ending": "CRLF",
        "decimal_separator": ".", "timestamp_semantics": "UTC",
        "mapping": "Code = city.id (exact BINARY comparison)",
        "appended_fields": {"Latitude": "city.latitude", "Longitude": "city.longitude",
                            "CountryCode": "city.country_code"},
        "source_values_unchanged": True, "catalog_unchanged": True,
        "query_types": {"Code": "text", "City": "text", "WindowsId": "text",
                        "IanaId": "text", "Latitude": "number", "Longitude": "number",
                        "CountryCode": "text"},
        "query_number_culture": "en-US",
        "query_migration": "Require all seven headers in this order; retain Code as the join key.",
    }
    atomic_write(OUTPUT, output_raw)
    verify_rows(original, parse_csv(OUTPUT.read_bytes(), HEADERS), extended)
    atomic_write(MANIFEST, (json.dumps(manifest, indent=2) + "\n").encode("utf-8"))
    print(json.dumps({"mode": "export", "output_file": OUTPUT.name, **summary}))


if __name__ == "__main__":
    try:
        main()
    except ValidationFailure as exc:
        print(json.dumps({"error": str(exc), "api_calls": 0}))
        sys.exit(2)
    except Exception:
        print('{"error":"LOCAL_READ_OR_WRITE_FAILED","api_calls":0}')
        sys.exit(2)
