using System.Collections.ObjectModel;
using System.ComponentModel;
using DXSharp.Noesis.Samples;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using N = Noesis;

namespace Starfighter;

// Keep the native Noesis reflection boundary limited to the original CLR contracts.
// Toolkit commands remain an implementation detail behind ActionCommand.
public abstract class ObservableObject : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify( [CallerMemberName] string? name = null ) => PropertyChanged?.Invoke( this, new( name ) );
    protected bool Set<T>( ref T field, T value, [CallerMemberName] string? name = null ) {
        if ( EqualityComparer<T>.Default.Equals( field, value ) ) return false;
        field = value; Notify( name ); return true;
    }
}
public enum SoundCue { Cannon, Impact, Explosion, BoostOn, BoostOff, HullHit, Lock, Overheat }

public sealed class FeedItem {
    public required string Stamp { get; init; }
    public required string Message { get; init; }
}

public sealed class RadarContact : ObservableObject {
    private float _left, _top;
    private float _size = 4;
    private string _color = "#F4B36F";
    public float Left { get => _left; set => Set( ref _left, value ); }
    public float Top { get => _top; set => Set( ref _top, value ); }
    public float Size { get => _size; set => Set( ref _size, value ); }
    public string Color { get => _color; set => Set( ref _color, value ); }
}

public sealed class Asteroid {
    public Vector3 Position;
    public Vector3 Velocity;
    public float Radius;
    public float Rotation;
    public float RotationX;
    public float RotationZ;
    public float Spin;
    public float OrbitSpeed;
    public float Life = float.PositiveInfinity;
    public int Hits;
    public uint Seed;
    public bool Fragment;
}

public sealed class PulseBolt {
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Side;
}

public sealed class Blast {
    public Vector3 Position;
    public float Age;
    public float Duration;
    public float Radius;
    public uint Seed;
}

/// <summary>
/// Six-degree-of-freedom fighter simulation in a small, persistent solar system.
/// The flight model uses a quaternion attitude and inertial velocity; no yaw/pitch
/// clamps or screen-space fake movement are used.
/// All simulation and view-model mutations occur on the WinForms/Noesis thread.
/// </summary>
public sealed class StarfighterGame : ObservableObject {
    private readonly Random _random = new( 0x51A7F1 );
    private readonly List<Asteroid> _asteroids = [];
    private readonly List<PulseBolt> _bolts = [];
    private readonly List<Blast> _blasts = [];
    private Vector3 _position = new( 0, 0.5f, -54 );
    private Vector3 _velocity = new( 0, 0, 8 );
    private Quaternion _orientation = Quaternion.Identity;
    private Vector3 _angularVelocity; // Body-local angular rates (rad/s).
    private Vector2 _mouseStick; // X = roll, Y = pitch; held until released.
    public Vector3 AngularVelocity => _angularVelocity;
    public static readonly Vector3 MaxAngularRate = new( 1.15f, 0.32f, 1.65f );
    public static readonly Vector3 MaxAngularAcceleration = new( 2.4f, 0.75f, 3.6f );
    // Head-look is deliberately independent from the fighter attitude. The pilot can
    // inspect the cockpit without steering the ship or changing the gun solution.
    private float _lookYaw, _lookPitch;
    private float _rollReadout, _cameraShake;
    private float _fov = 0.52f;
    private float _time, _spawnClock = 2.0f, _shotClock, _heat, _shieldDelay, _damageFlash;
    private float _boostEnergy = 100, _environmentImpactDelay;
    private int _combo, _comboTimer;
    private double _readoutClock, _performanceClock, _radarClock;
    private int _statsFrames;
    private bool _paused, _gameOver, _boostToggle, _boosting, _overheated, _chaseView;
    private bool _soundEnabled = true;
    private float _hull = 100, _shield = 100;
    private long _score;
    private int _kills, _wave = 1;
    private string _adapter = "Detecting adapter…", _performance = "Warming up renderer…";
    private string _status = "SYSTEMS NOMINAL", _comboText = "CHAIN —", _targetText = "SCANNING DEBRIS BELT";
    private string _targetName = "NO TARGET", _targetRange = "—", _flightMode = "INERTIAL CRUISE";
    private string _headingText = "HDG 000°", _viewModeText = "COCKPIT CAM", _vectorText = "DRIFT 00.0 / 00.0";
    private float _targetLeft = 276, _targetTop = 196;
    private float _horizonRoll, _pitchLadderOffset;
    private N.Visibility _targetVisibility = N.Visibility.Collapsed;
    private N.Visibility _gameOverVisibility = N.Visibility.Collapsed, _pauseVisibility = N.Visibility.Collapsed, _damageVisibility = N.Visibility.Collapsed;
    private float _hullBarWidth = 174, _shieldBarWidth = 174, _heatBarWidth, _boostBarWidth = 174;
    private string _hullText = "100%", _shieldText = "100%", _heatText = "STANDBY", _boostText = "100%";
    private string _scoreText = "000000", _waveText = "01", _killsText = "00", _speedText = "08.0 KM/S", _timeText = "00:00";
    private string _attitudeText = "P 00.0°  /  R 00.0°", _positionText = "X +00.0  Y +00.0  Z -54.0", _contactCountText = "120 OBJECTS", _weaponStateText = "DUAL PULSE / READY";

