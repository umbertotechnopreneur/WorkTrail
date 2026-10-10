#!/usr/bin/env python3
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
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Promote a complete Urban Wash set with per-target rollback on ordinary failures."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import os
import re
import shutil
import sqlite3
import subprocess
import tempfile
import warnings
from pathlib import Path


SEASONS = ("summer", "winter")
GENERATION_SCHEMA_VERSION = 1
RUNTIME_SCHEMA_VERSION = 1
PACKAGED_SCHEMA_VERSION = 1
STYLE_ID = "urban-wash-v1"
SKYLINE_DIRECTORY_NAME = "Skylines"
RUNTIME_DIRECTORY_NAME = "runtime-v4"
PACKAGED_MANIFEST_NAME = "PACKAGED-ASSET-MANIFEST.json"
PACKAGED_TRANSFORMATION = (
    "Decoded the reviewed 1280x720 alpha WebP runtime derivative and encoded a "
    "lossless 1280x720 RGBA PNG with FFmpeg/png, compression level 9, mixed prediction"
)
CITY_ID_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
REPARSE_POINT_ATTRIBUTE = 0x400
ARTWORK_AUTHOR = "WorkTrail"
ARTWORK_LICENSE = "WorkTrail project artwork - public publication authorized"
ARTWORK_PROVENANCE = "Assets/WorldClocks/PROVENANCE.md"
ARTWORK_RELEASE_STATUS = (
    "Owner-authorized for public publication on 2026-08-30; "
    "applicable ImageGen service terms accepted"
)


def read_json(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise RuntimeError(f"Expected a JSON object in {path}.")
    return data


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def is_reparse_point(path: Path) -> bool:
    attributes = getattr(path.lstat(), "st_file_attributes", 0)
    return path.is_symlink() or bool(attributes & REPARSE_POINT_ATTRIBUTE)


def reject_reparse_tree(root: Path) -> None:
    for path in (root, *root.rglob("*")):
        if is_reparse_point(path):
            raise RuntimeError(f"Reparse points are not supported in the promotion tree: {path}")


def require_direct_child(root: Path, path: Path, *, must_exist: bool = True) -> Path:
    resolved_root = root.resolve(strict=True)
    resolved_path = path.resolve(strict=must_exist)
    if resolved_path.parent != resolved_root:
        raise RuntimeError(f"Path is not a direct child of {resolved_root}: {resolved_path}")
    if must_exist and is_reparse_point(path):
        raise RuntimeError(f"Reparse-point input is not supported: {path}")
    return resolved_path


def expected_assets(
    generation_manifest: dict,
) -> tuple[dict[str, str], dict[tuple[str, str], dict[str, str]], dict[str, dict]]:
    if (
        generation_manifest.get("schemaVersion") != GENERATION_SCHEMA_VERSION
        or generation_manifest.get("styleId") != STYLE_ID
    ):
        raise RuntimeError("Unsupported generation manifest schema or style.")

    cities = generation_manifest.get("cities")
    if not isinstance(cities, list) or not cities:
        raise RuntimeError("Generation manifest must contain at least one city.")
    expected_asset_count = len(cities) * len(SEASONS)
    if generation_manifest.get("assetCountExpected") != expected_asset_count:
        raise RuntimeError("Generation manifest asset count does not match its declared city list.")

    names: dict[str, str] = {}
    for city in cities:
        if not isinstance(city, dict):
            raise RuntimeError(f"Invalid city entry in generation manifest: {city!r}")
        city_id = city.get("cityId")
        display_name = city.get("displayName")
        if (
            not isinstance(city_id, str)
            or CITY_ID_PATTERN.fullmatch(city_id) is None
            or not isinstance(display_name, str)
            or not display_name.strip()
            or city_id in names
        ):
            raise RuntimeError(f"Invalid or duplicate city in generation manifest: {city!r}")
        names[city_id] = display_name.strip()

    expected = {(city_id, season) for city_id in names for season in SEASONS}
    if len(expected) != expected_asset_count:
        raise AssertionError("Expected asset expansion is inconsistent.")

    reviewed_masters = generation_manifest.get("reviewedMasters")
    if not isinstance(reviewed_masters, list) or len(reviewed_masters) != expected_asset_count:
        raise RuntimeError(
            "Generation manifest must bind one reviewed master checksum per city and season."
        )

    reviewed: dict[tuple[str, str], dict[str, str]] = {}
    for binding in reviewed_masters:
        if not isinstance(binding, dict):
            raise RuntimeError(f"Invalid reviewed-master binding: {binding!r}")
        city_id = binding.get("cityId")
        season = binding.get("season")
        file_name = binding.get("fileName")
        checksum = binding.get("sha256")
        if not isinstance(city_id, str) or not isinstance(season, str):
            raise RuntimeError(f"Invalid reviewed-master binding: {binding!r}")
        key = (city_id, season)
        expected_file_name = f"{city_id}-{season}.png"
        if (
            key not in expected
            or key in reviewed
            or file_name != expected_file_name
            or not isinstance(checksum, str)
            or SHA256_PATTERN.fullmatch(checksum) is None
        ):
            raise RuntimeError(f"Invalid or duplicate reviewed-master binding: {binding!r}")
        reviewed[key] = {
            "fileName": file_name,
            "sha256": checksum,
        }

    if reviewed.keys() != expected:
        missing = sorted(expected - reviewed.keys())
        raise RuntimeError(f"Reviewed-master bindings are incomplete: {missing}")

    additional_catalog_cities = generation_manifest.get("additionalCatalogCities", [])
    if not isinstance(additional_catalog_cities, list):
        raise RuntimeError("additionalCatalogCities must be a list when supplied.")
    additional_records: dict[str, dict] = {}
    required_catalog_fields = {
        "cityId",
        "geonameId",
        "name",
        "countryCode",
        "countryName",
        "latitude",
        "longitude",
        "population",
        "timeZoneId",
        "isCapital",
        "hemisphere",
    }
    for record in additional_catalog_cities:
        if not isinstance(record, dict) or set(record) != required_catalog_fields:
            raise RuntimeError("Additional catalog city records must declare the complete city schema.")
        city_id = record["cityId"]
        if (
            not isinstance(city_id, str)
            or city_id not in names
            or city_id in additional_records
            or not isinstance(record["geonameId"], int)
            or record["geonameId"] <= 0
            or not isinstance(record["name"], str)
            or not record["name"].strip()
            or not isinstance(record["countryCode"], str)
            or len(record["countryCode"]) != 2
            or not isinstance(record["countryName"], str)
            or not record["countryName"].strip()
            or not isinstance(record["latitude"], (int, float))
            or not -90 <= record["latitude"] <= 90
            or not isinstance(record["longitude"], (int, float))
            or not -180 <= record["longitude"] <= 180
            or not isinstance(record["population"], int)
            or record["population"] < 0
            or not isinstance(record["timeZoneId"], str)
            or not record["timeZoneId"].strip()
            or not isinstance(record["isCapital"], bool)
            or record["hemisphere"] not in ("north", "south", "equatorial")
        ):
            raise RuntimeError(f"Invalid additional catalog city record: {record!r}")
        additional_records[city_id] = record
    return names, reviewed, additional_records


def command_output(command: list[str], description: str) -> str:
    result = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        details = (result.stderr or result.stdout).strip()
        raise RuntimeError(f"{description} failed: {details}")
    return f"{result.stdout}\n{result.stderr}"


def decode_and_validate_webp(path: Path, ffprobe: str, ffmpeg: str) -> tuple[str, int, int, int]:
    probe_text = command_output(
        [
            ffprobe,
            "-v",
            "error",
            "-select_streams",
            "v:0",
            "-show_entries",
            "stream=codec_name,width,height,pix_fmt",
            "-of",
            "json",
            str(path),
        ],
        f"ffprobe for {path.name}",
    )
    try:
        streams = json.loads(probe_text).get("streams", [])
    except json.JSONDecodeError as error:
        raise RuntimeError(f"ffprobe returned invalid JSON for {path}.") from error
    if len(streams) != 1:
        raise RuntimeError(f"Expected one image stream in {path}; found {len(streams)}.")
    stream = streams[0]
    pixel_format = str(stream.get("pix_fmt", ""))
    if (
        stream.get("codec_name") != "webp"
        or stream.get("width") != 1280
        or stream.get("height") != 720
        or not pixel_format.startswith("yuva")
    ):
        raise RuntimeError(f"Unexpected decoded WebP format for {path}: {stream!r}")

    alpha_text = command_output(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(path),
            "-vf",
            "alphaextract,signalstats,metadata=print:file=-",
            "-frames:v",
            "1",
            "-f",
            "null",
            os.devnull,
        ],
        f"alpha decode for {path.name}",
    )
    minimum = re.search(r"lavfi\.signalstats\.YMIN=(\d+)", alpha_text)
    maximum = re.search(r"lavfi\.signalstats\.YMAX=(\d+)", alpha_text)
    if minimum is None or maximum is None:
        raise RuntimeError(f"Alpha statistics were not reported for {path}.")
    alpha_minimum = int(minimum.group(1))
    alpha_maximum = int(maximum.group(1))
    if alpha_minimum != 0 or alpha_maximum != 255:
        raise RuntimeError(
            f"WebP {path} does not preserve the required 0-255 alpha range: "
            f"{alpha_minimum}-{alpha_maximum}."
        )
    return pixel_format, 1280, 720, path.stat().st_size


