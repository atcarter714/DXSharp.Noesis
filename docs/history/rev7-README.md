# DXSharp + Noesis Showcase — Orbital Command & Starfighter

Two separate, interactive desktop samples demonstrating the DXSharp D3D12 host and NoesisGUI integration.

**Revision 6 — VANGUARD flight experience:** Reworked Starfighter into a six-degree-of-freedom fighter inside a persistent orbital system. Added a modeled Viper-style hull and pilot cockpit, cockpit/chase camera, target-projected HUD, velocity parallax, procedural combat audio, and a substantially redesigned Noesis avionics overlay. The original Orbital Command sample remains unchanged. See `CHANGES.md`.

**Revision 3 — allocator correction:**

**Revision 2 — rendering, input and layout fixes:** UI recording now uses a dedicated command list; camera gestures have explicit ownership; camera framing, labels and picking share the measured world viewport. Render controls fit their panel, the briefing offers interface-size controls, and the shaders filter ring/star detail to reduce motion shimmer. See `CHANGES.md`.

## Open and run

1. Extract this entire folder. Open **DXSharp.Noesis.Showcase.sln** in Visual Studio 2026 with the **.NET desktop development** workload and the .NET 10 SDK. An equivalent `.slnx` is also included.
2. Allow NuGet restore. Select **OrbitalCommand** or **Starfighter** as the startup project, depending on which showcase you want to run.
3. Run **Release / Any CPU**. The application targets **Windows x64**. No DLL browsing, sibling DXSharp checkout, GitHub access, or generator setup is required.

Double-click `Run.cmd` for Orbital Command or `Run-Starfighter.cmd` for the new cockpit game after installing the SDK. From a terminal in this folder:

```powershell
dotnet build DXSharp.Noesis.Showcase.sln -c Release -m:1
dotnet run --project samples/OrbitalCommand -c Release --no-build
dotnet run --project samples/Starfighter -c Release
```

The first restore downloads Noesis and Microsoft's runtime dependencies from NuGet. The **DXSharp packages are included locally**. This is a source solution, not a prebuilt or completely offline application distribution.

## The showcase

- Rotating procedural planet with oceans, land, cloud cover, night-side lights, atmospheric scattering approximation, shadowed rings, starfield and orbital tracks.
- Indexed ship and station meshes with lighting, engine geometry, planetary occlusion and a highlighted selected vessel.
- Camera orbit, zoom and focus; labels project from the same world/camera coordinates as the mesh renderer.
- Six initial vessels, two additional launches, a fleet capacity, credit accounting and survey research rewards. Survey, patrol and recall change the simulation.
- Noesis XAML command deck with two-way fleet selection, MVVM commands, live telemetry, editable call sign, progress, mission feed, modal briefing, animation, scrolling and renderer controls.
- A shared D3D12 device/queue, isolated scene and UI command lists, two fenced frames, explicit depth/stencil targets, DPI handling, resize synchronization and drained shutdown.

The simulation uses illustrative circular tracks and simple radial transfers. This is an integration showcase, not the fleet-battler branch or an orbital dynamics engine.

## Controls

| Input | Action |
| --- | --- |
| Left/right drag over the world | Orbit camera |
| Middle drag over the world | Pan |
| Wheel over the world | Zoom |
| Click a ship, label or fleet row | Select vessel |
| F | Focus and follow selected vessel |
| Home | Reset camera |
| Space | Pause/resume simulation |
| F11 | Toggle fullscreen |
| Escape | Dismiss briefing |

UI panels and modal backgrounds block world input. Text fields retain keyboard input. Use the render controls to change exposure/atmosphere or toggle labels, orbit tracks and the cinematic camera. The time-scale slider changes simulation speed, not the render loop. Use **UI − / +** in the briefing to adjust interface size; large displays also receive a bounded automatic size increase. Dragging over a ship label now controls the camera, and a short click selects that label.

## The Starfighter sample — VANGUARD / K-09 Intercept

A separate cockpit-first arcade flight experience that uses the same DXSharp D3D12 device, direct queue, swap chain, two-frame/fence host, and native Noesis D3D12 overlay. It is not a second copy of the Orbital Command layout.

