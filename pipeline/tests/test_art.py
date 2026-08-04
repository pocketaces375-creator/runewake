#!/usr/bin/env python3
"""Tests for pipeline/modules/art.py — P6-07 Art module."""

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch, MagicMock

# Add pipeline to path
HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.art import (
    STRATUM_STYLES,
    STRATUM_FALLBACK_COLORS,
    STRATUM_GLYPH,
    DEFAULT_MODEL,
    MIP_LEVELS,
    build_prompt,
    generate_fallback,
    generate_image,
    save_image,
    _slugify,
    main,
)


# ── Helpers ───────────────────────────────────────────────────────────────────


def make_card(name="Test Card", strata="EMBER", prompt="a molten forge"):
    return {
        "id": "emb_c_test_card",
        "set": "buried_age",
        "name": name,
        "strata": strata,
        "type": "CREATURE",
        "rarity": "COMMON",
        "cost": 3,
        "art": {"prompt": prompt},
    }


def _test_image_bytes() -> bytes:
    """Return a small valid PNG/JPEG as bytes for save_image tests."""
    from PIL import Image
    import io
    img = Image.new("RGB", (1024, 1024), color=(10, 20, 30))
    buf = io.BytesIO()
    img.save(buf, "PNG")
    return buf.getvalue()


# ── Stratum style tests ───────────────────────────────────────────────────────


def test_all_strata_have_style_prefix():
    """Every stratum must have a locked style prefix."""
    for strata in ["VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN"]:
        assert strata in STRATUM_STYLES, f"missing style for {strata}"
        assert len(STRATUM_STYLES[strata]) > 20, f"style too short for {strata}"


def test_all_strata_have_fallback_color():
    """Every stratum must have a fallback colour."""
    for strata in ["VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN"]:
        assert strata in STRATUM_FALLBACK_COLORS
        color = STRATUM_FALLBACK_COLORS[strata]
        assert len(color) == 3
        assert all(0 <= c <= 255 for c in color)


def test_all_strata_have_glyph():
    """Every stratum must have a rune glyph."""
    for strata in ["VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN"]:
        assert strata in STRATUM_GLYPH
        assert STRATUM_GLYPH[strata]


# ── build_prompt tests ────────────────────────────────────────────────────────


def test_build_prompt_combines_style_and_card():
    """Prompt should be style prefix + card-specific art.prompt."""
    card = make_card(name="Lava Serpent", strata="EMBER", prompt="a serpent made of lava")
    prompt = build_prompt(card)
    assert prompt.startswith(STRATUM_STYLES["EMBER"])
    assert "serpent made of lava" in prompt


def test_build_prompt_missing_art_prompt():
    """Prompt should still work with no card-specific art.prompt."""
    card = make_card(name="Root Warden", strata="VERDANT")
    card["art"] = {}
    prompt = build_prompt(card)
    assert prompt.startswith(STRATUM_STYLES["VERDANT"])
    assert "verdant" in prompt.lower()


def test_build_prompt_unknown_strata_uses_default():
    """Unknown strata should fall back to VERDANT style."""
    card = make_card(strata="MYSTERY")
    prompt = build_prompt(card)
    assert prompt.startswith(STRATUM_STYLES["VERDANT"])


# ── slugify tests ─────────────────────────────────────────────────────────────


def test_slugify_basic():
    assert _slugify("Lava Serpent") == "lava_serpent"


def test_slugify_strips_punctuation():
    assert _slugify("Root Warden's Might") == "root_wardens_might"


# ── save_image tests ──────────────────────────────────────────────────────────


def test_save_image_writes_mip_levels():
    """save_image should write WebP files at each mip level."""
    with tempfile.TemporaryDirectory() as tmp:
        art_dir = Path(tmp)
        assets = save_image(_test_image_bytes(), "EMBER", "Lava Serpent", art_dir)
        assert str(MIP_LEVELS[0]) in assets
        assert str(MIP_LEVELS[1]) in assets
        for mip, path in assets.items():
            p = Path(path)
            assert p.exists(), f"missing mip {mip}"
            assert p.suffix == ".webp"
            # Verify the image actually opens and has the right dimensions
            from PIL import Image
            img = Image.open(p)
            assert img.size[0] == int(mip), f"mip {mip} has wrong width {img.size}"


# ── generate_image tests ──────────────────────────────────────────────────────


def test_generate_image_returns_b64():
    """Should decode b64_json responses."""
    import base64
    fake_bytes = b"fake-image-bytes"
    mock_resp = MagicMock()
    mock_resp.json.return_value = {"data": [{"b64_json": base64.b64encode(fake_bytes).decode()}]}
    with patch("modules.art.requests.post", return_value=mock_resp) as mock_post:
        result = generate_image("test prompt", "test-key")
    assert result == fake_bytes
    mock_post.assert_called_once()
    _, kwargs = mock_post.call_args
    assert kwargs["json"]["model"] == DEFAULT_MODEL
    assert kwargs["json"]["size"] == "1024x1024"


