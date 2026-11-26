using System.Collections.Concurrent;
using System.Diagnostics;
using DXSharp.Graphics;
using DXSharp.Noesis;
using N = global::Noesis;
using Forms = System.Windows.Forms;

namespace Starfighter;

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
                UseWarp = smoke || args.Contains( "--warp" ), UseAgility = false,
                EnableDebugLayer = smoke || args.Contains( "--debug" )
            } );
            Console.WriteLine( $"DXSharp adapter: {device.AdapterName}" );

            string? licenseName = Environment.GetEnvironmentVariable( "NOESIS_LICENSE_NAME" );
            string? licenseKey = Environment.GetEnvironmentVariable( "NOESIS_LICENSE_KEY" );
            if ( string.IsNullOrWhiteSpace( licenseName ) != string.IsNullOrWhiteSpace( licenseKey ) )
                throw new InvalidOperationException( "Set both Noesis license variables, or leave both unset for evaluation." );
            if ( !string.IsNullOrWhiteSpace( licenseName ) ) N.GUI.SetLicense( licenseName, licenseKey! );
            N.Log.SetLogCallback( ( level, channel, message ) => {
                if ( level >= N.LogLevel.Warning ) Console.Error.WriteLine( $"Noesis {level} [{channel}] {message}" );
                if ( level == N.LogLevel.Error ) noesisErrors.Enqueue( message );
            } );

            N.GUI.Init( );
            using var audio = smoke ? null : new ProceduralAudio( );
            try {
                string uiDirectory = Path.Combine( AppContext.BaseDirectory, "Ui" );
                NoesisApp.Application.SetThemeProviders( new NoesisApp.LocalXamlProvider( uiDirectory ) );
                N.GUI.SetFontProvider( new ThemeFontProvider( ) );
                N.GUI.LoadApplicationResources( "Theme/NoesisTheme.DarkBlue.xaml" );
                Run( device, smoke, audio );
            } finally { N.GUI.Shutdown( ); }

            var diagnostics = device.GetDiagnostics( );
            foreach ( string message in diagnostics.Messages ) Console.Error.WriteLine( message );
            if ( diagnostics.DeviceRemovedReason < 0 || diagnostics.Messages.Any( message =>
                    message.Contains( "SEVERITY_ERROR", StringComparison.Ordinal ) || message.Contains( "SEVERITY_CORRUPTION", StringComparison.Ordinal ) ) )
                throw new InvalidOperationException( "D3D12 validation failed." );
            if ( !noesisErrors.IsEmpty ) throw new InvalidOperationException( "Noesis logged an error. See the output above." );
            if ( smoke ) Console.WriteLine( "PASS: Starfighter solar system, 6-DOF simulation, cockpit mesh, Noesis HUD, D3D12 frame retirement and shutdown." );
            return 0;
        } catch ( Exception error ) { Console.Error.WriteLine( error ); return 1; }
    }

    private static void Run( GraphicsDevice device, bool smoke, ProceduralAudio? audio ) {
        var content = (N.FrameworkElement)N.GUI.LoadXaml( "Cockpit.xaml" );
        var game = new StarfighterGame { Adapter = device.AdapterName };
        if ( audio is not null ) game.SoundRequested += audio.Play;
        content.DataContext = game;
        using var window = new GameWindow {
            Text = "VANGUARD // Asteroid Intercept — DXSharp + Noesis",
            ClientSize = smoke ? new( 1280, 800 ) : new( 1600, 960 ),
            MinimumSize = new( 1100, 700 ),
            StartPosition = Forms.FormStartPosition.CenterScreen
        };
        using var appIcon = System.Drawing.Icon.ExtractAssociatedIcon( System.Windows.Forms.Application.ExecutablePath );
        if ( appIcon is not null ) window.Icon = appIcon;
        _ = window.Handle;
        float initialScale = window.DeviceDpi / 96f;
        if ( smoke ) window.ClientSize = new( (int)(1280 * initialScale), (int)(800 * initialScale) );
        else {
            var area = Forms.Screen.FromControl( window ).WorkingArea;
            window.MinimumSize = new( Math.Min( (int)(1100 * initialScale), area.Width - 32 ), Math.Min( (int)(700 * initialScale), area.Height - 40 ) );
            window.ClientSize = new( Math.Min( (int)(1600 * initialScale), area.Width - 64 ), Math.Min( (int)(960 * initialScale), area.Height - 100 ) );
        }

        using var queue = device.CreateCommandQueue( );
        using var swap = device.CreateSwapChain( queue, window.Handle, (uint)window.ClientSize.Width, (uint)window.ClientSize.Height );
        using var scene = new SceneRenderer( device, queue, swap.Format );
        using var heap = device.CreateRenderTargetHeap( swap.BufferCount );
        using var overlay = new D3D12Overlay( device, content, swap.Format );
        using var slot0 = new FrameSlot( device );
        using var slot1 = new FrameSlot( device );
        using var timer = new Forms.Timer { Interval = 1 };

        var buffers = new GpuResource?[(int)swap.BufferCount];
        var targets = new RenderTargetView[buffers.Length];
        var clock = Stopwatch.StartNew( );
        var size = window.ClientSize;
        float scale = DisplayScale( size.Height, window.DeviceDpi );
        long submitted = 0, completed = 0;
        int resizes = 0;
        double previous = 0;
        Exception? failure = null;
        Task? drain = null;
        bool fullscreen = false;
        Forms.FormBorderStyle restoredBorder = window.FormBorderStyle;
        System.Drawing.Rectangle restoredBounds = window.Bounds;
        var view = overlay.View;
        var hitTest = new OverlayInput( view ) { Scale = scale };
        var keys = new HashSet<Forms.Keys>( );
        // Noesis controls take priority. RMB steers, MMB head-looks, scene LMB fires.
        bool uiPointer = false, lookPointer = false, steering = false, firePointer = false, cursorHidden = false;
        N.MouseButton? uiButton = null;
        int lastMouseX = 0, lastMouseY = 0;

        game.ToggleFullscreen += () => {
            if ( !fullscreen ) {
                restoredBounds = window.Bounds; restoredBorder = window.FormBorderStyle;
                window.FormBorderStyle = Forms.FormBorderStyle.None; window.Bounds = Forms.Screen.FromControl( window ).Bounds;
            } else { window.FormBorderStyle = restoredBorder; window.Bounds = restoredBounds; }
            fullscreen = !fullscreen;
        };

        void AcquireBuffers( ) {
            for ( int i = 0; i < buffers.Length; i++ ) {
                var buffer = swap.AcquireBackBuffer( (uint)i ); buffers[i] = buffer;
                targets[i] = device.CreateRenderTargetView( buffer, heap, (uint)i );
            }
        }
        void ReleaseBuffers( ) {
            Array.Clear( targets );
            for ( int i = 0; i < buffers.Length; i++ ) { buffers[i]?.Dispose( ); buffers[i] = null; }
        }
        void ClearInput( ) {
            keys.Clear( ); uiPointer = lookPointer = steering = firePointer = false; uiButton = null; game.ClearAim( );
            if ( cursorHidden ) { Forms.Cursor.Show( ); cursorHidden = false; }
            view.Content.Mouse.Capture( null );
        }

        window.Activated += ( _, _ ) => view.Activate( );
        window.Deactivate += ( _, _ ) => { ClearInput( ); window.Capture = false; view.Deactivate( ); };
        window.MouseDown += ( _, e ) => {
            var button = MapButton( e.Button ); if ( button is null ) return;
            lastMouseX = e.X; lastMouseY = e.Y;
            if ( uiPointer || lookPointer || firePointer ) return;
            if ( e.Button == Forms.MouseButtons.Left && hitTest.BlocksSceneAt( e.X, e.Y ) ) {
                uiPointer = true; uiButton = button;
                view.MouseMove( e.X, e.Y ); view.MouseButtonDown( e.X, e.Y, button.Value );
                window.Capture = true;
            } else if ( e.Button is Forms.MouseButtons.Right or Forms.MouseButtons.Middle ) {
                steering = e.Button == Forms.MouseButtons.Right; lookPointer = true; window.Capture = true;
                if ( !cursorHidden ) { Forms.Cursor.Hide( ); cursorHidden = true; }
                Forms.Cursor.Position = window.PointToScreen( new System.Drawing.Point( window.ClientSize.Width / 2, window.ClientSize.Height / 2 ) );
            } else if ( e.Button == Forms.MouseButtons.Left ) {
                firePointer = true; window.Capture = true;
                game.AimAt( e.X, e.Y, window.ClientSize.Width, window.ClientSize.Height );
                game.FireCommand.Execute( null );
            }
        };
        window.MouseMove += ( _, e ) => {
            if ( uiPointer ) view.MouseMove( e.X, e.Y );
            else if ( lookPointer ) {
                int centerX = window.ClientSize.Width / 2, centerY = window.ClientSize.Height / 2;
                int dx = e.X - centerX, dy = e.Y - centerY;
                if ( dx != 0 || dy != 0 ) {
                    if ( steering ) game.Steer( dx, dy ); else game.Look( dx, dy );
                    // Re-centering keeps the pilot's aim free of desktop-edge limits.
                    Forms.Cursor.Position = window.PointToScreen( new System.Drawing.Point( centerX, centerY ) );
                }
            } else if ( firePointer ) game.AimAt( e.X, e.Y, window.ClientSize.Width, window.ClientSize.Height );
            else view.MouseMove( e.X, e.Y );
            lastMouseX = e.X; lastMouseY = e.Y;
        };
        window.MouseUp += ( _, e ) => {
            if ( MapButton( e.Button ) is { } button && uiPointer && uiButton == button ) {
                view.MouseButtonUp( e.X, e.Y, button ); uiPointer = false; uiButton = null;
            }
            if ( e.Button == Forms.MouseButtons.Left && firePointer ) { firePointer = false; game.ClearAim( ); }
            if ( lookPointer && e.Button == (steering ? Forms.MouseButtons.Right : Forms.MouseButtons.Middle) ) {
                lookPointer = steering = false;
                if ( cursorHidden ) { Forms.Cursor.Show( ); cursorHidden = false; }
            }
            if ( Forms.Control.MouseButtons == Forms.MouseButtons.None ) {
                uiPointer = false; uiButton = null; window.Capture = false;
            }
        };
        window.MouseWheel += ( _, e ) => {
            if ( hitTest.BlocksSceneAt( e.X, e.Y ) ) view.MouseWheel( e.X, e.Y, e.Delta );
            else game.AdjustZoom( e.Delta );
        };
        window.MouseCaptureChanged += ( _, _ ) => {
            if ( !window.Capture && (uiPointer || lookPointer || firePointer) ) {
                if ( uiPointer && uiButton is { } button ) view.MouseButtonUp( lastMouseX, lastMouseY, button );
                uiPointer = lookPointer = steering = firePointer = false; uiButton = null; game.ClearAim( );
                if ( cursorHidden ) { Forms.Cursor.Show( ); cursorHidden = false; }
            }
        };
        window.KeyDown += ( _, e ) => {
            if ( e.KeyCode == Forms.Keys.F11 ) { game.FullscreenCommand.Execute( null ); e.Handled = true; e.SuppressKeyPress = true; return; }
            if ( e.KeyCode == Forms.Keys.C ) { game.ViewCommand.Execute( null ); e.Handled = true; e.SuppressKeyPress = true; return; }
            if ( e.KeyCode is Forms.Keys.Escape or Forms.Keys.P ) { game.TogglePause( ); e.Handled = true; e.SuppressKeyPress = true; return; }
            if ( e.KeyCode == Forms.Keys.R && (game.GameOver || game.Paused) ) { game.RestartCommand.Execute( null ); e.Handled = true; e.SuppressKeyPress = true; return; }
            keys.Add( e.KeyCode );
            var key = MapKey( e.KeyCode ); if ( key != N.Key.None ) e.Handled = view.KeyDown( key );
        };
        window.KeyUp += ( _, e ) => {
            keys.Remove( e.KeyCode );
            var key = MapKey( e.KeyCode ); if ( key != N.Key.None ) e.Handled = view.KeyUp( key );
        };
        window.KeyPress += ( _, e ) => { if ( !char.IsControl( e.KeyChar ) ) e.Handled = view.Char( e.KeyChar ); };

        timer.Tick += ( _, _ ) => {
            timer.Stop( );
            try {
                if ( window.CloseRequested ) {
                    drain ??= Task.Run( queue.WaitIdle );
                    if ( !drain.IsCompleted ) return;
                    drain.GetAwaiter( ).GetResult( ); completed = submitted;
                    ReleaseBuffers( ); swap.Dispose( ); window.FinishClose( ); return;
                }
                var client = window.ClientSize;
                if ( window.WindowState == Forms.FormWindowState.Minimized || client.Width <= 0 || client.Height <= 0 ) return;
                float currentScale = DisplayScale( client.Height, window.DeviceDpi );
                if ( client != size || Math.Abs( currentScale - scale ) > 0.001f ) {
                    queue.WaitIdle( ); completed = submitted;
                    if ( client != size ) {
                        ReleaseBuffers( ); swap.Resize( (uint)client.Width, (uint)client.Height ); AcquireBuffers( ); resizes++;
                    }
                    size = client; scale = currentScale;
                    overlay.Resize( (uint)size.Width, (uint)size.Height, scale, completed );
                    scene.Resize( (uint)size.Width, (uint)size.Height ); hitTest.Scale = scale;
                    view.Update( clock.Elapsed.TotalSeconds );
                }

                var slot = (submitted & 1) == 0 ? slot0 : slot1;
                if ( slot.Fence != 0 ) { queue.Wait( slot.Fence ); completed = Math.Max( completed, slot.Frame ); }
                slot.Begin( );
                double now = clock.Elapsed.TotalSeconds;
                double dt = smoke ? 1.0 / 60 : Math.Min( now - previous, 0.05 ); previous = now;
                if ( firePointer ) game.AimAt( lastMouseX, lastMouseY, size.Width, size.Height );
                game.Tick( dt,
                    keys.Contains( Forms.Keys.Left ), keys.Contains( Forms.Keys.Right ),
                    keys.Contains( Forms.Keys.Up ), keys.Contains( Forms.Keys.Down ),
                    keys.Contains( Forms.Keys.Q ), keys.Contains( Forms.Keys.E ),
                    keys.Contains( Forms.Keys.W ), keys.Contains( Forms.Keys.S ),
                    keys.Contains( Forms.Keys.A ), keys.Contains( Forms.Keys.D ),
                    keys.Contains( Forms.Keys.R ), keys.Contains( Forms.Keys.F ),
                    keys.Contains( Forms.Keys.ShiftKey ) || keys.Contains( Forms.Keys.LShiftKey ) || keys.Contains( Forms.Keys.RShiftKey ),
                    keys.Contains( Forms.Keys.Space ) || firePointer );
                audio?.SetFlightState( game.Speed, game.Boosting, game.SoundEnabled );
                view.Update( now );

                uint index = swap.CurrentBackBufferIndex;
                var buffer = buffers[(int)index]!; var target = targets[(int)index];
                long frame = submitted + 1;
                var commands = slot.SceneCommands;
                commands.Transition( buffer, ResourceState.Present, ResourceState.RenderTarget );
                scene.Record( commands, target, (uint)size.Width, (uint)size.Height, game );
                commands.Close( );
                var ui = slot.UiCommands;
                overlay.Record( ui, target, completed, frame );
                ui.Transition( buffer, ResourceState.RenderTarget, ResourceState.Present ); ui.Close( );
                queue.Submit( commands ); slot.Fence = queue.Submit( ui ); slot.Frame = submitted = frame;
                swap.Present( );
                game.ReportFrame( dt, view.GetStats( ).Batches );
                if ( smoke && frame >= 120 ) window.Close( );
            } catch ( Exception error ) {
                failure = error;
                if ( window.CloseRequested ) window.FinishClose( ); else window.Close( );
            } finally { if ( !window.IsDisposed && window.IsHandleCreated ) timer.Start( ); }
        };

        try {
            AcquireBuffers( );
            overlay.Resize( (uint)size.Width, (uint)size.Height, scale, 0 );
            scene.Resize( (uint)size.Width, (uint)size.Height );
            view.Update( 0 ); timer.Start( ); Forms.Application.Run( window );
            if ( failure is not null ) throw new InvalidOperationException( "Starfighter rendering stopped.", failure );
            Console.WriteLine( $"Frames={submitted} Resizes={resizes} Score={game.ScoreText} Wave={game.WaveText} Kills={game.KillsText} Meshes={scene.MeshDraws}" );
        } finally {
            timer.Stop( );
            if ( cursorHidden ) { Forms.Cursor.Show( ); cursorHidden = false; }
            try { if ( drain is null ) queue.WaitIdle( ); else drain.GetAwaiter( ).GetResult( ); }
            finally { ReleaseBuffers( ); }
        }
    }

    private static float DisplayScale( float height, float dpi ) => Math.Max( dpi, 96 ) / 96f * Math.Clamp( height / (Math.Max( dpi, 96 ) / 96f) / 1100, 1, 1.22f );
    private static N.MouseButton? MapButton( Forms.MouseButtons button ) => button switch {
        Forms.MouseButtons.Left => N.MouseButton.Left, Forms.MouseButtons.Right => N.MouseButton.Right,
        Forms.MouseButtons.Middle => N.MouseButton.Middle, Forms.MouseButtons.XButton1 => N.MouseButton.XButton1,
        Forms.MouseButtons.XButton2 => N.MouseButton.XButton2, _ => null
    };
    private static N.Key MapKey( Forms.Keys key ) => key switch {
        Forms.Keys.ControlKey => N.Key.LeftCtrl, Forms.Keys.ShiftKey => N.Key.LeftShift, Forms.Keys.Menu => N.Key.LeftAlt,
        _ => Enum.TryParse<N.Key>( key.ToString( ), true, out var mapped ) ? mapped : N.Key.None
    };

    private sealed class GameWindow : Forms.Form {
        private bool _finishClose;
        internal bool CloseRequested { get; private set; }
        internal GameWindow( ) => SetStyle( Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.Opaque, true );
        protected override void OnPaintBackground( Forms.PaintEventArgs args ) { }
        protected override void OnPaint( Forms.PaintEventArgs args ) { }
        protected override bool IsInputKey( Forms.Keys keyData ) => true;
        protected override void OnFormClosing( Forms.FormClosingEventArgs args ) {
            if ( !_finishClose ) { args.Cancel = true; CloseRequested = true; }
            base.OnFormClosing( args );
        }
        internal void FinishClose( ) { _finishClose = true; Close( ); }
    }

    private sealed class FrameSlot : IDisposable {
        internal CommandAllocator SceneAllocator { get; }
        internal CommandAllocator UiAllocator { get; }
        internal GraphicsCommandList SceneCommands { get; }
        internal GraphicsCommandList UiCommands { get; }
        internal ulong Fence;
        internal long Frame;
        internal FrameSlot( GraphicsDevice device ) {
            SceneAllocator = device.CreateCommandAllocator( );
            try {
                UiAllocator = device.CreateCommandAllocator( );
                try {
                    SceneCommands = device.CreateCommandList( SceneAllocator );
                    try {
                        SceneCommands.Close( ); UiCommands = device.CreateCommandList( UiAllocator );
                        try { UiCommands.Close( ); } catch { UiCommands.Dispose( ); throw; }
                    } catch { SceneCommands.Dispose( ); throw; }
                } catch { UiAllocator.Dispose( ); throw; }
            } catch { SceneAllocator.Dispose( ); throw; }
        }
        internal void Begin( ) {
            SceneAllocator.Reset( ); UiAllocator.Reset( );
            SceneCommands.Reset( SceneAllocator ); UiCommands.Reset( UiAllocator );
        }
        public void Dispose( ) {
            UiCommands.Dispose( ); SceneCommands.Dispose( ); UiAllocator.Dispose( ); SceneAllocator.Dispose( );
        }
    }
}
