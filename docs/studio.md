# Noesis Studio 4.0.1

The bundled SDK documentation `Doc/Gui.Core.StudioIntegration.html` describes `.noesis` descriptors, provider factories, native `Noesis::Studio::Create`, editor lifecycle callbacks and metadata for custom components. The native SDK reference implementation is under `Src/Packages/App/StudioTool`. The project root determines XAML resource paths.

The two sample descriptors are an initial standalone Studio authoring entry point, not an embedded editor implementation. Runtime theme/font setup, programmatic DataContext and custom types may not be visible in standalone Studio; validate both files visually and add shared resource dictionaries and design data where needed. Keep runtime and Studio global resources synchronized. No separate XAML export step is required for files shared by both.

Next: inspect managed SDK exposure of Studio creation/options, verify editor binary licensing and exact build compatibility, then implement provider and type-registration bridges in a separate optional host. If the necessary API is native-only, use a minimal native shim with explicit ownership rather than assuming a managed wrapper exists. Support editor file notifications and save/discard lifecycle only after the standalone projects load correctly.

References: https://www.noesisengine.com/docs/Gui.Core.StudioIntegration.html and https://www.noesisengine.com/docs/Gui.Studio.Documentation_NoesisProject.html
