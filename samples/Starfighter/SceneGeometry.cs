using System.Numerics;
using System.Runtime.InteropServices;

namespace Starfighter;

[StructLayout( LayoutKind.Sequential )]
public readonly record struct Vertex( Vector3 Position, Vector3 Normal, Vector3 Color );
public sealed record MeshData( Vertex[] Vertices, ushort[] Indices );

public static class SceneGeometry {
    public static MeshData Asteroid( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        const int longitude = 20, latitude = 14;
        Vector3[,] points = new Vector3[latitude + 1, longitude + 1];
        for ( int y = 0; y <= latitude; y++ ) {
            float phi = MathF.PI * y / latitude;
            for ( int x = 0; x <= longitude; x++ ) {
                float theta = MathF.Tau * x / longitude;
                Vector3 n = new( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Cos( phi ), MathF.Sin( phi ) * MathF.Sin( theta ) );
                float noise = y == 0 || y == latitude ? 0.98f : 0.79f + Hash( x, y ) * 0.31f + MathF.Sin( theta * 5 + phi * 3 ) * 0.055f;
                points[y, x] = n * noise;
            }
        }
        for ( int y = 0; y < latitude; y++ ) for ( int x = 0; x < longitude; x++ ) {
            Vector3 a = points[y, x], b = points[y + 1, x], c = points[y, x + 1], d = points[y + 1, x + 1];
            if ( y > 0 ) AddFace( a, c, b, x, y );
            if ( y < latitude - 1 ) AddFace( c, d, b, x + 1, y + 1 );
        }
        return Finish( );

        void AddFace( Vector3 a, Vector3 b, Vector3 c, int x, int y ) {
            Vector3 center = (a + b + c) / 3;
            Vector3 cross = Vector3.Cross( b - a, c - a );
            if ( cross.LengthSquared( ) < 1e-10f ) return;
            Vector3 normal = Vector3.Normalize( cross );
            if ( Vector3.Dot( normal, center ) < 0 ) { (b, c) = (c, b); normal = -normal; }
            float shade = 0.54f + Hash( x + 31, y + 17 ) * 0.39f;
            Vector3 baseColor = Vector3.Lerp( new( 0.15f, 0.18f, 0.21f ), new( 0.49f, 0.37f, 0.25f ), Hash( x + 5, y + 41 ) );
            AddTriangle( a, b, c, normal, baseColor * shade );
        }
        void AddTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 color ) {
            ushort first = checked((ushort)vertices.Count);
            vertices.Add( new(a,normal,color) ); vertices.Add( new(b,normal,color) ); vertices.Add( new(c,normal,color) );
            indices.Add( first ); indices.Add( (ushort)(first + 1) ); indices.Add( (ushort)(first + 2) );
        }
        MeshData Finish( ) => new( vertices.ToArray( ), indices.ToArray( ) );
    }

    public static MeshData Sphere( int longitude = 28, int latitude = 18, Vector3? color = null ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        Vector3 baseColor = color ?? new( 0.3f, 0.48f, 0.64f );
        for ( int y = 0; y < latitude; y++ ) {
            float p0 = MathF.PI * y / latitude, p1 = MathF.PI * (y + 1) / latitude;
            for ( int x = 0; x < longitude; x++ ) {
                float t0 = MathF.Tau * x / longitude, t1 = MathF.Tau * (x + 1) / longitude;
                Vector3 a = SpherePoint( p0, t0 ), b = SpherePoint( p1, t0 ), c = SpherePoint( p0, t1 ), d = SpherePoint( p1, t1 );
                if ( y > 0 ) AddSphereFace( a, c, b, x, y );
                if ( y < latitude - 1 ) AddSphereFace( c, d, b, x, y );
            }
        }
        return Finish( );

        Vector3 SpherePoint( float phi, float theta ) => new( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Cos( phi ), MathF.Sin( phi ) * MathF.Sin( theta ) );
        void AddSphereFace( Vector3 a, Vector3 b, Vector3 c, int x, int y ) {
            Vector3 normal = Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
            if ( Vector3.Dot( normal, a + b + c ) < 0 ) { (b, c) = (c, b); normal = -normal; }
            float latitudeShade = 0.76f + 0.24f * MathF.Abs( normal.Y );
            float patches = 0.84f + Hash( x + 8, y + 18 ) * 0.2f;
            AddTriangle( a, b, c, normal, baseColor * latitudeShade * patches );
        }
        void AddTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 color ) {
            ushort first = checked((ushort)vertices.Count);
            vertices.Add( new(a,normal,color) ); vertices.Add( new(b,normal,color) ); vertices.Add( new(c,normal,color) );
            indices.Add( first ); indices.Add( (ushort)(first + 1) ); indices.Add( (ushort)(first + 2) );
        }
        MeshData Finish( ) => new( vertices.ToArray( ), indices.ToArray( ) );
    }

    public static MeshData Pulse( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        const int sides = 6;
        Vector3 nose = new( 0, 0, 1.45f ), tail = new( 0, 0, -1.1f );
        Vector3[] ring = new Vector3[sides];
        for ( int i = 0; i < sides; i++ ) {
            float angle = MathF.Tau * i / sides;
            ring[i] = new( MathF.Cos( angle ) * 0.075f, MathF.Sin( angle ) * 0.075f, 0 );
        }
        Vector3 color = new( 0.38f, 1.15f, 2.7f );
        for ( int i = 0; i < sides; i++ ) {
            AddColoredTriangle( nose, ring[i], ring[(i + 1) % sides], color );
            AddColoredTriangle( tail, ring[(i + 1) % sides], ring[i], color * 0.65f );
        }
        return Finish( );
        void AddColoredTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 tint ) {
            Vector3 normal = Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
            AddTriangle( a, b, c, normal, tint );
        }
        void AddTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 color ) {
            ushort first = checked((ushort)vertices.Count);
            vertices.Add( new(a,normal,color) ); vertices.Add( new(b,normal,color) ); vertices.Add( new(c,normal,color) );
            indices.Add( first ); indices.Add( (ushort)(first + 1) ); indices.Add( (ushort)(first + 2) );
        }
        MeshData Finish( ) => new( vertices.ToArray( ), indices.ToArray( ) );
    }


    public static MeshData Ring( int segments = 64 ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        for ( int i = 0; i < segments; i++ ) {
            float a0 = MathF.Tau * i / segments, a1 = MathF.Tau * (i + 1) / segments;
            Vector3 p0 = new( MathF.Cos(a0) * 0.78f, 0, MathF.Sin(a0) * 0.78f );
            Vector3 p1 = new( MathF.Cos(a0) * 1.0f, 0, MathF.Sin(a0) * 1.0f );
            Vector3 p2 = new( MathF.Cos(a1) * 0.78f, 0, MathF.Sin(a1) * 0.78f );
            Vector3 p3 = new( MathF.Cos(a1) * 1.0f, 0, MathF.Sin(a1) * 1.0f );
            Vector3 color = new( 0.32f + 0.12f * Hash(i,3), 0.30f + 0.11f * Hash(i,7), 0.25f + 0.08f * Hash(i,11) );
            Add( p0,p1,p3,color ); Add( p0,p3,p2,color );
        }
        return new( vertices.ToArray( ), indices.ToArray( ) );
        void Add( Vector3 a, Vector3 b, Vector3 c, Vector3 color ) {
            Vector3 n = Vector3.Normalize( Vector3.Cross( b-a,c-a ) );
            if ( n.Y < 0 ) { (b,c)=(c,b); n=-n; }
            ushort start=checked((ushort)vertices.Count);
            vertices.Add(new(a,n,color)); vertices.Add(new(b,n,color)); vertices.Add(new(c,n,color));
            indices.Add(start); indices.Add((ushort)(start+1)); indices.Add((ushort)(start+2));
        }
    }

    /// <summary>View-space cockpit shell: canopy spars, side rails, instrument coaming and center console.</summary>
    public static MeshData Cockpit( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        Vector3 graphite = new( 0.032f, 0.055f, 0.069f );
        Vector3 darkMetal = new( 0.075f, 0.105f, 0.13f );
        Vector3 edgeMetal = new( 0.16f, 0.22f, 0.25f );
        Vector3 cyan = new( 0.08f, 0.58f, 0.68f );
        Vector3 amber = new( 0.66f, 0.28f, 0.09f );

        // Broad lower coaming and the enclosed pilot tub. The forward center remains
        // optically clear; canopy side glazing and the roof close the peripheral shell.
        Quad( new(-2.65f,-0.82f,3.9f), new(2.65f,-0.82f,3.9f), new(2.48f,-1.88f,3.0f), new(-2.48f,-1.88f,3.0f), graphite );
        Quad( new(-2.42f,-0.92f,3.82f), new(2.42f,-0.92f,3.82f), new(2.28f,-1.28f,3.52f), new(-2.28f,-1.28f,3.52f), darkMetal );
        Quad( new(-2.35f,-1.36f,3.45f), new(2.35f,-1.36f,3.45f), new(2.08f,-1.78f,3.02f), new(-2.08f,-1.78f,3.02f), new(0.045f,0.072f,0.084f) );

        // Canopy arch and the swept left/right windscreen pillars.
        Box( new(-2.46f,0.26f,3.18f), new(-2.12f,1.58f,4.15f), edgeMetal );
        Box( new(2.12f,0.26f,3.18f), new(2.46f,1.58f,4.15f), edgeMetal );
        Box( new(-2.40f,1.40f,3.08f), new(2.40f,1.68f,4.12f), darkMetal );
        // Opaque, deeply tinted peripheral canopy panels stand in for glazing in this
        // untextured showcase renderer. The broad forward sightline stays unobstructed.
        Quad( new(-2.30f,0.34f,3.22f), new(-2.30f,1.34f,3.22f), new(-2.30f,1.34f,4.02f), new(-2.30f,0.34f,4.02f), new(0.012f,0.065f,0.082f) );
        Quad( new(2.30f,0.34f,3.22f), new(2.30f,0.34f,4.02f), new(2.30f,1.34f,4.02f), new(2.30f,1.34f,3.22f), new(0.012f,0.065f,0.082f) );
        // Overhead inner roof liner and two cross-bows make the pilot tub feel enclosed.
        Quad( new(-2.10f,1.48f,3.18f), new(2.10f,1.48f,3.18f), new(2.10f,1.48f,4.02f), new(-2.10f,1.48f,4.02f), graphite );
        Box( new(-2.12f,1.34f,3.36f), new(2.12f,1.43f,3.48f), edgeMetal );
        Box( new(-2.12f,1.34f,3.91f), new(2.12f,1.43f,4.03f), edgeMetal );
        Box( new(-2.14f,0.24f,3.08f), new(-1.98f,1.40f,4.05f), cyan );
        Box( new(1.98f,0.24f,3.08f), new(2.14f,1.40f,4.05f), cyan );
        // A pilot seat and rear bulkhead become visible when the head-look turns aft.
        Quad( new(-0.76f,-0.18f,-1.05f), new(0.76f,-0.18f,-1.05f), new(0.62f,1.02f,-1.28f), new(-0.62f,1.02f,-1.28f), darkMetal );
        Quad( new(-0.76f,-1.22f,-0.82f), new(0.76f,-1.22f,-0.82f), new(0.76f,-1.22f,0.48f), new(-0.76f,-1.22f,0.48f), graphite );
        Box( new(-2.72f,-0.86f,2.78f), new(-2.47f,0.36f,3.92f), graphite );
        Box( new(2.47f,-0.86f,2.78f), new(2.72f,0.36f,3.92f), graphite );

        // Side consoles are intentionally asymmetrical, like a hand-built fighter cockpit.
        Quad( new(-2.42f,-0.55f,3.45f), new(-1.30f,-0.55f,3.56f), new(-1.12f,-1.17f,3.18f), new(-2.26f,-1.24f,3.08f), darkMetal );
        Quad( new(1.30f,-0.55f,3.56f), new(2.42f,-0.55f,3.45f), new(2.26f,-1.24f,3.08f), new(1.12f,-1.17f,3.18f), darkMetal );
        Quad( new(-2.22f,-0.76f,3.36f), new(-1.48f,-0.76f,3.42f), new(-1.42f,-1.08f,3.22f), new(-2.18f,-1.08f,3.16f), edgeMetal );
        Quad( new(1.48f,-0.76f,3.42f), new(2.22f,-0.76f,3.36f), new(2.18f,-1.08f,3.16f), new(1.42f,-1.08f,3.22f), edgeMetal );
        Box( new(-2.06f,-1.03f,3.20f), new(-1.55f,-0.96f,3.26f), cyan );
        Box( new(1.55f,-1.03f,3.20f), new(2.06f,-0.96f,3.26f), amber );

        // Central targeting pedestal and the two low-profile hand-control housings.
        Quad( new(-0.70f,-0.88f,3.64f), new(0.70f,-0.88f,3.64f), new(0.48f,-1.52f,3.20f), new(-0.48f,-1.52f,3.20f), graphite );
        Quad( new(-0.48f,-1.02f,3.66f), new(0.48f,-1.02f,3.66f), new(0.34f,-1.24f,3.50f), new(-0.34f,-1.24f,3.50f), edgeMetal );
        Box( new(-0.12f,-1.33f,3.40f), new(0.12f,-1.30f,3.48f), cyan );
        Box( new(-1.32f,-1.50f,3.13f), new(-0.94f,-1.22f,3.41f), darkMetal );
        Box( new(0.94f,-1.50f,3.13f), new(1.32f,-1.22f,3.41f), darkMetal );
        Box( new(-1.28f,-1.31f,3.38f), new(-0.98f,-1.25f,3.43f), amber );
        Box( new(0.98f,-1.31f,3.38f), new(1.28f,-1.25f,3.43f), cyan );

        // Nose rail and a pair of illuminated canopy seams create a physical frame in depth.
        Box( new(-0.055f,-0.12f,4.02f), new(0.055f,1.38f,4.18f), edgeMetal );
        Box( new(-2.40f,0.32f,4.08f), new(-2.28f,1.34f,4.20f), cyan );
        Box( new(2.28f,0.32f,4.08f), new(2.40f,1.34f,4.20f), cyan );

        // Actual three-dimensional avionics: twin MFD bezels, keys, toggle banks and the pilot's stick.
        Quad( new(-2.14f,-0.79f,3.43f), new(-1.47f,-0.79f,3.48f), new(-1.42f,-1.02f,3.28f), new(-2.10f,-1.02f,3.22f), new(0.025f,0.14f,0.18f) );
        Quad( new(1.47f,-0.79f,3.48f), new(2.14f,-0.79f,3.43f), new(2.10f,-1.02f,3.22f), new(1.42f,-1.02f,3.28f), new(0.025f,0.12f,0.16f) );
        for ( int i = 0; i < 5; i++ ) {
            float x = -2.02f + i * 0.125f;
            Box( new(x,-1.105f,3.25f), new(x+0.055f,-1.075f,3.29f), i == 0 ? amber : cyan );
            float rx = 1.48f + i * 0.125f;
            Box( new(rx,-1.105f,3.25f), new(rx+0.055f,-1.075f,3.29f), i == 4 ? amber : cyan );
        }
        // Switch guards and tactile toggle rows rise above the side consoles.
        for ( int i = 0; i < 4; i++ ) {
            float x = -2.18f + i * 0.19f;
            Box( new(x,-0.89f,3.44f), new(x+0.07f,-0.82f,3.51f), i == 2 ? amber : edgeMetal );
            float rx = 1.43f + i * 0.19f;
            Box( new(rx,-0.89f,3.44f), new(rx+0.07f,-0.82f,3.51f), i == 1 ? amber : edgeMetal );
        }
        // Two hand controls are visibly mounted above the lower coaming.
        Box( new(-0.84f,-1.02f,2.98f), new(-0.72f,-0.52f,3.20f), edgeMetal );
        Box( new(-0.98f,-0.60f,3.00f), new(-0.58f,-0.47f,3.24f), darkMetal );
        Box( new(0.72f,-1.02f,2.98f), new(0.84f,-0.52f,3.20f), edgeMetal );
        Box( new(0.58f,-0.60f,3.00f), new(0.98f,-0.47f,3.24f), darkMetal );
        // Central flight stick, a short stalk and a wide asymmetric grip.
        Box( new(-0.045f,-1.24f,2.64f), new(0.045f,-0.88f,2.76f), edgeMetal );
        Box( new(-0.15f,-0.98f,2.68f), new(0.15f,-0.83f,2.96f), graphite );
        Box( new(-0.19f,-0.91f,2.84f), new(0.19f,-0.80f,2.94f), amber );
        // Low side armrests and footwell sidewalls add cockpit depth in the lower peripheral view.
        Box( new(-2.34f,-1.55f,2.72f), new(-1.82f,-1.28f,3.20f), graphite );
        Box( new(1.82f,-1.55f,2.72f), new(2.34f,-1.28f,3.20f), graphite );
        Box( new(-1.76f,-1.80f,2.72f), new(-1.58f,-1.48f,3.16f), edgeMetal );
        Box( new(1.58f,-1.80f,2.72f), new(1.76f,-1.48f,3.16f), edgeMetal );
        return Finish( );

        void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color ) {
            Vector3 n = Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
            // Cockpit surfaces face the pilot at negative view-space Z.
            if ( n.Z > 0 ) { (b, d) = (d, b); n = -n; }
            AddTriangle( a, b, c, n, color ); AddTriangle( a, c, d, n, color );
        }
        void Box( Vector3 min, Vector3 max, Vector3 color ) {
            Vector3 a = new(min.X,min.Y,min.Z), b = new(max.X,min.Y,min.Z), c = new(max.X,max.Y,min.Z), d = new(min.X,max.Y,min.Z);
            Vector3 e = new(min.X,min.Y,max.Z), f = new(max.X,min.Y,max.Z), g = new(max.X,max.Y,max.Z), h = new(min.X,max.Y,max.Z);
            Quad( a,b,c,d,color ); Quad( f,e,h,g,color ); Quad( e,a,d,h,color ); Quad( b,f,g,c,color ); Quad( d,c,g,h,color ); Quad( e,f,b,a,color );
        }
        void AddTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector3 color ) {
            ushort first = checked((ushort)vertices.Count);
            vertices.Add( new(a,n,color) ); vertices.Add( new(b,n,color) ); vertices.Add( new(c,n,color) );
            indices.Add( first ); indices.Add( (ushort)(first + 1) ); indices.Add( (ushort)(first + 2) );
        }
        MeshData Finish( ) => new( vertices.ToArray( ), indices.ToArray( ) );
    }


    /// <summary>Procedural Viper-style interceptor hull, swept delta wings, twin pods, canopy and guns.</summary>
    public static MeshData Fighter( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        Vector3 hull = new( 0.13f, 0.20f, 0.24f ), hullLight = new( 0.25f, 0.38f, 0.42f );
        Vector3 wing = new( 0.085f, 0.13f, 0.17f ), wingEdge = new( 0.17f, 0.27f, 0.30f );
        Vector3 canopy = new( 0.07f, 0.42f, 0.52f ), amber = new( 0.75f, 0.34f, 0.12f );
        Vector3 gun = new( 0.20f, 0.62f, 0.72f );

        // Long central fuselage: armored belly, tapered nose and raised dorsal spine.
        Box( new(-0.39f,-0.22f,-3.45f), new(0.39f,0.22f,2.55f), hull );
        Box( new(-0.26f,0.19f,-2.65f), new(0.26f,0.42f,1.72f), hullLight );
        Vector3 nose = new(0,0.05f,5.0f), topL = new(-0.39f,0.20f,1.75f), topR = new(0.39f,0.20f,1.75f);
        Vector3 lowL = new(-0.34f,-0.17f,1.72f), lowR = new(0.34f,-0.17f,1.72f);
        Face( nose, topR, topL, hullLight ); Face( nose, lowL, lowR, hull );
        Face( nose, topL, lowL, hull ); Face( nose, lowR, topR, hull );
        Face( topL, topR, lowR, hullLight ); Face( topL, lowR, lowL, hull );

        // Swept delta wings, with raised leading edges and dark ventral panels.
        Wing( false ); Wing( true );
        Box( new(-1.20f,-0.20f,-3.75f), new(-0.70f,0.02f,-0.52f), hullLight );
        Box( new(0.70f,-0.20f,-3.75f), new(1.20f,0.02f,-0.52f), hullLight );
        Box( new(-1.34f,-0.12f,-3.82f), new(-1.15f,0.18f,-0.85f), wingEdge );
        Box( new(1.15f,-0.12f,-3.82f), new(1.34f,0.18f,-0.85f), wingEdge );

        // Twin cannon rails and luminous muzzle collars.
        Box( new(-1.18f,-0.10f,2.15f), new(-0.96f,0.02f,5.15f), wingEdge );
        Box( new(0.96f,-0.10f,2.15f), new(1.18f,0.02f,5.15f), wingEdge );
        Box( new(-1.20f,-0.12f,4.83f), new(-0.94f,0.06f,5.15f), gun );
        Box( new(0.94f,-0.12f,4.83f), new(1.20f,0.06f,5.15f), gun );

        // Twin vertical tail fins, dorsal spine, cockpit canopy and amber service markings.
        Face( new(-0.46f,0.14f,-2.8f), new(-0.78f,1.12f,-3.55f), new(-0.78f,0.10f,-1.72f), hullLight );
        Face( new(0.46f,0.14f,-2.8f), new(0.78f,0.10f,-1.72f), new(0.78f,1.12f,-3.55f), hullLight );
        Box( new(-0.14f,0.37f,-2.95f), new(0.14f,0.53f,1.95f), wingEdge );
        // Faceted canopy shell.
        Vector3 c0 = new(-0.31f,0.23f,0.15f), c1 = new(0.31f,0.23f,0.15f);
        Vector3 c2 = new(0.24f,0.48f,1.55f), c3 = new(-0.24f,0.48f,1.55f), c4 = new(0,0.58f,0.95f);
        Quad( c0,c1,c2,c3, canopy ); Face( c0,c3,c4, gun ); Face( c1,c4,c2, gun );
        Box( new(-0.42f,0.12f,-3.10f), new(-0.35f,0.20f,-2.55f), amber );
        Box( new(0.35f,0.12f,-3.10f), new(0.42f,0.20f,-2.55f), amber );
        return Finish( );

        void Wing( bool mirror ) {
            float m = mirror ? -1 : 1;
            Vector3 rootFront = new(m * 0.34f, 0.04f, 1.85f);
            Vector3 rootBack = new(m * 0.40f, -0.11f, -3.28f);
            Vector3 tipFront = new(m * 3.55f, 0.015f, -1.72f);
            Vector3 tipBack = new(m * 2.78f, -0.05f, -3.92f);
            Quad( rootFront, tipFront, tipBack, rootBack, wing );
            // Narrow raised leading-edge spar and an inset service panel.
            Face( rootFront, tipFront, new(m * 3.42f,0.07f,-1.89f), wingEdge );
            Box( new(m * 2.42f - 0.045f, -0.02f, -3.05f), new(m * 2.42f + 0.045f, 0.045f, -2.35f), amber );
        }
        void Face( Vector3 a, Vector3 b, Vector3 c, Vector3 color ) {
            Vector3 n = Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
            Triangle( a,b,c,n,color ); Triangle( c,b,a,-n,color );
        }
        void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color ) {
            Face( a,b,c,color ); Face( a,c,d,color );
        }
        void Box( Vector3 min, Vector3 max, Vector3 color ) {
            Vector3 a=new(min.X,min.Y,min.Z), b=new(max.X,min.Y,min.Z), c=new(max.X,max.Y,min.Z), d=new(min.X,max.Y,min.Z);
            Vector3 e=new(min.X,min.Y,max.Z), f=new(max.X,min.Y,max.Z), g=new(max.X,max.Y,max.Z), h=new(min.X,max.Y,max.Z);
            Quad(a,b,c,d,color); Quad(f,e,h,g,color); Quad(e,a,d,h,color); Quad(b,f,g,c,color); Quad(d,c,g,h,color); Quad(e,f,b,a,color);
        }
        void Triangle( Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector3 color ) {
            ushort i=checked((ushort)vertices.Count); vertices.Add(new(a,n,color)); vertices.Add(new(b,n,color)); vertices.Add(new(c,n,color));
            indices.Add(i); indices.Add((ushort)(i+1)); indices.Add((ushort)(i+2));
        }
        MeshData Finish( ) => new(vertices.ToArray(),indices.ToArray());
    }

    /// <summary>Two faceted afterburner plumes positioned behind the fighter's engine pods.</summary>
    public static MeshData EngineFlare( ) {
        var vertices = new List<Vertex>( ); var indices = new List<ushort>( );
        for ( int side = -1; side <= 1; side += 2 ) {
            float x = side * 0.95f;
            Vector3 tip = new(x, -0.04f, -6.35f);
            Vector3 a = new(x-0.17f,-0.19f,-3.35f), b = new(x+0.17f,-0.19f,-3.35f);
            Vector3 c = new(x+0.17f,0.11f,-3.35f), d = new(x-0.17f,0.11f,-3.35f);
            Add(tip,a,b,new(0.12f,0.72f,1.15f)); Add(tip,b,c,new(0.18f,0.92f,1.35f));
            Add(tip,c,d,new(0.12f,0.72f,1.15f)); Add(tip,d,a,new(0.08f,0.40f,0.80f));
        }
        return new(vertices.ToArray(),indices.ToArray());
        void Add(Vector3 a,Vector3 b,Vector3 c,Vector3 color) {
            Vector3 n=Vector3.Normalize(Vector3.Cross(b-a,c-a)); ushort i=checked((ushort)vertices.Count);
            vertices.Add(new(a,n,color));vertices.Add(new(b,n,color));vertices.Add(new(c,n,color));
            indices.Add(i);indices.Add((ushort)(i+1));indices.Add((ushort)(i+2));
            ushort j=checked((ushort)vertices.Count); vertices.Add(new(c,-n,color));vertices.Add(new(b,-n,color));vertices.Add(new(a,-n,color));
            indices.Add(j);indices.Add((ushort)(j+1));indices.Add((ushort)(j+2));
        }
    }

    private static float Hash( int x, int y ) {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
        return (h & 0x00FFFFFF) / 16777215f;
    }
}