def test_generate_image_returns_url():
    """Should fetch and return image bytes from a URL response."""
    fake_bytes = b"fake-url-image"
    mock_create = MagicMock()
    mock_create.json.return_value = {"data": [{"url": "https://example.com/img.png"}]}
    mock_download = MagicMock()
    mock_download.content = fake_bytes
    with patch("modules.art.requests.post", return_value=mock_create) as mock_post, \
         patch("modules.art.requests.get", return_value=mock_download) as mock_get:
        result = generate_image("test prompt", "test-key")
    assert result == fake_bytes
    mock_get.assert_called_once()


def test_generate_image_http_error_returns_none():
    """Should return None and not raise on API failure."""
    import requests
    mock_resp = MagicMock()
    mock_resp.raise_for_status.side_effect = requests.HTTPError("HTTP 500")
    mock_resp.status_code = 500
    with patch("modules.art.requests.post", return_value=mock_resp):
        result = generate_image("test prompt", "test-key")
    assert result is None


# ── generate_fallback tests ───────────────────────────────────────────────────


def test_generate_fallback_creates_files():
    """Fallback should create WebP files at each mip level."""
    with tempfile.TemporaryDirectory() as tmp:
        art_dir = Path(tmp)
        assets = generate_fallback("EMBER", "Lava Serpent", art_dir)
        assert str(MIP_LEVELS[0]) in assets
        for mip, path in assets.items():
            p = Path(path)
            assert p.exists()
            assert p.suffix == ".webp"
            from PIL import Image
            img = Image.open(p)
            assert img.size[0] == int(mip)


def test_generate_fallback_unknown_strata():
    """Unknown strata should use a default colour."""
    with tempfile.TemporaryDirectory() as tmp:
        assets = generate_fallback("MYSTERY", "Test", Path(tmp))
        assert str(MIP_LEVELS[0]) in assets


# ── main() tests ──────────────────────────────────────────────────────────────


def _write_input(tmp: str, cards) -> Path:
    p = Path(tmp) / "05_deduplicated.json"
    p.write_text(json.dumps(cards))
    return p


def test_main_skip_api_generates_fallbacks():
    """--skip-api should produce fallback art for every card."""
    with tempfile.TemporaryDirectory() as tmp:
        input_path = _write_input(tmp, [make_card(), make_card(name="Lava Serpent")])
        work_dir = Path(tmp) / "work"
        code = main([
            "--input", str(input_path),
            "--work-dir", str(work_dir),
            "--skip-api",
        ])
        assert code == 0
        out_path = work_dir / "06_art.json"
        assert out_path.exists()
        cards = json.loads(out_path.read_text())
        assert len(cards) == 2
        for card in cards:
            assert card["art"]["fallback"] is True
            assert card["art"]["asset"]
            assert card["art"]["mips"]
        # verify art dir has webp files
        art_files = list((work_dir / "art").glob("*.webp"))
        assert len(art_files) >= 4  # 2 cards x 2 mips


def test_main_input_not_found():
    """Should return 1 if input file doesn't exist."""
    with tempfile.TemporaryDirectory() as tmp:
        code = main([
            "--input", str(Path(tmp) / "missing.json"),
            "--work-dir", str(Path(tmp) / "work"),
            "--skip-api",
        ])
        assert code == 1


def test_main_no_api_key_returns_1():
    """Without --skip-api and no key, should return 1."""
    with tempfile.TemporaryDirectory() as tmp:
        input_path = _write_input(tmp, [make_card()])
        with patch.dict("os.environ", {}, clear=True):
            code = main([
                "--input", str(input_path),
                "--work-dir", str(Path(tmp) / "work"),
            ])
        assert code == 1


def test_main_api_failure_uses_fallback():
    """If the API fails, cards should still pass with fallback art."""
    with tempfile.TemporaryDirectory() as tmp:
        input_path = _write_input(tmp, [make_card()])
        work_dir = Path(tmp) / "work"
        with patch("modules.art.generate_image", return_value=None):
            code = main([
                "--input", str(input_path),
                "--work-dir", str(work_dir),
                "--api-key", "test-key",
            ])
        assert code == 0
        cards = json.loads((work_dir / "06_art.json").read_text())
        assert len(cards) == 1
        assert cards[0]["art"]["fallback"] is True


def test_main_api_success():
    """On successful API call, cards should get real (non-fallback) assets."""
    with tempfile.TemporaryDirectory() as tmp:
        input_path = _write_input(tmp, [make_card()])
        work_dir = Path(tmp) / "work"
        with patch("modules.art.generate_image", return_value=_test_image_bytes()):
            code = main([
                "--input", str(input_path),
                "--work-dir", str(work_dir),
                "--api-key", "test-key",
            ])
        assert code == 0
        cards = json.loads((work_dir / "06_art.json").read_text())
        assert len(cards) == 1
        assert cards[0]["art"]["fallback"] is False
        assert cards[0]["art"]["asset"]