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
