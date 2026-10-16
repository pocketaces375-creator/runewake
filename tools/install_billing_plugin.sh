#!/usr/bin/env bash
# tools/install_billing_plugin.sh — FABLE-061
# Put the Godot Google Play Billing plugin (Billing Library 9) into client/addons and enable it, so an
# Android export can sell packs. Needs network (GitHub) and, if no prebuilt release is found, the Android
# SDK + JDK 17 to build it from source.
#
#   bash tools/install_billing_plugin.sh            install + enable
#   bash tools/install_billing_plugin.sh --disable  turn it off again (the export works without it;
#                                                   the shop then says packs come with the Play Store version)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLIENT="$ROOT/client"
NAME="GodotGooglePlayBilling"
TAG="${GPB_TAG:-3.3.0}"
DEST="$CLIENT/addons/$NAME"
PROJ="$CLIENT/project.godot"
LINE="enabled=PackedStringArray(\"res://addons/$NAME/plugin.cfg\")"

enable() {
  python3 - "$PROJ" "$NAME" <<'EOF'
import re, sys
p, name = sys.argv[1], sys.argv[2]
s = open(p).read()
entry = f'"res://addons/{name}/plugin.cfg"'
if "[editor_plugins]" in s:
    m = re.search(r"\[editor_plugins\]\s*\nenabled=PackedStringArray\((.*?)\)", s)
    if m and entry not in m.group(1):
        inner = m.group(1).strip()
        s = s[:m.start(1)] + (inner + ", " if inner else "") + entry + s[m.end(1):]
    elif not m:
        s = s.replace("[editor_plugins]", f"[editor_plugins]\n\nenabled=PackedStringArray({entry})", 1)
else:
    s = s.rstrip("\n") + f"\n\n[editor_plugins]\n\nenabled=PackedStringArray({entry})\n"
open(p, "w").write(s)
EOF
}

disable() {
  python3 - "$PROJ" "$NAME" <<'EOF'
import re, sys
p, name = sys.argv[1], sys.argv[2]
s = open(p).read()
entry = f'"res://addons/{name}/plugin.cfg"'
s = s.replace(", " + entry, "").replace(entry + ", ", "").replace(entry, "")
s = re.sub(r"\n*\[editor_plugins\]\s*\nenabled=PackedStringArray\(\)\s*", "\n", s)
open(p, "w").write(s)
EOF
}

if [ "${1:-}" = "--disable" ]; then disable; echo "billing plugin disabled"; exit 0; fi

if [ -f "$DEST/bin/release/$NAME-release.aar" ] && [ -f "$DEST/bin/debug/$NAME-debug.aar" ]; then
  echo "plugin already present ($DEST)"
else
  WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
  # 1. a prebuilt release zip (no Android build needed)
  URL=$(curl -fsSL -m 30 "https://api.github.com/repos/godot-sdk-integrations/godot-google-play-billing/releases/tags/$TAG" 2>/dev/null \
        | python3 -c "import json,sys; a=[x['browser_download_url'] for x in json.load(sys.stdin).get('assets',[]) if x['name'].endswith('.zip')]; print(a[0] if a else '')" 2>/dev/null || true)
  if [ -n "$URL" ] && curl -fsSL -m 120 -o "$WORK/p.zip" "$URL" && unzip -q -o "$WORK/p.zip" -d "$WORK/z"; then
    SRC=$(dirname "$(find "$WORK/z" -name plugin.cfg -path "*$NAME*" | head -1)")
    echo "prebuilt release: $URL"
  else
    # 2. build it from the tagged source
    echo "no prebuilt zip found — building $TAG from source"
    git clone -q --depth 1 --branch "$TAG" https://github.com/godot-sdk-integrations/godot-google-play-billing.git "$WORK/src"
    export ANDROID_HOME="${ANDROID_HOME:-/home/fictive/Android/Sdk}" ANDROID_SDK_ROOT="${ANDROID_HOME:-/home/fictive/Android/Sdk}"
    ( cd "$WORK/src" && ./gradlew -q assemble )
    SRC="$WORK/src/addons/$NAME"
  fi
  [ -f "$SRC/plugin.cfg" ] && [ -f "$SRC/bin/release/$NAME-release.aar" ] || { echo "plugin files not found after download/build"; exit 1; }
  rm -rf "$DEST"; mkdir -p "$CLIENT/addons"; cp -r "$SRC" "$DEST"
fi
enable
echo "billing plugin installed and enabled: $(grep -m1 version "$DEST/plugin.cfg")"
