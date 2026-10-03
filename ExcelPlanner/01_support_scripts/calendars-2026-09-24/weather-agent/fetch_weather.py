#!/usr/bin/env python3
"""Stage seven current-weather rows; never write outside this script's folder."""

import argparse
from collections import Counter
import csv
from datetime import datetime, timedelta, timezone
import io
import json
import math
import os
from pathlib import Path
import re
import socket
import sqlite3
import sys
import tempfile
from urllib import error, parse, request


HEADERS = [
    "Code", "ObservedUtc", "FetchedUtc", "TemperatureC", "FeelsLikeC",
    "Description", "WindMs", "Status", "Source",
]
CITIES = (
    ("hanoi", "Hanoi", "VN"), ("mumbai", "Mumbai", "IN"),
    ("minsk", "Minsk", "BY"), ("rome", "Rome", "IT"),
    ("london", "London", "GB"), ("new-york", "New York", "US"),
    ("los-angeles", "Los Angeles", "US"),
)
TTL_SECONDS = 30 * 60
MAX_CALLS = 7
STAMP = "%Y-%m-%dT%H:%M:%S"
SOURCE = "OpenWeather"
ENDPOINT = "https://api.openweathermap.org/data/2.5/weather"
STAGING = Path(__file__).resolve().parent
CSV_PATH = STAGING / "Weather.csv"
REPORT_PATH = STAGING / "weather-report.json"


class SafeFailure(Exception):
    """Expose only an internal, credential-free failure code."""


class SafeParser(argparse.ArgumentParser):
    def error(self, message):
        self.exit(2, '{"fatal_error":"INVALID_ARGUMENTS"}\n')


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # A redirect must never forward the key or consume another API call.
        return None


def utcnow():
    return datetime.now(timezone.utc).replace(microsecond=0)


def stamp(value):
    return value.astimezone(timezone.utc).strftime(STAMP)


def parse_stamp(value):
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", value):
        raise SafeFailure("INVALID_TIMESTAMP")
    return datetime.strptime(value, STAMP).replace(tzinfo=timezone.utc)


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise SafeFailure("INVALID_NUMBER")
    if not math.isfinite(value):
        raise SafeFailure("INVALID_NUMBER")
    return format(value, ".12g")


def description(value):
    if (not isinstance(value, str) or not value or len(value) > 200
            or value != value.strip() or any(ord(c) < 32 for c in value)
            or value[0] in "=+-@"):
        raise SafeFailure("INVALID_DESCRIPTION")
    return value


def inspect_catalog(path):
    # Read-only URI prevents SQLite from creating or modifying the source file.
    path = path.resolve(strict=True)
    with sqlite3.connect(path.as_uri() + "?mode=ro", uri=True) as connection:
        connection.execute("PRAGMA query_only=ON")
        connection.row_factory = sqlite3.Row
        columns = {row["name"] for row in connection.execute("PRAGMA table_info(city)")}
        required = {"id", "name", "country_code", "latitude", "longitude"}
        if not required.issubset(columns):
            raise SafeFailure("CATALOG_SCHEMA_MISMATCH")
        cities = []
        for code, label, country in CITIES:
            matches = connection.execute(
                "SELECT id, name, country_code, latitude, longitude FROM city "
                "WHERE id = ? OR name = ? COLLATE NOCASE LIMIT 3", (code, label),
            ).fetchall()
            if len(matches) != 1:
                raise SafeFailure("CATALOG_CITY_MISSING_OR_AMBIGUOUS")
            row = matches[0]
            if (row["id"], row["name"], row["country_code"]) != (code, label, country):
                raise SafeFailure("CATALOG_CITY_IDENTITY_MISMATCH")
            lat, lon = row["latitude"], row["longitude"]
            number(lat)
            number(lon)
            if not (-90 <= lat <= 90 and -180 <= lon <= 180):
                raise SafeFailure("CATALOG_INVALID_COORDINATES")
            cities.append({"code": code, "label": label, "country": country,
                           "latitude": lat, "longitude": lon})
    return cities


def read_key(key_file):
    # An explicitly present environment value takes precedence, even if invalid.
    secret = os.environ.get("OPENWEATHER_API_KEY")
    if secret is None:
        if key_file is None:
            raise SafeFailure("MISSING_API_KEY")
        try:
            with key_file.open("r", encoding="utf-8-sig") as stream:
                secret = stream.read(4097)
        except (OSError, UnicodeError):
            raise SafeFailure("KEY_FILE_UNREADABLE") from None
    secret = secret.strip()
    if not re.fullmatch(r"[A-Za-z0-9]{16,128}", secret):
        raise SafeFailure("INVALID_KEY_FORMAT")
    return secret


def ensure_safe(text, secret):
    if (secret and secret in text) or re.search(r"appid\s*(?:=|%3d)", text, re.I):
        raise SafeFailure("SECRET_SCAN_FAILED")


