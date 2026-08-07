from PIL import Image
import numpy as np

img = Image.open('/home/fictive/runewake/check-2316x1080.png')
a = np.array(img)
h, w = a.shape[:2]

# Detailed scan of the HandArea region (y=614 to y=1057)
# Look for any pixels that are NOT medium gray (76,76,76)
print("=== HandArea anomaly scan ===")
region = a[614:1058, :, :]
r = region[:,:,0].astype(int)
g = region[:,:,1].astype(int)
b = region[:,:,2].astype(int)

# Find pixels that are different from the medium gray background
non_med = ~((r > 60) & (r < 90) & (g > 60) & (g < 90) & (b > 60) & (b < 90))
count = non_med.sum()
print(f"Non-medium-gray pixels: {count}/{region.size//3} ({100*count/(region.size//3):.1f}%)")

if count > 0:
    coords = np.where(non_med)
    # Sample some colors
    colors = {}
    for i in range(0, min(len(coords[0]), 200), 5):
        cy, cx = coords[0][i], coords[1][i]
        color = tuple(region[cy, cx])
        if color not in colors:
            colors[color] = 0
        colors[color] += 1
    print(f"Unique non-gray colors: {len(colors)}")
    for c in sorted(colors.items(), key=lambda x: -x[1])[:20]:
        display_c = f"RGB({c[0][0]},{c[0][1]},{c[0][2]})"
        approx = "dark" if c[0][0] < 60 else "medium" if c[0][0] < 120 else "light" if c[0][0] < 200 else "white"
        print(f"  {display_c} ({approx}): {c[1]} pixels")

# Check for the specific HandCard background color (41,41,56)
card_bg = (r == 41) & (g == 41) & (b == 56)
print(f"HandCard background (41,41,56) pixels: {card_bg.sum()}")

# Check for any dark rectangles (potential hand cards)
print("\n=== Dark rectangle search in HandArea ===")
dark = (r < 60) & (g < 60) & (b < 70)
if dark.sum() > 0:
    coords = np.where(dark)
    min_y, max_y = coords[0].min(), coords[0].max()
    min_x, max_x = coords[1].min(), coords[1].max()
    print(f"  Dark pixel bounding box: x={min_x}-{max_x} ({max_x-min_x}px), y={min_y}-{max_y} ({max_y-min_y}px)")
    for y in range(min_y, max_y+1, 5):
        if dark[y].sum() > 10:
            print(f"  y=614+{y}: {dark[y].sum()} dark pixels")

# Also check for white text in the HandArea (card names, costs)
print("\n=== White text search in HandArea ===")
white = (r > 200) & (g > 200) & (b > 200)
if white.sum() > 0:
    coords = np.where(white)
    min_y, max_y = coords[0].min(), coords[0].max()
    min_x, max_x = coords[1].min(), coords[1].max()
    print(f"  White pixel bounding box: x={min_x}-{max_x} ({max_x-min_x}px), y={min_y}-{max_y} ({max_y-min_y}px)")
    for y in range(min_y, max_y+1, 5):
        if white[y].sum() > 5:
            print(f"  y=614+{y}: {white[y].sum()} white pixels")

# Let me also check the lane slots more carefully
print("\n=== Lane slot analysis ===")
# Enemy lanes: y=42-229
# At y=50, look for 5 horizontal rectangles
for y_check in [55, 80, 110, 140, 170, 500, 520, 550, 580]:
    row = a[min(y_check, h-1), :, :]
    # Find bright pixels (lanes slot borders)
    bright = (row[:,0] > 200) & (row[:,1] > 200) & (row[:,2] > 200)
    if bright.sum() > 10:
        indices = np.where(bright)[0]
        clusters = np.split(indices, np.where(np.diff(indices) > 10)[0] + 1)
        clusters = [(c[0], c[-1], c[-1]-c[0]+1) for c in clusters if len(c) > 3]
        print(f"  y={y_check}: {len(clusters)} bright clusters: {clusters[:10]}")