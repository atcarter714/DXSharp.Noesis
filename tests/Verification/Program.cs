using System.Numerics;
using OrbitalCommand;
using DXSharp.Noesis;
using Fighter = Starfighter;
using N = Noesis;


static class Program {
    static int _checks, _errors ;

    static int Main( ) {
        try {
            N.GUI.DisableInspector( ); N.GUI.DisableSocketInit( );
            N.Log.SetLogCallback( ( level, channel, message ) => {
                if ( level >= N.LogLevel.Warning ) Console.WriteLine( $"Noesis {level} [{channel}] {message}" );
                if ( level == N.LogLevel.Error ) _errors++;
            } );
            N.GUI.Init( );
            try { Verify( ); VerifyStarfighter( ); } finally { N.GUI.Shutdown( ); }
            Check( _errors == 0, "Noesis reported a XAML, font, or binding error." );
            Console.WriteLine( $"PASS: {_checks} checks — XAML, bindings, input, DPI, simulation, picking and mesh data." );
            return 0;
        } catch ( Exception error ) { Console.Error.WriteLine( error ); return 1; }
    }
    static void Verify( ) {
        NoesisApp.Application.SetThemeProviders( new NoesisApp.LocalXamlProvider( Path.Combine( AppContext.BaseDirectory, "Ui" ) ) );
        N.GUI.SetFontProvider( new ThemeFontProvider( ) );
        N.GUI.LoadApplicationResources( "Theme/NoesisTheme.DarkBlue.xaml" );
        var content = (N.FrameworkElement)N.GUI.LoadXaml( "Dashboard.xaml" );
        var deck = new CommandDeck( ); content.DataContext = deck;
        var view = N.GUI.CreateView( content );
        var input = new OverlayInput( view );
        float scale = 1; int width = 1600, height = 960;
        double clock = 0;
        T Find<T>( string name ) where T : class => content.FindName( name ) as T ?? throw new InvalidOperationException( name );
        var router = new SceneInput( deck, view, input ) { Width = width, Height = height };
        void Update( ) {
            view.Update( clock += 0.02 );
            var surface = Find<N.FrameworkElement>( "WorldSurface" );
            var a = surface.PointToScreen( new( 0, 0 ) );
            var b = surface.PointToScreen( new( surface.ActualWidth, surface.ActualHeight ) );
            deck.Camera.Viewport = new( a.X, a.Y, b.X - a.X, b.Y - a.Y );
            router.Width = width; router.Height = height;
            deck.Tick( 0, width, height, scale ); view.Update( clock );
        }
        void Click( string name ) {
            var button = Find<N.Button>( name );
            var center = button.PointToScreen( new( button.ActualWidth / 2, button.ActualHeight / 2 ) );
            int x = (int)center.X, y = (int)center.Y;
            Check( input.BlocksSceneAt( x, y ), $"{name}: UI hit test failed at scale {scale}." );
            router.Move( x, y ); router.Down( x, y, N.MouseButton.Left );
            router.Up( x, y, N.MouseButton.Left ); Update( );
        }
        try {
            view.SetSize( width, height ); view.Activate( ); Update( );
            Check( content.ActualWidth == width && content.ActualHeight == height, "Root layout size mismatch." );
            Check( deck.Ships.Count == 6 && deck.SelectedShip == deck.Ships[0], "Initial fleet selection mismatch." );
            Check( input.BlocksSceneAt( 700, 55 ), "A panel background leaked input." );
            Check( !input.BlocksSceneAt( width / 2, height / 2 ), "Empty scene space captured UI input." );
            Click( "SurveyButton" );
            Check( deck.Ships[0].Vessel.Mission == Mission.Survey, "The survey command did not reach the simulation." );
            Click( "LaunchButton" );
            Check( deck.Ships.Count == 7 && deck.SelectedShip == deck.Ships[^1], "Launch and automatic selection failed." );
            var list = Find<N.ListBox>( "FleetList" ); list.SelectedItem = deck.Ships[2]; Update( );
            Check( deck.SelectedShip == deck.Ships[2], "UI fleet selection did not update the view model." );
            deck.SelectedShip = deck.Ships[1]; Update( );
            Check( ReferenceEquals( list.SelectedItem, deck.Ships[1] ), "World selection did not update the fleet list." );
            var slider = Find<N.Slider>( "ExposureSlider" ); slider.Value = 1.8f; Update( );
            Check( Math.Abs( deck.Exposure - 1.8 ) < 0.001, "Slider-to-renderer binding failed." );
            deck.Exposure = 0.7f; Update( );
            Check( Math.Abs( slider.Value - 0.7 ) < 0.001, "Renderer-to-slider binding failed." );
            var text = Find<N.TextBox>( "CallSignInput" ); content.Keyboard.Focus( text );
            Check( input.IsEditingText, "Keyboard focus was not recognized as text editing." );
            text.Text = "ARKAEN / FLIGHT TEST"; Update( );
            Check( deck.CallSign == text.Text, "Text editing did not update its source." );
            view.Char( 'X' ); Update( );
            Check( deck.CallSign.Contains( 'X' ), "Character input did not reach the focused text box." );
            content.Keyboard.ClearFocus( );
            Click( "BriefingButton" );
            Check( deck.BriefingVisibility == N.Visibility.Visible && input.BlocksSceneAt( 800, 480 ), "Modal input did not block the scene." );
            Click( "DismissButton" );
            Check( deck.BriefingVisibility == N.Visibility.Collapsed, "Briefing dismissal failed." );
            foreach ( var size in new[] { (1280, 840, 1f), (1600, 1050, 1.25f), (1920, 1260, 1.5f), (2560, 1680, 2f), (1600, 960, 1f) } ) {
                (width, height, scale) = size; input.Scale = scale;
                view.SetScale( scale ); view.SetSize( width, height ); Update( );
                Check( Math.Abs( content.ActualWidth - width / scale ) < 1, "DPI layout did not use logical units." );
                var header = Find<N.FrameworkElement>( "HeaderPanel" );
                float headerBottom = header.PointToScreen( new( 0, header.ActualHeight ) ).Y;
                foreach ( string name in new[] { "HeaderCredits", "HeaderResearch", "HeaderFleetCount" } ) {
                    var value = Find<N.TextBlock>( name );
                    Check( value.ActualHeight >= 20 && value.PointToScreen( new( 0, value.ActualHeight ) ).Y <= headerBottom,
                        $"{name} clipped at scale {scale}." );
                }
                Click( "PauseButton" ); Check( deck.Paused, "Pause binding failed after resizing or changing DPI." );
                double before = deck.Simulation.Time; deck.Tick( 0.1, width, height, scale );
                Check( deck.Simulation.Time == before, "Paused simulation advanced." );
                Click( "PauseButton" ); Check( !deck.Paused, "Resume binding failed." );
            }
            // Replay the actual camera/router state transitions, including stale UI keyboard focus.
            var viewport = deck.Camera.GetViewport( width, height );
            int cx = (int)(viewport.X + viewport.Width / 2), cy = (int)(viewport.Y + viewport.Height / 2);
            content.Keyboard.Focus( Find<N.TextBox>( "CallSignInput" ) );
            float yaw = deck.Camera.Yaw;
            router.Down( cx, cy, N.MouseButton.Left );
            Check( router.Owner == PointerOwner.Scene, "World pointer-down was stolen by previously focused UI." );
            router.Move( cx + 40, cy + 15 );
            router.Move( 80, 140 );
            Check( router.IsDragging && deck.Camera.Yaw != yaw, "Dragging across a panel interrupted camera ownership." );
            router.Up( 80, 140, N.MouseButton.Left );
            Check( router.Owner == PointerOwner.None, "The world gesture did not release." );
            var target = deck.Camera.Target;
            router.Down( cx, cy, N.MouseButton.Middle ); router.Move( cx + 30, cy + 10 ); router.Up( cx + 30, cy + 10, N.MouseButton.Middle );
            Check( deck.Camera.Target != target, "Middle-drag panning failed." );
            float distance = deck.Camera.Distance;
            router.Wheel( cx, cy, 120 );
            Check( deck.Camera.Distance < distance, "World wheel input was swallowed by Noesis." );
            yaw = deck.Camera.Yaw;
            var fleetPanel=Find<N.FrameworkElement>("FleetPanel");
            var panelPoint=fleetPanel.PointToScreen(new N.Point(fleetPanel.ActualWidth/2,Math.Min(20,fleetPanel.ActualHeight/2)));
            int px=(int)panelPoint.X, py=(int)panelPoint.Y;
            Check(input.BlocksSceneAt(px,py),"Fleet panel did not block scene input.");
            router.Down(px,py,N.MouseButton.Left);
            Check(router.Owner==PointerOwner.Interface,"Panel gesture acquired scene ownership.");
            router.Move(cx,cy); router.Up(cx,cy,N.MouseButton.Left);
            Check( deck.Camera.Yaw == yaw, "Dragging from a panel into the world moved the camera." );
            router.Down( cx, cy, N.MouseButton.Right ); router.Cancel( );
            yaw = deck.Camera.Yaw; router.Move( cx + 80, cy );
            Check( router.Owner == PointerOwner.None && deck.Camera.Yaw == yaw, "Capture cancellation left a stuck camera drag." );
            deck.ResetCameraCommand.Execute( null ); Update( );
            foreach ( var ship in deck.Ships.Where( item => item.MarkerVisibility == N.Visibility.Visible ) ) {
                var bounds = ship.LabelBounds;
                Check( viewport.Contains( bounds.X, bounds.Y ) && viewport.Contains( bounds.X + bounds.Width, bounds.Y + bounds.Height ), "A label escaped the usable world viewport." );
                Check( deck.SelectAt( bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, width, height ) && deck.SelectedShip == ship, "A shifted label could not be picked." );
            }
            var reset = Find<N.FrameworkElement>( "ResetViewButton" );
            var graphics = Find<N.FrameworkElement>( "GraphicsPanel" );
            var resetBottom = reset.PointToScreen( new( 0, reset.ActualHeight ) );
            var graphicsBottom = graphics.PointToScreen( new( 0, graphics.ActualHeight ) );
            Check( resetBottom.Y < graphicsBottom.Y, "The render-controls reset button was clipped." );
            Check( DisplayMetrics.Scale( 2160, 96, 1 ) > 1.2f, "Large-display UI did not gain a readable scale." );
            Check( DisplayMetrics.Scale( 1280, 840, 192, 1.5f ) <= 1280f/1080f, "UI width cap failed." );
            var sol=ApproximateEphemeris.At(2451545.0);
            Check(sol.Count==8 && Math.Abs(Math.Sqrt(sol[2].X*sol[2].X+sol[2].Y*sol[2].Y+sol[2].Z*sol[2].Z)-.9833)<.003,"J2000 Earth distance mismatch.");
            Check(sol.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)),"Nonfinite ephemeris.");
            deck.Solar3DCommand.Execute(null); Check(deck.Solar3D&&deck.Camera.SolarMode&&deck.SolarStates.Count==8,"3D solar mode failed.");
            var earthBefore=deck.SolarStates[2];
            deck.SolarNextCommand.Execute(null);
            Check(deck.SolarStates[2].JulianDateTdb>earthBefore.JulianDateTdb&&deck.SolarStates[2].X!=earthBefore.X,"Solar epoch did not advance.");
            deck.Solar3DCommand.Execute(null); Check(!deck.Camera.SolarMode&&!deck.Solar3D,"Local view did not restore.");
            deck.IncreaseUiCommand.Execute( null ); Check( deck.InterfaceScale > 1, "Interface scaling command failed." );
            deck.DecreaseUiCommand.Execute( null );
            for ( int i = 0; i < 1400; i++ ) deck.Tick( 1.0 / 60, width, height, scale );
            Update( );
            Check( deck.Simulation.CompletedSurveys == 1 && deck.Simulation.Science == 114, "Survey completion/reward accounting failed." );
            Check( deck.Events.Any( item => item.Message.Contains( "survey complete" ) ), "The event feed missed survey completion." );
            deck.LaunchCommand.Execute( null ); Update( );
            Check( deck.Ships.Count == 8 && !deck.LaunchCommand.CanExecute( null ), "Fleet capacity was not enforced." );
            double credits = deck.Simulation.Credits; deck.LaunchCommand.Execute( null );
            Check( deck.Ships.Count == 8 && deck.Simulation.Credits == credits, "Rejected launch spent credits." );
            var vessel = deck.SelectedShip.Vessel;
            deck.PatrolCommand.Execute( null ); float phase = vessel.Phase;
            deck.Tick( 0.1, width, height, scale );
            Check( vessel.Mission == Mission.Patrol && vessel.Phase != phase, "Patrol did not advance the selected vessel." );
            deck.RecallCommand.Execute( null ); float radius = vessel.Radius;
            for(int i=0;i<120;i++) deck.Tick(1.0/60,width,height,scale);
            Check( vessel.Radius < radius, "Recall did not begin the bounded transfer." );
            float heading=vessel.Heading; deck.Tick(1.0/60,width,height,scale);
            Check(Math.Abs(MathF.IEEERemainder(vessel.Heading-heading,MathF.Tau))<=2.8f/60+.0001f,"Ship attitude snapped during transfer.");
            deck.Camera.Reset( );
            Vector3 front = Vector3.Normalize( deck.Camera.Position ) * 2;
            Check( deck.Camera.Project( front, width, height, out _ ), "A visible world point failed camera projection." );
            Check( !deck.Camera.Project( -front, width, height, out _ ), "A vessel behind the planet was not occluded." );
            deck.SelectedShip = deck.Ships[0]; deck.FocusCommand.Execute( null );
            deck.Camera.Zoom( 100000 );
            Check( deck.Camera.Position.Length( ) >= 1.34f, "Zoom moved the camera inside the planet." );
            deck.Camera.Reset( );
            foreach ( var ship in deck.Ships ) {
                if ( !deck.Camera.Project( ship.Vessel.Position, width, height, out var pixel ) ) continue;
                Check( deck.SelectAt( pixel.X, pixel.Y, width, height ), "Screen-space vessel picking failed." );
                Check( deck.SelectedShip == ship, "Picking selected the wrong vessel." );
                break;
            }
            foreach ( var mesh in new[] { SceneGeometry.Ship( ), SceneGeometry.Station( ) } ) {
                Check( mesh.Indices.Length % 3 == 0 && mesh.Indices.Length > 30, "Malformed mesh topology." );
                foreach ( ushort index in mesh.Indices ) Check( index < mesh.Vertices.Length, "Out-of-range mesh index." );
                foreach ( var vertex in mesh.Vertices ) {
                    Check( float.IsFinite( vertex.Position.LengthSquared( ) ), "Non-finite vertex." );
                    Check( Math.Abs( vertex.Normal.Length( ) - 1 ) < 0.001, "Mesh normal was not normalized." );
                }
            }
            Check( _errors == 0, "Noesis emitted an error during the interaction checks." );
        } finally { view.Dispose( ); content.Dispose( ); }
    }
    static void VerifyStarfighter( ) {
        var content = (N.FrameworkElement)N.GUI.LoadXaml( "Cockpit.xaml" );
        var game = new Fighter.StarfighterGame { Adapter = "Verification Adapter" };
        content.DataContext = game;
        var view = N.GUI.CreateView( content );
        T Find<T>( string name ) where T : class => content.FindName( name ) as T ?? throw new InvalidOperationException( $"Starfighter XAML control missing: {name}" );
        try {
            view.SetSize( 1280, 800 ); view.Activate( ); view.Update( 0.016 );
            Check( content.ActualWidth == 1280 && content.ActualHeight == 800, "Starfighter cockpit layout size mismatch." );
            Check( Find<N.Button>( "FireButton" ).Command is not null, "Fire command was not bound in the cockpit." );
            Check( Find<N.Button>( "TargetCycleButton" ).Command is not null, "Target cycle command was not bound." );
            Check( Find<N.Button>( "CenterLookButton" ).Command is not null, "Center-look cockpit control was not bound." );
            Check( game.Asteroids.Count >= 130, "The persistent orbital belt was not seeded." );

            var panel = new WorldPanelGeometry( new( -1, 1, 4 ), new( 2, 0, 0 ), new( 0, -2, 0 ), 512, 320 );
            Check( panel.TryPick( Vector3.Zero, Vector3.UnitZ, out var pixel, out float distance ) &&
                   Vector2.Distance( pixel, new( 256, 160 ) ) < 0.01f && Math.Abs( distance - 4 ) < 0.01f,
                   "World-panel center ray did not map to the texture center." );
            Check( !panel.TryPick( Vector3.Zero, Vector3.UnitX, out _, out _ ), "Parallel panel ray incorrectly hit." );
            Check( !panel.TryPick( Vector3.Zero, new( 2, 0, 1 ), out _, out _ ), "Ray outside the panel incorrectly hit." );
            var flightProbe = new Fighter.StarfighterGame( );
            flightProbe.Steer( 0, -80 );
            Check( flightProbe.Forward.Y > 0, "Moving the steering pointer up did not pitch the airframe up." );
            flightProbe.CenterLook( );
            var beforeLook = flightProbe.Forward;
            flightProbe.Look( 0, -30 );
            Check( flightProbe.CameraForward.Y > beforeLook.Y && flightProbe.Forward == beforeLook,
                "Independent head-look pitch changed the airframe or used inverted Y." );
            flightProbe.CenterLook( );
            flightProbe.AimAt( 800, 400, 1280, 800 );
            flightProbe.FireCommand.Execute( null );
            Check( Vector3.Dot( Vector3.Normalize( flightProbe.Bolts[0].Velocity - flightProbe.Velocity ), flightProbe.Right ) > 0.05f,
                "Pointer aim did not steer the cannon trajectory." );
            int soundEvents = 0; game.SoundRequested += _ => soundEvents++;
            Vector3 launchPosition = game.Position, launchForward = game.Forward, cameraForward = game.CameraForward;
            game.Look( 120, -45 );
            Check( game.Forward == launchForward && game.CameraForward != cameraForward,
                "Pilot head-look changed the ship attitude or failed to move the independent camera." );
            Find<N.Button>( "CenterLookButton" ).Command.Execute( null );
            Check( Vector3.Distance( game.CameraForward, game.Forward ) < 0.001f,
                "The CENTER PILOT VIEW cockpit control did not recenter head-look." );
            Find<N.Button>( "FireButton" ).Command.Execute( null ); view.Update( 0.02 );
            Check( game.Bolts.Count == 2, "The cockpit fire command did not launch twin pulse bolts." );
            Check( game.Bolts.All( bolt => Vector3.Dot( bolt.Position - launchPosition, launchForward ) > 5.0f ),
                "Pulse bolts spawned inside the cockpit instead of at the forward cannon muzzles." );
            Check( Math.Abs( Vector3.Dot( game.Bolts[0].Position - launchPosition, game.Right ) ) > 0.9f &&
                   Math.Abs( Vector3.Dot( game.Bolts[1].Position - launchPosition, game.Right ) ) > 0.9f,
                "The twin pulse bolts did not originate from the separated cannon rails." );
            Find<N.Button>( "BoostButton" ).Command.Execute( null );
            Vector3 initialPosition = game.Position, initialForward = game.Forward, initialUp = game.Up;
            for ( int i = 0; i < 40; i++ ) {
                game.Tick( 0.025, false, true, true, false, false, true, true, false, true, false, false, false, true, i < 3 );
                view.Update( (i + 1) * 0.025 );
            }
            Check( game.Time > 0.9 && game.Position != initialPosition, "Six-degree-of-freedom flight did not translate the fighter." );
            Check( game.Forward != initialForward && game.Up != initialUp, "Yaw, pitch and roll did not change the ship attitude." );
            Check( Math.Abs( game.HorizonRoll ) > 0.1f && Math.Abs( game.PitchLadderOffset ) > 0.1f, "Noesis attitude symbology did not track the live flight attitude." );
            Check( game.Speed > 8, "Inertial thrust did not increase ship velocity." );
            Check( game.Asteroids.Count >= 100, "The orbital belt disappeared during simulation." );
            Vector3 cockpitCamera = game.CameraPosition;
            game.ViewCommand.Execute( null );
            Check( game.ChaseView && Vector3.Distance( cockpitCamera, game.CameraPosition ) > 10, "External chase camera did not move behind the fighter." );
            game.ViewCommand.Execute( null );
            Check( !game.ChaseView && Vector3.Distance( game.CameraPosition, game.Position ) < 0.02f, "Cockpit camera did not restore the pilot eye point (excluding sub-centimeter engine vibration)." );
            Check( game.Bolts.Count <= 2, "Expired pulse bolts were not retired." );
            Check( soundEvents > 0, "Combat/boost simulation did not request audio cues." );
            Check( game.RadarContacts.Any( dot => dot.Left >= 0 && dot.Top >= 0 ), "Radar contacts did not project onto the Noesis tactical display." );
            // Put one deterministic contact in the forward cone so target-cycling is not
            // dependent on the seeded belt's random angular distribution.
            var target = game.Asteroids[0];
            target.Position = game.Position + game.Forward * 30f; target.Velocity = Vector3.Zero;
            target.OrbitSpeed = 0; target.Life = float.PositiveInfinity;
            game.TargetCycleCommand.Execute( null );
            uint designated = game.SelectedTargetSeed;
            Check( designated != 0, "Tactical target cycling did not designate a forward contact." );
            game.Tick( 0.02, false, false, false, false, false, false, false, false, false, false, false, false, false, false );
            Check( game.SelectedTargetSeed == designated, "The selected tactical target was not retained between HUD updates." );

            // A large rock directly on the nose must fracture into physical shards when hit.
            // Move incidental contacts away to make the collision assertion deterministic.
            for ( int i = 1; i < game.Asteroids.Count; i++ ) {
                game.Asteroids[i].Position = new( 500 + i * 4, 350 + i * 2, 450 + i * 3 );
                game.Asteroids[i].Velocity = Vector3.Zero; game.Asteroids[i].OrbitSpeed = 0;
            }
            target.Position = game.Position + game.Forward * 7.5f; target.Velocity = Vector3.Zero;
            target.OrbitSpeed = 0; target.Radius = 1.9f; target.Hits = 1; target.Life = float.PositiveInfinity;
            game.FireCommand.Execute( null );
            for ( int i = 0; i < 8; i++ ) game.Tick( 0.025, false, false, false, false, false, false, false, false, false, false, false, false, false, true );
            Check( game.Blasts.Count > 0 && game.Asteroids.Any( shard => shard.Fragment ), "Asteroid destruction did not create visible explosion and fragment objects." );

            Find<N.Button>( "PauseButton" ).Command.Execute( null ); view.Update( 1.1 );
            Check( game.Paused && game.PauseVisibility == N.Visibility.Visible, "Cockpit pause command or pause overlay failed." );
            double before = game.Time; game.Tick( 0.05, true, false, true, false, true, false, true, false, true, false, true, false, true, true );
            Check( game.Time == before, "Paused starfighter simulation advanced." );
            Find<N.Button>( "PauseButton" ).Command.Execute( null ); view.Update( 1.2 );
            Check( !game.Paused && game.PauseVisibility == N.Visibility.Collapsed, "Cockpit resume command failed." );
            Find<N.Button>( "RestartButton" ).Command.Execute( null ); view.Update( 1.3 );
            Check( game.Time == 0 && game.Asteroids.Count >= 130 && game.Bolts.Count == 0 && game.HullText == "100%", "Redeploy did not reset the solar-system sortie." );

            var meshes = new[] { Fighter.SceneGeometry.Asteroid( ), Fighter.SceneGeometry.Pulse( ), Fighter.SceneGeometry.Sphere( ), Fighter.SceneGeometry.Ring( ),
                Fighter.SceneGeometry.Cockpit( ), Fighter.SceneGeometry.Fighter( ), Fighter.SceneGeometry.EngineFlare( ) };
            foreach ( var mesh in meshes ) {
                Check( mesh.Vertices.Length > 0 && mesh.Indices.Length > 0 && mesh.Indices.Length % 3 == 0, "Starfighter mesh topology was malformed." );
                foreach ( ushort index in mesh.Indices ) Check( index < mesh.Vertices.Length, "Starfighter mesh index was out of range." );
                foreach ( var vertex in mesh.Vertices ) Check( float.IsFinite( vertex.Position.LengthSquared( ) ) && Math.Abs( vertex.Normal.Length( ) - 1 ) < 0.002, "Starfighter mesh contained invalid geometry." );
            }
            Check( _errors == 0, "Noesis emitted an error while loading or exercising the Starfighter cockpit." );
        } finally { view.Dispose( ); content.Dispose( ); }
    }
    static void Check( bool condition, string message ) {
        _checks++;
        if ( !condition ) throw new InvalidOperationException( message );
    }

}
