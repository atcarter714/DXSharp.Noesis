# Revision 7 — VANGUARD cockpit and control correction

- Separated pilot head-look from fighter attitude. RMB drag now changes a bounded camera-only yaw/pitch offset; it no longer steers the airframe or moves the gun boresight. The cockpit mesh is transformed by the ship's basis, so it remains attached to the airframe while the pilot looks around.
- Reserved left-click for Noesis cockpit controls. Clicking empty scene space no longer fires or captures a trigger state. Space is the continuous-fire key; the explicit FIRE button remains a one-shot cockpit control. Added a visible CENTER PILOT VIEW button to recenter independent head-look.
- Moved both pulse-bolt origins to the actual twin cannon rails on the fighter hull (matching the external mesh at approximately +/-1.07 lateral and +5.18 forward), beyond the forward canopy structure. Projectiles now travel along the ship's forward axis, not the pilot's temporary head-look direction.
- Removed time-varying per-pixel film grain. The previous grain seed changed every frame and could produce visible high-frequency shimmer; the replacement is a low-amplitude, fixed spatial dither.
- Corrected the Starfighter README and cockpit help text to reflect the revised controls.

**Validation boundary:** source changes are complete, but this environment does not have the .NET 10 SDK, DXC, Noesis runtime or Windows/D3D12 device. The Windows verification runner and `Run-Starfighter.cmd --smoke` still need to be executed on the target machine.

# Revision 6 — VANGUARD flight experience

- Rebuilt the flight model around an unconstrained quaternion attitude and inertial velocity. Keyboard controls provide yaw, pitch, roll, forward/reverse thrust, lateral and vertical thrusters; right-button drag provides continuous free-look. Mouse fire, UI clicks and look capture use independent pointer state.
- Replaced the approaching asteroid wave with a persistent orbital system: Helios, two orbiting planets, a ring and a seeded/replenished 138-object asteroid belt. Asteroid destruction creates multiple tumbling, collidable shards and an expanding blast.
- Added a procedural Viper-style fighter mesh for an external chase camera, including a swept delta hull, twin cannon pods, canopy, fins and afterburner flames. The default view is a real 3D pilot cockpit with canopy framing, side MFDs, control banks, stick, throttle housings and coaming.
- Rebuilt the Noesis HUD as a transparent avionics overlay rather than reusing Orbital Command's dashboard grid. It includes a heading ribbon, radar sweep with live bound contacts, an attitude-responsive pitch ladder, animated world-projected target brackets with persistent pilot target selection, flight telemetry, weapons/afterburner controls, alert overlays and camera switching.
- Added true 6-DOF flight controls with cursor-recentered right-button aiming, so the pilot can continue sweeping aim beyond the desktop edges. Mouse firing, look capture and Noesis UI gestures remain independent.
- Added velocity-driven sky/nebula parallax and speed streaks, procedural planetary terrain/clouds, a solar corona and diffraction flare, HDR-style tone mapping and impact/explosion flashes.
- Added original synthesized stereo engine, cannon, boost, lock, impact, explosion, hull-hit and overheat sounds using native WinMM waveOut streaming, with a layered turbine/noise engine bed. No sound assets or external audio packages are required.
- Expanded the device-independent verification runner for six-axis flight, cockpit/chase camera offsets, attitude HUD binding updates, persistent target designation, command bindings and all generated mesh topologies/normals.
- Switched projectile collision to swept-segment tests so fast pulse bolts cannot tunnel through small asteroids or newly created shards between 60 Hz frames; made the target-lock and breakup verification setup deterministic.

**Build boundary:** This revision has not been compiled or run in this Linux artifact-building environment because .NET 10 and DXC are not installed. XAML/XML, JSON, source structure, geometry index/normal invariants and archive integrity were checked. Execute the included verification and Windows `--smoke` commands before treating the new renderer path as runtime-validated.

# Revision 5 — Starfighter command typing fix

- Changed `StarfighterGame.PauseCommand` from the `ICommand` interface type to its concrete `ActionCommand` type. `ActionCommand` still implements `ICommand`, so Noesis command binding is unchanged; the concrete type exposes the custom `Refresh()` method used to raise `CanExecuteChanged` during pause/restart state transitions.
- Corrects the two CS1061 errors at the pause-command refresh call sites.

# Revision 4 — Starfighter cockpit sample

Added a separate playable first-person asteroid-intercept game while retaining the Orbital Command sample and shared DXSharp + Noesis host.

- Added a DXSharp D3D12 procedural space background and indexed low-poly asteroid / pulse meshes.
- Added first-person flight, mouse look, maneuvering, afterburner, twin pulse fire, weapon heat, collisions, shield/hull damage, asteroid fragmentation, score combos and escalating waves.
- Added a fully Noesis-authored cockpit HUD with bound telemetry, reticle, scanner, event feed, animations, pause and redeploy overlays.
- Reused the proven shared device/queue/swap-chain architecture, dedicated Noesis overlay list and separate per-list frame allocators.
- Extended device-independent verification to load the cockpit XAML, exercise its commands and simulation state, and validate generated mesh geometry.

# Revision 3 — allocator correction

Revision 2 incorrectly opened both command lists against the same allocator. The scene reset puts that allocator into use by a recording list, so the UI reset then fails with `E_INVALIDARG` (`0x80070057`).

Each frame slot now owns an independent allocator for each command list. The final UI submission fence still protects both lists; both allocators are reset only after that fence completes. Rendering state isolation and the Revision 2 input/layout changes remain in place.

Added `--allocator-smoke` to exercise the exact host reset method for 64 two-list frames with GPU buffer readback, without initializing Noesis or creating a window. The Windows rendering smoke test remains available.

The D3D12 rule is documented in [ID3D12GraphicsCommandList::Reset](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12graphicscommandlist-reset).

# Revision 2

- Isolated Noesis offscreen/onscreen recording in a dedicated command list. Scene and UI lists execute in order on the same queue; the final UI fence protects both lists. Revision 3 corrects allocator ownership.
- Suppressed WinForms background painting on the D3D12 surface and reduced timer scheduling delay.
- Replaced handled-event camera gating with explicit pointer ownership. World drags work across labels/panels; UI drags stay in UI. Added middle-drag panning and reliable world-wheel routing after text editing.
- Application camera shortcuts run before list-box type-ahead, while text fields retain their input. F11 remains available during text editing.
- Focus now centers and follows the selected ship from an outward-facing view. Reset ends tracking and the cinematic camera.
- Measured the actual XAML world viewport for CPU/GPU camera projection, framing, label placement and picking. Removed the old whole-window projection offset.
- Added physical-pixel label rounding, overlap avoidance, bounds checks and leader lines. Labels do not capture pointer gestures.
- Increased the controls panel's height and removed its wrapping-dependent clipping. Made the briefing scrollable at small sizes and added interface-size controls, with bounded large-display scaling.
- Filtered procedural ring bands and sky-star footprints, softened night-side lights, revised terrain/material shading and varied ship proportions by role. Removed duplicate coplanar reverse ship faces that could produce depth fighting.
- Expanded pointer, focus, label, panning and DPI checks. The Windows smoke mode now renders 60 frames and checks actual HUD text via fenced GPU readback on each frame, in addition to mesh/batch/resize/device diagnostics.

The user's recording confirms that the original Windows host renders on the RTX 3080 Ti. This revision cross-builds and has passed the checks described in `VALIDATION.md`; its D3D12 fixes still require a Windows runtime run.