def decode_and_validate_png(path: Path, ffprobe: str, ffmpeg: str) -> tuple[str, int, int, int]:
    probe_text = command_output(
        [
            ffprobe,
            "-v",
            "error",
            "-select_streams",
            "v:0",
            "-show_entries",
            "stream=codec_name,width,height,pix_fmt",
            "-of",
            "json",
            str(path),
        ],
        f"ffprobe for {path.name}",
    )
    try:
        streams = json.loads(probe_text).get("streams", [])
    except json.JSONDecodeError as error:
        raise RuntimeError(f"ffprobe returned invalid JSON for {path}.") from error
    if len(streams) != 1:
        raise RuntimeError(f"Expected one image stream in {path}; found {len(streams)}.")
    stream = streams[0]
    if (
        stream.get("codec_name") != "png"
        or stream.get("width") != 1280
        or stream.get("height") != 720
        or stream.get("pix_fmt") != "rgba"
    ):
        raise RuntimeError(f"Unexpected decoded PNG format for {path}: {stream!r}")

    alpha_text = command_output(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(path),
            "-vf",
            "alphaextract,signalstats,metadata=print:file=-",
            "-frames:v",
            "1",
            "-f",
            "null",
            os.devnull,
        ],
        f"alpha decode for {path.name}",
    )
    minimum = re.search(r"lavfi\.signalstats\.YMIN=(\d+)", alpha_text)
    maximum = re.search(r"lavfi\.signalstats\.YMAX=(\d+)", alpha_text)
    if minimum is None or maximum is None:
        raise RuntimeError(f"Alpha statistics were not reported for {path}.")
    alpha_minimum = int(minimum.group(1))
    alpha_maximum = int(maximum.group(1))
    if alpha_minimum != 0 or alpha_maximum != 255:
        raise RuntimeError(
            f"PNG {path} does not preserve the required 0-255 alpha range: "
            f"{alpha_minimum}-{alpha_maximum}."
        )
    return "rgba", 1280, 720, path.stat().st_size


