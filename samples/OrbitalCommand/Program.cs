using System.Collections.Concurrent;
using System.Diagnostics;
using DXSharp.Graphics;
using DXSharp.Noesis;
using OrbitalCommand;
using N = global::Noesis;
using Forms = System.Windows.Forms;

internal static class Program {
    [STAThread]
    private static int Main( string[] args ) {
        bool smoke = args.Contains( "--smoke" );
        var noesisErrors = new ConcurrentQueue<string>( );
        try {
            Forms.Application.SetHighDpiMode( Forms.HighDpiMode.PerMonitorV2 );
            Forms.Application.SetUnhandledExceptionMode( Forms.UnhandledExceptionMode.ThrowException );
            Forms.Application.EnableVisualStyles( );

            using var device = GraphicsDevice.Create( new( ) {
                UseWarp = smoke || args.Contains( "--warp" ),
                UseAgility = false,
                EnableDebugLayer = smoke || args.Contains( "--debug" )
            } );
            Console.WriteLine( device.AdapterName );

            string? name = Environment.GetEnvironmentVariable( "NOESIS_LICENSE_NAME" );
            string? key = Environment.GetEnvironmentVariable( "NOESIS_LICENSE_KEY" );
            if ( string.IsNullOrWhiteSpace( name ) != string.IsNullOrWhiteSpace( key ) )
                throw new InvalidOperationException( "Set both Noesis license variables, or leave both unset for evaluation." );
            if ( !string.IsNullOrWhiteSpace( name ) ) N.GUI.SetLicense( name, key! );
            N.Log.SetLogCallback( ( level, channel, message ) => {
                if ( level >= N.LogLevel.Warning ) Console.Error.WriteLine( $"Noesis {level} [{channel}] {message}" );
                if ( level == N.LogLevel.Error ) noesisErrors.Enqueue( message );
            } );
            if ( args.Contains( "--allocator-smoke" ) ) {
                VerifyFrameAllocators( device );
                return 0;
            }
            N.GUI.Init( );
            try {
                string uiDirectory = Path.Combine( AppContext.BaseDirectory, "Ui" );
                NoesisApp.Application.SetThemeProviders( new NoesisApp.LocalXamlProvider( uiDirectory ) );
                N.GUI.SetFontProvider( new ThemeFontProvider( ) );
                N.GUI.LoadApplicationResources( "Theme/NoesisTheme.DarkBlue.xaml" );
                Run( device, smoke );
            } finally {
                N.GUI.Shutdown( );
            }

            var diagnostics = device.GetDiagnostics( );
            foreach ( string message in diagnostics.Messages ) Console.Error.WriteLine( message );
            if ( diagnostics.DeviceRemovedReason < 0 || diagnostics.Messages.Any( message =>
                     message.Contains( "SEVERITY_ERROR", StringComparison.Ordinal ) ||
                     message.Contains( "SEVERITY_CORRUPTION", StringComparison.Ordinal ) ) )
                throw new InvalidOperationException( "D3D12 validation failed." );
            if ( !noesisErrors.IsEmpty )
                throw new InvalidOperationException( "Noesis logged an error. See the output above." );

            if ( smoke ) Console.WriteLine( "PASS: orbital simulation, XAML bindings, indexed meshes, input routing, two resizes, GPU retirement, and shutdown." );
            return 0;
        } catch ( Exception error ) {
            Console.Error.WriteLine( error );
            return 1;
        }
    }

