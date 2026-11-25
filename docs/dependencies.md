# Dependencies

Exact direct versions live in Directory.Packages.props. Noesis.GUI 4.0.1.18263 and Noesis.App 4.0.1 preserve the rev7 combination; DXSharp.Core 0.4.0-preview.3 replaces the locally built 0.4.0-net10-preview.2. Restore and compile are required to establish compatibility with the new SDK APIs. Do not silently mix native Noesis binaries from separate distributions into NuGet build output.

Record vendor archive hashes and file versions when evaluating an SDK upgrade. Compare actual managed/native build identity and validate Studio independently. Lock transitive versions after the first successful restore; lockfiles from the original private feed are not applicable. Review package license terms before distributing editor binaries or sample executables.
