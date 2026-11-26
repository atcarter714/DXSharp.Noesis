#!/usr/bin/env python3
"""Bake display globes from optional equirectangular source maps and a Milky Way backdrop.
No network by default. Run --download for credited NASA SVS and CC BY 4.0 Solar System Scope maps.
Requires numpy and Pillow. Existing source maps are reused; no runtime network access.
"""
import argparse, io, json, pathlib, urllib.request
import numpy as np
from PIL import Image, ImageFilter, ImageDraw
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'samples/OrbitalCommand/Ui/Solar'
CACHE=ROOT/'.local/solar-source'
BASE='https://www.solarsystemscope.com/textures/download/'
MAPS={n:BASE+f for n,f in dict(mercury='2k_mercury.jpg',venus='2k_venus_atmosphere.jpg',earth='2k_earth_daymap.jpg',mars='2k_mars.jpg',jupiter='2k_jupiter.jpg',saturn='2k_saturn.jpg',uranus='2k_uranus.jpg',neptune='2k_neptune.jpg',sun='2k_sun.jpg').items()}
SKY='https://svs.gsfc.nasa.gov/vis/a000000/a004800/a004851/milkyway_2020_4k_print.jpg'
COLORS=dict(mercury=(.49,.46,.42),venus=(.88,.7,.42),earth=(.1,.29,.63),mars=(.66,.27,.13),jupiter=(.72,.54,.38),saturn=(.77,.67,.44),uranus=(.48,.77,.8),neptune=(.2,.35,.74),sun=(1,.63,.14))

def download(url,path):
    if path.exists():return
    req=urllib.request.Request(url,headers={'User-Agent':'DXSharp.Noesis asset preparation/1.0'})
    with urllib.request.urlopen(req,timeout=60) as response:
        data=response.read(32*1024*1024+1)
    if len(data)>32*1024*1024:raise ValueError('Asset exceeds size limit')
    with Image.open(io.BytesIO(data)) as im: im.verify()
    path.parent.mkdir(parents=True,exist_ok=True);tmp=path.with_suffix('.tmp');tmp.write_bytes(data);tmp.replace(path)

def noise(w,h,seed):
    rng=np.random.default_rng(seed); result=np.zeros((h,w),dtype=np.float32)
    for size,weight in [(8,.48),(16,.25),(32,.13),(64,.08),(128,.04),(256,.02)]:
        a=(rng.random((size,max(1,2*size)))*255).astype(np.uint8)
        result+=np.asarray(Image.fromarray(a).resize((w,h),Image.Resampling.BICUBIC),dtype=np.float32)/255*weight
    return np.clip(result,0,1)

def fallback(name):
    w,h=1024,512; v,u=np.mgrid[0:h,0:w]; lat=(v/h-.5)*np.pi; a=noise(w,h,list(COLORS).index(name)+10); detail=noise(w,h,list(COLORS).index(name)+40)
    base=np.broadcast_to(np.array(COLORS[name]),(h,w,3)).copy()
    if name=='earth':
        land=np.clip((a-.48)*30,0,1); vegetation=np.stack([.13+detail*.28,.25+detail*.30,.09+detail*.12],axis=-1)
        base=base*(1-land[...,None])+vegetation*land[...,None]; ice=np.clip((np.abs(lat)-1.18)*5,0,1);base=base*(1-ice[...,None])+.92*ice[...,None]
        clouds=np.clip((detail-.57)*5,0,.8);base=base*(1-clouds[...,None])+clouds[...,None]*.94
    elif name in ('jupiter','saturn','uranus','neptune','venus'):
        strength={'jupiter':.42,'saturn':.25,'uranus':.055,'neptune':.10,'venus':.15}[name]
        band=np.sin(lat*27+detail*2)+.4*np.sin(lat*59+a*3);base*=np.clip(1+strength*band[...,None]+(detail[...,None]-.5)*.25,.3,1.8)
        if name=='jupiter':
            storm=np.exp(-((u/w-.55)/.06)**2-((v/h-.61)/.045)**2);base=base*(1-storm[...,None]*.7)+np.array([.73,.30,.15])*storm[...,None]*.7
    elif name=='sun':base*=.66+.75*a[...,None]+.3*detail[...,None]
    else:base*=.45+.95*a[...,None]+.35*detail[...,None]
    return np.uint8(np.clip(base,0,1)*255)

