# Showcase update: SOL atlas and Starfighter cockpit

Apply this overlay to the Noesis repository at the showcase-followup baseline (6ea2619). No dependency versions are changed.

## Orbital Command

SOL opens a planar heliocentric J2000 ecliptic atlas independent of the fictional Kepler Anchorage simulation. The distance slider sets the vertical half-span in AU (32 to 2); the radius slider independently exaggerates body sizes. At radius ×1 bodies use their physical relative diameters and can be subpixel. Exaggerated bodies have minimum and maximum visibility limits. Reference tracks are fitted JPL ellipses rather than integrated trajectories. The full-system view is the default. Planet artwork and Saturn's ring are illustrative; lighting, rotation and sky orientation are not epoch-correct.

To obtain the actual NASA/JPL state vectors and mapped artwork, run from the repository root:

```powershell
python -m pip install numpy Pillow
python eng/assets/fetch-horizons.py --date 2026-11-25
python eng/assets/prepare-solar-art.py --download
```

Omit the date for the current UTC calendar date (queried as a TDB calendar epoch). The existing Horizons CSV is preferred; without it the atlas uses JPL's published approximate fitted elements (1800–2050), with the Earth–Moon barycenter in place of Earth. The approximate current epoch uses a fixed 69.184-second UTC-to-TT/TDB approximation. The bundled images are procedural fallback illustrations, not NASA imagery. The fetch script prepares a NASA SVS sky image and shaded globes from source maps on a connected machine. The atlas backdrop is a 2D image, not a directional 3D skybox. Provenance appears in the atlas and in Ui/Solar/provenance.json.

Sources:
- JPL approximate positions: https://ssd.jpl.nasa.gov/planets/approx_pos.html
- JPL Horizons: https://ssd.jpl.nasa.gov/horizons/
- NASA SVS Deep Star Maps 2020: https://svs.gsfc.nasa.gov/4851/ (NASA/Goddard Space Flight Center Scientific Visualization Studio)
- Solar System Scope textures: https://www.solarsystemscope.com/textures/ — CC BY 4.0; modified by projection, lighting and alpha masking. License: https://creativecommons.org/licenses/by/4.0/

## Starfighter

The bundled cockpit.mesh was rebuilt from the supplied CC BY 4.0 Spaceship 001 - Cockpit Concept archive. The adjacent attribution is required when distributing the derived mesh. It uses the existing material-color renderer, not the original textured PBR pipeline. The cockpit eye transform is provisional and requires visual qualification. The external cockpit now loads by default if available; set DXSHARP_COCKPIT_PREVIEW=0 for the legacy procedural cockpit.

The Noesis logo is the supplied artwork, converted to a multi-resolution Windows icon and assigned to both executable resources and WinForms windows. The Houdini product branding is separate.

## Verification

Build and run both samples on Windows, then run tests/Verification. Check the two-row top bar at normal, narrow, fullscreen and high-DPI sizes; SOL/LOCAL transitions, sliders, texture loading, and cockpit placement. Windows and Noesis runtime validation remain pending. No proprietary Arkaen content is included.

## Live 3D SOL and controls (follow-up)

The header's **3D SOL** button selects a GPU-rendered analytic ray/sphere viewport, distinct from the existing **SOL** atlas. Drag to orbit, middle drag to pan, wheel to zoom, click a visible planet to focus, and use the date controls to advance or rewind the Julian epoch. The rate is in simulated days per second; the existing simulation pause also pauses the date. The model evaluates the published JPL 1800–2050 fitted elements in double precision and uses the Horizons cache epoch as its starting date, not the cached positions as a continuously valid ephemeris. Earth means the Earth–Moon barycenter. Circular orbit guides, ecliptic Saturn rings, procedural surface colors and procedural directional sky are illustrative. The downloaded artwork remains in the atlas; the 3D renderer does not sample it yet. Radius exaggeration uses a cap and a minimum apparent planet size except at true scale. Planet labels are culled for overlaps; picking is screen-space. Local fleet play remains separate and continues while browsing SOL.

Ship transfers use bounded radial acceleration and smooth heading changes, coast/flip/burn/brake states and throttle-dependent exhaust. This is a controlled flight model, not a two-body transfer integrator.

The procedural cockpit is once again the default while the imported eye mount is being qualified. To test the imported mesh with a provisional, adjustable eye:

```powershell
$env:DXSHARP_COCKPIT_PREVIEW='1'
$env:DXSHARP_COCKPIT_EYE='1.1,0.65,-0.65'
$env:DXSHARP_COCKPIT_YAW='0' # try 180 or +/-90 if the seat faces another direction
$env:DXSHARP_COCKPIT_SCALE='1'
dotnet run --project samples/Starfighter
```

These values are in the already baked mesh coordinates. They are not a visually qualified seat mount. Right mouse steers and middle mouse head-looks even over HUD backgrounds; left mouse still gives controls priority. CommunityToolkit.Mvvm 8.4.2 provides observable properties and relay commands in both samples and the linked-source verification project. Regenerate NuGet lockfiles with a normal restore before locked-mode CI. Windows runtime and HLSL qualification pending.