    public IReadOnlyList<Asteroid> Asteroids => _asteroids;
    public IReadOnlyList<PulseBolt> Bolts => _bolts;
    public IReadOnlyList<Blast> Blasts => _blasts;
    public uint SelectedTargetSeed => _lockedTarget?.Seed ?? 0;
    public ObservableCollection<FeedItem> Feed { get; } = [];
    public ObservableCollection<RadarContact> RadarContacts { get; } = [];
    public Vector3 Position => _position;
    public Vector3 Velocity => _velocity;
    public Vector3 Forward => Vector3.Normalize( Vector3.Transform( Vector3.UnitZ, _orientation ) );
    public Vector3 Right => Vector3.Normalize( Vector3.Transform( Vector3.UnitX, _orientation ) );
    public Vector3 Up => Vector3.Normalize( Vector3.Transform( Vector3.UnitY, _orientation ) );
    public Vector3 CameraPosition {
        get {
            Vector3 mount = _chaseView ? _position - Forward * 11.5f + Up * 2.6f : _position;
            Vector3 rattle = Right * (MathF.Sin( (float)_time * 83.0f ) * _cameraShake)
                + Up * (MathF.Sin( (float)_time * 71.0f + 1.2f ) * _cameraShake * 0.72f)
                + Forward * (MathF.Sin( (float)_time * 97.0f + 2.4f ) * _cameraShake * 0.22f);
            return mount + rattle;
        }
    }
    private Quaternion CameraOrientation => ChaseView
        ? _orientation
        : Quaternion.Normalize( _orientation * Quaternion.CreateFromYawPitchRoll( _lookYaw, _lookPitch, 0 ) );
    public Vector3 CameraForward => Vector3.Normalize( Vector3.Transform( Vector3.UnitZ, CameraOrientation ) );
    public Vector3 CameraRight => Vector3.Normalize( Vector3.Transform( Vector3.UnitX, CameraOrientation ) );
    public Vector3 CameraUp => Vector3.Normalize( Vector3.Transform( Vector3.UnitY, CameraOrientation ) );
    public float Fov => _fov;
    public float Time => _time;
    public int DrawCount { get; set; }
    public float Speed => _velocity.Length( );
    public bool Boosting => _boosting;
    public bool ChaseView { get => _chaseView; private set { if ( Set( ref _chaseView, value ) ) { ViewModeText = value ? "EXTERNAL / CHASE" : "COCKPIT / PILOT"; } } }

    public event Action<SoundCue>? SoundRequested;
    public event Action? ToggleFullscreen;

    public string Adapter { get => _adapter; set => Set( ref _adapter, value ); }
    public string Performance { get => _performance; private set => Set( ref _performance, value ); }
    public string Status { get => _status; private set => Set( ref _status, value ); }
    public string ComboText { get => _comboText; private set => Set( ref _comboText, value ); }
    public string TargetText { get => _targetText; private set => Set( ref _targetText, value ); }
    public string TargetName { get => _targetName; private set => Set( ref _targetName, value ); }
    public string TargetRange { get => _targetRange; private set => Set( ref _targetRange, value ); }
    public string FlightMode { get => _flightMode; private set => Set( ref _flightMode, value ); }
    public string HeadingText { get => _headingText; private set => Set( ref _headingText, value ); }
    public float HorizonRoll { get => _horizonRoll; private set => Set( ref _horizonRoll, value ); }
    public float PitchLadderOffset { get => _pitchLadderOffset; private set => Set( ref _pitchLadderOffset, value ); }
    public string ViewModeText { get => _viewModeText; private set => Set( ref _viewModeText, value ); }
    public string VectorText { get => _vectorText; private set => Set( ref _vectorText, value ); }
    public float TargetLeft { get => _targetLeft; private set => Set( ref _targetLeft, value ); }
    public float TargetTop { get => _targetTop; private set => Set( ref _targetTop, value ); }
    public N.Visibility TargetVisibility { get => _targetVisibility; private set => Set( ref _targetVisibility, value ); }
    public string HullText { get => _hullText; private set => Set( ref _hullText, value ); }
    public string ShieldText { get => _shieldText; private set => Set( ref _shieldText, value ); }
    public string HeatText { get => _heatText; private set => Set( ref _heatText, value ); }
    public string BoostText { get => _boostText; private set => Set( ref _boostText, value ); }
    public string ScoreText { get => _scoreText; private set => Set( ref _scoreText, value ); }
    public string WaveText { get => _waveText; private set => Set( ref _waveText, value ); }
    public string KillsText { get => _killsText; private set => Set( ref _killsText, value ); }
    public string SpeedText { get => _speedText; private set => Set( ref _speedText, value ); }
    public string TimeText { get => _timeText; private set => Set( ref _timeText, value ); }
    public string AttitudeText { get => _attitudeText; private set => Set( ref _attitudeText, value ); }
    public string PositionText { get => _positionText; private set => Set( ref _positionText, value ); }
    public string ContactCountText { get => _contactCountText; private set => Set( ref _contactCountText, value ); }
    public string WeaponStateText { get => _weaponStateText; private set => Set( ref _weaponStateText, value ); }
    public float HullBarWidth { get => _hullBarWidth; private set => Set( ref _hullBarWidth, value ); }
    public float ShieldBarWidth { get => _shieldBarWidth; private set => Set( ref _shieldBarWidth, value ); }
    public float HeatBarWidth { get => _heatBarWidth; private set => Set( ref _heatBarWidth, value ); }
    public float BoostBarWidth { get => _boostBarWidth; private set => Set( ref _boostBarWidth, value ); }
    public bool SoundEnabled {
        get => _soundEnabled;
        private set { if ( Set( ref _soundEnabled, value ) ) Notify( nameof(SoundText) ); }
    }
    public string SoundText => SoundEnabled ? "AUDIO / ONLINE" : "AUDIO / MUTED";
    public bool Paused { get => _paused; private set { if ( Set( ref _paused, value ) ) PauseVisibility = value ? N.Visibility.Visible : N.Visibility.Collapsed; } }
    public bool GameOver { get => _gameOver; private set { if ( Set( ref _gameOver, value ) ) GameOverVisibility = value ? N.Visibility.Visible : N.Visibility.Collapsed; } }
    public N.Visibility GameOverVisibility { get => _gameOverVisibility; private set => Set( ref _gameOverVisibility, value ); }
    public N.Visibility PauseVisibility { get => _pauseVisibility; private set => Set( ref _pauseVisibility, value ); }
    public N.Visibility DamageVisibility { get => _damageVisibility; private set => Set( ref _damageVisibility, value ); }

