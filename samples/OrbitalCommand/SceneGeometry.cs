using System.Numerics;
using System.Runtime.InteropServices;
namespace OrbitalCommand;
[StructLayout( LayoutKind.Sequential )]
public readonly record struct Vertex( Vector3 Position, Vector3 Normal, Vector3 Color );
public sealed record MeshData( Vertex[] Vertices, ushort[] Indices );
public static class SceneGeometry {
    public static MeshData Ship( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        Vector3 hull = new( 0.26f, 0.34f, 0.42f ), wing = new( 0.075f, 0.13f, 0.19f ), glass = new( 0.04f, 0.9f, 1.5f );
        Vector3 nose = new( 0, 0, -0.27f ), top = new( 0, 0.065f, 0.05f ), bottom = new( 0, -0.045f, 0.04f );
        Vector3 left = new( -0.075f, 0, 0.18f ), right = new( 0.075f, 0, 0.18f );
        Tri( nose, top, left, hull ); Tri( nose, right, top, hull ); Tri( nose, left, bottom, hull ); Tri( nose, bottom, right, hull );
        Tri( top, right, left, wing ); Tri( bottom, left, right, wing );
        Tri( nose, new( -0.20f, -0.01f, 0.16f ), left, wing );
        Tri( nose, right, new( 0.20f, -0.01f, 0.16f ), wing );
        Tri( new( -0.03f, 0.04f, -0.08f ), new( 0, 0.072f, 0.01f ), new( 0.03f, 0.04f, -0.08f ), glass );
        Tri( new( -0.045f, 0.018f, 0.181f ), new( 0.045f, 0.018f, 0.181f ), new( 0, 0, 0.49f ), new( 0.08f, 1.4f, 2.4f ) );
        Tri( new( 0.045f, -0.018f, 0.181f ), new( -0.045f, -0.018f, 0.181f ), new( 0, 0, 0.49f ), new( 0.08f, 1.4f, 2.4f ) );
        Tri( new( -0.072f, 0.008f, 0.02f ), new( -0.072f, 0.065f, 0.15f ), new( -0.072f, 0.008f, 0.19f ), wing );
        Tri( new( 0.072f, 0.008f, 0.19f ), new( 0.072f, 0.065f, 0.15f ), new( 0.072f, 0.008f, 0.02f ), wing );
        Tri( new( -0.016f, 0.052f, -0.09f ), new( 0, 0.067f, 0.02f ), new( 0.016f, 0.052f, -0.09f ), new( 0.42f, 0.56f, 0.62f ) );
        return new( vertices.ToArray( ), indices.ToArray( ) );
        void Tri( Vector3 a, Vector3 b, Vector3 c, Vector3 color ) {
            Vector3 normal = Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
            ushort start = checked((ushort)vertices.Count);
            vertices.Add( new( a, normal, color ) ); vertices.Add( new( b, normal, color ) ); vertices.Add( new( c, normal, color ) );
            indices.Add( start ); indices.Add( (ushort)(start + 1) ); indices.Add( (ushort)(start + 2) );

        }
    }

    public static MeshData Station( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        const int segments = 48, tube = 8;
        for ( int i = 0; i <= segments; i++ ) {
            float a = i * MathF.Tau / segments;
            for ( int j = 0; j <= tube; j++ ) {
                float b = j * MathF.Tau / tube;
                Vector3 n = new( MathF.Cos( a ) * MathF.Cos( b ), MathF.Sin( b ), MathF.Sin( a ) * MathF.Cos( b ) );
                Vector3 point = new( MathF.Cos( a ) * 0.27f, 0, MathF.Sin( a ) * 0.27f );
                vertices.Add( new( point + n * 0.028f, n, j % 4 == 0 ? new( 0.1f, 0.85f, 1.2f ) : new( 0.38f, 0.43f, 0.5f ) ) );
                if ( i == segments || j == tube ) continue;
                ushort k = (ushort)(i * (tube + 1) + j);
                indices.AddRange( [k, (ushort)(k + tube + 1), (ushort)(k + 1), (ushort)(k + 1), (ushort)(k + tube + 1), (ushort)(k + tube + 2)] );
            }
        }
        Box( Vector3.Zero, new( 0.07f, 0.09f, 0.07f ), new( 0.40f, 0.48f, 0.55f ) );
        Box( Vector3.Zero, new( 0.25f, 0.012f, 0.018f ), new( 0.25f, 0.34f, 0.4f ) );
        Box( Vector3.Zero, new( 0.018f, 0.012f, 0.25f ), new( 0.25f, 0.34f, 0.4f ) );
        Box( new( 0, 0.14f, 0 ), new( 0.012f, 0.08f, 0.012f ), new( 0.1f, 0.8f, 1.3f ) );
        for ( int i = 0; i < 4; i++ ) {
            float angle = i * MathF.PI / 2;
            Box( new( MathF.Cos( angle ) * 0.27f, 0, MathF.Sin( angle ) * 0.27f ),
                new( 0.038f, 0.042f, 0.038f ), new( 0.15f, 0.6f, 0.75f ) );
        }
        return new( vertices.ToArray( ), indices.ToArray( ) );
        void Box( Vector3 center, Vector3 half, Vector3 color ) {
            Vector3[] corners = [
                center + new Vector3( -half.X, -half.Y, -half.Z ), center + new Vector3( half.X, -half.Y, -half.Z ),
                center + new Vector3( half.X, half.Y, -half.Z ), center + new Vector3( -half.X, half.Y, -half.Z ),
                center + new Vector3( -half.X, -half.Y, half.Z ), center + new Vector3( half.X, -half.Y, half.Z ),
                center + new Vector3( half.X, half.Y, half.Z ), center + new Vector3( -half.X, half.Y, half.Z ) ];
            int[] faces = [0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5];
            for ( int i = 0; i < faces.Length; i += 3 ) {
                Vector3 x = corners[faces[i]], y = corners[faces[i + 1]], z = corners[faces[i + 2]];
                Vector3 normal = Vector3.Normalize( Vector3.Cross( y - x, z - x ) );
                ushort index = checked((ushort)vertices.Count);
                vertices.Add( new( x, normal, color ) ); vertices.Add( new( y, normal, color ) ); vertices.Add( new( z, normal, color ) );
                indices.AddRange( [index, (ushort)(index + 1), (ushort)(index + 2)] );
            }
        }

    }
}
