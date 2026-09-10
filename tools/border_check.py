#!/usr/bin/env python3
"""border_check.py — Numeric gate for root-bound border slices.

Loads the eight RGBA border slice PNGs and checks seven quality gates per slice.
Exits 0 if all 56 gates pass; exits 1 with a PASS/FAIL table if any fails.

Usage:
    python3 tools/border_check.py [art_dir]

Default art_dir: client/content/art/border
"""
import os
import sys
from PIL import Image

# Expected dimensions per slice (band = 58px)
SLICE_DIMS = {
    'corner_tl': (58, 58),
    'corner_tr': (58, 58),
    'corner_bl': (58, 58),
    'corner_br': (58, 58),
    'edge_top': (716, 58),
    'edge_bottom': (716, 58),
    'edge_left': (58, 1100),
    'edge_right': (58, 1100),
}

# Which slices are corners vs edges
CORNERS = {'corner_tl', 'corner_tr', 'corner_bl', 'corner_br'}
EDGES = {'edge_top', 'edge_bottom', 'edge_left', 'edge_right'}

# Gold keyline colour (tolerance 12)
GOLD_KEY = (161, 139, 79)
GOLD_TOL = 12
# Minimum gold pixels: 30 for edges, 10 for corners
GOLD_MIN_EDGE = 30
GOLD_MIN_CORNER = 10


def within_tol(c, target, tol):
    """True if colour c is within tol of target by Euclidean distance."""
    dr = c[0] - target[0]
    dg = c[1] - target[1]
    db = c[2] - target[2]
    return (dr * dr + dg * dg + db * db) <= tol * tol * 3