    public ICommand FireCommand { get; }
    public ICommand BoostCommand { get; }
    public ActionCommand PauseCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand TargetCycleCommand { get; }
    public ICommand SoundCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand CenterLookCommand { get; }

    public StarfighterGame( ) {
        FireCommand = new ActionCommand( FireOnce );
        BoostCommand = new ActionCommand( ToggleBoost );
        PauseCommand = new ActionCommand( () => { if(!GameOver)Paused=!Paused; }, () => !GameOver );
        RestartCommand = new ActionCommand( Restart );
        FullscreenCommand = new ActionCommand( () => ToggleFullscreen?.Invoke( ) );
        TargetCycleCommand = new ActionCommand( CycleTarget );
        SoundCommand = new ActionCommand( () => SoundEnabled = !SoundEnabled );
        ViewCommand = new ActionCommand( () => ChaseView = !ChaseView );
        CenterLookCommand = new ActionCommand( CenterLook );
        for ( int i = 0; i < 16; i++ ) RadarContacts.Add( new RadarContact { Size = i < 4 ? 5 : 3 } );
        SeedAsteroidBelt( 138 );
        AddFeed( "VANGUARD / FLIGHT COMPUTER ONLINE" );
        AddFeed( "K-09 ASTEROID BELT // 1.7 AU FROM HELIOS" );
        AddFeed( "WEAPONS FREE — WATCH YOUR SIX" );
        RefreshReadouts( 0.1 );
    }

    public void Look( float dx, float dy ) {
        if ( !float.IsFinite( dx ) || !float.IsFinite( dy ) ) return;
        // MMB head-look is camera-only. Keep the ship attitude and weapon boresight
        // unchanged, and limit the pilot's neck travel to a plausible field of view.
        _lookYaw = Math.Clamp( _lookYaw + dx * 0.0029f, -1.72f, 1.72f );
        _lookPitch = Math.Clamp( _lookPitch + dy * 0.0029f, -1.12f, 1.12f );
    }

    /// <summary>Deflect a virtual flight stick. Only Tick integrates the airframe attitude.</summary>
    public void Steer( float dx, float dy ) {
        if ( Paused || GameOver || !float.IsFinite( dx ) || !float.IsFinite( dy ) ) return;
        const float sensitivity = 1f / 145f;
        _mouseStick = Vector2.Clamp( _mouseStick + new Vector2( dx, -dy ) * sensitivity, -Vector2.One, Vector2.One );
    }
    public void ReleaseSteering( ) => _mouseStick = Vector2.Zero;

    // Pointer aim is limited to a small gimbal cone around the airframe. Space fires
    // along boresight; clicking the scene aims the twin guns at the pointer ray.
    private Vector3? _aimDirection;
    public void AimAt( float x, float y, int width, int height ) {
        if ( width <= 0 || height <= 0 || !float.IsFinite( x ) || !float.IsFinite( y ) ) return;
        float nx = 2 * x / width - 1, ny = 1 - 2 * y / height;
        Vector3 ray = Vector3.Normalize( CameraForward + CameraRight * (nx * Fov * width / height) + CameraUp * (ny * Fov) );
        const float maxTangent = 0.2679492f; // 15 degrees
        float z = Math.Max( 0.001f, Vector3.Dot( ray, Forward ) );
        Vector2 tangent = new( Vector3.Dot( ray, Right ) / z, Vector3.Dot( ray, Up ) / z );
        if ( tangent.LengthSquared( ) > maxTangent * maxTangent ) tangent = Vector2.Normalize( tangent ) * maxTangent;
        _aimDirection = Vector3.Normalize( Forward + Right * tangent.X + Up * tangent.Y );
    }
    public void ClearAim( ) => _aimDirection = null;

