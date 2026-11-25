// 40 DWORDs shared by the procedural background and indexed mesh passes.
cbuffer Scene : register(b0) {
    float4 CameraTime;
    float4 ForwardAspect;
    float4 RightExposure;
    float4 UpAtmosphere;
    float4 Options; // orbit visibility, survey progress, survey active, tan(FOV / 2)
    float4 Object; // translation + scale
    float4 Detail; // heading, selection, station, reserved
    float4 Tracking;
    float4 Viewport; // physical x, y, width, height of the usable world panel
    float4 Surface; // full target width and height
};
static const float3 Sun = normalize(float3(-0.65, 0.55, 0.8));
float Hash(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
float Noise(float3 p) {
    float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
        lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + 1), f.x), f.y), f.z);
}
float Fbm(float3 p) { float sum = 0, weight = 0.5; [unroll] for (int i = 0; i < 5; i++) { sum += Noise(p) * weight; p = p * 2.03 + 7.1; weight *= 0.5; } return sum; }
float Sphere(float3 ro, float3 rd, float radius) {
    float b = dot(ro, rd), h = b * b - dot(ro, ro) + radius * radius;
    return h < 0 ? 1e5 : (-b - sqrt(h) > 0 ? -b - sqrt(h) : 1e5);
}
float3 Tone(float3 value) { return pow(1 - exp(-max(value, 0) * RightExposure.w), 1.0 / 2.2); }
struct Fullscreen { float4 position : SV_Position; float2 uv : TEXCOORD0; };
Fullscreen VSPlanet(uint id : SV_VertexID) {
    Fullscreen o; float2 uv = float2((id << 1) & 2, id & 2);
    o.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); o.uv = uv; return o;
}
float4 PSPlanet(Fullscreen input) : SV_Target {
    float2 uv = (input.position.xy - Viewport.xy) / Viewport.zw;
    float2 xy = (uv * 2 - 1) * float2(Viewport.z / Viewport.w, -1);
    float3 ro = CameraTime.xyz;
    float3 rd = normalize(ForwardAspect.xyz + Options.w * (xy.x * RightExposure.xyz + xy.y * UpAtmosphere.xyz));
    float t = Sphere(ro, rd, 1);
    float3 nebula = rd * 2.8 + float3(3, 1, 8);
    float mist = pow(Fbm(nebula), 3) * 0.10;
    float3 color = float3(0.001, 0.0022, 0.0055) + mist * lerp(float3(0.035, 0.065, 0.19), float3(0.04, 0.16, 0.17), Noise(nebula * 2));
    // Stable sky-space stars with screen-space footprints; no hard voxel-cell blinking.
    float2 sky = float2(atan2(rd.z, rd.x) / 6.2831853, asin(clamp(rd.y, -1.0, 1.0)) / 3.1415927) * float2(900, 450);
    float2 cell = floor(sky);
    float footprint = max(length(fwidth(sky)), 0.02);
    [unroll] for (int sy = -1; sy <= 1; sy++) {
        [unroll] for (int sx = -1; sx <= 1; sx++) {
            float2 key = cell + float2(sx, sy);
            float seed = Hash(float3(key, 2));
            float2 center = key + float2(Hash(float3(key, 5)), Hash(float3(key, 11)));
            float distance = length(sky - center);
            float star = exp(-distance * distance / (footprint * footprint * 0.45 + 0.0007));
            star *= step(0.9955, seed) * min(1, 0.2 / footprint);
            color += star * lerp(float3(0.45, 0.65, 1.0), float3(1.0, 0.74, 0.42), Hash(float3(key, 8))) * 0.16;
        }
    }
    if (t < 1e4) {
        float3 n = ro + rd * t;
        float a = CameraTime.w * 0.012, cs = cos(a), sn = sin(a);
        float3 map = float3(cs * n.x - sn * n.z, n.y, sn * n.x + cs * n.z);
        float warp = Fbm(map * 2.2 + 4.7);
        float terrain = Fbm(map * 4.5 + warp * 0.8 + 1.4);
        float land = smoothstep(0.49, 0.515, terrain);
        float coast = smoothstep(0.44, 0.505, terrain);
        float3 ocean = lerp(float3(0.003, 0.022, 0.065), float3(0.012, 0.19, 0.23), coast * 0.7);
        float arid = smoothstep(0.51, 0.62, terrain + (1 - abs(n.y)) * 0.06);
        float3 ground = lerp(float3(0.028, 0.09, 0.044), float3(0.28, 0.23, 0.13), arid);
        ground *= 0.8 + Noise(map * 76) * 0.35;
        float ice = smoothstep(0.82, 0.96, abs(n.y) + Noise(map * 16) * 0.06);
        float3 albedo = lerp(lerp(ocean, ground, land), float3(0.7, 0.81, 0.84), ice);
        float day = saturate(dot(n, Sun));
        float cloud = smoothstep(0.51, 0.68, Fbm(map * 7 + float3(CameraTime.w * 0.004, 0, 0))) * 0.8;
        color = lerp(albedo, float3(0.82, 0.88, 0.94), cloud) * (0.018 + day * 1.5);
        color *= lerp(float3(0.82, 0.9, 1.0), float3(1.0, 0.97, 0.89), day);
        color += pow(saturate(dot(reflect(-Sun, n), -rd)), 75) * (1 - land) * day * 0.7;
        float cityFilter = 1 - smoothstep(0.4, 1.4, length(fwidth(map * 115)));
        float cities = pow(saturate((Noise(map * 115) - 0.66) * 5), 3) * land * cityFilter;
        cities *= (1 - smoothstep(-0.18, 0.12, dot(n, Sun))) * (1 - cloud);
        color += cities * float3(1.0, 0.48, 0.14) * 0.55;
        color += pow(1 - saturate(dot(n, -rd)), 3) * float3(0.02, 0.24, 0.52) * UpAtmosphere.w * (0.2 + day);
    }
    float closest = length(ro + rd * max(0, -dot(ro, rd)));
    float atmospheric = exp(-max(closest - 1, 0) * 35) * step(1, closest);
    color += atmospheric * float3(0.015, 0.18, 0.37) * UpAtmosphere.w;
    // Physical ring plane: shadowed by the globe, composited in ray order.
    float3 ringN = normalize(float3(0, 1, 0.17));
    float denom = dot(rd, ringN);
    float ringT = -dot(ro, ringN) / (abs(denom) < 0.00001 ? 0.00001 : denom);
    float3 ringP = ro + rd * ringT;
    float radius = length(ringP);
    if (ringT > 0 && ringT < t && radius > 1.30 && radius < 1.80) {
        float radialFootprint = max(fwidth(radius), 0.0001);
        // Integrate the high-frequency ring bands over a pixel to suppress motion shimmer.
        float phase = radialFootprint * 340 * 0.5;
        float bandFilter = sin(phase) / max(phase, 0.0001);
        float bands = 0.55 + 0.18 * sin(radius * 87) + 0.17 * sin(radius * 340) * bandFilter;
        float opacity = smoothstep(1.30, 1.33, radius) * (1 - smoothstep(1.76, 1.80, radius));
        opacity *= 1 - (1 - smoothstep(0.004, 0.014, abs(radius - 1.61))) * 0.92;
        float shadow = Sphere(ringP + Sun * 0.002, Sun, 1) < 1e4 ? 0.13 : 1;
        color = lerp(color, lerp(float3(0.24, 0.21, 0.17), float3(0.51, 0.43, 0.32), bands) * shadow, opacity * 0.85);
    }
    float planeT = -ro.y / (abs(rd.y) < 0.00001 ? 0.00001 : rd.y);
    float3 orbitP = ro + rd * planeT;
    float r = length(orbitP.xz);
    if (Options.x > 0.5 && planeT > 0 && planeT < t) {
        float delta = min(min(abs(r - 2.0), abs(r - 2.35)), min(abs(r - 2.75), abs(r - 3.05)));
        float aa = max(fwidth(r), 0.002);
        float track = 1 - smoothstep(aa * 0.35, aa * 1.6, delta);
        float dash = 0.75 + 0.25 * sin(atan2(orbitP.z, orbitP.x) * 42);
        color += track * dash * float3(0.008, 0.052, 0.074);
    }
    float targetT = dot(Tracking.xyz - ro, rd);
    float targetDistance = length(ro + rd * targetT - Tracking.xyz);
    if (targetT > 0 && targetT < t + 0.08) {
        float ring = exp(-abs(targetDistance - 0.16) * 170);
        color += ring * float3(0.03, 0.7, 0.76) * (0.65 + 0.35 * sin(CameraTime.w * 2));
        if (Options.z > 0.5) color += exp(-targetDistance * 18) * float3(0.02, 0.25, 0.3);
    }
    // Gentle lens vignette leaves the world legible behind glass panels.
    color *= 1 - saturate(dot(input.uv - 0.5, input.uv - 0.5) * 0.6);
    return float4(Tone(color), 1);
}
struct MeshInput { float3 position : POSITION; float3 normal : NORMAL; float3 color : COLOR; };
struct MeshOutput { float4 position : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 color : COLOR; };
float3 Rotate(float3 p) { float s = sin(Detail.x), c = cos(Detail.x); return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z); }
MeshOutput VSShip(MeshInput input) {
    MeshOutput o;
    float3 shape = Detail.z == 1 ? float3(0.85, 1.1, 1.25) : Detail.z == 2 ? float3(1.5, 1.3, 1.4) : Detail.z == 3 ? float3(1.2, 0.9, 1.05) : Detail.z == 4 ? float3(0.7, 0.8, 0.95) : float3(1, 1, 1);
    o.world = Rotate(input.position * Object.w * shape) + Object.xyz;
    o.normal = Rotate(input.normal / shape); o.color = input.color;
    float3 p = o.world - CameraTime.xyz;
    float z = dot(p, ForwardAspect.xyz);
    float2 clip = float2(dot(p, RightExposure.xyz) / (Options.w * Viewport.z / Viewport.w), dot(p, UpAtmosphere.xyz) / Options.w) / z;
    float2 pixel = Viewport.xy + (clip * float2(0.5, -0.5) + 0.5) * Viewport.zw;
    float2 fullClip = pixel / Surface.xy * float2(2, -2) + float2(-1, 1);
    o.position = float4(fullClip * z, 100.0 / 99.95 * z - 5.0 / 99.95, z);
    return o;
}
float4 PSShip(MeshOutput input, bool front : SV_IsFrontFace) : SV_Target {
    float3 toPoint = input.world - CameraTime.xyz;
    if (Sphere(CameraTime.xyz, normalize(toPoint), 1) < length(toPoint) - 0.015) discard;
    float3 n = normalize(input.normal) * (front ? 1 : -1);
    float diffuse = 0.09 + saturate(dot(n, Sun)) * 1.15;
    float emissive = step(0.8, max(input.color.g, input.color.b));
    float3 color = input.color * lerp(diffuse, 1.2, emissive);
    float specular = pow(saturate(dot(n, normalize(Sun - normalize(toPoint)))), 35) * 0.23 * (1 - emissive);
    color += specular * float3(0.7, 0.86, 1.0);
    color += pow(1 - abs(dot(n, normalize(-toPoint))), 3) * float3(0.02, 0.3, 0.37) * (0.5 + Detail.y);
    return float4(Tone(color), 1);
}