def luminance(r, g, b):
    """Relative luminance (ITU-R BT.709)."""
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def check_slice(name, img):
    """Run all 7 gates on one slice. Returns list of (gate, PASS/FAIL, detail)."""
    results = []
    w, h = img.size

    # RGBA gate
    mode_ok = img.mode == 'RGBA'
    results.append(('RGBA', 'PASS' if mode_ok else 'FAIL',
                    f'mode={img.mode}' if not mode_ok else ''))

    # SIZE gate
    expected = SLICE_DIMS[name]
    size_ok = (w, h) == expected
    results.append(('SIZE', 'PASS' if size_ok else 'FAIL',
                    f'{w}x{h} != {expected[0]}x{expected[1]}' if not size_ok else ''))

    pixels = img.load()
    total = w * h

    # Gather pixel data
    alpha_vals = []
    lum_vals = []
    gold_count = 0

    for y in range(h):
        for x in range(w):
            px = pixels[x, y]
            if len(px) == 4:
                r, g, b, a = px
            else:
                r, g, b = px[:3]
                a = 255
            alpha_vals.append(a)
            lum = luminance(r, g, b)
            lum_vals.append(lum)
            if within_tol((r, g, b), GOLD_KEY, GOLD_TOL):
                gold_count += 1

    # OPAQUE gate: >99.5% pixels with alpha > 250
    opaque_pixels = sum(1 for a in alpha_vals if a > 250)
    opaque_frac = opaque_pixels / total if total > 0 else 0
    opaque_ok = opaque_frac > 0.995
    results.append(('OPAQUE', 'PASS' if opaque_ok else 'FAIL',
                    f'{opaque_frac:.4f} ({opaque_pixels}/{total})' if not opaque_ok else f'{opaque_frac:.4f}'))

    # DARK gate: fraction of pixels with luminance < 40 must be exactly 0.0
    dark_pixels = sum(1 for lum in lum_vals if lum < 40)
    dark_ok = dark_pixels == 0
    results.append(('DARK', 'PASS' if dark_ok else 'FAIL',
                    f'{dark_pixels} dark pixels' if not dark_ok else f'0 dark ({dark_pixels}/{total})'))

    # PALE gate: mean luminance between 125 and 160
    mean_lum = sum(lum_vals) / total if total > 0 else 0
    pale_ok = 125 <= mean_lum <= 160
    results.append(('PALE', 'PASS' if pale_ok else 'FAIL',
                    f'mean={mean_lum:.1f}' if not pale_ok else f'mean={mean_lum:.1f}'))

    # GOLD gate
    gold_min = GOLD_MIN_CORNER if name in CORNERS else GOLD_MIN_EDGE
    gold_ok = gold_count >= gold_min
    results.append(('GOLD', 'PASS' if gold_ok else 'FAIL',
                    f'{gold_count} < {gold_min}' if not gold_ok else f'{gold_count} / min {gold_min}'))

    # SCALE gate: downscale so band=16px, check inner ring vs mean
    # For corners (58x58): downscale to 16x16; check the single innermost pixel
    scale_ok = True
    scale_detail = ''
    try:
        if name in CORNERS:
            # 58x58 → 16x16
            c_scaled = img.resize((16, 16), Image.LANCZOS)
            sp = c_scaled.load()
            # Innermost pixel (closest to art window)
            inner_px = {
                'corner_tl': (15, 15),
                'corner_tr': (0, 15),
                'corner_bl': (15, 0),
                'corner_br': (0, 0),
            }[name]
            ix, iy = inner_px
            inner_val = c_scaled.getpixel((ix, iy))
            inner_lum = luminance(inner_val[0], inner_val[1], inner_val[2])

            # Mean luminance of entire 16x16
            sum_lum_16 = 0
            for sy in range(16):
                for sx in range(16):
                    sv = c_scaled.getpixel((sx, sy))
                    sum_lum_16 += luminance(sv[0], sv[1], sv[2])
            mean_16 = sum_lum_16 / 256

            delta = mean_16 - inner_lum
            scale_ok = delta >= 6.0 and inner_lum >= 100
            scale_detail = f'inner={inner_lum:.1f}, mean={mean_16:.1f}, delta={delta:.1f}'
            if not scale_ok:
                scale_detail += ' (FAIL)' if not (delta >= 6.0 and inner_lum >= 100) else ''

        else:  # edge slice
            if name == 'edge_top' or name == 'edge_bottom':
                # horizontal edge: Wx58 → (round(W * 16/58), 16)
                new_h = 16
                new_w = max(1, round(w * 16 / 58))
            else:  # edge_left, edge_right
                # vertical edge: 58xH → (16, round(H * 16/58))
                new_w = 16
                new_h = max(1, round(h * 16 / 58))

            e_scaled = img.resize((new_w, new_h), Image.LANCZOS)
            es = e_scaled.load()
            ew, eh = e_scaled.size

            # Mean luminance of entire downscaled image
            sum_lum_e = 0
            for ey in range(eh):
                for ex in range(ew):
                    ev = e_scaled.getpixel((ex, ey))
                    sum_lum_e += luminance(ev[0], ev[1], ev[2])
            mean_e = sum_lum_e / (ew * eh)

            # Inner row/column (closest to art window)
            if name == 'edge_top':
                # Innermost = bottom row (row eh-1)
                row_lums = [luminance(e_scaled.getpixel((ex, eh - 1))[0],
                                       e_scaled.getpixel((ex, eh - 1))[1],
                                       e_scaled.getpixel((ex, eh - 1))[2])
                            for ex in range(ew)]
                inner_mean = sum(row_lums) / len(row_lums)
                inner_label = f'bottom_row'
            elif name == 'edge_bottom':
                # Innermost = top row (row 0)
                row_lums = [luminance(e_scaled.getpixel((ex, 0))[0],
                                       e_scaled.getpixel((ex, 0))[1],
                                       e_scaled.getpixel((ex, 0))[2])
                            for ex in range(ew)]
                inner_mean = sum(row_lums) / len(row_lums)
                inner_label = 'top_row'
            elif name == 'edge_left':
                # Innermost = right column (col ew-1)
                col_lums = [luminance(e_scaled.getpixel((ew - 1, ey))[0],
                                       e_scaled.getpixel((ew - 1, ey))[1],
                                       e_scaled.getpixel((ew - 1, ey))[2])
                            for ey in range(eh)]
                inner_mean = sum(col_lums) / len(col_lums)
                inner_label = 'right_col'
            else:  # edge_right
                # Innermost = left column (col 0)
                col_lums = [luminance(e_scaled.getpixel((0, ey))[0],
                                       e_scaled.getpixel((0, ey))[1],
                                       e_scaled.getpixel((0, ey))[2])
                            for ey in range(eh)]
                inner_mean = sum(col_lums) / len(col_lums)
                inner_label = 'left_col'

            delta_e = mean_e - inner_mean
            scale_ok = delta_e >= 6.0 and inner_mean >= 100
            scale_detail = f'{inner_label}={inner_mean:.1f}, mean={mean_e:.1f}, delta={delta_e:.1f}'
            if not scale_ok:
                scale_detail += ' (FAIL)'
    except Exception as e:
        scale_ok = False
        scale_detail = f'error: {e}'

    results.append(('SCALE', 'PASS' if scale_ok else 'FAIL', scale_detail))

    return results