    public void CenterLook( ) { _lookYaw = 0; _lookPitch = 0; }
    public void AdjustZoom( int wheelDelta ) => _fov = Math.Clamp( _fov * MathF.Exp( -wheelDelta * 0.00055f ), 0.34f, 0.78f );
    public void TogglePause( ) { if ( !GameOver ) Paused = !Paused; }

    public void Tick( double elapsed, bool yawLeft, bool yawRight, bool pitchUp, bool pitchDown,
                      bool rollLeft, bool rollRight, bool thrust, bool reverse,
                      bool strafeLeft, bool strafeRight, bool liftUp, bool liftDown,
                      bool boostKey, bool fireKey ) {
        float dt = (float)Math.Clamp( elapsed, 0, 0.05 );
        if ( GameOver || Paused ) { ReleaseSteering( ); RefreshReadouts( dt ); return; }
        _time += dt;
        _cameraShake *= MathF.Exp( -7.8f * dt );
        if ( _boosting ) _cameraShake = Math.Max( _cameraShake, 0.006f );

        float yawInput = (yawRight ? 1 : 0) - (yawLeft ? 1 : 0);
        float pitchInput = Math.Clamp( (pitchUp ? 1 : 0) - (pitchDown ? 1 : 0) + _mouseStick.Y, -1f, 1f );
        float rollInput = Math.Clamp( (rollRight ? 1 : 0) - (rollLeft ? 1 : 0) + _mouseStick.X, -1f, 1f );
        // A flight-assist rate controller commands reaction thrusters to accelerate
        // or counter-thrust toward a bounded body-local rate. No instant turns.
        Vector3 desiredRate = new Vector3( -pitchInput, yawInput, -rollInput ) * MaxAngularRate;
        Vector3 previousRate = _angularVelocity;
        Vector3 step = MaxAngularAcceleration * dt;
        _angularVelocity += Vector3.Clamp( desiredRate - _angularVelocity, -step, step );
        Vector3 rotation = (previousRate + _angularVelocity) * (0.5f * dt);
        float angle = rotation.Length( );
        if ( angle > 1e-8f ) {
            var delta = Quaternion.CreateFromAxisAngle( rotation / angle, angle );
            _orientation = Quaternion.Normalize( _orientation * delta );
        }
        _rollReadout = MathF.IEEERemainder( _rollReadout + rotation.Z, MathF.Tau );

        bool boosting = (boostKey || _boostToggle) && _boostEnergy > 1;
        bool boostChanged = boosting != _boosting;
        _boosting = boosting;
        if ( boostChanged ) Sound( boosting ? SoundCue.BoostOn : SoundCue.BoostOff );
        float thrustForce = boosting ? 37f : 19f;
        float sideForce = boosting ? 11f : 7.5f;
        Vector3 acceleration = Forward * (((thrust ? 1 : 0) - (reverse ? 0.62f : 0)) * thrustForce)
            + Right * (((strafeRight ? 1 : 0) - (strafeLeft ? 1 : 0)) * sideForce)
            + Up * (((liftUp ? 1 : 0) - (liftDown ? 1 : 0)) * sideForce);
        // Translational inertia is independent of attitude. There is no drag in
        // vacuum, nor a speed clamp which destroys momentum on boost release.
        _position += _velocity * dt + acceleration * (0.5f * dt * dt);
        _velocity += acceleration * dt;
        _boostEnergy = Math.Clamp( _boostEnergy + (boosting ? -28f : 13f) * dt, 0, 100 );
        _heat = Math.Max( 0, _heat - dt * 25 );
        if ( _overheated && _heat < 32 ) { _overheated = false; Status = "EMITTERS COOLED // WEAPONS READY"; }
        _shotClock -= dt;
        if ( fireKey && !_overheated && _heat < 96 && _shotClock <= 0 ) Shoot( );

        // The asteroid belt is a persistent torus around Helios, not a timed wave backdrop.
        _spawnClock -= dt;
        if ( _asteroids.Count < 138 && _spawnClock <= 0 ) {
            SpawnBeltAsteroid( ); _spawnClock = 0.12f + (float)_random.NextDouble( ) * 0.34f;
        }

        for ( int i = _asteroids.Count - 1; i >= 0; i-- ) {
            Asteroid a = _asteroids[i];
            if ( a.OrbitSpeed != 0 ) {
                float angleF = a.OrbitSpeed * dt, c = MathF.Cos( angleF ), s = MathF.Sin( angleF );
                a.Position = new( a.Position.X * c + a.Position.Z * s, a.Position.Y, -a.Position.X * s + a.Position.Z * c );
            } else a.Position += a.Velocity * dt;
            a.Rotation += a.Spin * dt; a.RotationX += a.Spin * 0.37f * dt; a.RotationZ -= a.Spin * 0.21f * dt;
            if ( float.IsFinite( a.Life ) ) { a.Life -= dt; if ( a.Life <= 0 ) { _asteroids.RemoveAt( i ); continue; } }
            float distance = Vector3.Distance( a.Position, _position );
            if ( distance < a.Radius + 1.05f ) {
                Damage( a.Radius > 1.4f ? 22 : 12 ); _asteroids.RemoveAt( i );
            } else if ( distance > 135 ) _asteroids.RemoveAt( i );
        }

        if ( _environmentImpactDelay > 0 ) _environmentImpactDelay -= dt;
        else CheckSystemBodyImpact( );

        for ( int b = _bolts.Count - 1; b >= 0; b-- ) {
            PulseBolt bolt = _bolts[b];
            Vector3 segmentStart = bolt.Position;
            Vector3 segmentEnd = segmentStart + bolt.Velocity * dt;
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = Math.Max( segment.LengthSquared( ), 1e-6f );
            bolt.Position = segmentEnd; bolt.Age += dt;
            bool hit = false;
            // Test the whole swept bolt segment, not just its endpoint. At fighter-cannon
            // velocity an endpoint-only test can tunnel through a small shard at 60 FPS.
            for ( int a = _asteroids.Count - 1; a >= 0; a-- ) {
                Asteroid asteroid = _asteroids[a];
                float along = Math.Clamp( Vector3.Dot( asteroid.Position - segmentStart, segment ) / segmentLengthSquared, 0, 1 );
                Vector3 closest = segmentStart + segment * along;
                float reach = asteroid.Radius + 0.23f;
                if ( Vector3.DistanceSquared( closest, asteroid.Position ) > reach * reach ) continue;
                bolt.Position = closest;
                asteroid.Hits--; hit = true; Sound( SoundCue.Impact );
                if ( asteroid.Hits <= 0 ) DestroyAsteroid( a, asteroid );
                else { asteroid.Radius *= 0.84f; AddBlast( closest, 0.45f, 0.22f ); }
                break;
            }
            if ( hit || bolt.Age > 2.8f ) _bolts.RemoveAt( b );
        }

        for ( int i = _blasts.Count - 1; i >= 0; i-- ) { _blasts[i].Age += dt; if ( _blasts[i].Age >= _blasts[i].Duration ) _blasts.RemoveAt( i ); }
        if ( _shieldDelay > 0 ) _shieldDelay -= dt;
        else _shield = Math.Min( 100, _shield + dt * 5.4f );
        if ( _damageFlash > 0 ) { _damageFlash -= dt; if ( _damageFlash <= 0 ) DamageVisibility = N.Visibility.Collapsed; }
        if ( _comboTimer > 0 ) _comboTimer -= (int)(dt * 1000);
        else if ( _combo != 0 ) { _combo = 0; ComboText = "CHAIN —"; }
        UpdateTargeting( );
        RefreshReadouts( dt );
    }

