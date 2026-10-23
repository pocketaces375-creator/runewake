# clean_plates.py SRC_DIR OUT_DIR: scrape the leftover words, sliders and switches off the clean screens (FABLE-088), deterministically
import sys
import numpy as np
from PIL import Image, ImageFilter
R = {  # scene: [(x0, y0, x1, y1, median, threshold) as fractions of the picture]
 "DeckBuilderScene": [(0.012, 0.015, 0.150, 0.070, 21, 22), (0.160, 0.015, 0.245, 0.070, 21, 22), (0.775, 0.385, 0.835, 0.425, 21, 22),
                      (0.775, 0.540, 0.855, 0.585, 21, 22), (0.775, 0.645, 0.880, 0.695, 21, 22), (0.240, 0.700, 0.320, 0.750, 21, 22),
                      (0.345, 0.695, 0.420, 0.722, 21, 22), (0.240, 0.810, 0.350, 0.880, 21, 22), (0.255, 0.910, 0.330, 0.955, 21, 22),
                      (0.080, 0.900, 0.180, 0.945, 21, 22), (0.800, 0.855, 0.885, 0.900, 21, 22), (0.920, 0.855, 0.980, 0.900, 21, 22),
                      (0.780, 0.915, 0.840, 0.960, 21, 22), (0.850, 0.915, 0.900, 0.960, 21, 22), (0.910, 0.915, 0.975, 0.960, 21, 22)],
 "RewardsScene": [(0.330, 0.015, 0.665, 0.125, 71, 18), (0.075, 0.160, 0.290, 0.230, 21, 22)]
                 + [(x - 0.035, 0.245, x + 0.035, 0.285, 21, 22) for x in (0.125, 0.220, 0.309, 0.398, 0.486, 0.575, 0.664)]
                 + [(x - 0.035, 0.318, x + 0.035, 0.355, 21, 22) for x in (0.125, 0.220, 0.309, 0.398, 0.486)]
                 + [(0.075, 0.440, 0.310, 0.510, 21, 22), (0.770, 0.440, 0.870, 0.475, 21, 22), (0.100, 0.915, 0.160, 0.970, 21, 22)]
                 + [(0.082, y - 0.028, 0.275, y + 0.028, 21, 22) for y in (0.535, 0.618, 0.700, 0.782)]
                 + [(0.272, y - 0.022, 0.515, y + 0.022, 41, 16) for y in (0.548, 0.630, 0.712, 0.793)],
 "SettingsScene": [(0.180, 0.355, 0.278, 0.640, 21, 22), (0.180, 0.650, 0.295, 0.712, 21, 22)]
                  + [(0.272, y - 0.024, 0.452, y + 0.024, 41, 16) for y in (0.385, 0.459, 0.533, 0.607)]
                  + [(0.428, 0.652, 0.487, 0.708, 41, 16), (0.183, 0.732, 0.275, 0.778, 21, 22), (0.528, 0.368, 0.802, 0.468, 21, 22),
                     (0.528, 0.522, 0.802, 0.792, 21, 22)],
}
def clean(im, regions):
    a = np.asarray(im.convert("RGB")).astype(np.float32); H, W = a.shape[:2]
    for fx0, fy0, fx1, fy1, med, thr in regions:
        x0, y0, x1, y1 = int(fx0 * W), int(fy0 * H), int(fx1 * W), int(fy1 * H)
        box = a[y0:y1, x0:x1].copy(); lum = box @ np.array([0.299, 0.587, 0.114])
        bg = np.asarray(Image.fromarray(np.clip(lum, 0, 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(med | 1))).astype(np.float32)
        m = np.abs(lum - bg) > thr
        m = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
        known = ~m
        for _ in range(600):
            if known.all(): break
            s = np.zeros_like(box); n = np.zeros(m.shape)
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
                k = np.roll(np.roll(known, dy, 0), dx, 1); v = np.roll(np.roll(box, dy, 0), dx, 1)
                s += v * k[..., None]; n += k
            fill = (~known) & (n > 0); box[fill] = s[fill] / n[fill][:, None]; known |= fill
        g = np.random.default_rng(x0 * 7 + y0).normal(0, 3.0, box.shape)
        box[m] = np.clip(box[m] + g[m], 0, 255); a[y0:y1, x0:x1] = box
    return Image.fromarray(a.astype(np.uint8))
def medallions(im, settings):
    """Trikzos: no faces - the coin portraits in the Daily Rewards corners become the rune medallions of the Settings frame."""
    from PIL import ImageDraw
    W, H = im.size; sw, sh = settings.size
    srcs = [(0.010, 0.000, 0.095, 0.150), (0.010, 0.795, 0.120, 1.000), (0.880, 0.795, 0.995, 1.000)]   # Settings: top-left, bottom-left, bottom-right
    dsts = [(0.005, 0.005, 0.075, 0.130), (0.010, 0.880, 0.075, 0.995), (0.930, 0.880, 0.995, 0.995)]   # Rewards coins
    for (a, b, c, d), (e, f, g, h) in zip(srcs, dsts):
        piece = settings.crop((int(a * sw), int(b * sh), int(c * sw), int(d * sh)))
        box = (int(e * W), int(f * H), int(g * W), int(h * H)); size = (box[2] - box[0], box[3] - box[1])
        piece = piece.resize(size, Image.LANCZOS)
        mask = Image.new("L", size, 0); ImageDraw.Draw(mask).ellipse((1, 1, size[0] - 2, size[1] - 2), fill=255)
        im.paste(piece, box[:2], mask.filter(ImageFilter.GaussianBlur(1.5)))
    return im

if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    for n, regs in R.items():
        im = clean(Image.open(f"{src}/{n}_ui.png"), regs)
        if n == "RewardsScene": im = medallions(im, Image.open(f"{src}/SettingsScene_ui.png").convert("RGB"))
        im.save(f"{out}/{n}_ui.png"); print("cleaned", n)
    if len(sys.argv) > 3:   # the painted chest, lifted out of the picked Daily Rewards (its plate has none)
        from PIL import ImageDraw
        v8 = Image.open(f"{sys.argv[3]}/v8_daily_rewards.png").convert("RGB"); W, H = v8.size
        ch = v8.crop((int(0.758 * W), int(0.478 * H), int(0.882 * W), int(0.642 * H))).convert("RGBA")
        m = Image.new("L", ch.size, 0); ImageDraw.Draw(m).ellipse((4, 4, ch.size[0] - 4, ch.size[1] - 4), fill=255)
        ch.putalpha(m.filter(ImageFilter.GaussianBlur(9))); ch.save(f"{out}/chest.png"); print("chest")