    private static void Run( GraphicsDevice device, bool smoke ) {
        var content = (N.FrameworkElement)N.GUI.LoadXaml( "Dashboard.xaml" );
        T Find<T>( string name ) where T : class => content.FindName( name ) as T
            ?? throw new InvalidOperationException( $"Missing XAML control {name}." );

        var deck = new CommandDeck { Adapter = device.AdapterName };
        content.DataContext = deck;
        using var window = new DemoWindow {
            Text = "Arkaen | Kepler Orbital Command — DXSharp + Noesis",
            ClientSize = smoke ? new( 1280, 840 ) : new( 1600, 960 ),
            MinimumSize = new( 1100, 780 )
        };
        _ = window.Handle;
        float initialScale = window.DeviceDpi / 96f;
        if ( smoke ) {
            window.ClientSize = new( (int)(1280 * initialScale), (int)(840 * initialScale) );
        } else {
            var area = Forms.Screen.FromControl( window ).WorkingArea;
            window.MinimumSize = new( Math.Min( (int)(1100 * initialScale), area.Width - 32 ),
                Math.Min( (int)(780 * initialScale), area.Height - 40 ) );
            window.ClientSize = new( Math.Min( (int)(1600 * initialScale), area.Width - 64 ),
                Math.Min( (int)(960 * initialScale), area.Height - 100 ) );
            window.StartPosition = Forms.FormStartPosition.CenterScreen;
        }
        using var queue = device.CreateCommandQueue( );
        using var swap = device.CreateSwapChain( queue, window.Handle,
            (uint)window.ClientSize.Width, (uint)window.ClientSize.Height );
        using var scene = new SceneRenderer( device, queue, swap.Format );
        using var heap = device.CreateRenderTargetHeap( swap.BufferCount );
        using var overlay = new D3D12Overlay( device, content, swap.Format );
        using var slot0 = new FrameSlot( device, smoke );
        using var slot1 = new FrameSlot( device, smoke );
        using var timer = new Forms.Timer { Interval = 1 };

        var buffers = new GpuResource?[(int)swap.BufferCount];
        var targets = new RenderTargetView[buffers.Length];
        var clock = Stopwatch.StartNew( );
        var size = window.ClientSize;
        float scale = DisplayMetrics.Scale( size.Height, window.DeviceDpi, deck.InterfaceScale );
        long submitted = 0, completed = 0;
        int resizes = 0;
        bool sawUiDraw = false;
        double previous = 0;
        Forms.FormBorderStyle border = window.FormBorderStyle;
        System.Drawing.Rectangle restoreBounds = window.Bounds;
        bool fullscreen = false;
        Exception? failure = null;
        Task? drain = null;
        var view = overlay.View;
        var hitTest = new OverlayInput( view ) { Scale = scale };
        var input = new SceneInput( deck, view, hitTest ) { Width = size.Width, Height = size.Height };
        var worldSurface = Find<N.FrameworkElement>( "WorldSurface" );
        void ReadViewport( ) {
            var topLeft = worldSurface.PointToScreen( new( 0, 0 ) );
            var bottomRight = worldSurface.PointToScreen( new( worldSurface.ActualWidth, worldSurface.ActualHeight ) );
            deck.Camera.Viewport = new( topLeft.X, topLeft.Y, Math.Max( 1, bottomRight.X - topLeft.X ), Math.Max( 1, bottomRight.Y - topLeft.Y ) );
            input.Width = size.Width; input.Height = size.Height;
        }
        deck.ToggleFullscreen += () => {
            if ( !fullscreen ) {
                restoreBounds = window.Bounds; border = window.FormBorderStyle;
                window.FormBorderStyle = Forms.FormBorderStyle.None;
                window.Bounds = Forms.Screen.FromControl( window ).Bounds;
            } else { window.FormBorderStyle = border; window.Bounds = restoreBounds; }
            fullscreen = !fullscreen;
        };

        void AcquireBuffers( ) {
            for ( int i = 0; i < buffers.Length; i++ ) {
                var buffer = swap.AcquireBackBuffer( (uint)i );
                buffers[i] = buffer;
                targets[i] = device.CreateRenderTargetView( buffer, heap, (uint)i );
            }
        }

        void ReleaseBuffers( ) {
            Array.Clear( targets );
            for ( int i = 0; i < buffers.Length; i++ ) {
                buffers[i]?.Dispose( );
                buffers[i] = null;
            }
        }

        void SmokeClick( string name ) {
            var button = Find<N.Button>( name );
            var center = button.PointToScreen( new N.Point( button.ActualWidth / 2, button.ActualHeight / 2 ) );
            int x = (int)center.X, y = (int)center.Y;
            view.MouseMove( x, y ); input.Down( x, y, N.MouseButton.Left ); input.Up( x, y, N.MouseButton.Left );
        }
        window.Activated += ( _, _ ) => view.Activate( );
        window.Deactivate += ( _, _ ) => { input.Cancel( ); window.Capture = false; view.Deactivate( ); };
        window.MouseMove += ( _, e ) => input.Move( e.X, e.Y );
        window.MouseDown += ( _, e ) => {
            if ( MapButton( e.Button ) is not { } button ) return;
            input.Down( e.X, e.Y, button );
            if ( input.Owner != PointerOwner.None ) window.Capture = true;
        };
        window.MouseUp += ( _, e ) => {
            if ( MapButton( e.Button ) is { } button ) input.Up( e.X, e.Y, button );
            if ( Forms.Control.MouseButtons == Forms.MouseButtons.None ) window.Capture = false;
        };
        window.MouseWheel += ( _, e ) => input.Wheel( e.X, e.Y, e.Delta );
        window.MouseCaptureChanged += ( _, _ ) => {
            if ( !window.Capture && Forms.Control.MouseButtons != Forms.MouseButtons.None ) input.Cancel( );
        };
        window.MouseDoubleClick += ( _, e ) => {
            if ( hitTest.BlocksSceneAt( e.X, e.Y ) && MapButton( e.Button ) is { } button ) view.MouseDoubleClick( e.X, e.Y, button );
        };
        window.KeyDown += ( _, e ) => {
            if ( e.KeyCode == Forms.Keys.Escape ) {
                deck.DismissCommand.Execute( null ); e.Handled = true; e.SuppressKeyPress = true; return;
            }
            if ( !hitTest.IsEditingText || e.KeyCode == Forms.Keys.F11 ) {
                System.Windows.Input.ICommand? action = e.KeyCode switch {
                    Forms.Keys.Space => deck.PauseCommand, Forms.Keys.F => deck.FocusCommand,
                    Forms.Keys.Home => deck.ResetCameraCommand, Forms.Keys.F11 => deck.FullscreenCommand, _ => null
                };
                if ( action is not null ) { action.Execute( null ); e.Handled = true; e.SuppressKeyPress = true; return; }
            }
            var key = MapKey( e.KeyCode );
            if ( key != N.Key.None ) e.Handled = view.KeyDown( key );
        };
        window.KeyUp += ( _, e ) => {
            var key = MapKey( e.KeyCode );
            if ( key != N.Key.None ) e.Handled = view.KeyUp( key );
        };
        window.KeyPress += ( _, e ) => {
            if ( !char.IsControl( e.KeyChar ) ) e.Handled = view.Char( e.KeyChar );
        };

        timer.Tick += ( _, _ ) => {
            timer.Stop( );
            try {
                if ( window.CloseRequested ) {
                    drain ??= Task.Run( queue.WaitIdle );
                    if ( !drain.IsCompleted ) return;
                    drain.GetAwaiter( ).GetResult( );
                    completed = submitted;
                    slot0.Probe?.Validate( ); slot1.Probe?.Validate( );
                    ReleaseBuffers( );
                    swap.Dispose( );
                    window.FinishClose( );
                    return;
                }
                var client = window.ClientSize;
                if ( window.WindowState == Forms.FormWindowState.Minimized || client.Width <= 0 || client.Height <= 0 )
                    return;

                float currentScale = DisplayMetrics.Scale( client.Height, window.DeviceDpi, deck.InterfaceScale );
                if ( client != size || Math.Abs( currentScale - scale ) > 0.001f ) {
                    queue.WaitIdle( );
                    completed = submitted;
                    if ( client != size ) {
                        ReleaseBuffers( );
                        swap.Resize( (uint)client.Width, (uint)client.Height );
                        AcquireBuffers( );
                        resizes++;
                    }
                    size = client;
                    scale = currentScale;
                    overlay.Resize( (uint)size.Width, (uint)size.Height, scale, completed );
                    scene.Resize( (uint)size.Width, (uint)size.Height );
                    hitTest.Scale = scale;
                    view.Update( clock.Elapsed.TotalSeconds );
                    ReadViewport( );
                }

                var slot = (submitted & 1) == 0 ? slot0 : slot1;
                if ( slot.Fence != 0 ) {
                    queue.Wait( slot.Fence );
                    completed = Math.Max( completed, slot.Frame );
                    slot.Probe?.Validate( );
                }
                slot.Begin( );

                double now = clock.Elapsed.TotalSeconds;
                double dt = smoke ? 1.0 / 60 : Math.Min( now - previous, 0.1 );
                previous = now;
                deck.Tick( dt, size.Width, size.Height, scale );
                view.Update( now );

                uint index = swap.CurrentBackBufferIndex;
                var buffer = buffers[(int)index]!;
                var target = targets[(int)index];
                var commands = slot.SceneCommands;
                long frame = submitted + 1;

                commands.Transition( buffer, ResourceState.Present, ResourceState.RenderTarget );
                scene.Record( commands, target, (uint)size.Width, (uint)size.Height, deck );
                commands.Close( );

                // No scene PSO/root/descriptor state can interrupt the Noesis pass.
                var ui = slot.UiCommands;
                overlay.Record( ui, target, completed, frame );
                if ( slot.Probe is { } probe ) {
                    var text = Find<N.FrameworkElement>( deck.BriefingVisibility == N.Visibility.Visible ? "DismissButton" : "BrandTitle" );
                    var center = text.PointToScreen( new( text.ActualWidth / 2, text.ActualHeight / 2 ) );
                    uint w = (uint)Math.Clamp( text.ActualWidth * scale, 1, 512 );
                    uint h = (uint)Math.Clamp( text.ActualHeight * scale, 1, 128 );
                    var origin = text.PointToScreen( new( 0, 0 ) );
                    float captureX = deck.BriefingVisibility == N.Visibility.Visible ? center.X - w / 2 : origin.X;
                    uint x = (uint)Math.Clamp( captureX, 0, size.Width - w );
                    uint y = (uint)Math.Clamp( center.Y - h / 2, 0, size.Height - h );
                    probe.Capture( ui, buffer, x, y, w, h, frame );
                }
                ui.Transition( buffer, ResourceState.RenderTarget, ResourceState.Present );
                ui.Close( );
                queue.Submit( commands );
                slot.Fence = queue.Submit( ui );
                slot.Frame = submitted = frame;
                swap.Present( );
                uint batches = view.GetStats( ).Batches;
                sawUiDraw |= batches > 0;
                deck.ReportFrame( dt, batches );

                if ( smoke ) {
                    if ( frame == 1 ) SmokeClick( "SurveyButton" );
                    if ( frame == 2 ) { SmokeClick( "LaunchButton" ); window.ClientSize = new( size.Width + 48, size.Height + 24 ); }
                    if ( frame == 4 ) { SmokeClick( "BriefingButton" ); }
                    if ( frame == 5 ) { SmokeClick( "DismissButton" ); window.ClientSize = new( size.Width + 48, size.Height + 24 ); }
                    if ( frame == 6 ) { input.Down( size.Width / 2, size.Height / 2, N.MouseButton.Left ); input.Up( size.Width / 2, size.Height / 2, N.MouseButton.Left ); }
                    if ( frame == 60 ) window.Close( );
                }
            } catch ( Exception error ) {
                failure = error;
                if ( window.CloseRequested ) window.FinishClose( );
                else window.Close( );
            } finally {
                if ( !window.IsDisposed && window.IsHandleCreated ) timer.Start( );
            }
        };

        try {
            AcquireBuffers( );
            overlay.Resize( (uint)size.Width, (uint)size.Height, scale, 0 );
            scene.Resize( (uint)size.Width, (uint)size.Height );
            view.Update( 0 );
            ReadViewport( );
            timer.Start( );
            Forms.Application.Run( window );
            if ( failure is not null ) throw new InvalidOperationException( "Rendering stopped.", failure );
            Console.WriteLine( $"Frames={submitted} Resizes={resizes} UI={input.UiClicks} Scene={input.SceneClicks} UiDraw={sawUiDraw} Meshes={scene.MeshDraws} Fleet={deck.Ships.Count} HudCaptures={(slot0.Probe?.Captures ?? 0) + (slot1.Probe?.Captures ?? 0)}" );
            if ( smoke && (submitted != 60 || resizes < 2 || input.UiClicks < 4 || input.SceneClicks != 1 || !sawUiDraw || scene.MeshDraws != 8 || deck.Ships.Count != 7 || deck.Ships[0].Vessel.Mission != Mission.Survey || deck.BriefingVisibility != N.Visibility.Collapsed || (slot0.Probe!.Captures + slot1.Probe!.Captures) != 60) )
                throw new InvalidOperationException( "The frame, resize, draw, or input assertions failed." );
        } finally {
            timer.Stop( );
            try {
                if ( drain is null ) queue.WaitIdle( );
                else drain.GetAwaiter( ).GetResult( );
            } finally {
                ReleaseBuffers( );
            }
        }
    }