    private void SeedAsteroidBelt( int count ) {
        for ( int i = 0; i < count; i++ ) SpawnBeltAsteroid( );
    }

    private void SpawnBeltAsteroid( ) {
        float angle = (float)_random.NextDouble( ) * MathF.Tau;
        float radius = 27f + (float)_random.NextDouble( ) * 10.5f;
        float vertical = ((float)_random.NextDouble( ) - 0.5f) * 5.8f;
        float orbitSpeed = (0.16f + 0.12f * MathF.Pow( 30f / radius, 1.35f )) * ( _random.Next( 2 ) == 0 ? 1 : -1 );
        float size = 0.32f + MathF.Pow( (float)_random.NextDouble( ), 1.8f ) * 1.65f;
        _asteroids.Add( new Asteroid {
            Position = new( MathF.Cos( angle ) * radius, vertical, MathF.Sin( angle ) * radius ),
            Radius = size, Rotation = (float)_random.NextDouble( ) * MathF.Tau,
            RotationX = (float)_random.NextDouble( ) * MathF.Tau, RotationZ = (float)_random.NextDouble( ) * MathF.Tau,
            Spin = ((float)_random.NextDouble( ) - 0.5f) * 1.8f, OrbitSpeed = orbitSpeed,
            Hits = size > 1.35f ? 2 : 1, Seed = (uint)_random.Next( 1, int.MaxValue )
        } );
    }

    private void Shoot( ) {
        Vector3 forward = Forward, right = Right, up = Up;
        Vector3 aim = _aimDirection ?? forward;
        Vector3 convergence = _position + forward * 5.18f + aim * 70f;
        // Match the actual twin cannon rails on the external fighter mesh. Muzzles sit
        // beyond the forward canopy bow, so bolts cannot originate inside the cockpit.
        const float muzzleForward = 5.18f;
        const float muzzleOffset = 1.07f;
        _bolts.Add( new( ) { Position = _position + forward * muzzleForward - right * muzzleOffset + up * 0.01f,
            Velocity = _velocity + Vector3.Normalize( convergence - (_position + forward * muzzleForward - right * muzzleOffset + up * 0.01f) ) * 155, Age = 0, Side = -1 } );
        _bolts.Add( new( ) { Position = _position + forward * muzzleForward + right * muzzleOffset + up * 0.01f,
            Velocity = _velocity + Vector3.Normalize( convergence - (_position + forward * muzzleForward + right * muzzleOffset + up * 0.01f) ) * 155, Age = 0, Side = 1 } );
        _heat = Math.Min( 100, _heat + 7.1f ); _shotClock = 0.115f;
        _cameraShake = Math.Max( _cameraShake, 0.012f );
        Sound( SoundCue.Cannon );
        if ( _heat >= 98 ) { _overheated = true; Status = "EMITTER THERMAL LIMIT"; Sound( SoundCue.Overheat ); AddFeed( "WEAPON THERMAL LIMIT // EMITTER CYCLING" ); }
    }