def main():
    art_dir = sys.argv[1] if len(sys.argv) > 1 else 'client/content/art/border'
    art_dir = os.path.abspath(art_dir)

    slice_names = ['corner_tl', 'corner_tr', 'corner_bl', 'corner_br',
                   'edge_top', 'edge_bottom', 'edge_left', 'edge_right']

    gate_names = ['RGBA', 'SIZE', 'OPAQUE', 'DARK', 'PALE', 'GOLD', 'SCALE']
    header = f'{"Slice":<14} ' + ' '.join(f'{g:<8}' for g in gate_names) + '  Score'
    sep = '-' * len(header)

    all_results = {}  # name -> [(gate, verdict, detail)]
    total_pass = 0
    total_fail = 0

    for name in slice_names:
        path = os.path.join(art_dir, f'rootbound_{name}.png')
        if not os.path.exists(path):
            print(f'  {name:<14}  MISSING — file not found', file=sys.stderr)
            total_fail += 7
            continue
        img = Image.open(path)
        results = check_slice(name, img)
        all_results[name] = results
        passes = sum(1 for _, v, _ in results if v == 'PASS')
        fails = sum(1 for _, v, _ in results if v == 'FAIL')
        total_pass += passes
        total_fail += fails

    # Print table
    print(f'border_check.py — numeric gate ({art_dir})')
    print()
    print(header)
    print(sep)
    for name in slice_names:
        if name not in all_results:
            print(f'  {name:<14}  MISSING')
            continue
        results = all_results[name]
        passes = sum(1 for _, v, _ in results if v == 'PASS')
        fails = sum(1 for _, v, _ in results if v == 'FAIL')
        row = f'  {name:<14} '
        for _, v, _ in results:
            row += f'{v:<8} '
        row += f'({passes}/7)'
        print(row)

    print(sep)
    total_gates = total_pass + total_fail
    print(f'  Total: {total_pass}/{total_gates} PASS')

    # Print details for FAILs
    has_fail = total_fail > 0
    if has_fail:
        print()
        print('FAIL details:')
        for name in slice_names:
            if name not in all_results:
                continue
            results = all_results[name]
            for gate, verdict, detail in results:
                if verdict == 'FAIL':
                    print(f'  {name}/{gate}: {detail}')

    # Print summary for PASSes
    print()
    for name in slice_names:
        if name not in all_results:
            continue
        results = all_results[name]
        for gate, verdict, detail in results:
            if verdict == 'PASS' and detail:
                # Only print informative details (not just empty detail)
                if 'mean=' in detail or 'inner' in detail or 'delta' in detail or '/' in detail:
                    print(f'  {name}/{gate}: {detail}')

    sys.exit(1 if has_fail else 0)


if __name__ == '__main__':
    main()