def validate_runtime(
    runtime_root: Path,
    generation_manifest: dict,
    generation_manifest_path: Path,
) -> tuple[dict[str, str], dict[str, dict], dict[tuple[str, str], dict], dict, Path]:
    names, reviewed_masters, additional_catalog_cities = expected_assets(generation_manifest)
    expected = set(reviewed_masters)
    expected_asset_count = len(expected)
    manifest_path = require_direct_child(runtime_root, runtime_root / "runtime-asset-manifest.json")
    runtime_manifest = read_json(manifest_path)
    assets = runtime_manifest.get("assets")
    if (
        runtime_manifest.get("schemaVersion") != RUNTIME_SCHEMA_VERSION
        or runtime_manifest.get("sourceManifest") != generation_manifest_path.name
        or runtime_manifest.get("sourceManifestSha256") != sha256(generation_manifest_path)
        or runtime_manifest.get("sourceManifestSchemaVersion") != GENERATION_SCHEMA_VERSION
        or runtime_manifest.get("sourceMasterBinding") != "generation-manifest-reviewed-sha256"
        or runtime_manifest.get("styleId") != STYLE_ID
        or not isinstance(runtime_manifest.get("toolchain"), dict)
        or not isinstance(runtime_manifest.get("transformation"), str)
        or not runtime_manifest["transformation"].strip()
        or runtime_manifest.get("complete") is not True
        or runtime_manifest.get("expectedAssetCount") != expected_asset_count
        or runtime_manifest.get("generatedAssetCount") != expected_asset_count
        or not isinstance(assets, list)
        or len(assets) != expected_asset_count
    ):
        raise RuntimeError("Runtime manifest is not a complete, source-bound asset set.")

    ffprobe = shutil.which("ffprobe")
    ffmpeg = shutil.which("ffmpeg")
    if ffprobe is None or ffmpeg is None:
        raise RuntimeError("ffprobe and ffmpeg are required to decode the runtime assets.")

    validated: dict[tuple[str, str], dict] = {}
    expected_file_names: set[str] = set()
    for asset in assets:
        if not isinstance(asset, dict):
            raise RuntimeError(f"Invalid runtime asset entry: {asset!r}")
        city_id = asset.get("cityId")
        season = asset.get("season")
        if not isinstance(city_id, str) or CITY_ID_PATTERN.fullmatch(city_id) is None:
            raise RuntimeError(f"Invalid runtime city id: {city_id!r}")
        if not isinstance(season, str):
            raise RuntimeError(f"Invalid runtime season: {season!r}")
        key = (city_id, season)
        expected_name = f"{city_id}-{season}.webp"
        if key not in expected or key in validated or asset.get("fileName") != expected_name:
            raise RuntimeError(f"Unexpected or duplicate runtime asset: {asset!r}")

        asset_path = require_direct_child(runtime_root, runtime_root / expected_name)
        pixel_format, width, height, bytes_count = decode_and_validate_webp(asset_path, ffprobe, ffmpeg)
        declared_hash = asset.get("sha256")
        if not isinstance(declared_hash, str) or SHA256_PATTERN.fullmatch(declared_hash) is None:
            raise RuntimeError(f"Invalid declared checksum for {expected_name}.")
        if sha256(asset_path) != declared_hash:
            raise RuntimeError(f"Checksum mismatch for {asset_path}.")
        reviewed_master = reviewed_masters[key]
        if (
            asset.get("sourceMasterFileName") != reviewed_master["fileName"]
            or asset.get("sourceMasterSha256") != reviewed_master["sha256"]
            or asset.get("width") != width
            or asset.get("height") != height
            or asset.get("pixelFormat") != pixel_format
            or asset.get("bytes") != bytes_count
        ):
            raise RuntimeError(f"Source binding or decoded metadata mismatch for {expected_name}.")
        validated[key] = asset
        expected_file_names.add(expected_name)

    actual_children = list(runtime_root.iterdir())
    if any(path.is_dir() for path in actual_children):
        raise RuntimeError("Runtime root must not contain subdirectories.")
    actual_file_names = {path.name for path in actual_children if path.is_file()}
    allowed_file_names = expected_file_names | {manifest_path.name}
    missing = expected - validated.keys()
    if missing or actual_file_names != allowed_file_names:
        raise RuntimeError(
            f"Runtime set mismatch; missing={sorted(missing)}, "
            f"unexpected={sorted(actual_file_names - allowed_file_names)}"
        )
    return names, additional_catalog_cities, validated, runtime_manifest, manifest_path