    private void FireOnce( ) { if ( !_paused && !_gameOver && !_overheated && _shotClock <= 0 ) Shoot( ); }

    private void DestroyAsteroid( int index, Asteroid asteroid ) {
        _asteroids.RemoveAt( index ); _kills++;
        _combo = _comboTimer > 0 ? _combo + 1 : 1; _comboTimer = 2600;
        int points = (int)(100 * _combo * _wave * Math.Max( 0.5f, asteroid.Radius ));
        _score += points;
        ComboText = _combo >= 2 ? $"{_combo}× CHAIN   +{points:N0}" : $"+{points:N0} // CONFIRMED";
        Status = "ASTEROID FRACTURE CONFIRMED";
        AddFeed( $"CONTACT SHATTERED // +{points:N0} SCORE" );
        AddBlast( asteroid.Position, Math.Max( 0.9f, asteroid.Radius * 1.65f ), 0.48f );
        Sound( SoundCue.Explosion );
        // Large rocks break into several independently tumbling, collidable fragments.
        int fragments = asteroid.Radius > 1.25f ? 4 + _random.Next( 3 ) : 2 + _random.Next( 2 );
        for ( int i = 0; i < fragments; i++ ) {
            Vector3 direction = RandomDirection( );
            float shardScale = asteroid.Radius > 1.25f ? 0.28f + (float)_random.NextDouble( ) * 0.19f : 0.22f + (float)_random.NextDouble( ) * 0.12f;
            _asteroids.Add( new Asteroid {
                Position = asteroid.Position + direction * asteroid.Radius * 0.42f,
                Velocity = asteroid.Velocity + direction * (2.8f + (float)_random.NextDouble( ) * 5.2f),
                Radius = Math.Max( 0.12f, asteroid.Radius * shardScale ), Rotation = (float)_random.NextDouble( ) * MathF.Tau,
                RotationX = (float)_random.NextDouble( ) * MathF.Tau, RotationZ = (float)_random.NextDouble( ) * MathF.Tau,
                Spin = ((float)_random.NextDouble( ) - 0.5f) * 6.0f, OrbitSpeed = 0,
                Life = 4.5f + (float)_random.NextDouble( ) * 4.0f, Hits = 1,
                Seed = asteroid.Seed + (uint)i + 1, Fragment = true
            } );
        }
        if ( _kills % 18 == 0 ) { _wave++; AddFeed( $"SECTOR THREAT ESCALATED // CONTRACT {_wave:00}" ); Status = $"SECTOR {_wave:00} // DEBRIS DENSITY RISING"; }
    }

    private Vector3 RandomDirection( ) {
        Vector3 value;
        do { value = new( (float)_random.NextDouble( ) * 2 - 1, (float)_random.NextDouble( ) * 2 - 1, (float)_random.NextDouble( ) * 2 - 1 ); }
        while ( value.LengthSquared( ) < 0.08f || value.LengthSquared( ) > 1 );
        return Vector3.Normalize( value );
    }

    private void AddBlast( Vector3 position, float radius, float duration ) => _blasts.Add( new( ) {
        Position = position, Radius = radius, Duration = duration, Age = 0, Seed = (uint)_random.Next( 1, int.MaxValue )
    } );

    private void CheckSystemBodyImpact( ) {
        float phase = _time * 0.024f;
        Vector3 innerPlanet = new( MathF.Cos( phase ) * 16.8f, 0.7f * MathF.Sin( phase * 0.7f ), MathF.Sin( phase ) * 16.8f );
        float outerPhase = _time * 0.010f + 1.7f;
        Vector3 outerPlanet = new( MathF.Cos( outerPhase ) * 47f, 1.8f * MathF.Sin( outerPhase * 0.43f ), MathF.Sin( outerPhase ) * 47f );
        if ( DeflectFromBody( Vector3.Zero, 7.2f, 42 ) || DeflectFromBody( innerPlanet, 2.3f, 27 ) || DeflectFromBody( outerPlanet, 4.35f, 30 ) ) {
            _environmentImpactDelay = 1.4f;
        }
    }

    private bool DeflectFromBody( Vector3 center, float radius, float damage ) {
        Vector3 delta = _position - center;
        float minimum = radius + 1.15f;
        float distance = delta.Length( );
        if ( distance >= minimum ) return false;
        Vector3 normal = distance > 0.001f ? delta / distance : -Forward;
        _position = center + normal * minimum;
        _velocity = Vector3.Reflect( _velocity, normal ) * 0.64f;
        AddFeed( "PROXIMITY WARNING // GRAVITY WELL / SURFACE DEFLECTION" );
        Damage( damage );
        return true;
    }

