# DXSharp.Noesis

Independent NoesisGUI integration for DXSharp and Direct3D 12, maintained by Arkaen Solutions. Experimental Windows x64 SDK foundation with two interactive reference applications. NoesisGUI is separately licensed.

## Build

Install .NET 10 SDK and Windows Graphics Tools for debug-layer smoke tests. Open `DXSharp.Noesis.slnx` in Visual Studio 2026 or execute:

```powershell
./eng/build.ps1
 dotnet run --project samples/OrbitalCommand -c Release --no-build
 dotnet run --project samples/Starfighter -c Release --no-build
```

The first restore generates fresh lockfiles for the public DXSharp package baseline. Review and commit those files, then use `./eng/build.ps1 -Locked` and enable locked mode in CI. The old private package lockfiles are intentionally excluded. No local DXSharp checkout is needed.

For the licensed runtime, provide `NOESIS_LICENSE_NAME` and `NOESIS_LICENSE_KEY` through local environment variables. Never commit license credentials or vendor SDK archives. Without credentials samples may use evaluation mode.

## Studio

Open `samples/OrbitalCommand/Ui/OrbitalCommand.noesis` or `samples/Starfighter/Ui/Starfighter.noesis` in the separately installed Noesis Studio 4.0.1. These descriptors share the runtime XAML rather than export copies. See [Studio integration](docs/studio.md) for the current limitations and roadmap.

## Architecture and verification

See [architecture](docs/architecture.md), [dependency policy](docs/dependencies.md), and [validation](docs/validation.md). The original showcase documentation lives in `docs/history` for reference (its build instructions refer to the old distribution).

## License

MIT for this repository's integration and samples. DXSharp, NoesisGUI and Microsoft dependencies retain their respective terms. No proprietary SDK distributions are included.