    private static N.MouseButton? MapButton( Forms.MouseButtons button ) => button switch {
        Forms.MouseButtons.Left => N.MouseButton.Left,
        Forms.MouseButtons.Right => N.MouseButton.Right,
        Forms.MouseButtons.Middle => N.MouseButton.Middle,
        Forms.MouseButtons.XButton1 => N.MouseButton.XButton1,
        Forms.MouseButtons.XButton2 => N.MouseButton.XButton2,
        _ => null
    };

    private static N.Key MapKey( Forms.Keys key ) => key switch {
        Forms.Keys.ControlKey => N.Key.LeftCtrl,
        Forms.Keys.ShiftKey => N.Key.LeftShift,
        Forms.Keys.Menu => N.Key.LeftAlt,
        _ => Enum.TryParse<N.Key>( key.ToString( ), true, out var mapped ) ? mapped : N.Key.None
    };

    private sealed class DemoWindow : Forms.Form {
        private bool _finishClose;
        internal DemoWindow( ) {
            SetStyle( Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.Opaque, true );
        }
        protected override void OnPaintBackground( Forms.PaintEventArgs args ) { }
        protected override void OnPaint( Forms.PaintEventArgs args ) { }
        internal bool CloseRequested { get; private set; }
        protected override bool IsInputKey( Forms.Keys keyData ) => true;
        protected override void OnFormClosing( Forms.FormClosingEventArgs args ) {
            if ( !_finishClose ) { args.Cancel = true; CloseRequested = true; }
            base.OnFormClosing( args );
        }
        internal void FinishClose( ) { _finishClose = true; Close( ); }
    }

