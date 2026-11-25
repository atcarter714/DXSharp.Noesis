# Showcase follow-up: flight, ephemerides, asset and Studio foundation

This change is based on the starter repository; the Windows build and GPU test remain required. No proprietary SDK contents are included. The original atmosphere shading is unchanged.

## Flight controls

RMB drag in the scene steers ship attitude. MMB drag is independent head-look. LMB on a UI control is routed to Noesis; LMB on the scene aims and fires with a 15-degree gun gimbal. Space fires along boresight when no pointer aim is active. Q/E bank left/right; arrow keys yaw and pitch, WASD translate, R/F lift, Shift boost, C chase view. Keep RMB held and use Space to steer and fire together. The cursor indicates the requested pointer aim, not a prediction of the clamped ballistic solution; a lead/gimbal reticle is a subsequent improvement.

The cockpit is transformed ship-local -> world -> view. Hardware homogeneous clipping replaces invalid near-plane vertex rejection. The distant procedural star field is direction-only. Orbital Command header is content-sized and its value elements have regression checks.

## JPL Horizons snapshot

From the repository root, with Python 3 installed:

```powershell
python eng/assets/fetch-horizons.py --date 2026-11-25
```

Build OrbitalCommand and click SOL; LOCAL returns to the original scene. The inner/outer toggle uses two linear AU scales. The view shows the ecliptic XY projection of geometric positions at the explicit TDB epoch, with symbolic marker sizes. Velocity and Z are retained in the cache but not yet used for motion or 3D rendering. No network I/O occurs in the frame loop or normal CI. Raw API responses and signatures are stored in a separate provenance JSON. If data is absent or invalid the panel shows an error, not fictional fallback data. The existing local-orbit game remains separate. The Horizons endpoint must still be exercised on a network-enabled machine.

Reference: https://ssd-api.jpl.nasa.gov/doc/horizons.html

## Cockpit asset preview

The supplied Sketchfab asset is CC-BY-4.0, author Mixmamo.studio. Preserve `samples/Starfighter/Assets/cockpit-attribution.txt` with the model. The source glTF has 1,586 mesh primitives and 188,958 vertices. The converter flattens node transforms, transforms normals, handles mirrored winding, uses material base-color factors and chunks into 16-bit meshes. This is not a PBR importer: textures, UVs, specular/roughness and material lighting are not preserved. The source contains peripheral geometry and requires authoring/calibration; it is not proven to be a closed flight-ready cabin.

```powershell
python -m pip install numpy trimesh
python eng/assets/prepare-cockpit.py C:/path/to/scene.gltf --eye -0.55 0.96 -4.8 --scale 2
$env:DXSHARP_COCKPIT_PREVIEW = '1'
dotnet run --project samples/Starfighter -c Release
Remove-Item Env:DXSHARP_COCKPIT_PREVIEW
```

The eye/scale are provisional, not verified in the cockpit. The optional binary is ignored by git and copied to the output when present. With no preview environment variable the original procedural cockpit is used. If preview is explicitly enabled without a valid mesh it fails clearly. Iterate in Houdini/Blender/Maya: remove authoring guides, set a named pilot eye, close hull/roof, add transparent glazing with proper material pass, and author panel anchor transforms. Commit only licensed, reviewed source/derived assets with provenance.

## Real sky

NASA SVS Deep Star Maps 2020 (https://svs.gsfc.nasa.gov/4851/) supplies a linear half-float EXR celestial map, including bright stars; use the 4096x2048 or 8192x4096 full star map. Credit NASA Goddard Scientific Visualization Studio / Ernie Wright and review source/catalog conditions before redistribution. Convert to a mipmapped HDR texture (e.g. BC6H DDS) with an offline asset step. Map world direction to ICRF RA/declination, account for RA increasing to the left and any ecliptic-to-equatorial transform; sample without translational offsets. The texture path and skybox upload are not implemented in this change; the procedural shader remains the fallback.

## Studio and world UI

The standalone `.noesis` files use the actual CLR namespaces, not the assembly names. Open the sample project descriptors in Noesis Studio 4.0.1. For Starfighter, open `Avionics.xaml` as an authoring prototype; it contains fallback data and binds to the existing game's commands. It is not yet rendered in the cockpit. Verify runtime theme/font resources and design-time binding behavior in Studio. In the supplied native SDK, `Include/NsGui/Studio.h` declares `Noesis::Studio::Create(const char*, const Options&)`; `StudioOptions.h` provides XAML/texture/font factories, assembly registration, file event callbacks, save-state and exit, and parent window. The native distribution includes NoesisEditor.dll. The managed archive contains documentation/sample sources, not runtime assemblies; do not assume a matching managed Studio entry point. Check the actual restored Noesis.GUI assembly surface and editor/runtime build compatibility before adding an optional managed host or C ABI bridge.

WorldPanelGeometry now provides ray-to-physical-pixel picking for a finite planar surface, with a regression test. The next implementation is a separate Noesis view per MFD: create a shader-readable color target and matching stencil, record Noesis offscreen and onscreen passes into the panel RTV, transition to pixel-shader resource state, sample on the ship-local panel geometry, and route pointer capture/press/release using the same UV transform. Use nearest-hit selection, cockpit occlusion, and explicit resource-state/frame ownership. Keep screen HUD rendering independent. Do not share the current overlay stencil between concurrently recorded panels. The render-target sampling and input routing to the MFD are not yet implemented.
