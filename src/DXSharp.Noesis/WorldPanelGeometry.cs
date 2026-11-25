using System.Numerics;

namespace DXSharp.Noesis;

/// <summary>World-space planar UI surface. Origin is top-left; U and V span the full surface right and down.</summary>
public readonly record struct WorldPanelGeometry( Vector3 Origin, Vector3 U, Vector3 V, int PixelWidth, int PixelHeight ) {
    /// <summary>Intersect a forward ray with the finite, possibly skewed panel and return physical Noesis view pixels.</summary>
    public bool TryPick( Vector3 rayOrigin, Vector3 rayDirection, out Vector2 pixels, out float distance ) {
        pixels = default; distance = 0;
        if ( PixelWidth <= 0 || PixelHeight <= 0 || !float.IsFinite( rayDirection.LengthSquared( ) ) || rayDirection.LengthSquared( ) < 1e-12f ) return false;
        Vector3 direction = Vector3.Normalize( rayDirection );
        Vector3 normal = Vector3.Cross( U, V );
        float normalLength = normal.Length( );
        if ( !float.IsFinite( normalLength ) || normalLength < 1e-8f ) return false;
        normal /= normalLength;
        float denominator = Vector3.Dot( direction, normal );
        if ( MathF.Abs( denominator ) < 1e-6f ) return false;
        float t = Vector3.Dot( Origin - rayOrigin, normal ) / denominator;
        if ( !float.IsFinite( t ) || t < 0 ) return false;
        Vector3 delta = rayOrigin + direction * t - Origin;
        float uu = Vector3.Dot( U, U ), uv = Vector3.Dot( U, V ), vv = Vector3.Dot( V, V );
        float du = Vector3.Dot( delta, U ), dv = Vector3.Dot( delta, V );
        float determinant = uu * vv - uv * uv;
        if ( !float.IsFinite( determinant ) || determinant <= 1e-16f ) return false;
        float x = (du * vv - dv * uv) / determinant, y = (dv * uu - du * uv) / determinant;
        if ( !float.IsFinite( x ) || !float.IsFinite( y ) || x < 0 || y < 0 || x > 1 || y > 1 ) return false;
        pixels = new( Math.Clamp( x * PixelWidth, 0, PixelWidth - 1 ), Math.Clamp( y * PixelHeight, 0, PixelHeight - 1 ) );
        distance = t;
        return true;
    }
}
