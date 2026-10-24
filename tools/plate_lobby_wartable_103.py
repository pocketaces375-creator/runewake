import numpy as np, cv2, sys
im = cv2.imread('wt.png'); out = im.copy()
lum = cv2.cvtColor(im, cv2.COLOR_BGR2GRAY).astype(np.float32)
H, W = lum.shape
def region_mask(box, mode, delta, dil=2):
    x0,y0,x1,y1 = box; m = np.zeros((H,W), np.uint8)
    r = lum[y0:y1, x0:x1]
    bg = cv2.medianBlur(r.astype(np.uint8), 31).astype(np.float32)
    if mode == 'dark': t = (bg - r) > delta      # dark ink on parchment / carved letters
    elif mode == 'abs': t = np.abs(r - bg) > delta
    else: t = (r - bg) > delta                   # light gold letters on stone
    m[y0:y1, x0:x1] = t.astype(np.uint8)*255
    if dil: m = cv2.dilate(m, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2*dil+1, 2*dil+1)))
    return m
def fill(m, rad=6, method=cv2.INPAINT_TELEA):
    global out
    out = cv2.inpaint(out, m, rad, method)
jobs = [
  ((468,132,908,165), 'light', 18, 2),   # subtitle
  ((318,362,428,462), 'dark', 22, 2),    # left parchment
  ((958,362,1058,468), 'dark', 22, 2),   # right parchment
  ((520,540,852,580), 'dark', 22, 2),    # friend's username scroll
  ((252,620,440,668), 'abs', 12, 3),   # Challenge
  ((486,620,662,668), 'abs', 12, 3),   # Leave
  ((718,620,892,668), 'abs', 12, 3),   # Start
  ((420,694,962,734), 'dark', 22, 2),    # bottom status scroll
]
for box, mode, d, dil in jobs: fill(region_mask(box, mode, d, dil))
# the code: carved letters in the glowing stone
cm = region_mask((515,250,868,352), 'abs', 16, 5)
out = cv2.inpaint(out, cm, 11, cv2.INPAINT_NS)
# unify the stone face: a soft blur blended in over the letters' area, then the stone's fine grain back
x0,y0,x1,y1 = 512,248,870,355
face = out[y0:y1,x0:x1].astype(np.float32)
bl = cv2.GaussianBlur(face, (0,0), 6)
wm = cv2.GaussianBlur(cm[y0:y1,x0:x1].astype(np.float32)/255, (0,0), 8)[...,None]
wm = np.clip(wm*1.6, 0, 1)
grain = face - cv2.GaussianBlur(face, (0,0), 2)
out[y0:y1,x0:x1] = np.clip(face*(1-wm) + (bl + grain*0.0)*wm, 0, 255).astype(np.uint8)
cv2.imwrite('wt_clean.png', out)
