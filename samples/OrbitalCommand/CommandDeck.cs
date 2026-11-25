using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using N = Noesis;

namespace OrbitalCommand;

public abstract class Observable : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify( [CallerMemberName] string? name = null ) => PropertyChanged?.Invoke( this, new( name ) );
    protected bool Set<T>( ref T field, T value, [CallerMemberName] string? name = null ) {
        if ( EqualityComparer<T>.Default.Equals( field, value ) ) return false;
        field = value; Notify( name ); return true;
    }
}

public sealed class ActionCommand( Action action, Func<bool>? enabled = null ) : ICommand {
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute( object? parameter ) => enabled?.Invoke( ) ?? true;
    public void Execute( object? parameter ) { if ( CanExecute( parameter ) ) action( ); }
    public void Refresh( ) => CanExecuteChanged?.Invoke( this, EventArgs.Empty );
}

public sealed class ShipViewModel : Observable {
    public Vessel Vessel { get; }
    public string Name => Vessel.Name;
    public string Role => Vessel.Role.ToUpperInvariant( );
    public string Status => Vessel.Mission switch {
        Mission.Survey => $"SURVEY  {Vessel.Survey:P0}", Mission.Patrol => "PATROL", Mission.Return => "RETURNING", _ => "IN ORBIT"
    };
    public string Distance => $"{Vessel.Radius * 6400:N0} km";
    public float Progress => Vessel.Survey * 100;
    public float ScreenX { get; private set; }
    public float ScreenY { get; private set; }
    public N.Visibility MarkerVisibility { get; private set; } = N.Visibility.Collapsed;
    public string MarkerText => $"◇  {Name[4..]}";
    public ICommand SelectCommand { get; }
    public ShipViewModel( Vessel vessel, Action<ShipViewModel> select ) { Vessel = vessel; SelectCommand = new ActionCommand( () => select( this ) ); }
    public void Refresh( ) { Notify( nameof(Status) ); Notify( nameof(Distance) ); Notify( nameof(Progress) ); }
    public ScreenViewport LabelBounds { get; private set; }
    public float AnchorX { get; private set; }
    public float AnchorY { get; private set; }
    public float LeaderX => ScreenX - AnchorX;
    public float LeaderY => ScreenY - AnchorY + 10;
    public void PlaceLabel( System.Numerics.Vector2 anchor, ScreenViewport bounds, float scale, bool visible ) {
        float x = MathF.Round( bounds.X ) / scale, y = MathF.Round( bounds.Y ) / scale;
        float ax = MathF.Round( anchor.X ) / scale, ay = MathF.Round( anchor.Y ) / scale;
        LabelBounds = bounds;
        if ( ScreenX != x ) { ScreenX = x; Notify( nameof(ScreenX) ); Notify( nameof(LeaderX) ); }
        if ( ScreenY != y ) { ScreenY = y; Notify( nameof(ScreenY) ); Notify( nameof(LeaderY) ); }
        if ( AnchorX != ax ) { AnchorX = ax; Notify( nameof(AnchorX) ); Notify( nameof(LeaderX) ); }
        if ( AnchorY != ay ) { AnchorY = ay; Notify( nameof(AnchorY) ); Notify( nameof(LeaderY) ); }
        var visibility = visible ? N.Visibility.Visible : N.Visibility.Collapsed;
        if ( MarkerVisibility != visibility ) { MarkerVisibility = visibility; Notify( nameof(MarkerVisibility) ); }
    }

}

public sealed class LogEntry {
    public required string Time { get; init; }
    public required string Message { get; init; }
}

