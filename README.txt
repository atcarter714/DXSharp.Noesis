DXSharp.Noesis showcase follow-up

Apply from the root of the repository (based on the starter solution):
  git switch -c feature/showcase-flight-studio-foundation
  git apply --check C:/path/to/DXSharp.Noesis-showcase-followup.patch
  git apply C:/path/to/DXSharp.Noesis-showcase-followup.patch
  pwsh -File eng/build.ps1

Read docs/showcase-followup.md for controls, asset and Horizons setup, and Studio.
Optional: copy samples/Starfighter/Assets/cockpit.mesh from this archive into the
corresponding repository folder. Set DXSHARP_COCKPIT_PREVIEW=1 before running
Starfighter; the eye position and scale are provisional. Preserve the attribution.
The binary mesh is derived from the supplied CC-BY-4.0 glTF model; material colors
only, no texture maps. It is ignored by git. The patch includes the converter.
No .NET or Windows GPU testing was performed here. Python fixture tests, mesh
conversion and binary integrity, XML parsing and git patch whitespace passed.
