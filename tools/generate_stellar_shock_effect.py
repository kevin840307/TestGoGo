#!/usr/bin/env python3
from pathlib import Path
from math import cos, sin, pi
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "client/Assets/Effects/RawData/Skill_Effect"
OUT.mkdir(parents=True, exist_ok=True)

def add_glow(base, draw_fn, blur=10, opacity=1.0):
    layer = Image.new("RGBA", base.size, (0,0,0,0))
    d = ImageDraw.Draw(layer, "RGBA")
    draw_fn(d)
    glow = layer.filter(ImageFilter.GaussianBlur(blur))
    if opacity != 1.0:
        a = glow.getchannel("A").point(lambda v: int(v * opacity))
        glow.putalpha(a)
    base.alpha_composite(glow)
    base.alpha_composite(layer)

def circle_points(cx, cy, r, count=128):
    return [(cx + cos(i*2*pi/count)*r, cy + sin(i*2*pi/count)*r) for i in range(count)]

def star_points(cx, cy, outer, inner, count=8, rotation=0):
    pts=[]
    for i in range(count*2):
        a=rotation + i*pi/count
        r=outer if i%2==0 else inner
        pts.append((cx+cos(a)*r, cy+sin(a)*r))
    return pts

def draw_orbit(draw, cx, cy, rx, ry, color, width=3, phase=0):
    pts=[]
    for i in range(160):
        a=i*2*pi/160
        x=cx+cos(a)*rx
        y=cy+sin(a)*ry
        # small phase rotation in 2D
        xr=cx+(x-cx)*cos(phase)-(y-cy)*sin(phase)
        yr=cy+(x-cx)*sin(phase)+(y-cy)*cos(phase)
        pts.append((xr,yr))
    draw.line(pts+[pts[0]], fill=color, width=width)

def cast_frame(i):
    n=10
    t=i/(n-1)
    # 0..5 expand/intensify, 6..9 fade
    power=min(1.0, t/0.55) if t<=0.55 else max(0.0, 1-(t-0.55)/0.45)
    growth=0.18 + 0.82*min(1.0,t/0.55)
    img=Image.new("RGBA",(256,256),(0,0,0,0))
    cx=cy=128

    # nebula aura
    aura=Image.new("RGBA",img.size,(0,0,0,0))
    ad=ImageDraw.Draw(aura,"RGBA")
    for r in range(95,10,-4):
        q=r/95
        alpha=int(6 + 38*(1-q)*power)
        ad.ellipse((cx-r,cy-r,cx+r,cy+r), fill=(118,92,210,alpha))
    aura=aura.filter(ImageFilter.GaussianBlur(12))
    img.alpha_composite(aura)

    # expanding cyan/purple shock rings
    for idx,(base_r,col) in enumerate([
        (38,(132,236,255,220)),
        (53,(178,142,255,180)),
        (69,(115,202,255,130)),
    ]):
        r=base_r*growth + idx*5*t
        add_glow(img, lambda d,r=r,col=col: d.line(circle_points(cx,cy,r), fill=col, width=2), blur=7)

    # orbital ellipses
    for k in range(3):
        phase=t*0.8 + k*pi/3
        rx=(44+9*k)*growth
        ry=(16+4*k)*growth
        add_glow(img, lambda d,rx=rx,ry=ry,phase=phase: draw_orbit(
            d,cx,cy,rx,ry,(100,231,255,int(190*power)),2,phase
        ), blur=5)

    # rune ring, tick marks and golden compass stars
    rr=45*growth
    add_glow(img, lambda d: d.line(circle_points(cx,cy,rr), fill=(224,205,255,int(235*power)), width=2), blur=5)
    d=ImageDraw.Draw(img,"RGBA")
    for k in range(16):
        a=k*2*pi/16 + t*0.2
        r0=rr-4
        r1=rr+4
        d.line((cx+cos(a)*r0,cy+sin(a)*r0,cx+cos(a)*r1,cy+sin(a)*r1),
               fill=(220,202,255,int(190*power)),width=1)
    for k in range(4):
        a=k*pi/2
        x=cx+cos(a)*(rr+8)
        y=cy+sin(a)*(rr+8)
        pts=star_points(x,y,9*growth+2,2.2,4,pi/4)
        add_glow(img, lambda dr,pts=pts: dr.polygon(pts, fill=(255,224,142,int(245*power))), blur=5)

    # radial rays
    ray_count=12
    for k in range(ray_count):
        a=k*2*pi/ray_count + t*0.15
        ln=(28+44*power)*(1 if k%3 else 1.35)*growth
        x1=cx+cos(a)*8
        y1=cy+sin(a)*8
        x2=cx+cos(a)*ln
        y2=cy+sin(a)*ln
        col=(203,235,255,int((150 if k%2 else 220)*power))
        add_glow(img, lambda dr,x1=x1,y1=y1,x2=x2,y2=y2,col=col:
                 dr.line((x1,y1,x2,y2),fill=col,width=2), blur=5)

    # central starburst
    core=star_points(cx,cy,24*growth+8*power,4,8,t*0.25)
    add_glow(img, lambda dr: dr.polygon(core,fill=(255,255,255,int(255*power))), blur=12)
    add_glow(img, lambda dr: dr.ellipse((cx-7,cy-7,cx+7,cy+7),
                                       fill=(255,250,238,int(255*power))), blur=8)

    # small stars
    for k in range(10):
        a=k*2*pi/10 + 0.37
        r=(55+((k*13)%25))*growth
        x=cx+cos(a)*r
        y=cy+sin(a)*r
        s=2+(k%3)
        pts=star_points(x,y,s*2,s*0.45,4,pi/4)
        d.polygon(pts,fill=(255,232,170,int(180*power)))

    return img

