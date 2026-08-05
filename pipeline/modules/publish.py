#!/usr/bin/env python3
"""P6-09: PUBLISH — Pack versioning, hashing, and publishing.

Reads approved cards from the review stage, builds a versioned content pack
with SHA-256 integrity hash, enforces immutable card identity, writes a
changelog, and produces a bundled fallback copy.

Usage:
    python -m pipeline.modules.publish --work-dir work/b_2026_ember_01 \\
        --set-id buried_age --content-dir content/packs
"""

import argparse
import hashlib
import json
import os
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent.parent  # pipeline/

# ── JSON canonicalisation (matched by C# ContentManager) ──────────────────────


def canonical_json(obj: Any) -> str:
    """Produce a canonical JSON string (sorted keys, compact, no trailing ws).

    This MUST produce byte-identical output to the C# Canonicalize() method
    in ContentManager.cs so that SHA-256 hashes computed on either side match.
    """
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_hex(text: str) -> str:
    """SHA-256 hex digest of a string's UTF-8 encoding."""
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


# ── Pack manifest helpers ────────────────────────────────────────────────────


def make_pack(
    set_id: str,
    version: int,
    cards: list[dict],
) -> dict[str, Any]:
    """Create a signed pack manifest with a SHA-256 hash.

    The hash is computed over the canonical JSON of the payload
    (set_id + version + cards) and stored in the 'hash' field.
    Verification: deserialise the pack, rebuild canonical JSON from
    the payload fields, recompute hash, compare.
    """
    # Build a content dict that excludes the hash field
    payload = {
        "set_id": set_id,
        "version": version,
        "cards": cards,
    }
    h = sha256_hex(canonical_json(payload))
    return {
        "set_id": set_id,
        "version": version,
        "hash": h,
        "cards": cards,
    }


def verify_pack(pack: dict) -> bool:
    """Verify that the pack's hash matches its content.

    Returns True if hash is valid, False otherwise.
    """
    declared = pack.get("hash", "")
    payload = {
        "set_id": pack.get("set_id", ""),
        "version": pack.get("version", 0),
        "cards": pack.get("cards", []),
    }
    expected = sha256_hex(canonical_json(payload))
    return declared == expected


def load_pack(path: Path) -> dict | None:
    """Load an existing pack from disk, or None if not found."""
    if not path.exists():
        return None
    with open(path) as f:
        data = json.load(f)
    if not verify_pack(data):
        print(f"[publish] WARNING: existing pack {path} has invalid hash — ignoring",
              file=sys.stderr)
        return None
    return data


# ── Changelog ─────────────────────────────────────────────────────────────────


def write_changelog_entry(
    changelog_path: Path,
    version: int,
    new_card_ids: list[str],
    changed_card_ids: list[tuple[str, str]],
) -> None:
    """Append a changelog entry for a new version.

    Args:
        new_card_ids: IDs of cards added in this version.
        changed_card_ids: List of (card_id, description) tuples for cards whose
            identity was carried forward but content changed (balance patch).
    """
    changelog: list[dict] = []
    if changelog_path.exists():
        with open(changelog_path) as f:
            changelog = json.load(f)

    entry: dict[str, Any] = {
        "version": version,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "added": sorted(new_card_ids),
    }
    if changed_card_ids:
        entry["changed"] = [
            {"card_id": cid, "description": desc}
            for cid, desc in sorted(changed_card_ids)
        ]

    changelog.append(entry)
    with open(changelog_path, "w") as f:
        json.dump(changelog, f, indent=2)
    print(f"[publish] Changelog updated: version {version}, "
          f"{len(new_card_ids)} new, {len(changed_card_ids)} changed")


# ── Publish ───────────────────────────────────────────────────────────────────


def find_approved_ids(decisions_path: Path) -> set[str]:
    """Read the decision log and return IDs of all approved cards."""
    if not decisions_path.exists():
        print(f"[publish] No decisions file at {decisions_path}", file=sys.stderr)
        return set()

    with open(decisions_path) as f:
        decisions = json.load(f)

    approved: set[str] = set()
    for d in decisions:
        if d.get("action") == "approved":
            approved.add(d.get("card_id", ""))
    return approved


def build_id_map(cards: list[dict]) -> dict[str, dict]:
    """Build a quick lookup from card id → card dict."""
    result: dict[str, dict] = {}
    for c in cards:
        cid = c.get("id")
        if cid:
            result[cid] = c
    return result


def check_card_equal(a: dict, b: dict) -> bool:
    """Compare two card dicts by canonical JSON for equality."""
    return canonical_json(a) == canonical_json(b)


