using System.Collections.ObjectModel;
using System.ComponentModel;
using DXSharp.Noesis.Samples;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using N = Noesis;

namespace OrbitalCommand;

// Keep the native Noesis reflection boundary limited to the original CLR contracts.
// Toolkit commands remain an implementation detail behind ActionCommand.
public abstract class Observable : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify( [CallerMemberName] string? name = null ) => PropertyChanged?.Invoke( this, new( name ) );
    protected bool Set<T>( ref T field, T value, [CallerMemberName] string? name = null ) {
        if ( EqualityComparer<T>.Default.Equals( field, value ) ) return false;
        field = value; Notify( name ); return true;
    }
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

public sealed record SolarWorldLabel(string Name,float X,float Y);

public sealed class LogEntry {
    public required string Time { get; init; }
    public required string Message { get; init; }
}

public sealed class CommandDeck : Observable {
    public Simulation Simulation { get; } = new( );
    public ObservableCollection<PlanetMarker> PlanetMarkers { get; } = [];
    public ObservableCollection<OrbitSegment> SolarOrbits { get; } = [];
    private IReadOnlyList<PlanetState> _planetStates = [];
    private bool _solarMap;
    private bool _solar3D;
    private HorizonsSeries? _series;
    private bool _usingHorizons;
    public string SolarEphemerisSource => _usingHorizons ? "JPL Horizons · interpolated states" : "JPL fitted elements · Earth–Moon barycenter";
    private double _solarJd=2461370.5;
    private bool _solarClockRunning=true;
    private float _daysPerSecond=2;
    private double _solarUiTime;
    public bool Solar3D => _solar3D;
    public N.Visibility Solar3DVisibility => Solar3D ? N.Visibility.Visible : N.Visibility.Collapsed;
    public string Solar3DButton => Solar3D ? "KEPLER" : "3D SOL";
    public string WorldTitle => Solar3D ? "SOL / HELIOCENTRIC" : "KEPLER / 07";
    public string WorldSubtitle => Solar3D ? "3D · J2000 ecliptic AU · "+SolarEphemerisSource : "Terrestrial world · equatorial ring system";
    public string SolarDateText => DateTime.UnixEpoch.AddDays(_solarJd-2440587.5).ToString("yyyy-MM-dd HH:mm")+" TDB (approx.)";
    public bool SolarClockRunning { get=>_solarClockRunning; set=>Set(ref _solarClockRunning,value); }
    public float DaysPerSecond { get=>_daysPerSecond; set { if(Set(ref _daysPerSecond,Math.Clamp(value,0,30))) Notify(nameof(SolarRateText)); } }
    public string SolarRateText => $"{DaysPerSecond:F1} days/s";
    public IReadOnlyList<PlanetState> SolarStates { get; private set; }=[];
    public ObservableCollection<SolarWorldLabel> SolarWorldLabels { get; }=[];
    public ICommand Solar3DCommand { get; }
    public ICommand SolarPreviousCommand { get; }
    public ICommand SolarNextCommand { get; }
    public ICommand SolarTodayCommand { get; }
    public ICommand SolarInnerCommand { get; }
    private int _solarFocus=-1;
    public void ClearSolarFocus()=>_solarFocus=-1;
    public static System.Numerics.Vector3 SolarPosition(PlanetState state)=>new((float)state.X,(float)state.Z,(float)state.Y);
    public float SolarRadius(int index) {
        double physical=(index<0?695700:ApproximateEphemeris.RadiiKm[index])/149597870.7;
        if(BodyScale<=1)return (float)physical;
        // A separate visibility cap prevents the exaggerated Sun swallowing Mercury.
        return (float)Math.Min(index<0?.15:0.10,physical*BodyScale);
    }
    private void SetSolarDate(double jd) {
        _solarJd=Math.Clamp(jd,2378496.5,2469807.5);
        RefreshSolarStates();
        Notify(nameof(SolarDateText));
    }
    private void RefreshSolarStates() {
        bool previous=_usingHorizons;
        if(_series!=null&&_series.TryAt(_solarJd,out var actual)) { SolarStates=actual; _usingHorizons=true; }
        else if(_planetStates.Count==8&&_planetStates.All(p=>p.JulianDateTdb==_solarJd)&&SolarStatus.StartsWith("NASA/JPL Horizons",StringComparison.Ordinal)) {
            SolarStates=_planetStates; _usingHorizons=true;
        } else { SolarStates=ApproximateEphemeris.At(_solarJd); _usingHorizons=false; }
        if(previous!=_usingHorizons) { Notify(nameof(SolarEphemerisSource));Notify(nameof(WorldSubtitle)); }
    }
    private void ToggleSolar3D() {
        _solar3D=!_solar3D;
        if(_solarMap) { _solarMap=false; Notify(nameof(SolarMapVisibility)); Notify(nameof(SolarMapButton)); }
        FollowSelection=false; AutoCamera=false; _solarFocus=-1;
        Camera.SolarMode=_solar3D; Camera.Reset();
        if(_solar3D && SolarStates.Count==0) {
            LoadSolarSnapshot();
            try { _series=HorizonsSeries.Load(Path.Combine(AppContext.BaseDirectory,"Data","horizons-series.csv")); }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or OverflowException) { _series=null; }
            SetSolarDate(_series?.Start??_epoch);
        }
        if(!_solar3D)SolarWorldLabels.Clear();
        Notify(nameof(Solar3D)); Notify(nameof(Solar3DVisibility)); Notify(nameof(Solar3DButton));
        Notify(nameof(WorldTitle)); Notify(nameof(WorldSubtitle));
    }
    private void LayoutSolarLabels(float width,float height,float scale) {
        SolarWorldLabels.Clear();
        if(!ShowLabels)return;
        var viewport=Camera.GetViewport(width,height);
        var occupied=new List<ScreenViewport>();
        var bodies=new[]{("Sun",System.Numerics.Vector3.Zero)}.Concat(SolarStates.Select(p=>(p.Name,SolarPosition(p))));
        foreach(var (name,pos) in bodies) {
            if(!Camera.Project(pos,width,height,out var screen))continue;
            float x=screen.X+9*scale,y=screen.Y-8*scale,w=(name.Length*7+12)*scale,h=20*scale;
            if(x+w>viewport.X+viewport.Width||y<viewport.Y+76*scale||y+h>viewport.Y+viewport.Height-100*scale)continue;
            var box=new ScreenViewport(x,y,w,h);
            if(occupied.Any(b=>box.X<b.X+b.Width&&box.X+box.Width>b.X&&box.Y<b.Y+b.Height&&box.Y+box.Height>b.Y))continue;
            occupied.Add(box); SolarWorldLabels.Add(new(name,x/scale,y/scale));
        }
    }
    private float _solarZoom = 0, _bodyScale = 600;
    private double _epoch = 2461370.5;
    private string _solarStatus = "Select SOL to load ephemerides.";
    public string SolarStatus { get => _solarStatus; private set => Set( ref _solarStatus, value ); }
    public N.Visibility SolarMapVisibility => _solarMap ? N.Visibility.Visible : N.Visibility.Collapsed;
    public N.BitmapImage? SolarBackdrop => SolarArtwork.Load("milkyway.jpg");
    public string SolarMapButton => _solarMap ? "LOCAL" : "SOL";
    public float SolarZoom { get => _solarZoom; set { if (Set(ref _solarZoom,Math.Clamp(value,0,4))) { Notify(nameof(SolarScaleText));UpdatePlanetMarkers(); } } }
    public float BodyScale { get => _bodyScale; set { if (Set(ref _bodyScale,Math.Clamp(value,1,2000))) { Notify(nameof(BodyScaleText));UpdatePlanetMarkers(); } } }
    public double SolarExtent => 32.0 / Math.Pow(2,SolarZoom);
    public string SolarScaleText => $"±{SolarExtent:F2} AU vertically";
    public string BodyScaleText => BodyScale <= 1 ? "True relative radii" : $"Radius ×{BodyScale:F0} (visibility limits)";
    public string SolarArtStatus => SolarArtwork.Status;
    public ICommand SolarMapCommand { get; }
    public ICommand SolarScaleCommand { get; }
    public ICommand SolarResetCommand { get; }
    private void LoadSolarSnapshot() {
        string path=Path.Combine(AppContext.BaseDirectory,"Data","horizons.csv");
        try {
            _planetStates=SolarSystemSnapshot.Load(path); _epoch=_planetStates[0].JulianDateTdb;
            SolarStatus=FormattableString.Invariant($"NASA/JPL Horizons · JD {_epoch:F5} TDB · geometric heliocentric J2000 ecliptic");
        } catch (Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or OverflowException) {
            _epoch=DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalDays+2440587.5+69.184/86400;
            _epoch=Math.Clamp(_epoch,2378496.5,2469807.5);
            _planetStates=ApproximateEphemeris.At(_epoch);
            SolarStatus=FormattableString.Invariant($"NASA/JPL fitted elements · approximate JD {_epoch:F5} TDB · Earth–Moon barycenter; Horizons cache unavailable");
        }
        UpdatePlanetMarkers();
    }
    private void UpdatePlanetMarkers() {
        PlanetMarkers.Clear(); SolarOrbits.Clear();
        if(_planetStates.Count==0)return;
        const double cx=600,cy=350; double unit=350/SolarExtent;
        string[] colors=["#8D8780","#D7BB85","#3982B5","#BC633C","#C4A27F","#D8BD85","#8CCAD0","#4269BB"];
        float diameter(double km) {
            double d=2*km/149597870.7*unit*BodyScale;
            return (float)(BodyScale<=1 ? d : Math.Clamp(d,5,km>100000?40:100));
        }
        float sun=diameter(695700); PlanetMarkers.Add(new("Sun",(float)(cx-sun/2),(float)(cy-sun/2),"0 AU",sun,"#F6BD4D",(float)(cx+sun/2+4),(float)cy));
        for(int i=0;i<8;i++) {
            var state=_planetStates[i];
            double x=cx+state.X*unit,y=cy-state.Y*unit;
            if(x>=0&&x<=1200&&y>=0&&y<=700) {
                float d=diameter(ApproximateEphemeris.RadiiKm[i]);
                double distance=Math.Sqrt(state.X*state.X+state.Y*state.Y+state.Z*state.Z);
                PlanetMarkers.Add(new(state.Name,(float)x-d/2,(float)y-d/2,$"{distance:F2} AU",d,colors[i],(float)Math.Clamp(x+d/2+5,0,1110),(float)Math.Clamp(y-d/2,0,665)));
            }
            // Reference ellipses from JPL fitted elements, not integrated Horizons tracks.
            var prev=ApproximateEphemeris.Position(i,_epoch,0);
            for(int k=1;k<=128;k++) {
                var next=ApproximateEphemeris.Position(i,_epoch,k*2*Math.PI/128);
                SolarOrbits.Add(new((float)(cx+prev.X*unit),(float)(cy-prev.Y*unit),(float)(cx+next.X*unit),(float)(cy-next.Y*unit)));prev=next;
            }
        }
    }

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
        Solar3DCommand=new ActionCommand(ToggleSolar3D);
        SolarPreviousCommand=new ActionCommand(()=>SetSolarDate(_solarJd-30));
        SolarNextCommand=new ActionCommand(()=>SetSolarDate(_solarJd+30));
        SolarTodayCommand=new ActionCommand(()=>SetSolarDate(DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalDays+2440587.5+69.184/86400));
        SolarInnerCommand=new ActionCommand(()=>{ClearSolarFocus();Camera.Target=System.Numerics.Vector3.Zero;Camera.Distance=Camera.Distance>12?5:78;});
        SolarMapCommand = new ActionCommand( () => {
            _solarMap = !_solarMap;
            if ( _solarMap ) LoadSolarSnapshot( );
            Notify( nameof(SolarMapVisibility) ); Notify( nameof(SolarMapButton) );
        } );
        SolarScaleCommand = new ActionCommand( () => {
            SolarZoom = SolarZoom > 0.1f ? 0 : 4;
        } );
        SolarResetCommand = new ActionCommand( () => {
            SolarZoom=0; BodyScale=600;
        } );
        foreach ( var ship in Simulation.Vessels ) Ships.Add( new( ship, Select ) );
        _selected = Ships[0];
        PauseCommand = new ActionCommand( () => Paused = !Paused );
        SurveyCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Survey ) );
        PatrolCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Patrol ) );
        RecallCommand = new ActionCommand( () => Simulation.Order( SelectedShip.Vessel, Mission.Return ) );
        FocusCommand = new ActionCommand( () => {
            if(Solar3D)return;
            var position = SelectedShip.Vessel.Position;
            Camera.Target = position; Camera.Distance = 2.8f;
            Camera.Yaw = MathF.Atan2( position.X, position.Z ); Camera.Pitch = 0.24f;
            AutoCamera = false; FollowSelection = true;
        } );
        ResetCameraCommand = new ActionCommand( () => { Camera.Reset( ); ClearSolarFocus(); FollowSelection = false; AutoCamera = false; } );
        IncreaseUiCommand = new ActionCommand( () => InterfaceScale += 0.1f );
        DecreaseUiCommand = new ActionCommand( () => InterfaceScale -= 0.1f );
        LaunchCommand = new ActionCommand( () => {
            if (Simulation.Vessels.Count>=8 || Simulation.Credits<160) return;
            if ( !Simulation.Launch( ) ) return;
            var ship = new ShipViewModel( Simulation.Vessels[^1], Select ); Ships.Add( ship ); SelectedShip = ship;
            Notify( nameof(FleetCount) ); LaunchCommand!.NotifyCanExecuteChanged( );
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
        double before=Simulation.Time;
        Simulation.Tick( dt );
        if(Solar3D) {
            if(SolarClockRunning && Simulation.Time>before) {
                _solarJd=Math.Min(2469807.5,_solarJd+(Simulation.Time-before)*DaysPerSecond);
                RefreshSolarStates();
            }
            if(_solarFocus>=0 && _solarFocus<SolarStates.Count)Camera.Target=SolarPosition(SolarStates[_solarFocus]);
            _solarUiTime+=dt;
            if(_solarUiTime>=.1||dt==0) { _solarUiTime=0; Notify(nameof(SolarDateText)); }
            LayoutSolarLabels(width,height,scale);
        }
        if ( FollowSelection && !Solar3D ) {
            Camera.Target = SelectedShip.Vessel.Position;
            float turn = MathF.Atan2( Camera.Target.X, Camera.Target.Z ) - MathF.Atan2( previousTarget.X, previousTarget.Z );
            Camera.Yaw += MathF.IEEERemainder( turn, MathF.Tau );
        }
        if ( AutoCamera ) Camera.Yaw += (float)dt * 0.055f;
        _notifyTime += dt;
        if(Solar3D) { foreach(var ship in Ships)ship.PlaceLabel(default,default,scale,false); } else LayoutLabels( width, height, scale );
        if ( _notifyTime < 0.1 ) return;
        _notifyTime = 0;
        foreach ( var ship in Ships ) ship.Refresh( );
        Notify( nameof(Credits) ); Notify( nameof(Research) ); Notify( nameof(Clock) );
        LaunchCommand.NotifyCanExecuteChanged( );
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
        if(Solar3D) {
            float bestSolar=32*32; int selected=-1;
            for(int i=0;i<SolarStates.Count;i++) {
                if(!Camera.Project(SolarPosition(SolarStates[i]),width,height,out var at))continue;
                float d=System.Numerics.Vector2.DistanceSquared(at,new(x,y));
                if(d<bestSolar){bestSolar=d;selected=i;}
            }
            if(selected<0)return false;
            _solarFocus=selected; Camera.Target=SolarPosition(SolarStates[selected]);
            Camera.Distance=Math.Max(.12f,SolarRadius(selected)*7);
            return true;
        }
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
