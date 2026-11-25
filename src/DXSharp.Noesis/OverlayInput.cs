using N = global::Noesis;

namespace DXSharp.Noesis;

/// <summary>Prevents scene input leaking through UI panels, including unhandled clicks on their backgrounds.</summary>
public sealed class OverlayInput( N.View view ) {
    public float Scale { get; set; } = 1;
    public bool IsEditingText {
        get {
            for ( var node = (N.DependencyObject?)view.Content.Keyboard.FocusedElement; node is not null; node = N.VisualTreeHelper.GetParent( node ) )
                if ( node is N.TextBox or N.PasswordBox ) return true;
            return false;
        }
    }
    public bool BlocksSceneAt( int x, int y ) {
        var hit = N.VisualTreeHelper.HitTest( view.Content, new N.Point( x / Scale, y / Scale ) );
        for ( var node = hit.VisualHit; node is not null; node = N.VisualTreeHelper.GetParent( node ) ) {
            if ( node is N.ButtonBase or N.TextBox or N.PasswordBox or N.Slider or N.ScrollViewer or N.Selector ) return true;
            if ( node is N.FrameworkElement element && Equals( element.Tag, "InputSurface" ) ) return true;
        }
        return false;
    }
}
