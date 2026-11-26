using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace DXSharp.Noesis.Samples;

/// <summary>Expose the minimal ICommand contract to native Noesis reflection.
/// The CommunityToolkit implementation is deliberately private.</summary>
public sealed class ActionCommand : ICommand {
    private readonly RelayCommand _command;
    public ActionCommand( Action action, Func<bool>? enabled = null ) {
        ArgumentNullException.ThrowIfNull( action );
        _command = enabled is null ? new RelayCommand( action ) : new RelayCommand( action, enabled );
        _command.CanExecuteChanged += ( _, e ) => CanExecuteChanged?.Invoke( this, e );
    }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute( object? parameter ) => _command.CanExecute( parameter );
    public void Execute( object? parameter ) { if ( CanExecute( parameter ) ) _command.Execute( parameter ); }
    public void NotifyCanExecuteChanged( ) => _command.NotifyCanExecuteChanged( );
}