public sealed class CommandDeck : Observable {
    public Simulation Simulation { get; } = new( );
    public OrbitCamera Camera { get; } = new( );
    public ObservableCollection<ShipViewModel> Ships { get; } = [];
    public ObservableCollection<LogEntry> Events { get; } = [];
    private ShipViewModel _selected = null!;
    private bool _paused, _autoCamera, _showOrbits = true, _showLabels = true;
    private float _exposure = 1.15f, _atmosphere = 1, _speed = 1;
    private string _callSign = "ARKAEN / ORBITAL CONTROL", _performance = "Initializing renderer…", _adapter = "";
    private N.Visibility _briefing = N.Visibility.Collapsed;
    public ShipViewModel SelectedShip { get => _selected; set { if ( value is not null && Set( ref _selected, value ) ) { Notify( nameof(SelectionIndex) ); } } }
    public int SelectionIndex => Ships.IndexOf( SelectedShip );
    public bool Paused { get => _paused; set { if ( Set( ref _paused, value ) ) { Simulation.Paused = value; Notify( nameof(PauseText) ); } } }
    public string PauseText => Paused ? "▶  RESUME" : "Ⅱ  PAUSE";
    public bool FollowSelection { get; set; }
    private float _interfaceScale = 1;
    public float InterfaceScale { get => _interfaceScale; set { if ( Set( ref _interfaceScale, Math.Clamp( value, 0.8f, 1.5f ) ) ) Notify( nameof(InterfaceScaleText) ); } }
    public string InterfaceScaleText => $"{InterfaceScale:P0}";
    public ICommand IncreaseUiCommand { get; }
    public ICommand DecreaseUiCommand { get; }
    public bool AutoCamera { get => _autoCamera; set => Set( ref _autoCamera, value ); }
    public bool ShowOrbits { get => _showOrbits; set => Set( ref _showOrbits, value ); }
    public bool ShowLabels { get => _showLabels; set => Set( ref _showLabels, value ); }
    public float Exposure { get => _exposure; set => Set( ref _exposure, value ); }
    public float Atmosphere { get => _atmosphere; set => Set( ref _atmosphere, value ); }
    public float Speed { get => _speed; set { if ( Set( ref _speed, value ) ) { Simulation.Speed = value; Notify( nameof(SpeedText) ); } } }
    public string SpeedText => $"{Speed:F1}×";
    public string Credits => $"{Simulation.Credits:N0}";
    public string Research => $"{Simulation.Science:N0}";
    public string FleetCount => $"{Ships.Count:00} / 08";
    public string Clock => "T+ " + TimeSpan.FromSeconds( Simulation.Time ).ToString( @"hh\:mm\:ss" );
    public string CallSign { get => _callSign; set => Set( ref _callSign, value ); }
    public string Performance { get => _performance; private set => Set( ref _performance, value ); }
    public string Adapter { get => _adapter; set => Set( ref _adapter, value ); }
    public N.Visibility BriefingVisibility { get => _briefing; set => Set( ref _briefing, value ); }
    public ICommand PauseCommand { get; }
    public ICommand SurveyCommand { get; }
    public ICommand PatrolCommand { get; }
    public ICommand RecallCommand { get; }
    public ICommand FocusCommand { get; }
    public ICommand ResetCameraCommand { get; }
    public ActionCommand LaunchCommand { get; }
    public ICommand BriefingCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand FullscreenCommand { get; }
    public event Action? ToggleFullscreen;
    private double _notifyTime, _statsTime;
    private int _frames;
    public uint LastBatches { get; private set; }

