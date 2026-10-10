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

from __future__ import annotations

import hashlib
import json
import struct
from pathlib import Path

from PIL import Image, ImageCms, ImageFilter, PngImagePlugin


ROOT = Path(__file__).resolve().parent
SOURCE_DIR = ROOT / "source"
OUTPUT_DIR = ROOT / "output"
SOCIAL_PREVIEW_PATH = OUTPUT_DIR / "worktrail-social-preview-github-1280x640.png"

SOURCE_FILES = {
    "dark": SOURCE_DIR / "worktrail-recall-timeline-theme-dark-source.png",
    "light": SOURCE_DIR / "worktrail-recall-timeline-theme-light-source.png",
}

MASTER_SIZE = (3840, 1280)
README_SIZE = (2400, 800)
SQUARE_SIZE = (1024, 1024)


def srgb_profile() -> bytes:
    profile = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB"))
    return profile.tobytes()


def png_metadata(title: str, description: str) -> PngImagePlugin.PngInfo:
    info = PngImagePlugin.PngInfo()
    info.add_text("Title", title)
    info.add_text("Description", description)
    info.add_text("Software", "WorkTrail recall-timeline asset renderer")
    info.add_text("Provenance", "AI-generated source artwork; deterministic crop, color, and export refinement. No text wordmark.")
    return info


def save_rgba(image: Image.Image, path: Path, *, title: str, description: str) -> None:
    rgba = image.convert("RGBA")
    rgba.save(
        path,
        format="PNG",
        optimize=True,
        compress_level=9,
        dpi=(96, 96),
        icc_profile=srgb_profile(),
        pnginfo=png_metadata(title, description),
    )


def crop_to_three_by_one(source: Image.Image) -> Image.Image:
    crop_height = source.width // 3
    if crop_height > source.height:
        raise ValueError(f"Source is too wide to crop to 3:1: {source.size}")
    top = (source.height - crop_height) // 2
    return source.crop((0, top, source.width, top + crop_height))


def refined_art(theme: str) -> Image.Image:
    with Image.open(SOURCE_FILES[theme]) as source:
        cropped = crop_to_three_by_one(source.convert("RGB"))
        master = cropped.resize(MASTER_SIZE, Image.Resampling.LANCZOS)
    # A restrained pass restores edge definition after enlargement without changing the artwork.
    master = master.filter(ImageFilter.UnsharpMask(radius=1.2, percent=65, threshold=3))
    return master.convert("RGBA")


def square_mark(art: Image.Image) -> Image.Image:
    # The crop preserves the coral retrieval node, lifted page, and enough surrounding timeline context.
    left = 760
    top = 0
    box_size = 1280
    crop = art.crop((left, top, left + box_size, top + box_size))
    return crop.resize(SQUARE_SIZE, Image.Resampling.LANCZOS).filter(
        ImageFilter.UnsharpMask(radius=0.8, percent=45, threshold=3)
    )


def png_ihdr(path: Path) -> dict[str, int]:
    with path.open("rb") as stream:
        signature = stream.read(8)
        if signature != b"\x89PNG\r\n\x1a\n":
            raise ValueError(f"Not a PNG: {path}")
        length = struct.unpack(">I", stream.read(4))[0]
        chunk_type = stream.read(4)
        if length != 13 or chunk_type != b"IHDR":
            raise ValueError(f"Unexpected PNG header: {path}")
        width, height, bit_depth, color_type, _, _, _ = struct.unpack(">IIBBBBB", stream.read(13))
    return {
        "width": width,
        "height": height,
        "bit_depth": bit_depth,
        "color_type": color_type,
    }


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def render() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    outputs: list[Path] = []

    for theme in ("dark", "light"):
        art = refined_art(theme)
        readme_banner = art.resize(README_SIZE, Image.Resampling.LANCZOS)
        mark = square_mark(art)

        # Active app configuration still consumes these legacy filenames. Rename
        # them only when their configuration consumers migrate in the same change.
        generated = {
            OUTPUT_DIR / f"worktrail-recall-timeline-art-theme-{theme}-master-3840x1280.png": (
                art,
                f"WorkTrail recall timeline artwork, {theme} theme",
                "Art-only master with the selected page retrieval gesture.",
            ),
            OUTPUT_DIR / f"worktrail-recall-timeline-banner-theme-{theme}-master-3840x1280.png": (
                art,
                f"WorkTrail recall timeline banner, {theme} theme",
                "Art-only 3:1 banner with the selected page retrieval gesture.",
            ),
            OUTPUT_DIR / f"worktrail-recall-timeline-banner-theme-{theme}-readme-2400x800.png": (
                readme_banner,
                f"WorkTrail recall timeline README banner, {theme} theme",
                "Art-only GitHub README 3:1 banner with the selected page retrieval gesture.",
            ),
            OUTPUT_DIR / f"worktrail-recall-timeline-mark-theme-{theme}-1024x1024.png": (
                mark,
                f"WorkTrail recall timeline square mark, {theme} theme",
                "Square crop focused on the rediscovered-page gesture.",
            ),
        }

        for path, (image, title, description) in generated.items():
            save_rgba(image, path, title=title, description=description)
            outputs.append(path)

    dark_preview = Image.open(
        OUTPUT_DIR / "worktrail-recall-timeline-banner-theme-dark-readme-2400x800.png"
    ).convert("RGBA")
    light_preview = Image.open(
        OUTPUT_DIR / "worktrail-recall-timeline-banner-theme-light-readme-2400x800.png"
    ).convert("RGBA")
    preview = Image.new("RGBA", (2400, 1600), (255, 255, 255, 255))
    preview.alpha_composite(dark_preview, (0, 0))
    preview.alpha_composite(light_preview, (0, 800))
    preview_path = OUTPUT_DIR / "worktrail-recall-timeline-theme-pair-preview-2400x1600.png"
    save_rgba(
        preview,
        preview_path,
        title="WorkTrail recall timeline dark and light preview",
        description="Stacked preview of the selected dark and light README banners.",
    )
    outputs.append(preview_path)

    if not SOCIAL_PREVIEW_PATH.is_file():
        raise FileNotFoundError(f"Required WorkTrail social preview is missing: {SOCIAL_PREVIEW_PATH}")
    outputs.append(SOCIAL_PREVIEW_PATH)

    manifest = {
        "product": "WorkTrail",
        "concept": "Recall Timeline",
        "status": "selected direction, WorkTrail social preview added; legacy banner outputs retained pending configuration migration",
        "format": "PNG truecolor RGBA, 8 bits per channel; generated outputs embed an sRGB profile",
        "social_preview": {
            "file": SOCIAL_PREVIEW_PATH.relative_to(ROOT).as_posix(),
            "classification": "Illustrative promotional artwork generated with Codex ImageGen; not a product screenshot or UI depiction.",
            "source": "Approved ImageGen artifact retained outside the repository.",
            "derivative": "Size-only high-quality resize from 1774 x 887 to 1280 x 640; no text or compositing.",
        },
        "outputs": [],
    }
    for path in sorted(outputs):
        header = png_ihdr(path)
        if header["bit_depth"] != 8 or header["color_type"] != 6:
            raise ValueError(f"Expected 32-bit RGBA PNG, got {header}: {path}")
        manifest["outputs"].append(
            {
                "file": path.relative_to(ROOT).as_posix(),
                **header,
                "sha256": sha256(path),
            }
        )

    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    render()
