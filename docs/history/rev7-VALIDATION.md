# Validation — 18 November 2026

## Performed

- .NET SDK **10.0.403**, Linux x64 host, Windows cross-targeting.
- The uploaded Revision 3 solution built in both **Debug** and **Release** with **zero warnings and zero errors** using `-m:1`. This is the baseline result; the Revision 4 Starfighter additions have not been compiled in this artifact-building environment.
- The uploaded Revision 3 executable verification runner passed **4,260 assertions**. These include real Noesis XAML/theme loading, command/selection/slider/text bindings, mouse/character input, modal hit testing, 100%/125%/150%/200% DPI changes, pause/resume, survey rewards, launch capacity, camera projection/picking, and vertex/index validation. Most assertions validate generated geometry; the count is not a count of independent scenarios.
- No Noesis XAML, font or binding errors. Expected evaluation-license warnings remain when no license is supplied.
- DXC **1.9.2609.5** compiles the Revision 3 Orbital Command entry points `VSPlanet`, `PSPlanet`, `VSShip` and `PSShip` as Shader Model 6.0 with `-Ges -WX`. The Starfighter shader entry points still require compilation on the development machine.
- Actual Noesis UI rendering and a translated version of the scene shaders/mesh data were visually checked with OpenGL 4.5 on Mesa software rendering. `Preview.png` comes from that check.
- HUD text remained present in **90 consecutive Noesis OpenGL frames**, including UI scale changes, verified by reading rendered pixels. This is a separate appearance/persistence check; it does not exercise D3D12 command-list state.
- Pointer ownership checks use the same `SceneInput` controller as the Windows host, including world-to-panel drags, panel-to-world drags, wheel input after text focus, capture cancellation, label picking and middle-button panning.
- Build output contains managed `DXSharp.Core.dll`, `DXSharp.Native.dll`, Noesis managed assemblies, Windows x64 `Noesis.dll`, `dxcompiler.dll`, `dxil.dll`, XAML and HLSL assets.
- Bundled DXSharp packages were freshly built in Release from the source commit recorded in `packages/SDK-Provenance.json`. The SDK working tree is unchanged.

## Revision 2 Windows smoke test

`--smoke` now renders **60 frames**. Every frame copies a small text region to its frame slot's readback buffer. The slot validates that the UI text is present only after its UI fence completes; this retains the two-frame overlap instead of synchronizing each frame for readback. While the briefing is open, the capture moves to its return button. A disappeared HUD fails the test even if Noesis still reports nonzero batch counts.

## Revision 3 correction

Revision 2 failed on Windows at its second command-list reset because the first list was already recording with the same allocator. Revision 3 assigns an allocator to each list and shares only the final frame-completion fence. Debug/Release cross-builds and the existing verification runner are rechecked for this correction.

The included Windows `--allocator-smoke --warp --debug` mode uses the exact frame-slot reset method for 64 two-list frames, checks GPU readback and rejects D3D12 validation errors. It has not been executed on this Linux host.

## Execution boundary

The Windows D3D12 host and its WARP/debug-layer smoke test **have not been executed on this Linux host**. The OpenGL appearance check is not a substitute for D3D12 validation. Run the included Windows `--smoke` command to verify presentation, native Noesis D3D12 recording, depth/stencil compatibility, GPU frame retirement, resize and shutdown on the target machine.

The sample remains experimental. It does not claim production qualification, D3D11/ARM support, IME composition, device-loss recreation or a full game engine.

## Reproduce

```powershell
dotnet restore DXSharp.Noesis.Showcase.sln --locked-mode -m:1
dotnet build DXSharp.Noesis.Showcase.sln -c Release --no-restore -m:1
dotnet run --project tests/Verification -c Release --no-build
# On Windows, with Graphics Tools installed:
dotnet run --project samples/OrbitalCommand -c Release --no-build -- --smoke
```

All build outputs and restored package caches are excluded from the ZIP. The optional source-reference mode requires an SDK checkout with the APIs used by this sample; package mode is the default.


## Revision 4 Starfighter addition

The Starfighter sample was added as an independent project. Its D3D12 scene renderer uses the same package APIs and command-list submission pattern as Orbital Command; its Noesis cockpit is loaded through the shared application/theme provider. The device-independent verification runner now loads `Cockpit.xaml`, checks command bindings and pause/redeploy behavior, advances the game simulation and validates asteroid/pulse mesh indices and normals.