    public CommandDeck( ) {
        foreach ( var ship in Simulation.Vessels ) Ships.Add( new( ship, Select ) );
        _selected = Ships[0];
        PauseCommand = new ActionCommand( () => Paused = !Paused );
        SurveyCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Survey ) );
        PatrolCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Patrol ) );
        RecallCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Return ) );
        FocusCommand = new ActionCommand( () => {
            var position = SelectedShip.Vessel.Position;
            Camera.Target = position; Camera.Distance = 2.8f;
            Camera.Yaw = MathF.Atan2( position.X, position.Z ); Camera.Pitch = 0.24f;
            AutoCamera = false; FollowSelection = true;
        } );
        ResetCameraCommand = new ActionCommand( () => { Camera.Reset( ); FollowSelection = false; AutoCamera = false; } );
        IncreaseUiCommand = new ActionCommand( () => InterfaceScale += 0.1f );
        DecreaseUiCommand = new ActionCommand( () => InterfaceScale -= 0.1f );
        LaunchCommand = new ActionCommand( () => {
            if ( !Simulation.Launch( ) ) return;
            var ship = new ShipViewModel( Simulation.Vessels[^1], Select ); Ships.Add( ship ); SelectedShip = ship;
            Notify( nameof(FleetCount) ); LaunchCommand!.Refresh( );
        }, () => Simulation.Vessels.Count < 8 && Simulation.Credits >= 160 );
        BriefingCommand = new ActionCommand( () => BriefingVisibility = N.Visibility.Visible );
        DismissCommand = new ActionCommand( () => BriefingVisibility = N.Visibility.Collapsed );
        FullscreenCommand = new ActionCommand( () => ToggleFullscreen?.Invoke( ) );
        Simulation.Event += AddEvent;
        AddEvent( "Kepler Anchorage online. Fleet telemetry synchronized." );
        AddEvent( "Select a vessel, then survey the outer ring or engage patrol." );
    }

    private void Select( ShipViewModel ship ) => SelectedShip = ship;
    public void AddEvent( string text ) {
        Events.Insert( 0, new( ) { Time = TimeSpan.FromSeconds( Simulation.Time ).ToString( @"mm\:ss" ), Message = text } );
        while ( Events.Count > 5 ) Events.RemoveAt( Events.Count - 1 );
    }
    public void Tick( double dt, float width, float height, float scale ) {
        var previousTarget = Camera.Target;
        Simulation.Tick( dt );
        if ( FollowSelection ) {
            Camera.Target = SelectedShip.Vessel.Position;
            float turn = MathF.Atan2( Camera.Target.X, Camera.Target.Z ) - MathF.Atan2( previousTarget.X, previousTarget.Z );
            Camera.Yaw += MathF.IEEERemainder( turn, MathF.Tau );
        }
        if ( AutoCamera ) Camera.Yaw += (float)dt * 0.055f;
        _notifyTime += dt;
        LayoutLabels( width, height, scale );
        if ( _notifyTime < 0.1 ) return;
        _notifyTime = 0;
        foreach ( var ship in Ships ) ship.Refresh( );
        Notify( nameof(Credits) ); Notify( nameof(Research) ); Notify( nameof(Clock) );
        LaunchCommand.Refresh( );
    }
    public void ReportFrame( double dt, uint batches ) {
        LastBatches = batches; _frames++; _statsTime += dt;
        if ( _statsTime < 0.5 ) return;
        Performance = $"{_frames / _statsTime:F0} FPS   ·   {1000 * _statsTime / _frames:F1} ms   ·   {batches} UI batches   ·   2 frames in flight";
        _statsTime = 0; _frames = 0;
    }
    private void LayoutLabels( float width, float height, float scale ) {
        var viewport = Camera.GetViewport( width, height );
        var occupied = new List<ScreenViewport>( );
        foreach ( var ship in Ships.OrderByDescending( item => ReferenceEquals( item, SelectedShip ) ) ) {
            bool visible = ShowLabels && Camera.Project( ship.Vessel.Position, width, height, out _ );
            Camera.Project( ship.Vessel.Position, width, height, out var anchor );
            float w = (ship.MarkerText.Length * 6.8f + 16) * scale, h = 24 * scale;
            ScreenViewport box = default;
            bool placed = false;
            foreach ( var offset in new System.Numerics.Vector2[] { new( 18, -12 ), new( 18, -40 ), new( 18, 16 ), new( -w / scale - 18, -12 ), new( 18, -68 ), new( 18, 44 ) } ) {
                float x = Math.Clamp( anchor.X + offset.X * scale, viewport.X + 8 * scale, Math.Max( viewport.X + 8 * scale, viewport.X + viewport.Width - w - 8 * scale ) );
                float y = Math.Clamp( anchor.Y + offset.Y * scale, viewport.Y + 88 * scale, Math.Max( viewport.Y + 88 * scale, viewport.Y + viewport.Height - h - 46 * scale ) );
                box = new( x, y, w, h );
                if ( occupied.Any( other => box.X < other.X + other.Width + 4 * scale && box.X + box.Width + 4 * scale > other.X && box.Y < other.Y + other.Height + 3 * scale && box.Y + box.Height + 3 * scale > other.Y ) ) continue;
                placed = true; break;
            }
            visible &= placed;
            if ( visible ) occupied.Add( box );
            ship.PlaceLabel( anchor, box, scale, visible );
        }
    }
    public bool SelectAt( float x, float y, float width, float height ) {
        foreach ( var ship in Ships ) {
            if ( ship.MarkerVisibility == N.Visibility.Visible && ship.LabelBounds.Contains( x, y ) ) { SelectedShip = ship; return true; }
        }
        float best = 40 * 40;
        ShipViewModel? picked = null;
        foreach ( var ship in Ships ) {
            if ( !Camera.Project( ship.Vessel.Position, width, height, out var pixel ) ) continue;
            float distance = System.Numerics.Vector2.DistanceSquared( pixel, new( x, y ) );
            if ( distance >= best ) continue;
            best = distance; picked = ship;
        }
        if ( picked is null ) return false;
        SelectedShip = picked; return true;
    }
}
