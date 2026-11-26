using System.Globalization;
using System.Numerics;

namespace Starfighter;

/// <summary>Converts the bundled DXCM cockpit from baked coordinates to the +Z-forward pilot frame.</summary>
public readonly record struct CockpitCalibration( Vector3 Eye, float YawDegrees, float Scale ) {
    // Measured from the bundled mesh: seat center X=1.1, dashboard toward -Z.
    // The glTF scene is +Z forward, but the existing DXCM bake reflected Z.
    public static CockpitCalibration Bundled => new( new Vector3( 1.1f, .85f, 1.2f ), 180f, 1f );
    public static CockpitCalibration FromEnvironment( ) {
        var defaults = Bundled;
        Vector3 eye = defaults.Eye;
        string? text = Environment.GetEnvironmentVariable( "DXSHARP_COCKPIT_EYE" );
        if ( !string.IsNullOrWhiteSpace( text ) ) {
            string[] fields = text.Split( ',' );
            if ( fields.Length != 3 ) throw new FormatException( "DXSHARP_COCKPIT_EYE expects x,y,z." );
            eye = new( Parse( fields[0] ), Parse( fields[1] ), Parse( fields[2] ) );
        }
        float yaw = Parse( Environment.GetEnvironmentVariable( "DXSHARP_COCKPIT_YAW" ) ?? "180" );
        float scale = Parse( Environment.GetEnvironmentVariable( "DXSHARP_COCKPIT_SCALE" ) ?? "1" );
        if ( scale <= 0 || scale > 100 ) throw new ArgumentOutOfRangeException( nameof(scale) );
        return new( eye, yaw, scale );
        static float Parse( string value ) {
            float f = float.Parse( value, CultureInfo.InvariantCulture );
            if ( !float.IsFinite( f ) ) throw new FormatException( "Cockpit calibration values must be finite." );
            return f;
        }
    }

    // DXCM v1 discarded material names/opacity. In this specific bundled bake
    // FX_Glass is the unique RGB8 triplet (209,107,1); the source contains 16
    // windshield triangles. Do not render them opaque until we have a glass pass.
    private static bool IsBundledWindshield( Vector3 color ) =>
        Vector3.DistanceSquared( color, new Vector3( 209f / 255, 107f / 255, 1f / 255 ) ) < 1e-8f;

    public MeshData Apply( MeshData source ) {
        Matrix4x4 rotation = Matrix4x4.CreateRotationY( YawDegrees * MathF.PI / 180 );
        Vector3 eye = Eye;
        float scale = Scale;
        var vertices = new Vertex[source.Vertices.Length];
        for ( int i = 0; i < vertices.Length; ++i ) {
            var v = source.Vertices[i];
            vertices[i] = new( Vector3.Transform( v.Position - eye, rotation ) * scale,
                Vector3.Normalize( Vector3.TransformNormal( v.Normal, rotation ) ), v.Color );
        }
        var indices = new List<ushort>( source.Indices.Length );
        for ( int i = 0; i < source.Indices.Length; i += 3 ) {
            ushort a = source.Indices[i], b = source.Indices[i+1], c = source.Indices[i+2];
            if ( IsBundledWindshield( source.Vertices[a].Color ) &&
                 IsBundledWindshield( source.Vertices[b].Color ) &&
                 IsBundledWindshield( source.Vertices[c].Color ) ) continue;
            indices.Add( a ); indices.Add( b ); indices.Add( c );
        }
        return new( vertices, indices.ToArray( ) );
    }
}
