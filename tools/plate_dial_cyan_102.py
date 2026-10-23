import numpy as np, sys
from PIL import Image, ImageFilter
src, out, k = sys.argv[1], sys.argv[2], float(sys.argv[3])
im = Image.open(src).convert('RGB'); a = np.array(im).astype(np.float32)
h = np.array(im.convert('HSV')).astype(np.float32)
hue, sat, val = h[...,0], h[...,1]/255, h[...,2]/255
# soft cyan weight: hue around 128 (cyan) +-28, ramps with saturation
hw = np.clip(1 - np.abs(hue-125)/30, 0, 1)
w = hw * np.clip((sat-0.25)/0.35, 0, 1) * np.clip((val-0.15)/0.25, 0, 1)
w = np.array(Image.fromarray((w*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32)/255
lum = (a*[0.3,0.59,0.11]).sum(2, keepdims=True)
wood = np.concatenate([lum*1.05, lum*0.92, lum*0.72], 2) * 0.62   # dim warm wood tone the veins fade into
target = a*(1-k) + wood*k
o = a*(1-w[...,None]) + target*w[...,None]
Image.fromarray(np.clip(o,0,255).astype(np.uint8)).save(out)
hh = np.array(Image.open(out).convert('HSV')).astype(float); m=(hh[...,0]>110)&(hh[...,0]<145)&(hh[...,1]>90)&(hh[...,2]>90)
print(out, 'k', k, 'cyan %.2f%%' % (100*m.mean()))
