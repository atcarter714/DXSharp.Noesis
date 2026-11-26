// Analytic ray/sphere 3D viewport in heliocentric AU; all geometry shares one camera.
cbuffer Scene : register(b0) {
 float4 EyeExposure, ForwardFov, RightOrbits, UpAtmosphere, Viewport;
 float4 Bodies[9]; // Sun, Mercury ... Neptune; position.xyz and display radius
 float4 Surface; // width,height,time, visibility exaggeration
};
struct Fullscreen {float4 position:SV_Position;};
Fullscreen VSMain(uint id:SV_VertexID) {
 Fullscreen o; float2 uv=float2((id<<1)&2,id&2);
 o.position=float4(uv*float2(2,-2)+float2(-1,1),0,1);return o;
}
float hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
float noise(float3 p){float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
 return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
 lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+1),f.x),f.y),f.z);}
float fbm(float3 p){float v=0,a=.5;[unroll]for(int k=0;k<5;k++){v+=a*noise(p);p=p*2.03+7.1;a*=.5;}return v;}
float sphere(float3 ro,float3 rd,float3 c,float r){
 float3 oc=ro-c;float b=dot(oc,rd);float3 perp=oc-b*rd;float h=r*r-dot(perp,perp);
 if(h<0)return 1e9;float t=-b-sqrt(h);if(t<=0)t=-b+sqrt(h);return t>0?t:1e9;
}
float3 sky(float3 d){
 // Directional procedural fallback, not the observed NASA panorama.
 float3 gal=normalize(float3(.28,.62,.73));float band=exp(-pow(dot(d,gal)*7,2));
 float dust=smoothstep(.35,.65,fbm(d*13));float3 c=float3(.001,.002,.004)+band*(.02+.09*fbm(d*32))*(1-.75*dust)*float3(.8,.72,.65);
 float2 uv=float2(atan2(d.z,d.x)/6.2831853,asin(clamp(d.y,-1,1))/3.1415927)*float2(1250,625);
 float2 cell=floor(uv),q=frac(uv)-.5;float seed=hash(float3(cell,5));float aa=max(length(fwidth(uv)),.02);
 c+=step(.993,seed)*exp(-dot(q,q)/(.002+aa*aa*.3))*float3(.7,.83,1)*min(1,.3/aa);
 return c;
}
float3 surface(int i,float3 n){
 float3 p=n*4;float f=fbm(p);float small=fbm(p*5);float lat=asin(clamp(n.y,-1,1));
 if(i==0)return float3(4,1.6,.35)*(.65+f*.9);
 if(i==1)return float3(.27,.24,.21)*(.6+small*.9);
 if(i==2)return float3(.85,.65,.34)*(.7+.6*f);
 if(i==3){float land=smoothstep(.48,.52,f);float3 a=lerp(float3(.015,.065,.22),lerp(float3(.05,.24,.09),float3(.36,.25,.12),small),land);
  a=lerp(a,float3(.8,.88,.93),smoothstep(.85,.96,abs(n.y)));return lerp(a,float3(.8,.85,.9),smoothstep(.57,.72,fbm(n*11))*0.72);}
 if(i==4)return lerp(float3(.27,.07,.025),float3(.65,.27,.11),f);
 if(i==5||i==6){float b=.5+.5*sin(lat*25+f*3);return lerp(i==5?float3(.38,.19,.10):float3(.48,.35,.18),i==5?float3(.85,.7,.52):float3(.88,.78,.55),b);}
 return (i==7?float3(.24,.66,.69):float3(.11,.24,.68))*(.8+.2*sin(lat*22+f*2));
}
float4 PSMain(Fullscreen input):SV_Target {
 float2 uv=(input.position.xy-Viewport.xy)/max(Viewport.zw,1);float2 xy=(uv*2-1)*float2(Viewport.z/max(Viewport.w,1),-1);
 float3 ro=EyeExposure.xyz,rd=normalize(ForwardFov.xyz+ForwardFov.w*(xy.x*RightOrbits.xyz+xy.y*UpAtmosphere.xyz));
 float3 color=sky(rd);float t=1e9;int hit=-1;float radius=0;
 [unroll]for(int i=0;i<9;i++){
  float r=Bodies[i].w;
  if(Surface.w>.5&&i>0)r=max(r,length(Bodies[i].xyz-ro)*ForwardFov.w*2/max(Viewport.w,1));
  float h=sphere(ro,rd,Bodies[i].xyz,r);if(h<t){t=h;hit=i;radius=r;}
 }
 if(hit>=0){float3 n=normalize(ro+rd*t-Bodies[hit].xyz);float3 a=surface(hit,n);
  if(hit==0)color=a;else{float3 light=normalize(-Bodies[hit].xyz);float day=saturate(dot(n,light));color=a*(.035+day*1.35);
   if(hit==3||hit==2||hit==7||hit==8)color+=pow(1-saturate(dot(n,-rd)),4)*float3(.02,.16,.3)*UpAtmosphere.w*(.15+day);}}
 // Saturn rings lie in the ecliptic for this illustrative renderer.
 float3 sc=Bodies[6].xyz;float rt=(sc.y-ro.y)/(abs(rd.y)<1e-7?1e-7:rd.y);
 float rr=length((ro+rd*rt-sc).xz)/max(Bodies[6].w,1e-9);
 if(rt>0&&rt<t&&rr>1.24&&rr<2.25){float a=.3+.25*sin(rr*43)+.2*sin(rr*88);color=lerp(color,float3(.55,.43,.28),saturate(a));t=rt;}
 if(RightOrbits.w>.5){float pt=-ro.y/(abs(rd.y)<1e-7?1e-7:rd.y);float r=length((ro+rd*pt).xz);
  float delta=1e6;static const float axes[8]={.3871,.7233,1,1.5237,5.2029,9.5367,19.189,30.0699};
  [unroll]for(int j=0;j<8;j++)delta=min(delta,abs(r-axes[j]));
  float aa=max(fwidth(r),.0001);if(pt>0&&pt<t)color+=(1-smoothstep(aa*.35,aa*1.4,delta))*float3(.008,.035,.05);
 }
 return float4(pow(1-exp(-max(color,0)*EyeExposure.w),1.0/2.2),1);
}
