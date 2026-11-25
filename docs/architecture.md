# Architecture

The initial library preserves the rev7 D3D12Overlay and OverlayInput implementation. Samples own the WinForms host, graphics device, swap chain, frame allocators, synchronization and scene logic. Overlay owns the Noesis view, renderer and stencil surface; callers drain outstanding GPU work before resize or disposal. Scene submission precedes UI submission on the same direct queue. Avoid external draws on the UI list. The integration does not own the process-wide Noesis GUI initialization; samples configure providers and license independently.

## Planned boundaries

- `src/DXSharp.Noesis`: reusable render/input bridge; public APIs remain independent of sample windowing.
- Future `DXSharp.Noesis.Hosting`: optional reusable platform lifecycle and resource providers after comparing both sample implementations.
- Future `DXSharp.Noesis.Studio`: optional editor host/extension layer, isolated from the runtime package and vendor licensing requirements.
- `samples`: distinct showcases plus future minimal host sample.
- `tests/Verification`: existing executable regression checks using actual Noesis bindings, geometry and input. GPU smoke tests exercise the real sample paths.
- `eng`: shared build orchestration for GitHub Actions and Azure Pipelines. Publishing is deliberately manual until the package passes target-machine validation.

Do not add speculative project references or native editor binaries before verifying the supported managed Studio API and deployment terms.
