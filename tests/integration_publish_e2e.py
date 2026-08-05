#!/usr/bin/env python3
"""P6-09 End-to-end integration: publish with Python, verify with C# PackVerifier.

Proves the full cross-language integrity flow:
  1. Python publish.py builds a signed, hashed pack.
  2. The C# PackVerifier tool loads it, verifies the SHA-256 hash, and
     confirms a tampered copy is rejected.
"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE / "pipeline"))

from modules.publish import publish, verify_pack


def main() -> int:
    print("=" * 64)
    print("P6-09 End-to-End Integration Test (Python publish -> C# verify)")
    print("=" * 64)

    with tempfile.TemporaryDirectory() as tmpdir:
        work_dir = Path(tmpdir) / "work"
        content_dir = Path(tmpdir) / "content" / "packs"
        work_dir.mkdir(parents=True)

        cards = [
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
        (work_dir / "06_art.json").write_text(json.dumps(cards))
        (work_dir / "07_decisions.json").write_text(json.dumps([
            {"card_id": "vrd_c_root_warden", "action": "approved"},
            {"card_id": "emb_c_cinder_runner", "action": "approved"},
        ]))

        # 1. Python publish
        print("\n[1/3] Python publish...")
        result = publish(work_dir, "buried_age", content_dir)
        assert result["status"] == "published", f"Publish failed: {result}"
        print(f"       v{result['version']} hash={result['hash'][:16]}...")

        pack_path = content_dir / "buried_age.json"
        pack = json.loads(pack_path.read_text())
        assert verify_pack(pack), "Python verify_pack failed"
        print("       Python self-verify: OK")

        # 2. C# verify via PackVerifier
        print("\n[2/3] C# PackVerifier...")
        verifier = Path(__file__).resolve().parent.parent / "tools" / "PackVerifier"
        cmd = ["dotnet", "run", "--project", str(verifier), "--", str(pack_path)]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        print(f"       {proc.stdout.strip()}")
        assert proc.returncode == 0, f"C# verification failed:\n{proc.stderr}"
        assert "VERIFY_RESULT=True" in proc.stdout
        assert "TAMPER_REJECTED=True" in proc.stdout
        print("       C# verified pack + rejected tamper: OK")

        # 3. Confirm a tampered pack file is rejected by C# verifier
        print("\n[3/3] C# rejects tampered file...")
        tampered_path = Path(tmpdir) / "tampered.json"
        tampered = pack_path.read_text().replace("Root Warden", "ROOT_TAMPERED")
        tampered_path.write_text(tampered)
        proc2 = subprocess.run(cmd, capture_output=True, text=True)  # still points at original
        # Point at the tampered file instead:
        cmd2 = ["dotnet", "run", "--project", str(verifier), "--", str(tampered_path)]
        proc2 = subprocess.run(cmd2, capture_output=True, text=True)
        assert proc2.returncode == 1, f"Tampered pack unexpectedly accepted"
        print("       Tampered pack rejected (exit 1): OK")

    print("\n" + "=" * 64)
    print("INTEGRATION PASSED: Python publish -> C# hash verify -> tamper reject")
    print("=" * 64)
    return 0


if __name__ == "__main__":
    sys.exit(main())