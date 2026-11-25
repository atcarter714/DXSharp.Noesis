using System.Numerics;

namespace OrbitalCommand;

public enum Mission { Orbit, Survey, Patrol, Return }

public sealed class Vessel {
    public required string Name { get; init; }
    public required string Role { get; init; }
    public float Radius { get; set; }
    public float Phase { get; set; }
    public float Inclination { get; init; }
    public float Speed { get; init; }
    public float Survey { get; set; }
    public Mission Mission { get; set; }
    public Vector3 Position => new( MathF.Cos( Phase ) * Radius, MathF.Sin( Phase ) * Inclination,
        MathF.Sin( Phase ) * Radius );
    public float Heading => MathF.PI - Phase;
}

/// <summary>Deterministic, bounded showcase simulation. All mutations run on the UI/render thread.</summary>
public sealed class Simulation {
    public List<Vessel> Vessels { get; } = [];
    public double Time { get; private set; }
    public double Credits { get; private set; } = 1250;
    public double Science { get; private set; } = 74;
    public int CompletedSurveys { get; private set; }
    public bool Paused { get; set; }
    public float Speed { get; set; } = 1;
    public event Action<string>? Event;

    public Simulation( ) {
        Add( "ACV Wayfarer", "Pathfinder", 2.00f, 0.1f, 0.24f );
        Add( "ACV Resolute", "Corvette", 2.36f, 1.2f, 0.10f );
        Add( "ACV Meridian", "Surveyor", 2.75f, 2.8f, 0.33f );
        Add( "ACV Kestrel", "Interceptor", 1.82f, 4.2f, 0.16f );
        Add( "ACV Atlas", "Freighter", 3.05f, 5.1f, 0.07f );
        Add( "ACV Vigilant", "Corvette", 2.34f, 1.45f, 0.10f );
    }

    private void Add( string name, string role, float radius, float phase, float inclination ) => Vessels.Add( new( ) {
        Name = name, Role = role, Radius = radius, Phase = phase, Inclination = inclination,
        Speed = 0.22f / MathF.Pow( radius, 1.5f )
    } );

    public void Tick( double realSeconds ) {
        if ( !double.IsFinite( realSeconds ) || realSeconds < 0 ) throw new ArgumentOutOfRangeException( nameof(realSeconds) );
        if ( Paused ) return;
        float dt = (float)Math.Min( realSeconds, 0.1 ) * Math.Clamp( Speed, 0.25f, 4 );
        Time += dt;
        Credits += dt * (2.5 + Vessels.Count * 0.3);
        foreach ( var ship in Vessels ) {
            float multiplier = ship.Mission == Mission.Patrol ? 2.2f : 1;
            ship.Phase = (ship.Phase + dt * ship.Speed * multiplier) % MathF.Tau;
            float targetRadius = ship.Mission == Mission.Return ? 1.58f : ship.Mission == Mission.Survey ? 3.25f : ship.Radius;
            ship.Radius += (targetRadius - ship.Radius) * Math.Min( 1, dt * 0.4f );
            if ( ship.Mission != Mission.Survey ) continue;
            ship.Survey = Math.Min( 1, ship.Survey + dt / 22 );
            if ( ship.Survey < 1 ) continue;
            ship.Mission = Mission.Orbit;
            Science += 40;
            Credits += 100;
            CompletedSurveys++;
            Event?.Invoke( $"{ship.Name}: survey complete. +40 research / +100 credits." );
        }
    }

    public void Order( Vessel vessel, Mission mission ) {
        if ( !Vessels.Contains( vessel ) ) throw new ArgumentException( "The vessel is not in this fleet.", nameof(vessel) );
        vessel.Mission = mission;
        if ( mission == Mission.Survey ) vessel.Survey = 0;
        Event?.Invoke( $"{vessel.Name}: {mission switch { Mission.Survey => "outer-ring survey accepted", Mission.Patrol => "high-speed patrol engaged", Mission.Return => "returning to inner anchorage", _ => "holding orbital track" }}." );
    }

    public bool Launch( ) {
        if ( Vessels.Count >= 8 || Credits < 160 ) return false;
        Credits -= 160;
        int index = Vessels.Count + 1;
        Add( $"ACV Pioneer {index:00}", "Pathfinder", 1.65f, index * 0.7f, 0.2f );
        Event?.Invoke( $"Launch confirmed: ACV Pioneer {index:00}." );
        return true;
    }
}

public readonly record struct ScreenViewport( float X, float Y, float Width, float Height ) {
    public bool Contains( float x, float y ) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public sealed class OrbitCamera {
    public float Yaw { get; set; } = 0.35f;
    public float Pitch { get; set; } = 0.46f;
    public float Distance { get; set; } = 5.6f;
    public Vector3 Target { get; set; }
    public const float TanHalfFov = 0.46f;
    public ScreenViewport? Viewport { get; set; }
    public ScreenViewport GetViewport( float width, float height ) => Viewport ?? new( 0, 0, width, height );
    public Vector3 Position {
        get {
            Vector3 position = Target + Distance * new Vector3( MathF.Cos( Pitch ) * MathF.Sin( Yaw ),
                MathF.Sin( Pitch ), MathF.Cos( Pitch ) * MathF.Cos( Yaw ) );
            return position.LengthSquared( ) < 1.35f * 1.35f ? Vector3.Normalize( position + new Vector3( 0, 0.0001f, 0 ) ) * 1.35f : position;
        }
    }
    public Vector3 Forward => Vector3.Normalize( Target - Position );
    public Vector3 Right => Vector3.Normalize( Vector3.Cross( Forward, Vector3.UnitY ) );
    public Vector3 Up => Vector3.Cross( Right, Forward );

    public void Orbit( float dx, float dy ) {
        Yaw -= dx * 0.005f;
        Pitch = Math.Clamp( Pitch + dy * 0.004f, -0.05f, 1.3f );
    }
    public void Pan( float dx, float dy, float width, float height ) {
        float unit = 2 * Distance * TanHalfFov / Math.Max( GetViewport( width, height ).Height, 1 );
        Target += (-Right * dx + Up * dy) * unit;
    }
    public void Zoom( float wheel ) => Distance = Math.Clamp( Distance * MathF.Exp( -wheel * 0.0008f ), 2.5f, 13 );
    public void Reset( ) { Yaw = 0.35f; Pitch = 0.46f; Distance = 5.6f; Target = Vector3.Zero; }

    public bool Project( Vector3 position, float width, float height, out Vector2 pixel ) {
        Vector3 delta = position - Position;
        float z = Vector3.Dot( delta, Forward );
        pixel = default;
        if ( z <= 0.05f ) return false;
        var viewport = GetViewport( width, height );
        pixel = new( viewport.X + viewport.Width * 0.5f + Vector3.Dot( delta, Right ) / (z * TanHalfFov) * viewport.Height * 0.5f,
            viewport.Y + viewport.Height * 0.5f - Vector3.Dot( delta, Up ) / (z * TanHalfFov) * viewport.Height * 0.5f );
        Vector3 ray = Vector3.Normalize( delta );
        float b = Vector3.Dot( Position, ray ), c = Position.LengthSquared( ) - 1;
        float h = b * b - c;
        bool occluded = h > 0 && -b - MathF.Sqrt( h ) > 0 && -b - MathF.Sqrt( h ) < delta.Length( ) - 0.08f;
        return !occluded && viewport.Contains( pixel.X, pixel.Y );
    }
}