def read_cache(now):
    if not CSV_PATH.exists():
        return {}
    with CSV_PATH.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream, delimiter=";")
        if reader.fieldnames != HEADERS:
            raise SafeFailure("CACHE_SCHEMA_MISMATCH")
        rows = list(reader)
    expected = {city[0] for city in CITIES}
    cache = {}
    for row in rows:
        if set(row) != set(HEADERS) or any(value is None for value in row.values()):
            raise SafeFailure("CACHE_INVALID_ROW")
        code = row["Code"]
        if code not in expected or code in cache or row["Source"] != SOURCE:
            raise SafeFailure("CACHE_INVALID_CODE_OR_SOURCE")
        status = row["Status"]
        if not re.fullmatch(
            r"(?:fetched|cached|stale:[A-Z0-9_]+|unavailable:[A-Z0-9_]+)"
            r"(?:\|aged_observation)?", status,
        ):
            raise SafeFailure("CACHE_INVALID_STATUS")
        if not row["FetchedUtc"]:
            if not status.startswith("unavailable:") or any(
                row[field] for field in ("ObservedUtc", "TemperatureC", "FeelsLikeC", "Description", "WindMs")
            ):
                raise SafeFailure("CACHE_INVALID_EMPTY_ROW")
        else:
            fetched, observed = parse_stamp(row["FetchedUtc"]), parse_stamp(row["ObservedUtc"])
            if (fetched > now + timedelta(seconds=120)
                    or observed > fetched + timedelta(seconds=120)
                    or status.startswith("unavailable:")):
                raise SafeFailure("CACHE_INVALID_TIME_OR_STATUS")
            for field in ("TemperatureC", "FeelsLikeC", "WindMs"):
                if not math.isfinite(float(row[field])):
                    raise SafeFailure("CACHE_INVALID_NUMBER")
            if float(row["WindMs"]) < 0:
                raise SafeFailure("CACHE_INVALID_WIND")
            description(row["Description"])
        cache[code] = row
    if set(cache) != expected:
        raise SafeFailure("CACHE_INCOMPLETE")
    return cache


def fetch_current(opener, city, secret):
    # The keyed URL exists only in memory. Never print requests, HTTPError or bodies.
    query = parse.urlencode({
        "lat": city["latitude"], "lon": city["longitude"], "appid": secret,
        "units": "metric", "lang": "it",
    })
    req = request.Request(ENDPOINT + "?" + query, headers={
        "Accept": "application/json", "User-Agent": "LocalExcelWeatherStage/1.0",
    })
    try:
        with opener.open(req, timeout=12) as response:
            if response.status != 200:
                return None, "HTTP_" + str(int(response.status))
            raw = response.read(65537)
            if len(raw) > 65536:
                return None, "RESPONSE_TOO_LARGE"
            payload = json.loads(raw)
        fetched = utcnow()
        if str(payload["cod"]) != "200":
            return None, "API_REJECTED"
        if payload["sys"]["country"] != city["country"]:
            return None, "RESPONSE_COUNTRY_MISMATCH"
        timestamp = payload["dt"]
        if isinstance(timestamp, bool) or not isinstance(timestamp, int) or timestamp <= 0:
            return None, "INVALID_OBSERVATION_TIME"
        observed = datetime.fromtimestamp(timestamp, timezone.utc)
        if observed > fetched + timedelta(seconds=120):
            return None, "FUTURE_OBSERVATION_TIME"
        wind = number(payload["wind"]["speed"])
        if float(wind) < 0:
            return None, "INVALID_WIND_SPEED"
        row = dict(zip(HEADERS, [
            city["code"], stamp(observed), stamp(fetched),
            number(payload["main"]["temp"]), number(payload["main"]["feels_like"]),
            description(payload["weather"][0]["description"]), wind, "fetched", SOURCE,
        ]))
        ensure_safe(json.dumps(row, ensure_ascii=False), secret)
        return row, None
    except error.HTTPError as exc:
        # Only the numeric code is safe; exception text includes the request URL.
        code = "HTTP_" + str(int(exc.code))
        exc.close()
        return None, code
    except (TimeoutError, socket.timeout):
        return None, "TIMEOUT"
    except error.URLError:
        return None, "NETWORK_ERROR"
    except SafeFailure:
        return None, "RESPONSE_VALIDATION_FAILED"
    except Exception:
        return None, "INVALID_RESPONSE"


def age_seconds(row, field, now):
    return max(0, int((now - parse_stamp(row[field])).total_seconds())) if row[field] else None


def set_age_status(row, now):
    row["Status"] = row["Status"].split("|")[0]
    age = age_seconds(row, "ObservedUtc", now)
    if age is not None and age >= TTL_SECONDS:
        row["Status"] += "|aged_observation"


def atomic_write(path, text, encoding):
    # Same-directory replacement preserves the previous complete file on failure.
    temp_path = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding=encoding, newline="",
                                         prefix=".weather-", suffix=".tmp", dir=STAGING,
                                         delete=False) as stream:
            temp_path = Path(stream.name)
            stream.write(text)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp_path, path)
    finally:
        if temp_path is not None and temp_path.exists():
            temp_path.unlink()