The Starfighter addition has **not been compiled or executed in this artifact-building environment**: the current container does not have the .NET SDK or DXC installed. Run `dotnet restore DXSharp.Noesis.Showcase.sln --locked-mode -m:1`, `dotnet build DXSharp.Noesis.Showcase.sln -c Release --no-restore -m:1`, and `dotnet run --project tests/Verification -c Release --no-build` on the development machine. Then run `dotnet run --project samples/Starfighter -c Release -- --smoke` on Windows with Graphics Tools installed, followed by the interactive hardware-adapter run.


## Revision 5 compile-error correction

The two Starfighter CS1061 errors (`ICommand` has no member `Refresh`) were caused by declaring `PauseCommand` as `ICommand` while invoking the custom `ActionCommand.Refresh()` method. The property is now declared as `ActionCommand`; because that class implements `ICommand`, Noesis binding compatibility is retained. Static source checks confirm the property declaration exposes `Refresh()` and both call sites resolve to that concrete property type. A .NET build was not run in this container because the .NET SDK is not installed.


## Revision 6 — VANGUARD flight experience

Revision 6 substantially replaces the Starfighter presentation and simulation: six-axis quaternion flight, persistent orbital asteroid belt, procedural Viper hull/cockpit geometry, external chase view, target-projected Noesis HUD, velocity parallax, procedural WinMM audio and a new glass-canopy avionics layout. The validation runner now includes the fighter and engine-flare meshes and camera switching.

The current artifact environment does not contain the .NET 10 SDK or DXC, so neither the C# solution nor the new HLSL could be compiled here, and D3D12/Noesis runtime execution was not possible. XML parsing, package-lock JSON parsing, C# delimiter/source checks, shader entry-point/constant-layout inspection, mesh index/normal calculations and ZIP integrity checks were performed. The source-only Starfighter checks include deterministic forward target-lock selection, target retention, swept projectile/asteroid collision, breakup fragments, cockpit/chase offsets (within the procedural engine-vibration amplitude), six-axis flight and UI binding state. The shader's solar corona, diffraction and emissive flashes are procedural bloom-style effects; this sample does not implement a separate downsample/blur/upsample render-target bloom chain. The audio path uses Windows WinMM waveOut and is Windows-only.

On Windows x64, verify with:

```powershell
dotnet restore DXSharp.Noesis.Showcase.sln --locked-mode -m:1
dotnet build DXSharp.Noesis.Showcase.sln -c Release --no-restore -m:1
dotnet run --project tests/Verification -c Release --no-build
dotnet run --project samples/Starfighter -c Release --no-build -- --smoke
```

Then launch interactively on the hardware adapter and exercise cockpit/chase switching, yaw/pitch/roll, pointer capture after firing, audio toggle, asteroid fragmentation, shield/hull damage, resize and fullscreen. WARP smoke success is not a substitute for that hardware-adapter play test.

## Revision 7 — cockpit/input and shimmer correction

Revision 7 separates head-look from the fighter quaternion: `Look()` now changes only a bounded camera yaw/pitch offset, while the cockpit mesh is transformed by the airframe basis. Twin pulse-bolt origins now match the external hull's cannon rails and sit forward of the cockpit canopy bow. Left-click no longer triggers weapons; it is reserved for Noesis controls, while Space provides continuous fire. The animated per-frame film-grain seed was removed in favor of fixed low-amplitude spatial dither.

The Starfighter verification runner now asserts that head-look changes the camera but not ship attitude, and that both projectiles originate beyond the cockpit at separated cannon-muzzle positions. The cockpit mesh verification continues to check every generated vertex normal and index. The new tinted side-canopy panels, overhead liner, cross-bows, seat and aft bulkhead are included in that geometry check.

**Not runtime-validated here:** this container has no `dotnet` executable, DXC, Noesis runtime or Windows/D3D12 device. XAML XML parsing and source-level checks can be performed here, but C# compilation, HLSL compilation, Noesis interaction and visual flicker confirmation must be performed on the Windows development machine. Run the commands above, then specifically test RMB head-look while observing the cockpit frame, left-click on each visible control, Space firing while moving, and the same scene with `--debug` enabled. If flicker persists after static grain, capture a D3D12 debug-layer log and a short video; do not assume the remaining issue is shader noise.