- Six-degree-of-freedom inertial flight with quaternion yaw, pitch and roll; local thrust, reverse, strafe, vertical maneuvering, afterburner and FOV control. RMB head-look is independent of ship attitude and weapon boresight; the cockpit remains fixed to the airframe while the pilot looks around.
- Persistent solar-system scene with Helios, two orbiting planets with procedural terrain/cloud shading, a particulate ring, 138 seeded irregular low-poly asteroids and a continuously replenished orbital belt.
- Procedural Viper-style fighter hull, swept delta wings, twin cannon rails, faceted canopy, 3D cockpit coaming, canopy spars, MFD bezels, flight stick, throttle housings and illuminated control banks. **C** switches between the pilot cockpit and a quaternion-following external chase camera.
- Twin projectile cannon with continuous fire, weapon heat and cooldown, contact locks, shield/hull damage, collisions, expanding blast meshes, independently tumbling asteroid fragments, scoring chains and escalating contracts.
- A distinct glass-canopy Noesis HUD: heading ribbon, live radar with animated sweep and item-bound contacts, bank/pitch-responsive ladder, persistent target selection with animated world-projected brackets, flight vector readouts, shield/heat/afterburner gauges, event feed, pause/redeploy dialogs, custom control template and animated alert symbology.
- Velocity-aware star/nebula parallax and speed streaks, a procedural solar corona/diffraction flare, HDR-style tone mapping, explosion flashes and original synthesized stereo engine/cannon/impact/boost audio using native Windows waveOut. No third-party audio runtime or copyrighted sound files are required.
- The D3D12 3D world renders to the swap-chain backbuffer first; the Noesis XAML avionics overlay renders afterward on its isolated UI command list. Scene and UI allocators remain independent and are retired by the same final UI fence.

### Starfighter controls

| Input | Action |
| --- | --- |
| Left / Right arrows | Yaw left / right |
| Up / Down arrows | Pitch up / down |
| Q / E | Roll left / right |
| W / S | Forward thrust / reverse |
| A / D | Strafe left / right |
| R / F | Vertical thrusters up / down |
| Right mouse + drag | Independent cockpit free-look; release to return the cursor |
| Left mouse | Operate visible Noesis cockpit controls; never fires |
| Space | Fire twin pulse cannons; hold for continuous fire |
| Shift | Hold afterburner |
| Cockpit BOOST button | Toggle afterburner |
| C / CAM button | Switch cockpit / external chase view |
| Mouse wheel over the world | Adjust field of view |
| P or Escape | Pause / resume |
| R while paused or destroyed | Redeploy |
| F11 | Toggle fullscreen |

Run the game with `Run-Starfighter.cmd` or:

```powershell
dotnet run --project samples/Starfighter -c Release
```

The optional `--smoke` argument creates a WARP device, enables the D3D12 debug layer, runs 120 frames and exercises the same render/present/shutdown path without requiring a physical GPU. It does not replace an interactive hardware-adapter test.

The solar corona, star halos, diffraction, speed streaks and impact flashes are analytic one-pass optical effects with a filmic tone curve. This version does not claim a separate multi-resolution render-target bloom/blur chain. The fighter, canopy/cockpit, planets, asteroids, fragments and effects are generated meshes/shaders; no external 3D model downloads or attribution-dependent assets are required. Audio is synthesized at runtime via Windows WinMM waveOut.

## What references what

| Project | References |
| --- | --- |
| `samples/OrbitalCommand` | Project reference to `DXSharp.Noesis`; `Noesis.App` **4.0.1** |
| `samples/Starfighter` | Project reference to `DXSharp.Noesis`; `Noesis.App` **4.0.1** |
| `src/DXSharp.Noesis` | `DXSharp.Core` **0.4.0-net10-preview.2**; `Noesis.GUI` **4.0.1.18263** |
| `DXSharp.Core` package | `DXSharp.Native` **0.4.0-net10-preview.2** transitively; Microsoft's D3D12 and DXC runtime packages |
| `tests/Verification` | Noesis plus linked shared model/UI-input/geometry sources; no graphics device required |

`NuGet.Config` maps `DXSharp.*` to the included `packages/` feed. A solution-local `.packages-cache/` prevents an older package with the same preview version in your global cache from overriding these binaries. `packages/SDK-Provenance.json` records source commit and package hashes.

The included SDK packages were built from **integration/fleet-sdk-parity**, commit **df03fef814fbbcd47542619bd5b33ff8d27453ee**. No changes to the DXSharp repository are required by this sample. There is **no consumer CsWin32 generator**, duplicate COM interface definition, direct `DXSharp.Core.dll` reference, or proprietary SDK source copied into DXSharp.

For explicit source development against that SDK revision:

```powershell
dotnet build DXSharp.Noesis.Showcase.sln -c Release -m:1 -p:UseDXSharpSource=true -p:DXSharpRoot=C:\source\DXSharp
```

Setting `DXSharpRoot` alone does not change the normal package-based build. Rebuild all projects after switching modes. Keep source and package SDK APIs aligned.

## Noesis license

Noesis is restored from its official NuGet packages; its proprietary binaries are not bundled in this source ZIP. Without a license, the runtime uses Noesis' evaluation mode and reports its evaluation restrictions in the console. To use your licensed SDK, set both environment variables before starting Visual Studio or the application:

```powershell
$env:NOESIS_LICENSE_NAME = 'your registered name'
$env:NOESIS_LICENSE_KEY = 'your license key'
dotnet run --project samples/OrbitalCommand -c Release
```

Keep credentials out of source control. This integration/samples are MIT licensed; Noesis and Microsoft dependencies retain their own licenses. The integration remains separate from DXSharp and can serve a licensed commercial application.

