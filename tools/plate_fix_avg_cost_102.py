import numpy as np, cv2, sys
from PIL import Image
for f in sys.argv[1:]:
    a=np.array(Image.open(f).convert('RGB')).astype(np.float32)
    lum=a.mean(2)
    # R box in plate coords
    x0,y0,x1,y1=1152,268,1159,278
    box=lum[y0:y1,x0:x1]; bg=np.percentile(box,25); fg=np.percentile(box,97)
    txtcol=a[y0:y1,x0:x1][box>=np.percentile(box,92)].mean(0)
    # erase R by inpaint of its bright pixels (dilated)
    m=np.zeros(lum.shape,np.uint8); m[y0:y1,x0:x1]=(box>bg+(fg-bg)*0.35).astype(np.uint8)*255
    m=cv2.dilate(m,np.ones((3,3),np.uint8)); m[:,1159:]=0
    a=cv2.inpaint(a.astype(np.uint8),m,3,cv2.INPAINT_TELEA).astype(np.float32)
    # S from FACTIONS
    sx0,sy0,sx1,sy1=1141,425,1149,438
    s=lum[sy0:sy1,sx0:sx1]; sb,sf=np.percentile(s,15),np.percentile(s,97)
    al=np.clip((s-sb)/(sf-sb),0,1)
    h=8; w=max(5,round(al.shape[1]*h/al.shape[0]*1.1))
    al=np.clip(cv2.resize(al,(w,h),interpolation=cv2.INTER_AREA)*1.35,0,1)
    # place S where R was: left edge of R, baseline aligned
    ty=270; tx=1153
    reg=a[ty:ty+h,tx:tx+w]
    a[ty:ty+h,tx:tx+w]=reg*(1-al[...,None]*1.0)+txtcol*al[...,None]*1.0
    Image.fromarray(np.clip(a,0,255).astype(np.uint8)).save(f)
    print(f,'S',w,h)
