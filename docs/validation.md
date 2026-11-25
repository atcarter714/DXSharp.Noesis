# Initial migration validation

Source files are preserved from rev7. XML and archive integrity checks are performed during scaffold creation. No .NET SDK or Windows D3D12 device is available in the creation environment; no compilation, package restore, Studio launch or GPU execution is claimed for this migration. Historical rev7 checks are separate, see history/rev7-VALIDATION.md.

Run `./eng/build.ps1` on Windows, review and commit generated lockfiles, then rerun with `-Locked`. Run `./eng/build.ps1 -Locked -Smoke` on a Windows desktop with Graphics Tools. Check both samples interactively on a hardware adapter, especially resize, DPI, input focus, frame retirement, shutdown and Starfighter head-look. Open both Studio descriptors and verify theme, fonts, resources, bindings and authoring behavior. Confirm the package contains only intended integration artifacts before publishing.

Hosted CI builds and runs non-GPU verification. GPU workflow is manual, on a trusted interactive Windows self-hosted runner with label dxsharp-gpu. Do not expose such a runner to arbitrary public PR code.
