# FABLE-097: after the generator - the corner coins become rune medallions, and the plate is sized for the game.
import sys
from pathlib import Path
from PIL import Image
key, src, out, screens = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4])
im = Image.open(src).convert("RGB").resize((1376, 768), Image.LANCZOS)
settings = Image.open(screens / "SettingsScene_ui.webp").convert("RGB")   # the frame whose rune medallions every screen wears
if key == "RewardsScene":
    sys.path.insert(0, str(Path(__file__).parent)); import clean_plates
    im = clean_plates.medallions(im, settings)
elif key == "DeckBuilderScene":
    tmp = out.with_suffix(".tmp.png"); im.save(tmp)
    import subprocess; subprocess.run([sys.executable, str(Path(__file__).parent / "deckforge_medallions_096.py"), str(tmp), str(screens / "SettingsScene_ui.webp"), str(tmp)], check=True)
    im = Image.open(tmp).convert("RGB"); tmp.unlink()
im.save(out, quality=92)
print("finished", key, out)