def hit_frame(i):
    n=8
    t=i/(n-1)
    power=min(1.0,t/0.42) if t<=0.42 else max(0.0,1-(t-0.42)/0.58)
    growth=0.22 + min(1.0,t/0.55)*0.78
    img=Image.new("RGBA",(192,192),(0,0,0,0))
    cx=cy=96

    # soft violet hit cloud
    cloud=Image.new("RGBA",img.size,(0,0,0,0))
    cd=ImageDraw.Draw(cloud,"RGBA")
    for r in range(68,8,-3):
        q=r/68
        cd.ellipse((cx-r,cy-r,cx+r,cy+r),
                   fill=(125,95,215,int(25*(1-q)*power)))
    cloud=cloud.filter(ImageFilter.GaussianBlur(10))
    img.alpha_composite(cloud)

    # cyan ring
    rr=48*growth
    add_glow(img, lambda d: d.line(circle_points(cx,cy,rr),fill=(110,235,255,int(225*power)),width=3), blur=7)

    # rotating broken orbit
    phase=t*1.0
    add_glow(img, lambda d: draw_orbit(d,cx,cy,54*growth,20*growth,
                                       (128,225,255,int(185*power)),2,phase),blur=5)

    # explosive rays
    for k in range(10):
        a=k*2*pi/10 + t*0.45
        inner=7
        outer=(28+42*growth)*(1.3 if k%3==0 else 1.0)
        col=(223,233,255,int((230 if k%2==0 else 165)*power))
        add_glow(img, lambda d,a=a,outer=outer,col=col:
                 d.line((cx+cos(a)*inner,cy+sin(a)*inner,
                         cx+cos(a)*outer,cy+sin(a)*outer),
                        fill=col,width=2),blur=5)

    # center
    star=star_points(cx,cy,25*growth+7,3.5,8,t*0.55)
    add_glow(img,lambda d:d.polygon(star,fill=(255,255,255,int(255*power))),blur=10)

    # gold satellite stars
    d=ImageDraw.Draw(img,"RGBA")
    for k in range(7):
        a=k*2*pi/7+0.2
        r=(43+9*(k%2))*growth
        x=cx+cos(a)*r
        y=cy+sin(a)*r
        pts=star_points(x,y,4.5,1,4,pi/4)
        d.polygon(pts,fill=(255,224,145,int(215*power)))
    return img

def save_frames():
    for i in range(10):
        p=OUT/f"Stellar_Use100.spr.{i:04d}.png"
        cast_frame(i).save(p,optimize=True,compress_level=9)
    for i in range(8):
        p=OUT/f"Stellar_Hit100.spr.{i:04d}.png"
        hit_frame(i).save(p,optimize=True,compress_level=9)

if __name__ == "__main__":
    save_frames()
    print("Generated Stellar Shock PNG frames:", OUT)
