#!/usr/bin/env python3
"""Add drops arrays to all encounter files per TASK-DROPS-DATA-1 spec."""
import json, os, sys

encounter_dir = "/home/fictive/runewake/content/encounters"
encounter_files = ["region_01_early.json", "region_01_mid.json", "region_01_late.json", "region_01_boss.json"]

def card_rarity(card_id):
    parts = card_id.split("_")
    prefix = parts[1] if len(parts) >= 2 else "c"
    return {"c": "C", "u": "U", "r": "R", "x": "M", "t": "M", "m": "M"}.get(prefix, "C")

DROP_RATES = {"C": 0.40, "U": 0.25, "R": 0.10, "M": 0.03}

BOSS_SIGNATURES = {
    "r1_warden_aelin": "vrd_r_bloomweaver",
    "r1_boss_warden_aelin": "dwn_r_sealing_light",
}

for fname in encounter_files:
    fpath = os.path.join(encounter_dir, fname)
    with open(fpath) as f:
        data = json.load(f)
    
    for enc in data["encounters"]:
        enc_id = enc["id"]
        deck = enc.get("deck", [])
        
        # Group deck cards by rarity
        by_rarity = {"C": [], "U": [], "R": [], "M": []}
        for cid in deck:
            by_rarity[card_rarity(cid)].append(cid)
        
        drops = []
        seen = set()
        
        for rarity, cards in by_rarity.items():
            rate = DROP_RATES[rarity]
            for cid in cards:
                if cid not in seen:
                    drops.append({"card_id": cid, "rate": rate})
                    seen.add(cid)
        
        # Boss signature override
        if enc_id in BOSS_SIGNATURES:
            sig_id = BOSS_SIGNATURES[enc_id]
            drops = [d for d in drops if d["card_id"] != sig_id]
            drops.insert(0, {"card_id": sig_id, "rate": 1.00})
        
        # Ensure at least 3 non-signature drops
        non_sig = [d for d in drops if d["rate"] < 1.0]
        if len(non_sig) < 3:
            for cid in deck:
                if cid not in seen and len([d for d in drops if d["rate"] < 1.0]) < 3:
                    drops.append({"card_id": cid, "rate": 0.10})
                    seen.add(cid)
        
        drops.sort(key=lambda d: (-d["rate"], d["card_id"]))
        enc["drops"] = drops
    
    with open(fpath, "w") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    
    print(f"✅ {fname}: {sum(len(e['drops']) for e in data['encounters'])} total drops across {len(data['encounters'])} encounters")

# Verify boss
with open(os.path.join(encounter_dir, "region_01_boss.json")) as f:
    boss_data = json.load(f)
for enc in boss_data["encounters"]:
    sig = next((d for d in enc["drops"] if d["rate"] == 1.0), None)
    print(f"  {enc['id']}: {len(enc['drops'])} drops" + (f", sig={sig['card_id']}" if sig else ""))

print("\nDone. All encounters now have drops arrays.")