    private static void VerifyFrameAllocators( GraphicsDevice device ) {
        using var queue = device.CreateCommandQueue( );
        using var first = new FrameSlot( device, false );
        using var second = new FrameSlot( device, false );
        using var source = device.CreateBuffer( 4, BufferMemory.Upload );
        using var destination = device.CreateBuffer( 4, BufferMemory.Readback );
        source.Write<uint>( [0xA11C0CA7u] );
        try {
            for ( int frame = 0; frame < 64; frame++ ) {
                var slot = (frame & 1) == 0 ? first : second;
                if ( slot.Fence != 0 ) queue.Wait( slot.Fence );
                // Exercise the exact reset sequence used by the render host: both
                // lists enter recording before either one closes.
                slot.Begin( );
                slot.SceneCommands.Close( );
                slot.UiCommands.CopyBuffer( source, destination, 4 );
                slot.UiCommands.Close( );
                queue.Submit( slot.SceneCommands );
                slot.Fence = queue.Submit( slot.UiCommands );
            }
            queue.WaitIdle( );
            uint[] actual = new uint[1]; destination.Read<uint>( actual );
            if ( actual[0] != 0xA11C0CA7u ) throw new InvalidOperationException( "Allocator test readback failed." );
            var diagnostics = device.GetDiagnostics( );
            if ( diagnostics.DeviceRemovedReason < 0 || diagnostics.Messages.Any( value =>
                value.Contains( "SEVERITY_ERROR", StringComparison.Ordinal ) || value.Contains( "SEVERITY_CORRUPTION", StringComparison.Ordinal ) ) )
                throw new InvalidOperationException( "Allocator test reported a D3D12 validation error." );
            Console.WriteLine( "PASS: 64 two-list frames, independent allocators, fence retirement and GPU readback." );
        } finally { queue.WaitIdle( ); }
    }

