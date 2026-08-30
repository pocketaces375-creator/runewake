"""9-slice a generated border frame to ANY band thickness.

The generative model ignores "make the band thin" instructions — measured
bands came back at ~16% when 5% was asked for. So stop asking: detect the
painted band, slice it 9 ways (4 corners, 4 edges), and rebuild the frame
at whatever band thickness we choose. Corners keep their aspect, edges
stretch along their length only. Band thickness becomes a number we control,
not a dice roll.

Usage:
    from nineslice import nineslice_frame, BAND_FRAC
    border, (x0, y0, x1, y1) = nineslice_frame(frame_path, out_size)
    art_window = (x0, y0, x1, y1)  # paste card art here
    canvas.alpha_composite(border)  # layer border on top
"""
from PIL import Image
from detect_v2 import detect_window_v2

BAND_FRAC = 0.08  # band = BAND_FRAC * card_width


def nineslice_frame(frame_path, out_size, band_px=None, src_size=(832, 1216)):
    """Rebuild the border at out_size with a band exactly band_px thick.

    Returns (border_rgba, (x0, y0, x1, y1)) — the latter is the art window
    in out_size coordinates (paste card art here, then composite border over).
    Returns None if the window cannot be detected in the source frame.
    """
    if band_px is None:
        band_px = int(out_size[0] * BAND_FRAC)
    b = band_px

    src = Image.open(frame_path).convert("RGB").resize(src_size, Image.LANCZOS)
    det = detect_window_v2(src)
    if det is None:
        return None
    _, (wx0, wy0, wx1, wy1) = det

    L = wx0                        # left band width in source
    T = wy0                        # top band height in source
    R = src.width - wx1            # right band width in source
    B_val = src.height - wy1       # bottom band height in source

    # Extract the 9 patches
    tl = src.crop((wx0 - L, wy0 - T, wx0, wy0))
    tr = src.crop((wx1, wy0 - T, wx1 + R, wy0))
    bl = src.crop((wx0 - L, wy1, wx0, wy1 + B_val))
    br = src.crop((wx1, wy1, wx1 + R, wy1 + B_val))
    top = src.crop((wx0, wy0 - T, wx1, wy0))
    bot = src.crop((wx0, wy1, wx1, wy1 + B_val))
    lef = src.crop((wx0 - L, wy0, wx0, wy1))
    rig = src.crop((wx1, wy0, wx1 + R, wy1))

    W, H = out_size
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.paste(tl.resize((b, b), Image.LANCZOS), (0, 0))
    out.paste(tr.resize((b, b), Image.LANCZOS), (W - b, 0))
    out.paste(bl.resize((b, b), Image.LANCZOS), (0, H - b))
    out.paste(br.resize((b, b), Image.LANCZOS), (W - b, H - b))
    out.paste(top.resize((W - 2 * b, b), Image.LANCZOS), (b, 0))
    out.paste(bot.resize((W - 2 * b, b), Image.LANCZOS), (b, H - b))
    out.paste(lef.resize((b, H - 2 * b), Image.LANCZOS), (0, b))
    out.paste(rig.resize((b, H - 2 * b), Image.LANCZOS), (W - b, b))

    return out, (b, b, W - b, H - b)