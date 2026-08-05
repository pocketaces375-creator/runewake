"""Tests for pipeline/modules/publish.py — pack versioning, hashing, and publishing."""

import hashlib
import json
from pathlib import Path

from modules.publish import (
    canonical_json,
    sha256_hex,
    make_pack,
    verify_pack,
    load_pack,
    publish,
    find_approved_ids,
    check_card_equal,
)

# ── Fixtures ──────────────────────────────────────────────────────────────────


def sample_cards() -> list[dict]:
    return [
        {
            "id": "vrd_c_root_warden",
            "name": "Root Warden",
            "strata": "VERDANT",
            "type": "CREATURE",
            "rarity": "COMMON",
            "cost": 3,
            "attack": 2,
            "vigor": 4,
            "keywords": ["GUARD"],
            "power_score": 7.1,
            "content_version": 1,
        },
        {
            "id": "emb_c_cinder_runner",
            "name": "Cinder Runner",
            "strata": "EMBER",
            "type": "CREATURE",
            "rarity": "COMMON",
            "cost": 2,
            "attack": 3,
            "vigor": 1,
            "keywords": ["SWIFT"],
            "power_score": 3.0,
            "content_version": 1,
        },
    ]


# ── Canonical JSON tests ──────────────────────────────────────────────────────


def test_canonical_json_sorts_keys():
    """Canonical JSON should have sorted keys and compact separators."""
    result = canonical_json({"b": 2, "a": 1})
    assert result == '{"a":1,"b":2}', f"Got: {result}"


def test_canonical_json_compact():
    """Canonical JSON should have no whitespace."""
    result = canonical_json({"x": [1, 2, 3]})
    assert " " not in result


def test_canonical_json_stable():
    """Same input should produce identical output."""
    obj = {"cards": sample_cards(), "version": 1, "set_id": "buried_age"}
    a = canonical_json(obj)
    b = canonical_json(obj)
    assert a == b


def test_canonical_json_nested():
    """Nested dicts should also be sorted."""
    result = canonical_json({"z": {"b": 2, "a": 1}, "a": 1})
    assert result == '{"a":1,"z":{"a":1,"b":2}}'


# ── SHA-256 tests ────────────────────────────────────────────────────────────


def test_sha256_hex_known():
    """Known input should produce known output."""
    expected = hashlib.sha256(b"hello").hexdigest()
    assert sha256_hex("hello") == expected


# ── make_pack / verify_pack tests ─────────────────────────────────────────────


def test_make_pack_contains_hash():
    """Pack manifest should have a non-empty hash field."""
    cards = sample_cards()
    pack = make_pack("buried_age", 1, cards)
    assert "hash" in pack
    assert len(pack["hash"]) == 64
    assert pack["set_id"] == "buried_age"
    assert pack["version"] == 1
    assert len(pack["cards"]) == 2


def test_make_pack_hash_deterministic():
    """Identical inputs should produce identical hashes."""
    cards = sample_cards()
    a = make_pack("buried_age", 1, cards)
    b = make_pack("buried_age", 1, cards)
    assert a["hash"] == b["hash"]


def test_make_pack_hash_changes_with_content():
    """Different cards should produce different hashes."""
    cards_a = sample_cards()
    cards_b = sample_cards()
    cards_b[0]["name"] = "Different Name"
    a = make_pack("buried_age", 1, cards_a)
    b = make_pack("buried_age", 1, cards_b)
    assert a["hash"] != b["hash"]


def test_make_pack_hash_changes_with_version():
    """Different version should produce different hash."""
    cards = sample_cards()
    a = make_pack("buried_age", 1, cards)
    b = make_pack("buried_age", 2, cards)
    assert a["hash"] != b["hash"]


def test_verify_pack_valid():
    """A valid pack should verify successfully."""
    pack = make_pack("buried_age", 1, sample_cards())
    assert verify_pack(pack) is True


def test_verify_pack_tampered_hash():
    """A pack with a tampered hash should fail verification."""
    pack = make_pack("buried_age", 1, sample_cards())
    pack["hash"] = "0" * 64
    assert verify_pack(pack) is False