def build_packaged_assets(
    runtime_root: Path,
    image_root: Path,
    runtime_assets: dict[tuple[str, str], dict],
    runtime_manifest: dict,
    runtime_manifest_path: Path,
) -> tuple[dict[tuple[str, str], dict], dict]:
    ffprobe = shutil.which("ffprobe")
    ffmpeg = shutil.which("ffmpeg")
    if ffprobe is None or ffmpeg is None:
        raise RuntimeError("ffprobe and ffmpeg are required to build the packaged PNG assets.")

    def convert(item: tuple[tuple[str, str], dict]) -> tuple[tuple[str, str], dict]:
        key, runtime_asset = item
        city_id, season = key
        source_name = runtime_asset["fileName"]
        destination_name = f"{city_id}-{season}.png"
        source = require_direct_child(runtime_root, runtime_root / source_name)
        destination = require_direct_child(
            image_root, image_root / destination_name, must_exist=False
        )
        command_output(
            [
                ffmpeg,
                "-hide_banner",
                "-loglevel",
                "error",
                "-n",
                "-i",
                str(source),
                "-frames:v",
                "1",
                "-c:v",
                "png",
                "-compression_level",
                "9",
                "-pred",
                "mixed",
                "-pix_fmt",
                "rgba",
                "-threads",
                "1",
                str(destination),
            ],
            f"PNG packaging conversion for {source_name}",
        )
        pixel_format, width, height, bytes_count = decode_and_validate_png(
            destination, ffprobe, ffmpeg
        )
        return key, {
            "cityId": city_id,
            "season": season,
            "fileName": destination_name,
            "sourceRuntimeFileName": source_name,
            "sourceRuntimeSha256": runtime_asset["sha256"],
            "sourceMasterFileName": runtime_asset["sourceMasterFileName"],
            "sourceMasterSha256": runtime_asset["sourceMasterSha256"],
            "width": width,
            "height": height,
            "pixelFormat": pixel_format,
            "bytes": bytes_count,
            "sha256": sha256(destination),
        }

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        converted = dict(executor.map(convert, sorted(runtime_assets.items())))
    if set(converted) != set(runtime_assets) or len(converted) != len(runtime_assets):
        raise RuntimeError("Packaged PNG conversion did not produce the exact runtime asset set.")

    ffmpeg_version = next(
        line
        for line in command_output([ffmpeg, "-version"], "ffmpeg version").splitlines()
        if line.strip()
    )
    encoder_description = next(
        line
        for line in command_output(
            [ffmpeg, "-hide_banner", "-h", "encoder=png"], "FFmpeg PNG encoder details"
        ).splitlines()
        if line.strip()
    )
    packaged_manifest = {
        "schemaVersion": PACKAGED_SCHEMA_VERSION,
        "sourceRuntimeManifest": {
            "repositoryFile": runtime_manifest_path.name,
            "packagedFile": "RUNTIME-ASSET-MANIFEST.json",
            "sha256": sha256(runtime_manifest_path),
        },
        "sourceManifest": {
            "repositoryFile": runtime_manifest["sourceManifest"],
            "packagedFile": "SOURCE-MANIFEST.json",
            "sha256": runtime_manifest["sourceManifestSha256"],
        },
        "sourceRuntimeTransformation": runtime_manifest["transformation"],
        "styleId": STYLE_ID,
        "transformation": PACKAGED_TRANSFORMATION,
        "toolchain": {
            "ffmpeg": ffmpeg_version,
            "encoder": "png",
            "encoderDescription": encoder_description,
        },
        "expectedAssetCount": len(runtime_assets),
        "generatedAssetCount": len(converted),
        "complete": len(converted) == len(runtime_assets),
        "assets": [converted[key] for key in sorted(converted)],
    }
    return converted, packaged_manifest


def update_database(
    source_database: Path,
    destination_database: Path,
    names: dict[str, str],
    additional_catalog_cities: dict[str, dict],
    assets: dict[tuple[str, str], dict],
    transformation: str,
) -> None:
    shutil.copy2(source_database, destination_database)
    connection = sqlite3.connect(destination_database)
    try:
        connection.execute("PRAGMA foreign_keys = ON")
        for city_id, record in additional_catalog_cities.items():
            expected_city = (
                record["geonameId"],
                record["name"],
                record["countryCode"],
                record["countryName"],
                record["latitude"],
                record["longitude"],
                record["population"],
                record["timeZoneId"],
                int(record["isCapital"]),
                record["hemisphere"],
            )
            existing_city = connection.execute(
                """
                SELECT geoname_id, name, country_code, country_name, latitude, longitude,
                       population, timezone_id, is_capital, hemisphere
                  FROM city
                 WHERE id = ?
                """,
                (city_id,),
            ).fetchone()
            if existing_city is None:
                connection.execute(
                    """
                    INSERT INTO city(
                        id, geoname_id, name, country_code, country_name, latitude, longitude,
                        population, timezone_id, is_capital, hemisphere)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (city_id, *expected_city),
                )
            elif existing_city != expected_city:
                raise RuntimeError(f"SQLite city record does not match the declared catalog entry: {city_id}.")

        catalog_city_ids = {row[0] for row in connection.execute("SELECT id FROM city")}
        if catalog_city_ids != set(names):
            raise RuntimeError("SQLite city IDs do not exactly match the generation manifest.")

        for (city_id, season), asset in sorted(assets.items()):
            title = f"{names[city_id]} Urban Wash - {season}"
            description = (
                f"Project-directed Urban Wash watercolor for {names[city_id]} ({season}); "
                "transparent 16:9 packaged PNG derivative."
            )
            values = (
                f"{SKYLINE_DIRECTORY_NAME}/{asset['fileName']}",
                title,
                ARTWORK_AUTHOR,
                "",
                "",
                ARTWORK_LICENSE,
                "",
                description,
                1280,
                720,
                asset["sha256"],
                city_id,
                season,
            )
            cursor = connection.execute(
                """
                UPDATE skyline_asset
                   SET relative_path = ?, title = ?, author = ?, source_url = ?,
                       download_url = ?, license_name = ?, license_url = ?, description = ?,
                       source_width = ?, source_height = ?, sha256 = ?
                 WHERE city_id = ? AND season = ?
                """,
                values,
            )
            if cursor.rowcount == 0:
                connection.execute(
                    """
                    INSERT INTO skyline_asset(
                        city_id, season, relative_path, title, author, source_url, download_url,
                        license_name, license_url, description, source_width, source_height, sha256)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        city_id,
                        season,
                        f"{SKYLINE_DIRECTORY_NAME}/{asset['fileName']}",
                        title,
                        ARTWORK_AUTHOR,
                        "",
                        "",
                        ARTWORK_LICENSE,
                        "",
                        description,
                        1280,
                        720,
                        asset["sha256"],
                    ),
                )

        metadata = {
            "image_source": "WorkTrail Urban Wash project-generated artwork",
            "image_transform": transformation,
            "image_provenance": ARTWORK_PROVENANCE,
            "image_release_status": ARTWORK_RELEASE_STATUS,
        }
        connection.executemany(
            "INSERT OR REPLACE INTO catalog_metadata(key, value) VALUES(?, ?)",
            metadata.items(),
        )
        if connection.execute("SELECT COUNT(*) FROM city").fetchone()[0] != len(names):
            raise RuntimeError("SQLite catalog city count does not match the generation manifest.")
        if connection.execute("SELECT COUNT(*) FROM skyline_asset").fetchone()[0] != len(assets):
            raise RuntimeError("SQLite catalog asset count does not match the generation manifest.")
        connection.commit()
        if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise RuntimeError("SQLite integrity check failed after watercolor promotion.")
    finally:
        connection.close()