## Verification

Run the device-independent checks (Windows x64 or Linux x64):

```powershell
dotnet run --project tests/Verification -c Release
```

This loads the actual XAML and theme, exercises pointer/character input, selection and command bindings, sliders, modal hit testing, 100%/125%/150%/200% DPI changes, simulation pause/rewards/capacity, projection/picking and generated mesh data. The runner returns nonzero on failure or a Noesis error. It also loads the Starfighter cockpit XAML, exercises cockpit commands, camera switching and pause/redeploy bindings, advances the inertial solar-system simulation, and validates asteroid, pulse, cockpit, fighter and afterburner mesh data.

A focused Windows allocator/submission check, without creating a window or initializing Noesis:

```powershell
dotnet run --project samples/OrbitalCommand -c Release -- --allocator-smoke --warp --debug
```

It uses the host's actual frame-slot reset method for 64 two-list frames and verifies GPU buffer readback. Omit `--debug` if Graphics Tools is unavailable.

Windows rendering checks:

```powershell
dotnet run --project samples/OrbitalCommand -c Release -- --smoke
```

`--smoke` uses WARP and the D3D12 debug layer, renders sixty frames, issues real UI clicks, launches a vessel, resizes twice, checks mesh draws, UI batches and GPU-read HUD text on every frame, drains the GPU, and rejects device removal or debug-layer errors. Windows **Graphics Tools** must be installed for the debug layer. `--warp` alone runs interactively without requiring the debug layer; `--debug` enables it on the hardware adapter.

See `VALIDATION.md` for the checks actually performed on this ZIP and the Windows execution boundary.

## Integration lifecycle

`D3D12Overlay` owns a Noesis view/render device and a D24S8 stencil target. `Resize` requires the host queue to be drained. `Record` receives a **dedicated, freshly reset UI command list**, monotonically increasing host frame IDs, and the latest frame known complete on the same queue. It records Noesis offscreen work, restores the host RTV/viewport/stencil target, and then records the onscreen UI without a foreign graphics pass between those phases.

The host records and submits its **scene list first**, leaving the back buffer in RenderTarget state. It then submits the UI list, whose final barrier transitions the buffer to Present. The frame slot stores the **UI submission's fence**, so waiting that fence also retires the scene submission. Each of the two slots owns two command lists and **two independent allocators**. Both allocators reset only after that slot’s final UI fence completes. A D3D12 allocator cannot serve two simultaneously recording lists. Noesis frame IDs describe completion of the whole host frame, not individual submissions. Keep the overlay alive until the final frame completes.

This replaces the previous `Record(..., Action drawScene)` callback API. Mixing external PSO/root/descriptor updates into Noesis' pass can invalidate its cached GPU-state assumptions. The host drains before resize or disposal, keeps the HWND/message pump alive during shutdown, and releases the swap chain before allowing the window to close.

The scene uses DXSharp's managed mesh, pipeline, root-constant, swap-chain and fence APIs. The adapter obtains typed native device/command-list leases through `DXSharp.Interop.NativeInterface<T>` only at Noesis' native boundary. It uses the generated interfaces supplied by `DXSharp.Native`.

`OverlayInput` recognizes controls and ancestors tagged `InputSurface`. `SceneInput` assigns gesture ownership at pointer-down using that hit test, rather than treating Noesis' handled return value as camera ownership. A world drag stays a world drag across panels; a UI drag never moves the camera. Labels are decorative Noesis elements with CPU picking, so they cannot capture and swallow a camera drag. Text editing retains typed keys; application shortcuts take priority over list-box type-ahead when a text field is not being edited.

Input coordinates enter the Noesis view in physical pixels; hit tests use logical coordinates. The host measures `WorldSurface` through `PointToScreen`, and its physical rectangle drives both shader projection and CPU projection/picking. Labels are rounded to physical pixels, kept within that rectangle and moved to avoid overlap. `ThemeFontProvider` resolves fonts embedded in the Noesis theme assembly.

## Files

- `src/DXSharp.Noesis/`: reusable D3D12 overlay and UI input boundary.
- `samples/OrbitalCommand/`: WinForms HWND host, renderer, shared procedural geometry, simulation, view model, theme-font provider, HLSL and XAML.
- `tests/Verification/`: executable checks without a GPU or WinForms host.
- `HudReadback.cs`: optional Windows smoke-test capture using the SDK-provided native interface boundary; each slot checks its capture after its own fence retires.
- `packages/`: pinned DXSharp packages, provenance and upstream MIT license.
- `Preview.png`: visual preview rendered with Noesis' OpenGL backend and a translation of the scene shaders on Mesa software rendering. It checks appearance; it is not a D3D12 execution result.

The XAML is ordinary external source and can be edited independently from the host. The sample loads it at startup. Noesis Studio project discovery, live reload and production asset packaging are outside this deliverable.