def test_verify_pack_tampered_card():
    """A pack with a tampered card should fail verification."""
    pack = make_pack("buried_age", 1, sample_cards())
    pack["cards"][0]["power_score"] = 99.9
    assert verify_pack(pack) is False


def test_verify_pack_tampered_version():
    """A pack with a tampered version should fail."""
    pack = make_pack("buried_age", 1, sample_cards())
    pack["version"] = 999
    assert verify_pack(pack) is False


# ── load_pack tests ──────────────────────────────────────────────────────────


def test_load_pack_not_found(tmp_path):
    """Loading a non-existent pack should return None."""
    result = load_pack(tmp_path / "nonexistent.json")
    assert result is None


def test_load_pack_valid(tmp_path):
    """Loading a valid pack should return its contents."""
    pack = make_pack("buried_age", 1, sample_cards())
    path = tmp_path / "buried_age.json"
    with open(path, "w") as f:
        json.dump(pack, f)
    result = load_pack(path)
    assert result is not None
    assert result["version"] == 1


def test_load_pack_tampered(tmp_path):
    """Loading a tampered pack should return None with a warning."""
    pack = make_pack("buried_age", 1, sample_cards())
    pack["cards"] = []
    path = tmp_path / "buried_age.json"
    with open(path, "w") as f:
        json.dump(pack, f)
    result = load_pack(path)
    assert result is None


# ── find_approved_ids tests ───────────────────────────────────────────────────


def test_find_approved_ids(tmp_path):
    """Should return set of approved card IDs."""
    decisions = [
        {"card_id": "vrd_c_root_warden", "action": "approved"},
        {"card_id": "emb_c_cinder_runner", "action": "rejected", "reason": "BALANCE"},
        {"card_id": "hol_c_gravewrit_thrall", "action": "approved"},
    ]
    path = tmp_path / "07_decisions.json"
    with open(path, "w") as f:
        json.dump(decisions, f)
    ids = find_approved_ids(path)
    assert ids == {"vrd_c_root_warden", "hol_c_gravewrit_thrall"}


def test_find_approved_ids_no_file(tmp_path):
    """Missing decision file should return empty set."""
    ids = find_approved_ids(tmp_path / "nope.json")
    assert ids == set()


# ── check_card_equal tests ────────────────────────────────────────────────────


def test_check_card_equal():
    """Identical cards should be equal."""
    a = {"id": "xyz", "name": "Test", "cost": 3}
    b = {"cost": 3, "name": "Test", "id": "xyz"}
    assert check_card_equal(a, b)


def test_check_card_not_equal():
    """Different cards should not be equal."""
    a = {"id": "xyz", "name": "Test"}
    b = {"id": "xyz", "name": "Changed"}
    assert not check_card_equal(a, b)


# ── Integration: publish end-to-end ───────────────────────────────────────────


def test_publish_new_set(tmp_path):
    """Publishing a new set with approved cards should create a pack."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    # Write 06_art.json
    cards = sample_cards()
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards, f)

    # Write decision log: approve both
    decisions = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
        {"card_id": "emb_c_cinder_runner", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions, f)

    result = publish(work_dir, "buried_age", content_dir)
    assert result["status"] == "published"
    assert result["version"] == 1
    assert result["new_cards"] == 2
    assert result["changed_cards"] == 0
    assert len(result["hash"]) == 64

    # Verify pack file exists and is valid
    pack_path = content_dir / "buried_age.json"
    assert pack_path.exists()
    loaded = load_pack(pack_path)
    assert loaded is not None
    assert loaded["version"] == 1
    assert len(loaded["cards"]) == 2

    # Verify bundled exists
    bundled_path = content_dir / "buried_age.bundled.json"
    assert bundled_path.exists()

    # Verify changelog
    changelog_path = content_dir / "buried_age.changelog.json"
    assert changelog_path.exists()
    with open(changelog_path) as f:
        changelog = json.load(f)
    assert len(changelog) == 1
    assert changelog[0]["version"] == 1
    assert len(changelog[0]["added"]) == 2


def test_publish_incremental_add(tmp_path):
    """Publishing again with new cards should increment version."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    # First publish: card A only
    cards_v1 = [sample_cards()[0]]
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards_v1, f)
    decisions_v1 = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions_v1, f)
    result1 = publish(work_dir, "buried_age", content_dir)
    assert result1["version"] == 1
    assert result1["new_cards"] == 1

    # Second publish: card B (keep card A in art file, approve card B)
    cards_v2 = sample_cards()  # both cards
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards_v2, f)
    decisions_v2 = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
        {"card_id": "emb_c_cinder_runner", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions_v2, f)
    result2 = publish(work_dir, "buried_age", content_dir)
    assert result2["status"] == "published"
    assert result2["version"] == 2
    assert result2["new_cards"] == 1  # only card B is new
    assert result2["changed_cards"] == 0

    # Verify pack v2 has both cards
    pack = load_pack(content_dir / "buried_age.json")
    assert pack is not None
    assert pack["version"] == 2
    assert len(pack["cards"]) == 2

    # Verify changelog has both entries
    with open(content_dir / "buried_age.changelog.json") as f:
        cl = json.load(f)
    assert len(cl) == 2
    assert cl[1]["version"] == 2
    assert cl[1]["added"] == ["emb_c_cinder_runner"]