def attribution_assets(
    names: dict[str, str], assets: dict[tuple[str, str], dict]
) -> list[dict[str, str]]:
    output = []
    for (city_id, season), asset in sorted(
        assets.items(), key=lambda item: (names[item[0][0]], item[0][1])
    ):
        output.append(
            {
                "cityId": city_id,
                "city": names[city_id],
                "season": season,
                "relativePath": f"{SKYLINE_DIRECTORY_NAME}/{asset['fileName']}",
                "title": f"{names[city_id]} Urban Wash - {season}",
                "author": ARTWORK_AUTHOR,
                "sourceUrl": "",
                "license": ARTWORK_LICENSE,
                "licenseUrl": "",
                "sha256": asset["sha256"],
            }
        )
    return output


def write_attribution_files(
    json_path: Path,
    markdown_path: Path,
    names: dict[str, str],
    assets: dict[tuple[str, str], dict],
    runtime_manifest: dict,
    packaged_manifest: dict,
    packaged_manifest_hash: str,
) -> None:
    manifest_assets = attribution_assets(names, assets)
    attribution = {
        "schemaVersion": 3,
        "cityData": {
            "source": "GeoNames cities500",
            "url": "https://download.geonames.org/export/dump/cities500.zip",
            "license": "CC BY 4.0",
            "licenseUrl": "https://creativecommons.org/licenses/by/4.0/",
        },
        "artwork": {
            "styleId": STYLE_ID,
            "author": ARTWORK_AUTHOR,
            "provenance": ARTWORK_PROVENANCE,
            "releaseStatus": ARTWORK_RELEASE_STATUS,
        },
        "sourceManifest": {
            "file": "SOURCE-MANIFEST.json",
            "sha256": runtime_manifest["sourceManifestSha256"],
        },
        "runtimeManifest": {
            "file": "RUNTIME-ASSET-MANIFEST.json",
            "sha256": packaged_manifest["sourceRuntimeManifest"]["sha256"],
        },
        "packagedManifest": {
            "file": PACKAGED_MANIFEST_NAME,
            "sha256": packaged_manifest_hash,
        },
        "assets": manifest_assets,
        "sourceRuntimeTransformation": runtime_manifest["transformation"],
        "transformation": packaged_manifest["transformation"],
        "toolchain": packaged_manifest["toolchain"],
    }
    json_path.write_text(
        json.dumps(attribution, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    lines = [
        "# World clock data and skyline attribution",
        "",
        "City coordinates, population, and IANA time zones are derived from GeoNames `cities500`,",
        "licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).",
        "",
        "The seasonal skyline images are WorkTrail-directed Urban Wash watercolor artwork.",
        "Their exact generation, intermediate WebP, and packaged PNG manifests are stored in",
        "[`SOURCE-MANIFEST.json`](SOURCE-MANIFEST.json),",
        "[`RUNTIME-ASSET-MANIFEST.json`](RUNTIME-ASSET-MANIFEST.json),",
        f"[`{PACKAGED_MANIFEST_NAME}`]({PACKAGED_MANIFEST_NAME}), and",
        "[`PROVENANCE.md`](PROVENANCE.md).",
        "The repository and packaged asset locations are summarized in [`ASSET-MAP.md`](ASSET-MAP.md).",
        "They are not included in the repository's MIT grant; see the repository's",
        "[`ASSET_LICENSING.md`](../../../ASSET_LICENSING.md).",
        "",
        f"Release status: {ARTWORK_RELEASE_STATUS}.",
        "",
        f"Intermediate runtime transformation: {runtime_manifest['transformation']}.",
        f"Packaged transformation: {packaged_manifest['transformation']}.",
        "",
        "| City | Season | Asset | SHA-256 |",
        "|---|---|---|---|",
    ]
    for asset in manifest_assets:
        lines.append(
            f"| {asset['city']} | {asset['season']} | `{asset['relativePath']}` | `{asset['sha256']}` |"
        )
    markdown_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def write_packaged_provenance(
    path: Path,
    generation_manifest_hash: str,
    runtime_manifest_hash: str,
    runtime_manifest: dict,
    packaged_manifest_hash: str,
    packaged_manifest: dict,
) -> None:
    lines = [
        "# Urban Wash world-clock artwork provenance",
        "",
        f"- Style: `{STYLE_ID}`.",
        f"- Scope: {len(packaged_manifest['assets']) // len(SEASONS)} cities, two independently generated seasonal variants, {len(packaged_manifest['assets'])} packaged assets.",
        "- Generation mode: built-in OpenAI ImageGen, one call per distinct master; rejected outputs were not promoted.",
        "- Direction: transparent editorial architectural watercolor with landmark clusters outside the central celestial-orb safe area.",
        "- Atmosphere layers: the separately generated `Overlays/` tree is preserved and composed at runtime; source-backed weather layers are not inferred from the clock.",
        f"- Source manifest SHA-256: `{generation_manifest_hash}` (`SOURCE-MANIFEST.json`).",
        f"- Intermediate WebP manifest SHA-256: `{runtime_manifest_hash}` (`RUNTIME-ASSET-MANIFEST.json`).",
        f"- Intermediate WebP transformation: {runtime_manifest['transformation']}.",
        f"- Packaged PNG manifest SHA-256: `{packaged_manifest_hash}` (`{PACKAGED_MANIFEST_NAME}`).",
        f"- Packaged PNG transformation: {packaged_manifest['transformation']}.",
        f"- Packaged toolchain: {packaged_manifest['toolchain'].get('ffmpeg', 'unknown')}; encoder `png`.",
        "",
        "## Publication authorization",
        "",
        "The images are reserved WorkTrail project artwork and are outside the repository MIT grant.",
        "On 2026-08-30 the project owner authorized public publication of the complete generated",
        "asset set and accepted the applicable ImageGen service terms in that context. This",
        "authorization does not place the artwork under the repository MIT license. The checksums",
        "in the attribution and packaged manifest bind this record to the exact published files.",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def write_asset_map(path: Path, city_count: int, asset_count: int) -> None:
    lines = [
        "# World-clock asset locations",
        "",
        "## Packaged runtime",
        "",
        f"- `Skylines/`: exactly {asset_count} city/season RGBA PNG files at 1280×720.",
        "- `Overlays/Backdrops/`: eight generated RGBA PNG atmosphere backdrops.",
        "- `Overlays/Foregrounds/`: three generated RGBA PNG weather foregrounds.",
        "- `Overlays/PROVENANCE.md`: overlay prompts, layering contract, checksums, and release boundary.",
        "- `world-clocks.sqlite3`: city catalog and relative skyline paths under `Skylines/`.",
        f"- `SOURCE-MANIFEST.json`, `RUNTIME-ASSET-MANIFEST.json`, `{PACKAGED_MANIFEST_NAME}`, `ATTRIBUTION.*`, and `PROVENANCE.md`: exact source/intermediate/package chain.",
        "",
        "The obsolete generic `Images/` directory is intentionally absent.",
        "",
        "## Repository-only masters and build output",
        "",
        f"- `design/world-clocks/watercolor/masters-v1/`: {asset_count} original transparent PNG city masters.",
        f"- `design/world-clocks/watercolor/{RUNTIME_DIRECTORY_NAME}/`: {asset_count} converted alpha WebP files plus the intermediate runtime manifest; these files are not loaded by WinUI.",
        f"- `design/world-clocks/watercolor/generation-manifest-v1.json`: the {city_count}-city, two-season generation contract.",
        "- `design/world-clocks/watercolor/GENERATION_RULES.md` and `PROVENANCE.md`: prompt and review records.",
        "",
        "Overlay PNGs are currently both the selected generated originals and the packaged runtime files;",
        "they are not duplicated into the city master or WebP directories.",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def verify_staged_product(
    product_root: Path,
    names: dict[str, str],
    assets: dict[tuple[str, str], dict],
    runtime_manifest: dict,
    packaged_manifest: dict,
    generation_manifest_hash: str,
    runtime_manifest_hash: str,
    packaged_manifest_hash: str,
) -> None:
    image_root = product_root / SKYLINE_DIRECTORY_NAME
    overlay_root = product_root / "Overlays"
    if (product_root / "Images").exists():
        raise RuntimeError("The obsolete generic Images directory remains in the staged product.")
    if not image_root.is_dir():
        raise RuntimeError("The staged skyline directory is missing.")
    if not overlay_root.is_dir() or not (overlay_root / "PROVENANCE.md").is_file():
        raise RuntimeError("The intentional atmosphere overlay tree is missing from the staged product.")
    expected_names = {asset["fileName"] for asset in assets.values()}
    image_children = list(image_root.iterdir())
    if any(not path.is_file() for path in image_children):
        raise RuntimeError("Staged skyline directory must contain files only.")
    actual_names = {path.name for path in image_children}
    if actual_names != expected_names or len(actual_names) != len(assets):
        raise RuntimeError("Staged product image directory is not the exact manifest-bound set.")
    if any(Path(name).suffix != ".png" for name in actual_names):
        raise RuntimeError("Staged product image directory must contain packaged PNG files only.")
    for asset in assets.values():
        image_path = require_direct_child(image_root, image_root / asset["fileName"])
        if sha256(image_path) != asset["sha256"]:
            raise RuntimeError(f"Staged checksum mismatch for {image_path}.")

    connection = sqlite3.connect(product_root / "world-clocks.sqlite3")
    try:
        database_rows = {
            (row[0], row[1]): row[2:]
            for row in connection.execute(
                """
                SELECT city_id, season, relative_path, title, author, source_url,
                       download_url, license_name, license_url, description,
                       source_width, source_height, sha256
                  FROM skyline_asset
                """
            )
        }
        if set(database_rows) != set(assets) or len(database_rows) != len(assets):
            raise RuntimeError("Staged SQLite keys do not match the packaged manifest.")
        for (city_id, season), asset in assets.items():
            expected_row = (
                f"{SKYLINE_DIRECTORY_NAME}/{asset['fileName']}",
                f"{names[city_id]} Urban Wash - {season}",
                ARTWORK_AUTHOR,
                "",
                "",
                ARTWORK_LICENSE,
                "",
                (
                    f"Project-directed Urban Wash watercolor for {names[city_id]} ({season}); "
                    "transparent 16:9 packaged PNG derivative."
                ),
                1280,
                720,
                asset["sha256"],
            )
            if database_rows[(city_id, season)] != expected_row:
                raise RuntimeError(f"Staged SQLite packaged fields mismatch for {city_id}/{season}.")
        if {row[0] for row in connection.execute("SELECT id FROM city")} != set(names):
            raise RuntimeError("Staged SQLite city IDs do not match the generation manifest.")
        expected_metadata = {
            "image_source": "WorkTrail Urban Wash project-generated artwork",
            "image_transform": packaged_manifest["transformation"],
            "image_provenance": ARTWORK_PROVENANCE,
            "image_release_status": ARTWORK_RELEASE_STATUS,
        }
        actual_metadata = dict(
            connection.execute(
                "SELECT key, value FROM catalog_metadata WHERE key IN (?, ?, ?, ?)",
                tuple(expected_metadata),
            )
        )
        if actual_metadata != expected_metadata:
            raise RuntimeError("Staged SQLite artwork metadata does not match the packaged manifest.")
        if list(connection.execute("PRAGMA foreign_key_check")):
            raise RuntimeError("Staged SQLite foreign-key check failed.")
        if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise RuntimeError("Staged SQLite integrity check failed.")
    finally:
        connection.close()

    attribution = read_json(product_root / "ATTRIBUTION.json")
    if (
        attribution.get("assets") != attribution_assets(names, assets)
        or attribution.get("sourceManifest")
        != {"file": "SOURCE-MANIFEST.json", "sha256": generation_manifest_hash}
        or attribution.get("runtimeManifest")
        != {"file": "RUNTIME-ASSET-MANIFEST.json", "sha256": runtime_manifest_hash}
        or attribution.get("packagedManifest")
        != {"file": PACKAGED_MANIFEST_NAME, "sha256": packaged_manifest_hash}
        or attribution.get("sourceRuntimeTransformation") != runtime_manifest["transformation"]
        or attribution.get("transformation") != packaged_manifest["transformation"]
        or attribution.get("toolchain") != packaged_manifest["toolchain"]
    ):
        raise RuntimeError("Staged attribution fields do not match the packaged manifest.")

    source_manifest_path = product_root / "SOURCE-MANIFEST.json"
    runtime_manifest_path = product_root / "RUNTIME-ASSET-MANIFEST.json"
    packaged_manifest_path = product_root / PACKAGED_MANIFEST_NAME
    if sha256(source_manifest_path) != generation_manifest_hash:
        raise RuntimeError("Staged generation-manifest checksum mismatch.")
    if sha256(runtime_manifest_path) != runtime_manifest_hash:
        raise RuntimeError("Staged runtime-manifest checksum mismatch.")
    if sha256(packaged_manifest_path) != packaged_manifest_hash:
        raise RuntimeError("Staged packaged-manifest checksum mismatch.")
    if read_json(packaged_manifest_path) != packaged_manifest:
        raise RuntimeError("Staged packaged manifest content changed after generation.")
    if (
        packaged_manifest.get("schemaVersion") != PACKAGED_SCHEMA_VERSION
        or packaged_manifest.get("styleId") != STYLE_ID
        or packaged_manifest.get("complete") is not True
        or packaged_manifest.get("expectedAssetCount") != len(assets)
        or packaged_manifest.get("generatedAssetCount") != len(assets)
        or packaged_manifest.get("assets") != [assets[key] for key in sorted(assets)]
        or packaged_manifest.get("sourceRuntimeManifest")
        != {
            "repositoryFile": "runtime-asset-manifest.json",
            "packagedFile": "RUNTIME-ASSET-MANIFEST.json",
            "sha256": runtime_manifest_hash,
        }
        or packaged_manifest.get("sourceManifest")
        != {
            "repositoryFile": runtime_manifest["sourceManifest"],
            "packagedFile": "SOURCE-MANIFEST.json",
            "sha256": generation_manifest_hash,
        }
    ):
        raise RuntimeError("Staged packaged manifest is not the exact source-bound city asset set.")


def build_staged_product(
    product_root: Path,
    staged_root: Path,
    runtime_root: Path,
    generation_manifest_path: Path,
    runtime_manifest_path: Path,
    names: dict[str, str],
    additional_catalog_cities: dict[str, dict],
    runtime_assets: dict[tuple[str, str], dict],
    runtime_manifest: dict,
) -> None:
    """Build and fully validate the same staged catalog used by real promotion."""
    shutil.copytree(product_root, staged_root)
    legacy_image_root = staged_root / "Images"
    if legacy_image_root.exists():
        require_direct_child(staged_root, legacy_image_root)
        shutil.rmtree(legacy_image_root)
    staged_image_root = staged_root / SKYLINE_DIRECTORY_NAME
    if staged_image_root.exists():
        require_direct_child(staged_root, staged_image_root)
        shutil.rmtree(staged_image_root)
    staged_image_root.mkdir()
    packaged_assets, packaged_manifest = build_packaged_assets(
        runtime_root,
        staged_image_root,
        runtime_assets,
        runtime_manifest,
        runtime_manifest_path,
    )
    packaged_manifest_path = staged_root / PACKAGED_MANIFEST_NAME
    packaged_manifest_path.write_text(
        json.dumps(packaged_manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    packaged_manifest_hash = sha256(packaged_manifest_path)

    staged_database = staged_root / "world-clocks.sqlite3.next"
    update_database(
        staged_root / "world-clocks.sqlite3",
        staged_database,
        names,
        additional_catalog_cities,
        packaged_assets,
        str(packaged_manifest["transformation"]),
    )
    os.replace(staged_database, staged_root / "world-clocks.sqlite3")
    write_attribution_files(
        staged_root / "ATTRIBUTION.json",
        staged_root / "ATTRIBUTION.md",
        names,
        packaged_assets,
        runtime_manifest,
        packaged_manifest,
        packaged_manifest_hash,
    )
    shutil.copy2(generation_manifest_path, staged_root / "SOURCE-MANIFEST.json")
    shutil.copy2(runtime_manifest_path, staged_root / "RUNTIME-ASSET-MANIFEST.json")
    generation_manifest_hash = sha256(generation_manifest_path)
    runtime_manifest_hash = sha256(runtime_manifest_path)
    write_packaged_provenance(
        staged_root / "PROVENANCE.md",
        generation_manifest_hash,
        runtime_manifest_hash,
        runtime_manifest,
        packaged_manifest_hash,
        packaged_manifest,
    )
    write_asset_map(staged_root / "ASSET-MAP.md", len(names), len(packaged_assets))
    verify_staged_product(
        staged_root,
        names,
        packaged_assets,
        runtime_manifest,
        packaged_manifest,
        generation_manifest_hash,
        runtime_manifest_hash,
        packaged_manifest_hash,
    )


def install_staged_targets_with_rollback(
    product_root: Path, staged_root: Path, backup_root: Path
) -> None:
    """Install targets sequentially and roll them back after ordinary failures.

    The directory renames are per-target operations, not one crash-atomic catalog swap.
    A process or machine interruption can therefore leave recovery work in the backup tree.
    """
    target_names = (
        SKYLINE_DIRECTORY_NAME,
        "world-clocks.sqlite3",
        "ATTRIBUTION.json",
        "ATTRIBUTION.md",
        "SOURCE-MANIFEST.json",
        "RUNTIME-ASSET-MANIFEST.json",
        PACKAGED_MANIFEST_NAME,
        "PROVENANCE.md",
        "ASSET-MAP.md",
    )
    retired_names = ("Images",)
    backup_root.mkdir()
    moved_existing: list[str] = []
    installed: list[str] = []
    try:
        for name in (*retired_names, *target_names):
            current = product_root / name
            if current.exists():
                current.rename(backup_root / name)
                moved_existing.append(name)

        for name in target_names:
            staged = staged_root / name
            if not staged.exists():
                raise RuntimeError(f"Required staged target is missing: {staged}")
            staged.rename(product_root / name)
            installed.append(name)
    except BaseException as error:
        rollback_errors: list[str] = []
        for name in reversed(installed):
            current = product_root / name
            try:
                if current.exists():
                    current.rename(staged_root / name)
            except OSError as rollback_error:
                rollback_errors.append(f"new {name}: {rollback_error}")
        for name in reversed(moved_existing):
            backup = backup_root / name
            try:
                if backup.exists():
                    backup.rename(product_root / name)
            except OSError as rollback_error:
                rollback_errors.append(f"old {name}: {rollback_error}")
        if rollback_errors:
            raise RuntimeError(
                f"Promotion failed and rollback was incomplete; preserve {backup_root}. "
                + "; ".join(rollback_errors)
            ) from error
        raise


def promote(
    runtime_root: Path,
    generation_manifest_path: Path,
    product_root: Path,
    *,
    validate_only: bool = False,
) -> None:
    repository_root = Path(__file__).resolve().parent.parent
    expected_lexical_root = repository_root / "WorkTrail" / "Assets" / "WorldClocks"
    if is_reparse_point(expected_lexical_root):
        raise RuntimeError(f"Product directory must not be a reparse point: {expected_lexical_root}")
    expected_product_root = expected_lexical_root.resolve(strict=True)
    if not expected_product_root.is_relative_to(repository_root):
        raise RuntimeError("Resolved product directory escapes the repository.")

    runtime_input = runtime_root.absolute()
    if is_reparse_point(runtime_input):
        raise RuntimeError(f"Runtime directory must not be a reparse point: {runtime_input}")
    runtime_root = runtime_root.resolve(strict=True)
    generation_manifest_path = generation_manifest_path.resolve(strict=True)
    product_root = product_root.resolve(strict=True)
    if product_root != expected_product_root:
        raise RuntimeError(f"Refusing to modify unexpected product directory: {product_root}")
    reject_reparse_tree(runtime_root)
    reject_reparse_tree(product_root)

    generation_manifest = read_json(generation_manifest_path)
    names, additional_catalog_cities, assets, runtime_manifest, runtime_manifest_path = validate_runtime(
        runtime_root, generation_manifest, generation_manifest_path
    )
    database_path = product_root / "world-clocks.sqlite3"
    overlay_root = product_root / "Overlays"
    if not database_path.is_file() or not overlay_root.is_dir():
        raise RuntimeError("Packaged world-clock catalog is incomplete.")

    temporary_root = Path(
        tempfile.mkdtemp(prefix=".world-clock-watercolor-", dir=product_root.parent)
    )
    next_root = temporary_root / "WorldClocks.next"
    backup_root = temporary_root / "WorldClocks.backup"
    installed = False
    cleanup_warning: str | None = None
    try:
        build_staged_product(
            product_root,
            next_root,
            runtime_root,
            generation_manifest_path,
            runtime_manifest_path,
            names,
            additional_catalog_cities,
            assets,
            runtime_manifest,
        )

        if validate_only:
            print(
                json.dumps(
                    {
                        "runtimeRoot": str(runtime_root),
                        "productRoot": str(product_root),
                        "cityCount": len(names),
                        "assetCount": len(assets),
                        "stagedCatalogValidated": True,
                        "productModified": False,
                        "valid": True,
                    },
                    indent=2,
                )
            )
            return

        install_staged_targets_with_rollback(product_root, next_root, backup_root)
        installed = True

        try:
            shutil.rmtree(backup_root)
        except OSError as error:
            cleanup_warning = f"Promoted successfully; old catalog backup remains at {backup_root}: {error}"
            warnings.warn(cleanup_warning)
    finally:
        backup_has_files = backup_root.exists() and any(backup_root.iterdir())
        if cleanup_warning is None and (installed or not backup_has_files):
            shutil.rmtree(temporary_root, ignore_errors=True)

    print(
        json.dumps(
            {
                "productRoot": str(product_root),
                "cityCount": len(names),
                "assetCount": len(assets),
                "imageSource": "WorkTrail Urban Wash project-generated artwork",
                "perTargetRollback": True,
                "crashAtomic": False,
                "cleanupWarning": cleanup_warning,
            },
            indent=2,
        )
    )


def main() -> None:
    repository_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--runtime",
        type=Path,
        default=repository_root
        / "design"
        / "world-clocks"
        / "watercolor"
        / RUNTIME_DIRECTORY_NAME,
    )
    parser.add_argument(
        "--generation-manifest",
        type=Path,
        default=repository_root
        / "design"
        / "world-clocks"
        / "watercolor"
        / "generation-manifest-v1.json",
    )
    parser.add_argument(
        "--product-root",
        type=Path,
        default=repository_root / "WorkTrail" / "Assets" / "WorldClocks",
    )
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    promote(
        args.runtime,
        args.generation_manifest,
        args.product_root,
        validate_only=args.validate_only,
    )


if __name__ == "__main__":
    main()