    private sealed class FrameSlot : IDisposable {
        internal CommandAllocator SceneAllocator { get; }
        internal CommandAllocator UiAllocator { get; }
        internal GraphicsCommandList SceneCommands { get; }
        internal GraphicsCommandList UiCommands { get; }
        internal HudReadback? Probe { get; }
        internal ulong Fence;
        internal long Frame;
        internal FrameSlot( GraphicsDevice device, bool smoke ) {
            SceneAllocator = device.CreateCommandAllocator( );
            try {
                UiAllocator = device.CreateCommandAllocator( );
                try {
                    SceneCommands = device.CreateCommandList( SceneAllocator );
                    try {
                        SceneCommands.Close( );
                        UiCommands = device.CreateCommandList( UiAllocator );
                        try { UiCommands.Close( ); if ( smoke ) Probe = new( device ); }
                        catch { UiCommands.Dispose( ); throw; }
                    } catch { SceneCommands.Dispose( ); throw; }
                } catch { UiAllocator.Dispose( ); throw; }
            } catch { SceneAllocator.Dispose( ); throw; }
        }
        // The caller has waited Fence, which follows both ordered submissions.
        internal void Begin( ) {
            SceneAllocator.Reset( ); UiAllocator.Reset( );
            SceneCommands.Reset( SceneAllocator ); UiCommands.Reset( UiAllocator );
        }
        public void Dispose( ) {
            Probe?.Dispose( ); UiCommands.Dispose( ); SceneCommands.Dispose( );
            UiAllocator.Dispose( ); SceneAllocator.Dispose( );
        }
    }
}
