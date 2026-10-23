#!/bin/bash
# FABLE-098: find the four lobby paintings Trikzos chose between (war table, crossroads, summoning circle, arena gate)
# wherever they were kept on this machine, newest first. Prints "kind<TAB>path" lines.
for kind in "war*table" "crossroad" "summon" "arena*gate"; do
  find ~/runewake_art_archive ~/runewake_library ~/runewake ~/fable_tools /home/fictive/runewake/art_output -type f \
       \( -iname "*${kind}*.png" -o -iname "*${kind}*.webp" -o -iname "*${kind}*.jpg" \) -size +80k 2>/dev/null \
    | grep -viE 'thumb|_small|/\.godot/' | xargs -r ls -t 2>/dev/null | head -3 | sed "s|^|${kind}\t|"
done
