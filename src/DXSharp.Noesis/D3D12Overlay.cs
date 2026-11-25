using DXSharp.Graphics;
using DXSharp.Interop;
using Windows.Win32.Graphics.Direct3D12;
using N = global::Noesis;

namespace DXSharp.Noesis;

/// <summary>Records a Noesis overlay on the host's direct D3D12 command list.</summary>
/// <remarks>
/// Use one direct queue and call from the Noesis view's owning thread. Keep the
/// current back buffer, its RTV heap, and this overlay alive through submission
/// and GPU completion. Drain the queue before destroying or resizing the overlay.
/// </remarks>
public sealed class D3D12Overlay : IDisposable {
    private const DepthStencilFormat StencilFormat = DepthStencilFormat.D24UnormS8Uint;
    private readonly GraphicsDevice _graphics;
    private readonly NativeInterface<ID3D12Device> _device;
    private readonly N.Renderer _renderer;
    private readonly N.RenderDeviceD3D12 _renderDevice;
    private DepthTarget? _stencil;
    private uint _width, _height;
    private long _lastFrame, _lastCompleted;
    private bool _disposed;

    public N.View View { get; }

    public D3D12Overlay( GraphicsDevice device, N.FrameworkElement content, PixelFormat colorFormat ) {
        ArgumentNullException.ThrowIfNull( device );
        ArgumentNullException.ThrowIfNull( content );
        _graphics = device;
        _device = device.QueryNativeInterface<ID3D12Device>( );
        N.View? view = null;
        N.RenderDeviceD3D12? renderDevice = null;
        try {
            View = view = N.GUI.CreateView( content );
            _renderer = View.Renderer;
            _renderDevice = renderDevice = new( _device.Pointer, (int)colorFormat, (int)StencilFormat, 1 );
            _renderer.Init( _renderDevice );
        } catch {
            renderDevice?.Dispose( );
            view?.Dispose( );
            _device.Dispose( );
            throw;
        }
    }

    /// <summary>Replaces the stencil target and updates the view after the queue drains.</summary>
    public void Resize( uint width, uint height, float scale, long completedFrame ) {
        ObjectDisposedException.ThrowIf( _disposed, this );
        ArgumentOutOfRangeException.ThrowIfZero( width );
        ArgumentOutOfRangeException.ThrowIfZero( height );
        if ( !float.IsFinite( scale ) || scale <= 0 ) throw new ArgumentOutOfRangeException( nameof(scale) );
        if ( completedFrame != _lastFrame )
            throw new InvalidOperationException( "Drain the graphics queue before resizing the overlay." );

        if ( _stencil is null || width != _width || height != _height ) {
            var replacement = _graphics.CreateDepthTarget( width, height, StencilFormat );
            _stencil?.Dispose( );
            _stencil = replacement;
            _width = width;
            _height = height;
        }

        View.SetScale( scale );
        View.SetSize( checked((int)width), checked((int)height) );
        _lastCompleted = completedFrame;
    }

    /// <summary>Records the complete Noesis pass into a dedicated, freshly reset direct command list.</summary>
    /// <remarks>
    /// Submit the scene list first, leaving the back buffer in RenderTarget state.
    /// Do not record foreign graphics draws in this list: the Noesis render device
    /// caches GPU state between its offscreen and onscreen passes. After this call,
    /// the host may transition the back buffer to Present and submit this UI list
    /// on the same queue. Frame IDs describe completion of both lists together.
    /// </remarks>
    public void Record( GraphicsCommandList commands, RenderTargetView target,
                        long completedFrame, long frame ) {
        ObjectDisposedException.ThrowIf( _disposed, this );
        ArgumentNullException.ThrowIfNull( commands );
        if ( _stencil is null ) throw new InvalidOperationException( "Resize the overlay before recording." );
        if ( frame <= _lastFrame ) throw new ArgumentOutOfRangeException( nameof(frame) );
        if ( completedFrame < _lastCompleted || completedFrame > _lastFrame )
            throw new ArgumentOutOfRangeException( nameof(completedFrame) );

        using var nativeCommands = commands.QueryNativeInterface<ID3D12GraphicsCommandList>( );

        _renderDevice.SetCommandList( nativeCommands.Pointer, completedFrame, frame );
        _renderer.UpdateRenderTree( );
        _renderer.RenderOffscreen( );

        commands.SetViewport( _width, _height );
        commands.ClearDepthStencil( _stencil, DepthStencilClear.Stencil );
        commands.SetRenderTarget( target, _stencil );
        _renderer.Render( );

        _lastCompleted = completedFrame;
        _lastFrame = frame;
    }

    public void Dispose( ) {
        if ( _disposed ) return;
        _disposed = true;
        try {
            _renderer.Shutdown( );
        } finally {
            _renderer.Dispose( );
            View.Dispose( );
            _renderDevice.Dispose( );
            _stencil?.Dispose( );
            _device.Dispose( );
        }
    }

}
