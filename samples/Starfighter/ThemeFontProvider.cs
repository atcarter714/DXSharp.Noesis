using Noesis;
using NoesisApp;
using System.Reflection;

/// <summary>Resolves bundled theme fonts for relative and assembly-qualified XAML URIs.</summary>
internal sealed class ThemeFontProvider : FontProvider {
    private readonly Assembly _assembly = Theme.Assembly;
    private readonly string[] _resources = Theme.Assembly.GetManifestResourceNames( )
        .Where( value => value.EndsWith( ".otf", StringComparison.OrdinalIgnoreCase ) ||
                         value.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) ).ToArray( );

    public override void ScanFolder( System.Uri folder ) {
        foreach ( string resource in _resources ) RegisterFont( folder, resource );
    }

    public override Stream OpenFont( System.Uri folder, string id ) =>
        _assembly.GetManifestResourceStream( id )
        ?? throw new FileNotFoundException( "The theme's embedded font was not found.", id );
}
