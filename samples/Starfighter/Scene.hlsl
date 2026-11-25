cbuffer Scene : register(b0) {
    float4 CameraTime;     // camera world position + elapsed time
    float4 ForwardAspect;  // camera forward + aspect ratio
    float4 RightExposure;  // camera right + exposure
    float4 UpAtmosphere;   // camera up + nebula intensity
    float4 Options;        // inertial velocity xyz + tan(FOV / 2)
    float4 Object;         // object translation + scale
    float4 Detail;         // rotation Y, render mode, rotation X, rotation Z
    float4 Tracking;       // material tint RGB + intensity
    float4 Viewport;       // physical viewport x/y/width/height
    float4 Surface;        // full render target width/height
    float4 PilotRight;     // player-local right axis
    float4 PilotUp;        // player-local up axis
    float4 PilotForward;   // player-local forward axis
};

float Hash21(float2 p) {
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}
float Hash31(float3 p) {
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float Noise3(float3 p) {
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash31(i), b = Hash31(i + float3(1,0,0));
    float c = Hash31(i + float3(0,1,0)), d = Hash31(i + float3(1,1,0));
    float e = Hash31(i + float3(0,0,1)), f1 = Hash31(i + float3(1,0,1));
    float g = Hash31(i + float3(0,1,1)), h = Hash31(i + 1);
    return lerp(lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y),lerp(lerp(e,f1,f.x),lerp(g,h,f.x),f.y),f.z);
}
float Fbm(float3 p) {
    float result = 0, weight = 0.5;
    [unroll] for (int i=0; i<5; i++) { result += Noise3(p) * weight; p = p * 2.04 + 7.17; weight *= 0.5; }
    return result;
}
float3 Tone(float3 c) {
    c = max(c, 0);
    c = 1.0 - exp(-c * RightExposure.w);
    c = pow(c, 1.0 / 2.2);
    return saturate(c);
}

struct FullscreenOut { float4 position : SV_Position; float2 uv : TEXCOORD0; };
FullscreenOut VSPlanet(uint id : SV_VertexID) {
    FullscreenOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.position = float4(uv * float2(2,-2) + float2(-1,1), 0, 1);
    o.uv = uv;
    return o;
}
float4 PSPlanet(FullscreenOut input) : SV_Target {
    float2 centered = input.uv * 2 - 1;
    float3 ray = normalize(ForwardAspect.xyz + Options.w * (centered.x * ForwardAspect.w * RightExposure.xyz - centered.y * UpAtmosphere.xyz));
    // Nearby nebula structure and stars exhibit translational parallax, so the
    // world visibly slides past the canopy during inertial flight.
    float3 nebulaCoord = ray * 3.2 + CameraTime.xyz * 0.0085 + float3(0.5, 1.4, CameraTime.w * 0.0015);
    float clouds = pow(saturate(Fbm(nebulaCoord * 1.7 + 2.1)), 3.0);
    float filaments = pow(saturate(Fbm(nebulaCoord * 4.6 + float3(4,2,1)) - 0.43), 2.0);
    float3 color = float3(0.0018, 0.003, 0.009)
        + clouds * float3(0.014, 0.030, 0.082)
        + filaments * float3(0.055,0.12,0.23);

    // Dense star atlas with twinkle, small diffraction spikes and a soft optical halo.
    float2 sky = float2(atan2(ray.z, ray.x) / 6.2831853, asin(clamp(ray.y,-1,1)) / 3.1415927) * float2(920,460);
    sky += CameraTime.xz * 0.085;
    float2 cell = floor(sky);
    float footprint = max(length(fwidth(sky)), 0.025);
    float speed = length(Options.xyz);
    float2 screenDrift = float2(dot(Options.xyz, RightExposure.xyz), dot(Options.xyz, UpAtmosphere.xyz));
    screenDrift = normalize(screenDrift + float2(0.0001, 0.0001));
    float streakStrength = saturate((speed - 18.0) / 54.0);
    [unroll] for (int y=-1; y<=1; y++) [unroll] for (int x=-1; x<=1; x++) {
        float2 key = cell + float2(x,y);
        float seed = Hash21(key + 17.2);
        float2 center = key + float2(Hash21(key + 3.1), Hash21(key + 8.7));
        float2 delta = sky - center;
        float d = length(delta);
        float bright = step(0.9945, seed);
        float star = exp(-d*d / (footprint*footprint*0.34 + 0.00035)) * bright;
        float halo = exp(-d*d / (footprint*footprint*8.0 + 0.012)) * bright * 0.13;
        float spike = exp(-abs(delta.x) * 15.0) * exp(-abs(delta.y) / (footprint*2.5 + 0.02)) * bright * 0.045;
        float along = dot(delta, screenDrift);
        float across = abs(delta.x * screenDrift.y - delta.y * screenDrift.x);
        float streak = exp(-across * 2.2) * exp(-abs(along) * 0.20) * step(0.0, along) * bright * streakStrength * 0.15;
        float twinkle = 0.70 + 0.30 * sin(CameraTime.w * (1.1 + seed*3.0) + seed*40);
        float3 starColor = lerp(float3(0.42,0.62,1.0), float3(1.0,0.78,0.52), Hash21(key + 22.4));
        color += (star + halo + spike + streak) * twinkle * starColor;
    }

    // Helios sits at the system origin. Disk, corona and radial flare are a procedural bloom pass.
    float3 sunDirection = normalize(-CameraTime.xyz);
    float sunAlignment = saturate(dot(ray, sunDirection));
    float sunDisk = smoothstep(0.99975, 0.99997, sunAlignment);
    float corona = pow(sunAlignment, 22.0) * 0.16 + pow(sunAlignment, 7.0) * 0.045;
    float coronaNoise = 0.82 + 0.18 * Fbm(ray * 65.0 + CameraTime.w * 0.025);
    float diffraction = pow(saturate(1.0 - abs(sunAlignment - 0.9994) * 1700.0), 7.0);
    float3 solar = float3(1.0, 0.36, 0.085);
    color += solar * (corona * coronaNoise + diffraction * 0.11);
    color += float3(3.6, 2.05, 0.78) * sunDisk;
    float lens = pow(saturate(1.0 - abs(centered.x + centered.y * 0.17)), 12.0)
        * pow(sunAlignment, 9.0) * 0.018;
    color += float3(0.10,0.34,0.58) * lens;

    float arc = pow(saturate(1.0 - abs(ray.y + 0.16) * 3.4), 12) * 0.016;
    color += arc * float3(0.18,0.65,0.9);
    float vignette = 1.0 - saturate(dot(centered, centered) * 0.19);
    // Fixed spatial dither avoids per-frame random grain, which reads as high-frequency
    // screen flicker on bright stars and fine cockpit/HUD edges.
    float grain = (Hash21(floor(input.uv * Surface.xy)) - 0.5) * 0.003;
    return float4(Tone(color * vignette + grain), 1);
}