    private void Damage( float amount ) {
        _shieldDelay = 3.2f; float absorbed = Math.Min( _shield, amount * 0.78f ); _shield -= absorbed;
        _hull = Math.Max( 0, _hull - (amount - absorbed) );
        _damageFlash = 0.32f; _cameraShake = Math.Max( _cameraShake, amount > 30 ? 0.24f : 0.13f );
        DamageVisibility = N.Visibility.Visible; Status = "PROXIMITY IMPACT // EVADE";
        AddFeed( $"HULL STRIKE // SHIELDS ABSORBED {absorbed:0}%" ); Sound( SoundCue.HullHit );
        if ( _hull <= 0 ) { GameOver = true; AddFeed( "FLIGHT TERMINATED // EJECTION SYSTEM ARMED" ); }
    }

    private Asteroid? _lockedTarget;
    private void UpdateTargeting( ) {
        Asteroid? selected = null;
        float bestRange = float.MaxValue;

        // Keep a pilot-selected contact until it is destroyed or leaves the forward acquisition cone.
        // Automatic target acquisition only runs when the current lock is no longer viable.
        if ( _lockedTarget is not null && _asteroids.Contains( _lockedTarget ) ) {
            Vector3 lockedDelta = _lockedTarget.Position - _position;
            float lockedRange = lockedDelta.Length( );
            float lockedAlignment = lockedRange > 0.001f ? Vector3.Dot( Forward, lockedDelta / lockedRange ) : 1;
            if ( lockedRange < 115 && lockedAlignment > 0.12f ) { selected = _lockedTarget; bestRange = lockedRange; }
        }
        if ( selected is null ) {
            float bestDot = 0.94f;
            foreach ( Asteroid asteroid in _asteroids ) {
                Vector3 delta = asteroid.Position - _position; float distance = delta.Length( );
                if ( distance < 1 || distance > 90 ) continue;
                float alignment = Vector3.Dot( Forward, delta / distance );
                if ( alignment > bestDot || (Math.Abs( alignment - bestDot ) < 0.002f && distance < bestRange) ) {
                    bestDot = alignment; bestRange = distance; selected = asteroid;
                }
            }
        }
        if ( !ReferenceEquals( selected, _lockedTarget ) && selected is not null ) Sound( SoundCue.Lock );
        _lockedTarget = selected;
        TargetName = selected is null ? "NO TARGET" : selected.Fragment ? "DEBRIS SHARD" : "ASTEROID / HOSTILE";
        TargetRange = selected is null ? "—" : $"{bestRange:0.0} KM";
        TargetText = selected is null ? "SCANNING / NO LOCK" : $"TRACK LOCK // {bestRange:0.0} KM";
        if ( selected is null ) { TargetVisibility = N.Visibility.Collapsed; return; }
        Vector3 deltaToTarget = selected.Position - CameraPosition;
        float depth = Vector3.Dot( Forward, deltaToTarget );
        if ( depth <= 0.4f ) { TargetVisibility = N.Visibility.Collapsed; return; }
        float nx = Vector3.Dot( Right, deltaToTarget ) / (depth * _fov * 1.68f);
        float ny = Vector3.Dot( Up, deltaToTarget ) / (depth * _fov);
        TargetLeft = Math.Clamp( 296 + nx * 245, 8, 536 );
        TargetTop = Math.Clamp( 214 - ny * 170, 8, 376 );
        TargetVisibility = Math.Abs( nx ) < 1.13f && Math.Abs( ny ) < 1.13f ? N.Visibility.Visible : N.Visibility.Collapsed;
    }

    private void UpdateRadar( ) {
        int index = 0;
        foreach ( Asteroid asteroid in _asteroids ) {
            Vector3 delta = asteroid.Position - _position;
            float x = Vector3.Dot( delta, Right ), y = Vector3.Dot( delta, Forward );
            if ( Math.Abs( x ) > 50 || Math.Abs( y ) > 50 ) continue;
            if ( index >= RadarContacts.Count ) break;
            RadarContact dot = RadarContacts[index++];
            dot.Left = Math.Clamp( 87 + x * 1.33f, 8, 166 ); dot.Top = Math.Clamp( 87 - y * 1.33f, 8, 166 );
            dot.Size = asteroid.Fragment ? 3 : asteroid.Radius > 1.3f ? 6 : 4;
            dot.Color = asteroid.Fragment ? "#E9D7A0" : "#FFB477";
        }
        while ( index < RadarContacts.Count ) { RadarContacts[index].Left = -20; RadarContacts[index].Top = -20; index++; }
    }

