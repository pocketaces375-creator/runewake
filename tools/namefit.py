"""Auto-fit card names: the name NEVER escapes its safe zone."""
from PIL import ImageFont


def _balanced_split(words):
    assert len(words) > 1, "_balanced_split requires >1 word"
    best, bd = None, None
    for i in range(1, len(words)):
        a, b = " ".join(words[:i]), " ".join(words[i:])
        d = abs(len(a) - len(b))
        if bd is None or d < bd:
            best, bd = (a, b), d
    return list(best)  # type: ignore  # best is never None when len(words)>1


def fit_name(text, font_path, max_w, base, floor=None, hard_min=12):
    floor = floor if floor is not None else max(hard_min, int(base * 0.62))

    def width(t, sz):
        return ImageFont.truetype(font_path, sz).getlength(t)

    sz = base
    while sz > floor and width(text, sz) > max_w:
        sz -= 1
    if width(text, sz) <= max_w:
        return ImageFont.truetype(font_path, sz), [text], int(sz * 1.12)

    words = text.split()
    if len(words) > 1:
        lines = _balanced_split(words)
        sz = base - 2
        widest = max(lines, key=lambda t: width(t, sz))
        while sz > hard_min and width(widest, sz) > max_w:
            sz -= 1
        return ImageFont.truetype(font_path, sz), lines, int(sz * 1.08)

    while sz > hard_min and width(text, sz) > max_w:
        sz -= 1
    return ImageFont.truetype(font_path, sz), [text], int(sz * 1.12)