def test_publish_no_changes(tmp_path):
    """Re-publishing with same cards should be a no-op."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    cards = sample_cards()
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards, f)
    decisions = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
        {"card_id": "emb_c_cinder_runner", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions, f)

    # Publish v1
    publish(work_dir, "buried_age", content_dir)

    # Publish again with same data
    result = publish(work_dir, "buried_age", content_dir)
    assert result["status"] == "skipped"
    assert result["reason"] == "no_changes"


def test_publish_immutability_enforced(tmp_path):
    """Changing an existing card should record a changelog, not silently edit."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    card = sample_cards()[0]
    cards_v1 = [card]
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards_v1, f)
    decisions_v1 = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions_v1, f)

    # Publish v1
    publish(work_dir, "buried_age", content_dir)

    # Change the card in the art file
    changed_card = dict(card)
    changed_card["vigor"] = 5  # was 4
    cards_v2 = [changed_card]
    with open(work_dir / "06_art.json", "w") as f:
        json.dump(cards_v2, f)
    decisions_v2 = [
        {"card_id": "vrd_c_root_warden", "action": "approved", "timestamp": "2026-01-01T00:00:00Z"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions_v2, f)

    # Publish v2 — should record change in changelog, not silently edit
    result = publish(work_dir, "buried_age", content_dir)
    assert result["version"] == 2
    assert result["new_cards"] == 0
    assert result["changed_cards"] == 1

    # Verify the changelog recorded the change
    with open(content_dir / "buried_age.changelog.json") as f:
        cl = json.load(f)
    assert len(cl) == 2
    assert cl[1]["version"] == 2
    assert len(cl[1]["changed"]) == 1
    assert cl[1]["changed"][0]["card_id"] == "vrd_c_root_warden"
    assert "content changed" in cl[1]["changed"][0]["description"]


def test_publish_no_approved(tmp_path):
    """No approved cards should result in skipped."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    with open(work_dir / "06_art.json", "w") as f:
        json.dump(sample_cards(), f)
    decisions = [
        {"card_id": "vrd_c_root_warden", "action": "rejected", "reason": "BALANCE"},
    ]
    with open(work_dir / "07_decisions.json", "w") as f:
        json.dump(decisions, f)

    result = publish(work_dir, "buried_age", content_dir)
    assert result["status"] == "skipped"
    assert result["reason"] == "nothing_approved"


def test_publish_missing_decisions(tmp_path):
    """Missing decisions file should error."""
    work_dir = tmp_path / "work"
    content_dir = tmp_path / "content" / "packs"
    work_dir.mkdir(parents=True)

    with open(work_dir / "06_art.json", "w") as f:
        json.dump(sample_cards(), f)

    result = publish(work_dir, "buried_age", content_dir)
    assert result["status"] == "error"


def test_canonical_round_trip():
    """Canonical JSON should produce consistent hash across encode-decode."""
    obj = {"a": [1, {"c": 3, "b": 2}], "z": "test"}
    canonical = canonical_json(obj)
    # Round-trip through JSON.parse
    rebuilt = json.loads(canonical)
    assert canonical_json(rebuilt) == canonical