    private void RefreshReadouts( double dt ) {
        _readoutClock += dt; _radarClock += dt;
        if ( _radarClock >= 0.08 ) { _radarClock = 0; UpdateRadar( ); }
        if ( _readoutClock < 0.08 ) return;
        _readoutClock = 0;
        HullText = $"{_hull:0}%"; ShieldText = $"{_shield:0}%";
        HeatText = _overheated ? "LOCKOUT" : _heat > 72 ? "HOT" : "READY";
        BoostText = $"{_boostEnergy:0}%"; ScoreText = _score.ToString( "000000" ); WaveText = _wave.ToString( "00" ); KillsText = _kills.ToString( "00" );
        SpeedText = $"{Speed:00.0} KM/S"; TimeText = TimeSpan.FromSeconds( _time ).ToString( @"mm\:ss" );
        FlightMode = _boosting ? "AFTERBURNER / ENGAGED" : Speed > 0.6f ? "INERTIAL / DRIFT" : "MANEUVER / STATION KEEP";
        WeaponStateText = _overheated ? "EMITTER LOCKOUT" : _heat > 70 ? "DUAL PULSE / HOT" : "DUAL PULSE / READY";
        PositionText = $"X {_position.X:+00.0;-00.0}  Y {_position.Y:+00.0;-00.0}  Z {_position.Z:+00.0;-00.0}";
        float pitch = MathF.Asin( Math.Clamp( Forward.Y, -1, 1 ) ) * 180 / MathF.PI;
        float heading = (MathF.Atan2( Forward.X, Forward.Z ) * 180 / MathF.PI + 360) % 360;
        HeadingText = $"HDG {heading:000}°";
        AttitudeText = $"PIT {pitch:+000;-000}°   ROL {_rollReadout * 180 / MathF.PI:+000;-000}°";
        HorizonRoll = -_rollReadout * 180 / MathF.PI;
        PitchLadderOffset = Math.Clamp( pitch * 1.30f, -72, 72 );
        VectorText = $"DRIFT {Vector3.Dot( Velocity, Right ):00.0} / {Vector3.Dot( Velocity, Up ):00.0}";
        HullBarWidth = 174 * _hull / 100; ShieldBarWidth = 174 * _shield / 100;
        HeatBarWidth = 174 * _heat / 100; BoostBarWidth = 174 * _boostEnergy / 100;
        ContactCountText = $"{_asteroids.Count:000} CONTACTS";
        PauseCommand.NotifyCanExecuteChanged( );
    }

    private void ToggleBoost( ) {
        _boostToggle = !_boostToggle; AddFeed( _boostToggle ? "AFTERBURNER // MANUAL OVERRIDE" : "AFTERBURNER // DISENGAGED" );
    }

    private void CycleTarget( ) {
        var candidates = _asteroids
            .Select( asteroid => (Asteroid: asteroid, Delta: asteroid.Position - _position) )
            .Select( item => (item.Asteroid, Range: item.Delta.Length( ), Alignment: Vector3.Dot( Forward, Vector3.Normalize( item.Delta ) )) )
            .Where( item => item.Range > 1 && item.Range < 115 && item.Alignment > 0.68f )
            .OrderBy( item => item.Range )
            .ToArray( );
        if ( candidates.Length == 0 ) { _lockedTarget = null; UpdateTargeting( ); return; }
        int current = Array.FindIndex( candidates, item => ReferenceEquals( item.Asteroid, _lockedTarget ) );
        var next = candidates[(current + 1) % candidates.Length];
        _lockedTarget = next.Asteroid;
        UpdateTargeting( );
        AddFeed( $"TRACK DESIGNATED // {next.Range:0.0} KM" );
    }

    private void Sound( SoundCue cue ) { if ( SoundEnabled ) SoundRequested?.Invoke( cue ); }
    public void ReportFrame( double dt, uint batches ) {
        _statsFrames++; _performanceClock += dt;
        if ( _performanceClock < 0.5 ) return;
        Performance = $"{_statsFrames / _performanceClock:F0} FPS  ·  {1000 * _performanceClock / _statsFrames:F1} ms  ·  {batches} UI batches  ·  {DrawCount} world draws";
        _statsFrames = 0; _performanceClock = 0;
    }

    private void AddFeed( string text ) {
        Feed.Insert( 0, new( ) { Stamp = TimeSpan.FromSeconds( _time ).ToString( @"mm\:ss" ), Message = text } );
        while ( Feed.Count > 5 ) Feed.RemoveAt( Feed.Count - 1 );
    }

    public void Restart( ) {
        _asteroids.Clear( ); _bolts.Clear( ); _blasts.Clear( ); _position = new( 0, 0.5f, -54 ); _velocity = new( 0, 0, 8 );
        _orientation = Quaternion.Identity; _angularVelocity = Vector3.Zero; _rollReadout = 0; _fov = 0.52f;
        _time = 0; _readoutClock = 0.08; _spawnClock = 1.5f; _shotClock = _heat = 0; _shieldDelay = 0; _damageFlash = _cameraShake = 0; _boostEnergy = 100; _environmentImpactDelay = 0;
        _combo = _comboTimer = 0; _hull = _shield = 100; _score = 0; _kills = 0; _wave = 1;
        _overheated = _boostToggle = _boosting = _paused = _gameOver = false;
        _lookYaw = _lookPitch = 0; ReleaseSteering( ); ClearAim( );
        ChaseView = false;
        PauseVisibility = GameOverVisibility = DamageVisibility = N.Visibility.Collapsed;
        TargetVisibility = N.Visibility.Collapsed;
        _lockedTarget = null; SeedAsteroidBelt( 138 ); Status = "SYSTEMS NOMINAL"; ComboText = "CHAIN —";
        AddFeed( "FLIGHT REINITIALIZED // K-09 PATROL" ); PauseCommand.NotifyCanExecuteChanged( ); RefreshReadouts( 0.1 );
    }
}