struct MeshInput { float3 position : POSITION; float3 normal : NORMAL; float3 color : COLOR; };
struct MeshOutput { float4 position : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 color : COLOR; float viewZ : TEXCOORD2; };
float3 RotateEuler(float3 p) {
    float sx=sin(Detail.z), cx=cos(Detail.z), sy=sin(Detail.x), cy=cos(Detail.x), sz=sin(Detail.w), cz=cos(Detail.w);
    p = float3(p.x, cx*p.y - sx*p.z, sx*p.y + cx*p.z);
    p = float3(cy*p.x + sy*p.z, p.y, -sy*p.x + cy*p.z);
    return float3(cz*p.x - sz*p.y, sz*p.x + cz*p.y, p.z);
}
MeshOutput VSBody(MeshInput input) {
    MeshOutput o;
    int mode = (int)round(Detail.y); // 0 world mesh, 1 camera pulse, 2 cockpit, 3 emissive, 4 player-local ship
    float3 localPosition = input.position * Object.w;
    float3 localNormal = input.normal;
    float3 relative;
    if (mode == 2) {
        // Cockpit geometry is modeled in pilot-local coordinates and follows all six flight axes.
        relative = localPosition + Object.xyz;
        // The cockpit belongs to the airframe, not the pilot's head. Camera basis can
        // yaw/pitch independently while this mesh stays fixed to the ship's attitude.
        o.world = CameraTime.xyz + PilotRight.xyz * relative.x + PilotUp.xyz * relative.y + PilotForward.xyz * relative.z;
        o.normal = normalize(PilotRight.xyz * localNormal.x + PilotUp.xyz * localNormal.y + PilotForward.xyz * localNormal.z);
    } else if (mode == 1) {
        float3 pulsePosition = RightExposure.xyz * localPosition.x + UpAtmosphere.xyz * localPosition.y + ForwardAspect.xyz * localPosition.z;
        float3 pulseNormal = RightExposure.xyz * localNormal.x + UpAtmosphere.xyz * localNormal.y + ForwardAspect.xyz * localNormal.z;
        o.world = pulsePosition + Object.xyz;
        o.normal = normalize(pulseNormal);
        relative = o.world - CameraTime.xyz;
    } else if (mode == 4) {
        // External chase view: the Viper mesh uses the ship's full quaternion basis.
        o.world = Object.xyz + PilotRight.xyz * localPosition.x + PilotUp.xyz * localPosition.y + PilotForward.xyz * localPosition.z;
        o.normal = normalize(PilotRight.xyz * localNormal.x + PilotUp.xyz * localNormal.y + PilotForward.xyz * localNormal.z);
        relative = o.world - CameraTime.xyz;
    } else {
        float3 rotated = RotateEuler(localPosition);
        float3 rotatedNormal = RotateEuler(localNormal);
        o.world = rotated + Object.xyz;
        o.normal = normalize(rotatedNormal);
        relative = o.world - CameraTime.xyz;
    }
    o.color = input.color;
    float z = dot(relative, ForwardAspect.xyz);
    o.viewZ = z;
    if (z <= 0.12) { o.position = float4(2,2,2,1); return o; }
    float2 projected = float2(dot(relative, RightExposure.xyz) / (Options.w * ForwardAspect.w), dot(relative, UpAtmosphere.xyz) / Options.w) / z;
    float2 pixel = Viewport.xy + (projected * float2(0.5,-0.5) + 0.5) * Viewport.zw;
    float2 clip = pixel / Surface.xy * float2(2,-2) + float2(-1,1);
    o.position = float4(clip * z, 100.0/99.95*z - 5.0/99.95, z);
    return o;
}
float4 PSBody(MeshOutput input, bool front : SV_IsFrontFace) : SV_Target {
    clip(input.viewZ - 0.12);
    int mode = (int)round(Detail.y);
    float3 tint = max(Tracking.rgb, 0.001);
    float intensity = max(Tracking.w, 0.01);
    if (mode == 1 || mode == 3 || (mode == 4 && Tracking.w > 1.5)) {
        float pulse = 0.88 + 0.12 * sin(CameraTime.w * 42.0 + input.world.z * 2.0);
        float3 glow = input.color * tint * intensity * pulse;
        return float4(Tone(glow), 1);
    }
    float3 n = normalize(input.normal) * (front ? 1 : -1);
    float3 light = normalize(float3(-0.48,0.72,0.46));
    float diffuse = 0.14 + saturate(dot(n, light)) * 0.96;
    float3 lit;
    if (mode == 5) {
        // Procedural planetary surface: warped continents, shelf seas, high cloud and
        // latitude bands. This is deliberately generated in-shader; no image assets needed.
        float3 sphereN = normalize(input.normal);
        float warp = Fbm(sphereN * 3.7 + float3(2.1, 6.4, 1.7));
        float continents = smoothstep(0.47, 0.61, Fbm(sphereN * 5.2 + warp * 1.8));
        float shelves = smoothstep(0.39, 0.49, Fbm(sphereN * 8.7 + float3(8.0, 2.0, 4.0)));
        float bands = 0.5 + 0.5 * sin(sphereN.y * 21.0 + Fbm(sphereN * 2.2) * 5.0);
        float3 ocean = lerp(float3(0.018,0.09,0.17), float3(0.035,0.25,0.34), shelves * 0.72);
        float3 land = lerp(float3(0.10,0.20,0.17), float3(0.39,0.36,0.25), Fbm(sphereN * 11.0 + 1.4));
        land = lerp(land, float3(0.72,0.76,0.70), smoothstep(0.63,0.84,abs(sphereN.y)) * 0.72);
        float3 surface = lerp(ocean, land, continents);
        surface = lerp(surface, surface * (0.82 + bands * 0.25), 0.34);
        float cloudNoise = Fbm(sphereN * 13.0 + float3(CameraTime.w * 0.012, 3.0, 7.0));
        float clouds = smoothstep(0.60,0.76,cloudNoise) * (1.0 - smoothstep(0.89,0.98,abs(sphereN.y)) * 0.35);
        surface = lerp(surface, float3(0.64,0.78,0.82), clouds * 0.72);
        float3 viewDirection = normalize(CameraTime.xyz - input.world);
        float rim = pow(1.0 - saturate(dot(n, viewDirection)), 3.4);
        lit = surface * tint * diffuse;
        lit += rim * float3(0.035,0.24,0.46) * 1.8;
        lit += clouds * saturate(dot(n, light)) * float3(0.14,0.20,0.22);
    } else {
        lit = input.color * tint * diffuse;
        float3 viewDirection = normalize(CameraTime.xyz - input.world);
        float rim = pow(1.0 - saturate(dot(n, viewDirection)), 3.0);
        lit += rim * float3(0.035,0.15,0.23);
    }
    // The scene's high-energy corona, emissive bolts and blast cores retain highlight headroom
    // for the one-pass filmic optical glow used by the starfield shader.
    return float4(Tone(lit), 1);
}