def publish(
    work_dir: Path,
    set_id: str,
    content_dir: Path,
) -> dict[str, Any]:
    """Run the publish stage: build versioned pack from approved cards.

    Returns a summary dict.
    """
    # Ensure content dir exists
    content_dir.mkdir(parents=True, exist_ok=True)

    # Paths
    art_path = work_dir / "06_art.json"
    decisions_path = work_dir / DECISIONS_FILE

    if not art_path.exists():
        print(f"[publish] No art file at {art_path}", file=sys.stderr)
        return {"status": "error", "reason": "no_art_file"}

    # Load all cards
    with open(art_path) as f:
        raw = json.load(f)
    all_cards = raw if isinstance(raw, list) else [raw]

    # Load approved IDs
    if not decisions_path.exists():
        print(f"[publish] No decisions file — cannot determine approved cards",
              file=sys.stderr)
        return {"status": "error", "reason": "no_decisions"}

    approved_ids = find_approved_ids(decisions_path)
    if not approved_ids:
        print("[publish] No approved cards found", file=sys.stderr)
        return {"status": "skipped", "reason": "nothing_approved"}

    approved_cards = [
        c for c in all_cards if c.get("id") in approved_ids
    ]

    # Load existing pack (if any)
    pack_path = content_dir / PACK_FILENAME_TEMPLATE.format(set_id=set_id)
    bundled_path = content_dir / BUNDLED_FILENAME_TEMPLATE.format(set_id=set_id)
    changelog_path = content_dir / CHANGELOG_FILENAME_TEMPLATE.format(set_id=set_id)

    existing = load_pack(pack_path)
    existing_cards: list[dict] = existing.get("cards", []) if existing else []
    current_version: int = existing.get("version", 0) if existing else 0
    existing_map = build_id_map(existing_cards)

    # Enforce immutable identity
    new_card_ids: list[str] = []
    changed_card_ids: list[tuple[str, str]] = []
    final_cards: list[dict] = list(existing_cards)  # start with existing cards

    for card in approved_cards:
        cid = card.get("id", "")
        if not cid:
            continue

        if cid in existing_map:
            # Card exists already — check identity immutability
            existing_card = existing_map[cid]
            if check_card_equal(existing_card, card):
                # Identical — skip silently (idempotent re-publish)
                continue
            # Content changed — enforce version bump
            old_name = existing_card.get("name", "?")
            new_name = card.get("name", "?")
            desc = (
                f"Card '{new_name}' ({cid}): content changed "
                f"(stat/ability update, see card JSON for details)"
            )
            changed_card_ids.append((cid, desc))
            # Replace the card in the final list (moved to new version)
            for i, ec in enumerate(final_cards):
                if ec.get("id") == cid:
                    final_cards[i] = card
                    break
        else:
            # New card — append
            new_card_ids.append(cid)
            final_cards.append(card)

    # If nothing changed, no-op
    if not new_card_ids and not changed_card_ids:
        print("[publish] No new or changed cards — nothing to publish")
        return {
            "status": "skipped",
            "reason": "no_changes",
            "version": current_version,
            "set_id": set_id,
        }

    # Increment version
    new_version = current_version + 1

    # Build pack manifest with hash
    pack = make_pack(set_id, new_version, final_cards)

    # Write pack
    with open(pack_path, "w") as f:
        json.dump(pack, f, indent=2)
    print(f"[publish] Written pack v{new_version}: {pack_path} ({len(final_cards)} cards)")

    # Write bundled fallback copy
    with open(bundled_path, "w") as f:
        json.dump(pack, f, indent=2)
    print(f"[publish] Written bundled fallback: {bundled_path}")

    # Append changelog
    write_changelog_entry(changelog_path, new_version, new_card_ids, changed_card_ids)

    return {
        "status": "published",
        "version": new_version,
        "set_id": set_id,
        "total_cards": len(final_cards),
        "new_cards": len(new_card_ids),
        "changed_cards": len(changed_card_ids),
        "hash": pack["hash"],
    }


# ── Pack format constants ────────────────────────────────────────────────────

DECISIONS_FILE = "07_decisions.json"
PACK_FILENAME_TEMPLATE = "{set_id}.json"
BUNDLED_FILENAME_TEMPLATE = "{set_id}.bundled.json"
CHANGELOG_FILENAME_TEMPLATE = "{set_id}.changelog.json"


# ── CLI ───────────────────────────────────────────────────────────────────────


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — PUBLISH stage",
    )
    parser.add_argument(
        "--work-dir", required=True,
        help="Work directory containing 06_art.json and 07_decisions.json",
    )
    parser.add_argument(
        "--set-id", required=True,
        help="Content set identifier, e.g. 'buried_age'",
    )
    parser.add_argument(
        "--content-dir", default="content/packs",
        help="Output directory for pack files (default: content/packs)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    work_dir = Path(args.work_dir)
    if not work_dir.exists():
        print(f"[publish] Work directory not found: {work_dir}", file=sys.stderr)
        return 1

    content_dir = Path(args.content_dir)

    result = publish(work_dir, args.set_id, content_dir)

    if result.get("status") == "error":
        print(f"[publish] FAILED: {result.get('reason')}", file=sys.stderr)
        return 1

    print(f"[publish] {result.get('status').upper()}: "
          f"set={result.get('set_id')} "
          f"v{result.get('version')} "
          f"hash={result.get('hash', '?')[:16]}...")
    return 0


if __name__ == "__main__":
    sys.exit(main())