def globe(name,tex,path):
    size=384; yy,xx=np.mgrid[0:size,0:size].astype(float);x=(xx+.5-size/2)/(size/2-2);y=-(yy+.5-size/2)/(size/2-2);r=np.sqrt(x*x+y*y);z=np.sqrt(np.maximum(0,1-r*r));
    # Fixed illustrative globe orientation; map latitude/longitude from sphere normal.
    tilt=.23; ny=y*np.cos(tilt)+z*np.sin(tilt); nz=z*np.cos(tilt)-y*np.sin(tilt)
    u=(np.arctan2(x,nz)/(2*np.pi)+.53)%1;v=np.clip(.5-np.arcsin(np.clip(ny,-1,1))/np.pi,0,1)
    h,w=tex.shape[:2]; rgb=tex[np.minimum((v*h).astype(int),h-1),np.minimum((u*w).astype(int),w-1)].astype(float)/255
    # Fixed studio illumination, explicitly not true solar phase.
    shade=.12+.88*np.clip(-.46*x+.45*y+.76*z,0,1);shade=shade**.6
    if name=='sun':shade=.76+.24*z
    rgb*=shade[...,None]
    if name in ('earth','venus','uranus','neptune'):
        limb=np.clip(1-z,0,1)**4;rgb+=limb[...,None]*np.array([.07,.14,.23])
    alpha=np.clip((1-r)*(size/2),0,1)
    rgba=np.uint8(np.dstack([np.clip(rgb,0,1),alpha])*255);Image.fromarray(rgba,'RGBA').save(path)

def procedural_sky(path):
    w,h=1600,900;y,x=np.mgrid[0:h,0:w];u=x/w;v=y/h;a=noise(w,h,123);b=noise(w,h,456)
    center=.51+.22*np.sin((u-.1)*4.8);delta=(v-center);band=np.exp(-(delta/.12)**2);dust=np.clip((a-.45)*2.8,0,1)
    glow=band*np.clip(.28+b*1.4-dust*.9,.08,1)*(.3+.7*a)
    rgb=np.stack([.012+glow*.35,.019+glow*.30,.035+glow*.38],axis=-1)
    img=Image.fromarray(np.uint8(np.clip(rgb,0,1)*255),'RGB');d=ImageDraw.Draw(img);rng=np.random.default_rng(8172)
    for _ in range(5400):
        px=int(rng.integers(w));py=int(rng.integers(h));val=int(50+205*rng.random()**5);c=(val,min(255,val+5),min(255,val+15));d.point((px,py),fill=c)
        if val>235:d.line((px-1,py,px+1,py),fill=c);d.line((px,py-1,px,py+1),fill=c)
    img.save(path,quality=92)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--download',action='store_true');p.add_argument('--source',type=pathlib.Path,default=CACHE);args=p.parse_args();OUT.mkdir(parents=True,exist_ok=True)
    sources={}
    for name,url in MAPS.items():
        f=args.source/(name+'.jpg')
        if args.download:download(url,f)
        if f.exists():
            with Image.open(f) as im:tex=np.asarray(im.convert('RGB'))
            sources[name]=url if args.download and args.source==CACHE else 'local map: '+str(f)
        else:tex=fallback(name);sources[name]='procedural illustration (not measured imagery)'
        globe(name,tex,OUT/(name+'.png'))
    f=args.source/'milkyway.jpg'
    if args.download:download(SKY,f)
    if f.exists():
        with Image.open(f) as im:im.convert('RGB').save(OUT/'milkyway.jpg',quality=95)
        sources['milkyway']=SKY if args.download and args.source==CACHE else 'local map: '+str(f)
    else:procedural_sky(OUT/'milkyway.jpg');sources['milkyway']='procedural illustration (not a measured sky map)'
    (OUT/'provenance.json').write_text(json.dumps(sources,indent=2)+'\n',encoding='utf-8')
    print(OUT)
if __name__=='__main__':main()
