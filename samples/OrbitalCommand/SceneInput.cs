using DXSharp.Noesis;
using N = Noesis;
namespace OrbitalCommand;

public enum PointerOwner { None, Interface, Scene }

/// <summary>Assigns the whole gesture at pointer-down; hover changes never steal an active camera drag.</summary>
public sealed class SceneInput( CommandDeck deck, N.View view, OverlayInput hitTest ) {
    public PointerOwner Owner { get; private set; }
    public bool IsDragging => Owner == PointerOwner.Scene && _moved;
    public int SceneClicks { get; private set; }
    public int UiClicks { get; private set; }
    public float Width { get; set; }
    public float Height { get; set; }
    private N.MouseButton _button;
    private int _downX, _downY, _lastX, _lastY;
    private bool _moved;

    public void Down( int x, int y, N.MouseButton button ) {
        if ( Owner != PointerOwner.None ) return;
        _button = button; _downX = _lastX = x; _downY = _lastY = y; _moved = false;
        // Noesis' handled return value is not a gesture-ownership contract.
        // Empty world surfaces and previously focused controls can consume events.
        if ( hitTest.BlocksSceneAt( x, y ) || !deck.Camera.GetViewport( Width, Height ).Contains( x, y ) ) {
            Owner = PointerOwner.Interface; UiClicks++;
            view.MouseMove( x, y ); view.MouseButtonDown( x, y, button );
            return;
        }
        if ( button is not (N.MouseButton.Left or N.MouseButton.Right or N.MouseButton.Middle) ) return;
        Owner = PointerOwner.Scene;
        view.Content.Keyboard.ClearFocus( );
        view.Content.Mouse.Capture( null );
    }
    public void Move( int x, int y ) {
        if ( Owner == PointerOwner.Scene ) {
            float scale = hitTest.Scale;
            _moved |= Math.Abs( x - _downX ) + Math.Abs( y - _downY ) > 4 * scale;
            if ( _moved ) {
                deck.AutoCamera = false;
                if ( _button == N.MouseButton.Middle ) {
                    deck.FollowSelection = false;
                    deck.ClearSolarFocus();
                    deck.Camera.Pan( x - _lastX, y - _lastY, Width, Height );
                } else deck.Camera.Orbit( (x - _lastX) / scale, (y - _lastY) / scale );
            }
        } else view.MouseMove( x, y );
        _lastX = x; _lastY = y;
    }
    public void Up( int x, int y, N.MouseButton button ) {
        if ( Owner == PointerOwner.None || button != _button ) return;
        if ( Owner == PointerOwner.Interface ) view.MouseButtonUp( x, y, button );
        else if ( !_moved && button == N.MouseButton.Left ) { SceneClicks++; deck.SelectAt( x, y, Width, Height ); }
        Owner = PointerOwner.None; _moved = false;
    }
    public void Wheel( int x, int y, int delta ) {
        if ( Owner == PointerOwner.Scene || (Owner != PointerOwner.Interface && !hitTest.BlocksSceneAt( x, y ) && deck.Camera.GetViewport( Width, Height ).Contains( x, y )) )
            deck.Camera.Zoom( delta );
        else view.MouseWheel( x, y, delta );
    }
    public void Cancel( ) {
        if ( Owner == PointerOwner.Interface ) view.MouseButtonUp( _lastX, _lastY, _button );
        Owner = PointerOwner.None; _moved = false;
        view.Content.Mouse.Capture( null );
    }
}

public static class DisplayMetrics {
    public static float Scale( float width, float height, float dpi, float adjustment ) =>
        Math.Min( Scale( height, dpi, adjustment ), Math.Min( width / 1080f, height / 740f ) );
    public static float Scale( float height, float dpi, float adjustment ) {
        float systemScale = Math.Max( dpi, 96 ) / 96;
        float largeDisplay = Math.Clamp( height / systemScale / 1100, 1, 1.25f );
        return systemScale * largeDisplay * Math.Clamp( adjustment, 0.8f, 1.5f );
    }
}
