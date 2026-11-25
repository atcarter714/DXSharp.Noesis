using DXSharp.Graphics;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi.Common;
namespace OrbitalCommand;

/// <summary>Smoke-test readback. Each frame slot validates its preceding HUD draw after that slot's fence retires.</summary>
internal sealed unsafe class HudReadback : IDisposable {
    private const uint Pitch = 2048, MaxHeight = 128;
    private readonly GpuResource _buffer;
    private readonly byte[] _pixels = new byte[Pitch * MaxHeight];
    private uint _width, _height;
    private long _frame;
    internal int Captures { get; private set; }
    internal HudReadback( GraphicsDevice device ) => _buffer = device.CreateBuffer( Pitch * MaxHeight, BufferMemory.Readback );
    internal void Capture( GraphicsCommandList commands, GpuResource source, uint x, uint y, uint width, uint height, long frame ) {
        _width = Math.Min( width, 512 ); _height = Math.Min( height, MaxHeight ); _frame = frame;
        using var nativeSource = source.QueryNativeInterface<ID3D12Resource>( );
        using var nativeDestination = _buffer.QueryNativeInterface<ID3D12Resource>( );
        var from = new D3D12_TEXTURE_COPY_LOCATION {
            pResource = (ID3D12Resource_unmanaged*)nativeSource.Pointer,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX
        };
        var to = new D3D12_TEXTURE_COPY_LOCATION {
            pResource = (ID3D12Resource_unmanaged*)nativeDestination.Pointer,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT
        };
        to.Anonymous.PlacedFootprint.Footprint = new( ) {
            Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, Width = _width, Height = _height, Depth = 1, RowPitch = Pitch
        };
        var box = new D3D12_BOX { left = x, top = y, right = x + _width, bottom = y + _height, back = 1 };
        commands.Transition( source, ResourceState.RenderTarget, ResourceState.CopySource );
        commands.RecordNative( native => {
            var region = box;
            native.CopyTextureRegion( to, 0, 0, 0, from, &region );
        }, source, _buffer );
        commands.Transition( source, ResourceState.CopySource, ResourceState.RenderTarget );
    }
    internal void Validate( ) {
        if ( _frame == 0 ) return;
        _buffer.Read<byte>( _pixels );
        int bright = 0;
        for ( uint y = 0; y < _height; y++ ) for ( uint x = 0; x < _width; x++ ) {
            int i = checked((int)(y * Pitch + x * 4));
            if ( _pixels[i] > 135 && _pixels[i + 1] > 150 && _pixels[i + 2] > 150 ) bright++;
        }
        if ( bright < 16 ) throw new InvalidOperationException( $"Frame {_frame}: HUD text disappeared from GPU readback ({bright} bright pixels)." );
        Captures++; _frame = 0;
    }
    public void Dispose( ) => _buffer.Dispose( );
}