def summarize(report):
    return {field: report[field] for field in (
        "run_started_utc", "run_completed_utc", "rows", "api_calls", "fetch_errors",
        "status_counts", "credential_scan_passed",
    )}


def main():
    parser = SafeParser(description=__doc__)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--key-file", type=Path, help="Used only if OPENWEATHER_API_KEY is absent.")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--check-only", action="store_true", help="Validate catalog/cache; no key read or network.")
    modes.add_argument("--verify-output", action="store_true", help="Scan staged CSV/report using the key; no network/writes.")
    args = parser.parse_args()
    started = utcnow()
    cities = inspect_catalog(args.catalog)
    cache = read_cache(started)
    if args.check_only:
        print(json.dumps({"mode": "check_only", "api_calls": 0, "headers": HEADERS,
                          "cities": cities, "cache_rows": len(cache)}, ensure_ascii=True))
        return 0

    secret = read_key(args.key_file)
    if args.verify_output:
        for path in (CSV_PATH, REPORT_PATH):
            ensure_safe(path.read_text(encoding="utf-8-sig"), secret)
        report = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        if (report["headers"] != HEADERS or report["rows"] != len(cache)
                or len(cache) != 7 or report["api_calls"] > MAX_CALLS
                or report["fetch_errors"] != sum(row["error"] is not None for row in report["cities"])):
            raise SafeFailure("REPORT_VALIDATION_FAILED")
        for item in report["cities"]:
            row = cache[item["code"]]
            if (item["observed_utc"], item["fetched_utc"], item["status"]) != (
                row["ObservedUtc"], row["FetchedUtc"], row["Status"]
            ):
                raise SafeFailure("REPORT_ROW_MISMATCH")
        print(json.dumps({"verification_passed": True, "verification_api_calls": 0, **summarize(report)}))
        return 0

    # Disable redirects and automatic proxy discovery. urllib does not retry here.
    opener = request.build_opener(request.ProxyHandler({}), NoRedirect())
    rows, city_reports = [], []
    calls = errors = 0
    for city in cities:
        now = utcnow()
        previous = cache.get(city["code"])
        age = age_seconds(previous, "FetchedUtc", now) if previous else None
        failure = None
        if age is not None and age < TTL_SECONDS:
            row = dict(previous)
            row["Status"] = "cached"
        else:
            if calls >= MAX_CALLS:
                raise SafeFailure("API_CALL_BUDGET_EXCEEDED")
            calls += 1
            row, failure = fetch_current(opener, city, secret)
            if failure is not None:
                errors += 1
                if previous and previous["FetchedUtc"]:
                    row = dict(previous)
                    row["Status"] = "stale:" + failure
                else:
                    row = {field: "" for field in HEADERS}
                    row.update(Code=city["code"], Status="unavailable:" + failure, Source=SOURCE)
        now = utcnow()
        set_age_status(row, now)
        rows.append(row)
        city_reports.append({**city, "observed_utc": row["ObservedUtc"],
                             "fetched_utc": row["FetchedUtc"], "status": row["Status"],
                             "fetch_age_seconds": age_seconds(row, "FetchedUtc", now),
                             "observation_age_seconds": age_seconds(row, "ObservedUtc", now),
                             "error": failure})

    buffer = io.StringIO(newline="")
    writer = csv.DictWriter(buffer, fieldnames=HEADERS, delimiter=";", lineterminator="\r\n")
    writer.writeheader()
    writer.writerows(rows)
    csv_text = buffer.getvalue()
    report = {"run_started_utc": stamp(started), "run_completed_utc": stamp(utcnow()),
              "headers": HEADERS, "rows": len(rows), "api_calls": calls,
              "max_api_calls": MAX_CALLS, "fetch_errors": errors,
              "cache_ttl_seconds": TTL_SECONDS, "units": "metric", "lang": "it",
              "product": "current_weather", "source": "https://openweathermap.org/api/current",
              "status_counts": dict(Counter(row["Status"] for row in rows)),
              "credential_scan_passed": True, "cities": city_reports}
    report_text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    ensure_safe(csv_text, secret)
    ensure_safe(report_text, secret)
    atomic_write(CSV_PATH, csv_text, "utf-8-sig")
    atomic_write(REPORT_PATH, report_text, "utf-8")
    # Re-read the serialized outputs before reporting success.
    for path in (CSV_PATH, REPORT_PATH):
        ensure_safe(path.read_text(encoding="utf-8-sig"), secret)
    print(json.dumps(summarize(report), ensure_ascii=True))
    return 1 if errors else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print('{"fatal_error":"INTERRUPTED"}')
        sys.exit(130)
    except SafeFailure as exc:
        # SafeFailure is only ever constructed from fixed internal constants.
        print(json.dumps({"fatal_error": str(exc)}))
        sys.exit(2)
    except Exception:
        # Do not print tracebacks, exception strings, arguments, URLs or response bodies.
        print('{"fatal_error":"LOCAL_VALIDATION_OR_IO_FAILED"}')
        sys.exit(2)
