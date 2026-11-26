using System.Numerics;
using System.Runtime.InteropServices;
using DXSharp.Graphics;
using DXSharp.Shaders;

namespace OrbitalCommand;

/// <summary>Procedural planetary shading and indexed mesh draws on DXSharp's managed graphics API.</summary>
public sealed class SceneRenderer : IDisposable {
    private readonly GraphicsDevice _device;
    private readonly RootSignature _root;
    private readonly PipelineState _planetPipeline, _meshPipeline;
    private readonly RootSignature _solarRoot;
    private readonly PipelineState _solarPipeline;
    private readonly uint[] _solarConstants=new uint[60];
    private readonly Mesh _ship, _station;
    private DepthTarget? _depth;
    private readonly uint[] _constants = new uint[40];
    public uint MeshDraws { get; private set; }

    public SceneRenderer( GraphicsDevice device, CommandQueue queue, PixelFormat format ) {
        _device = device;
        _root = device.CreateGraphicsConstantsRootSignature( 40 );
        try {
            using var compiler = new ShaderCompiler( );
            string source = File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "Scene.hlsl" ) );
            _planetPipeline = device.CreateGraphicsPipeline( _root, compiler.Compile( source, "VSPlanet", "vs_6_0" ),
                compiler.Compile( source, "PSPlanet", "ps_6_0" ), format );
            try {
                VertexElement[] layout = [new( "POSITION", VertexFormat.Float3, 0 ), new( "NORMAL", VertexFormat.Float3, 12 ),
                    new( "COLOR", VertexFormat.Float3, 24 )];
                _meshPipeline = device.CreateGraphicsPipeline( _root, compiler.Compile( source, "VSShip", "vs_6_0" ),
                    compiler.Compile( source, "PSShip", "ps_6_0" ), layout, format, depthEnabled: true );
                try {
                    using var allocator = device.CreateCommandAllocator( );
                    using var commands = device.CreateCommandList( allocator );
                    var ship = SceneGeometry.Ship( );
                    _ship = device.CreateMesh<Vertex>( commands, ship.Vertices, ship.Indices );
                    try {
                        var station = SceneGeometry.Station( );
                        _station = device.CreateMesh<Vertex>( commands, station.Vertices, station.Indices );
                        try { commands.Close( ); queue.Wait( queue.Submit( commands ) ); }
                        catch { _station.Dispose( ); throw; }
                    } catch { _ship.Dispose( ); throw; }
                } catch { _meshPipeline.Dispose( ); throw; }
            } catch { _planetPipeline.Dispose( ); throw; }
        } catch { _root.Dispose( ); throw; }
        try {
            _solarRoot=device.CreateGraphicsConstantsRootSignature(60);
            try {
                using var compiler=new ShaderCompiler();
                string solar=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SolarScene.hlsl"));
                _solarPipeline=device.CreateGraphicsPipeline(_solarRoot,compiler.Compile(solar,"VSMain","vs_6_0"),compiler.Compile(solar,"PSMain","ps_6_0"),format);
            } catch { _solarRoot.Dispose(); throw; }
        } catch { _station.Dispose(); _ship.Dispose(); _meshPipeline.Dispose(); _planetPipeline.Dispose(); _root.Dispose(); throw; }
    }

    public void Resize( uint width, uint height ) {
        var replacement = _device.CreateDepthTarget( width, height );
        _depth?.Dispose( ); _depth = replacement;
    }

    private void Put( int index, Vector3 vector, float w ) { Put( index, vector.X ); Put( index + 1, vector.Y ); Put( index + 2, vector.Z ); Put( index + 3, w ); }
    private void Put( int index, float value ) => _constants[index] = BitConverter.SingleToUInt32Bits( value );

    public void Record( GraphicsCommandList commands, RenderTargetView target, uint width, uint height, CommandDeck deck ) {
        if ( _depth is null ) throw new InvalidOperationException( "Resize the scene before drawing." );
        if(deck.Solar3D){RecordSolar(commands,target,width,height,deck);return;}
        Put( 0, deck.Camera.Position, (float)deck.Simulation.Time );
        Put( 4, deck.Camera.Forward, (float)width / height );
        Put( 8, deck.Camera.Right, deck.Exposure );
        Put( 12, deck.Camera.Up, deck.Atmosphere );
        Put( 16, deck.ShowOrbits ? 1 : 0 ); Put( 17, deck.SelectedShip.Vessel.Survey );
        Put( 18, deck.SelectedShip.Vessel.Mission == Mission.Survey ? 1 : 0 ); Put( 19, OrbitCamera.TanHalfFov );
        Put( 28, deck.SelectedShip.Vessel.Position, 0 );
        var viewport = deck.Camera.GetViewport( width, height );
        Put( 32, new Vector3( viewport.X, viewport.Y, viewport.Width ), viewport.Height );
        Put( 36, new Vector3( width, height, 0 ), 0 );
        commands.SetViewport( width, height );
        commands.SetRenderTarget( target );
        commands.SetGraphicsPipeline( _planetPipeline );
        commands.SetGraphicsConstants( _constants );
        commands.Draw( 3 );
        commands.ClearDepthStencil( _depth );
        commands.SetRenderTarget( target, _depth );
        commands.SetGraphicsPipeline( _meshPipeline );
        MeshDraws = 0;
        for ( int i = 0; i < deck.Ships.Count; i++ ) {
            var ship = deck.Ships[i].Vessel;
            Put( 20, ship.Position, ship.Role == "Freighter" ? 1.1f : 0.85f );
            Put( 24, ship.Heading ); Put( 25, i == deck.SelectionIndex ? 1 : 0 ); Put( 26, ship.Role switch { "Corvette" => 1, "Freighter" => 2, "Surveyor" => 3, "Interceptor" => 4, _ => 0 } ); Put( 27, ship.Throttle );
            commands.SetGraphicsConstants( _constants ); commands.DrawMesh( _ship ); MeshDraws++;
        }
        Put( 20, new Vector3( 0, 0.19f, 1.58f ), 1 );
        Put( 24, (float)deck.Simulation.Time * 0.025f ); Put( 25, 0 ); Put( 26, 10 );
        commands.SetGraphicsConstants( _constants ); commands.DrawMesh( _station ); MeshDraws++;
    }

    private void RecordSolar(GraphicsCommandList commands,RenderTargetView target,uint width,uint height,CommandDeck deck) {
        void put(int i,float x)=>_solarConstants[i]=BitConverter.SingleToUInt32Bits(x);
        void vec(int i,Vector3 v,float w){put(i,v.X);put(i+1,v.Y);put(i+2,v.Z);put(i+3,w);}
        var vp=deck.Camera.GetViewport(width,height);
        vec(0,deck.Camera.Position,deck.Exposure); vec(4,deck.Camera.Forward,OrbitCamera.TanHalfFov);
        vec(8,deck.Camera.Right,deck.ShowOrbits?1:0); vec(12,deck.Camera.Up,deck.Atmosphere);
        vec(16,new(vp.X,vp.Y,vp.Width),vp.Height);
        vec(20,Vector3.Zero,deck.SolarRadius(-1));
        for(int i=0;i<8;i++)vec(24+i*4,CommandDeck.SolarPosition(deck.SolarStates[i]),deck.SolarRadius(i));
        vec(56,new(width,height,(float)deck.Simulation.Time),deck.BodyScale>1?1:0);
        commands.SetViewport(width,height);commands.SetRenderTarget(target);commands.SetGraphicsPipeline(_solarPipeline);
        commands.SetGraphicsConstants(_solarConstants);commands.Draw(3);MeshDraws=0;
    }

    public void Dispose( ) { _solarPipeline.Dispose(); _solarRoot.Dispose(); _depth?.Dispose( ); _station.Dispose( ); _ship.Dispose( ); _meshPipeline.Dispose( ); _planetPipeline.Dispose( ); _root.Dispose( ); }
}
