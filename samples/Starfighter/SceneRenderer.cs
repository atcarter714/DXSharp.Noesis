using System.Numerics;
using DXSharp.Graphics;
using DXSharp.Shaders;

namespace Starfighter;

/// <summary>
/// DXSharp D3D12 scene renderer: procedural solar system, orbital asteroid torus,
/// fractured debris, pulse bolts and a true view-attached low-poly cockpit shell.
/// Noesis remains a separate overlay pass on the host's dedicated UI command list.
/// </summary>
public sealed class SceneRenderer : IDisposable {
    private readonly GraphicsDevice _device;
    private readonly RootSignature _root;
    private readonly PipelineState _backgroundPipeline, _meshPipeline;
    private readonly Mesh _asteroid, _pulse, _cockpit, _sun, _planet, _ring, _fighter, _exhaust;
    private readonly List<Mesh> _importedCockpit = [];
    private readonly uint[] _constants = new uint[52];
    private DepthTarget? _depth;
    public uint MeshDraws { get; private set; }

    public SceneRenderer( GraphicsDevice device, CommandQueue queue, PixelFormat format ) {
        _device = device;
        _root = device.CreateGraphicsConstantsRootSignature( 52 );
        try {
            using var compiler = new ShaderCompiler( );
            string source = File.ReadAllText( Path.Combine( AppContext.BaseDirectory, "Scene.hlsl" ) );
            _backgroundPipeline = device.CreateGraphicsPipeline( _root,
                compiler.Compile( source, "VSPlanet", "vs_6_0" ),
                compiler.Compile( source, "PSPlanet", "ps_6_0" ), format );
            try {
                VertexElement[] layout = [new( "POSITION", VertexFormat.Float3, 0 ), new( "NORMAL", VertexFormat.Float3, 12 ),
                    new( "COLOR", VertexFormat.Float3, 24 )];
                _meshPipeline = device.CreateGraphicsPipeline( _root,
                    compiler.Compile( source, "VSBody", "vs_6_0" ),
                    compiler.Compile( source, "PSBody", "ps_6_0" ), layout, format, depthEnabled: true );
                try {
                    using var allocator = device.CreateCommandAllocator( );
                    using var commands = device.CreateCommandList( allocator );
                    MeshData asteroidData = SceneGeometry.Asteroid( );
                    _asteroid = device.CreateMesh<Vertex>( commands, asteroidData.Vertices, asteroidData.Indices );
                    try {
                        MeshData pulseData = SceneGeometry.Pulse( );
                        _pulse = device.CreateMesh<Vertex>( commands, pulseData.Vertices, pulseData.Indices );
                        try {
                            MeshData cockpitData = SceneGeometry.Cockpit( );
                            _cockpit = device.CreateMesh<Vertex>( commands, cockpitData.Vertices, cockpitData.Indices );
                            try {
                                MeshData sunData = SceneGeometry.Sphere( 36, 22, new( 1.45f, 0.73f, 0.24f ) );
                                _sun = device.CreateMesh<Vertex>( commands, sunData.Vertices, sunData.Indices );
                                try {
                                    MeshData planetData = SceneGeometry.Sphere( 30, 18, new( 0.28f, 0.49f, 0.64f ) );
                                    _planet = device.CreateMesh<Vertex>( commands, planetData.Vertices, planetData.Indices );
                                    try {
                                        MeshData ringData = SceneGeometry.Ring( );
                                        _ring = device.CreateMesh<Vertex>( commands, ringData.Vertices, ringData.Indices );
                                        try {
                                            MeshData fighterData = SceneGeometry.Fighter( );
                                            _fighter = device.CreateMesh<Vertex>( commands, fighterData.Vertices, fighterData.Indices );
                                            try {
                                                MeshData exhaustData = SceneGeometry.EngineFlare( );
                                                _exhaust = device.CreateMesh<Vertex>( commands, exhaustData.Vertices, exhaustData.Indices );
                                                try {
                                                    if ( Environment.GetEnvironmentVariable( "DXSHARP_COCKPIT_PREVIEW" ) == "1" ) {
                                                        foreach ( MeshData chunk in CockpitMeshFile.Load( Path.Combine( AppContext.BaseDirectory, "Assets", "cockpit.mesh" ) ) )
                                                            _importedCockpit.Add( device.CreateMesh<Vertex>( commands, chunk.Vertices, chunk.Indices ) );
                                                    }
                                                    commands.Close( ); queue.Wait( queue.Submit( commands ) );
                                                } catch {
                                                    foreach ( Mesh mesh in _importedCockpit ) mesh.Dispose( );
                                                    _exhaust.Dispose( ); throw;
                                                }
                                            } catch { _fighter.Dispose( ); throw; }
                                        } catch { _ring.Dispose( ); throw; }
                                    } catch { _planet.Dispose( ); throw; }
                                } catch { _sun.Dispose( ); throw; }
                            } catch { _cockpit.Dispose( ); throw; }
                        } catch { _pulse.Dispose( ); throw; }
                    } catch { _asteroid.Dispose( ); throw; }
                } catch { _meshPipeline.Dispose( ); throw; }
            } catch { _backgroundPipeline.Dispose( ); throw; }
        } catch { _root.Dispose( ); throw; }
    }

    public void Resize( uint width, uint height ) {
        var replacement = _device.CreateDepthTarget( width, height );
        _depth?.Dispose( ); _depth = replacement;
    }

    private void Put( int index, float value ) => _constants[index] = BitConverter.SingleToUInt32Bits( value );
    private void Put( int index, Vector3 value, float w ) { Put( index, value.X ); Put( index + 1, value.Y ); Put( index + 2, value.Z ); Put( index + 3, w ); }

    public void Record( GraphicsCommandList commands, RenderTargetView target, uint width, uint height, StarfighterGame game ) {
        if ( _depth is null ) throw new InvalidOperationException( "Resize the scene before drawing." );
        float aspect = (float)width / height;
        Put( 0, game.CameraPosition, game.Time );
        Put( 4, game.CameraForward, aspect );
        Put( 8, game.CameraRight, 1.32f );
        Put( 12, game.CameraUp, 0.92f );
        Put( 16, game.Velocity, game.Fov );
        Put( 20, Vector3.Zero, 1 ); Put( 24, 0 ); Put( 25, 0 ); Put( 26, 0 ); Put( 27, 0 );
        Put( 28, Vector3.One, 1 );
        Put( 32, 0 ); Put( 33, 0 ); Put( 34, width ); Put( 35, height );
        Put( 36, width ); Put( 37, height ); Put( 38, 0 ); Put( 39, 0 );
        Put( 40, game.Right, 0 ); Put( 44, game.Up, 0 ); Put( 48, game.Forward, 0 );

        commands.SetViewport( width, height );
        commands.SetRenderTarget( target );
        commands.SetGraphicsPipeline( _backgroundPipeline );
        commands.SetGraphicsConstants( _constants );
        commands.Draw( 3 );

        commands.ClearDepthStencil( _depth );
        commands.SetRenderTarget( target, _depth );
        commands.SetGraphicsPipeline( _meshPipeline );
        MeshDraws = 0;

        // Helios, two orbiting planets and a particulate ring provide a real world-space landmark.
        DrawMesh( commands, _sun, Vector3.Zero, 7.0f, (float)game.Time * 0.015f, 3,
            new( 1.0f, 0.78f, 0.48f ), 2.25f );
        float innerPhase = (float)game.Time * 0.024f;
        Vector3 innerPlanet = new( MathF.Cos( innerPhase ) * 16.8f, 0.7f * MathF.Sin( innerPhase * 0.7f ), MathF.Sin( innerPhase ) * 16.8f );
        DrawMesh( commands, _planet, innerPlanet, 2.15f, (float)game.Time * 0.07f, 5,
            new( 0.60f, 1.0f, 1.25f ), 1.0f );
        DrawMesh( commands, _ring, innerPlanet, 3.65f, (float)game.Time * 0.018f, 0,
            new( 1.0f, 0.82f, 0.55f ), 0.95f, 0.42f );
        float outerPhase = (float)game.Time * 0.010f + 1.7f;
        Vector3 outerPlanet = new( MathF.Cos( outerPhase ) * 47f, 1.8f * MathF.Sin( outerPhase * 0.43f ), MathF.Sin( outerPhase ) * 47f );
        DrawMesh( commands, _planet, outerPlanet, 4.2f, (float)game.Time * 0.026f, 5,
            new( 1.15f, 0.58f, 0.32f ), 1.0f, 0.12f );

        foreach ( Asteroid rock in game.Asteroids ) {
            DrawMesh( commands, _asteroid, rock.Position, rock.Radius, rock.Rotation, 0,
                rock.Fragment ? new( 0.88f, 0.72f, 0.52f ) : new( 0.88f, 0.92f, 1.0f ), 1.0f,
                rock.RotationX, rock.RotationZ );
        }
        foreach ( Blast blast in game.Blasts ) {
            float age = blast.Age / blast.Duration;
            float size = blast.Radius * (0.52f + age * 1.9f);
            DrawMesh( commands, _sun, blast.Position, size, blast.Age * 2.8f, 3,
                new( 1.3f, 0.54f + (1 - age) * 0.5f, 0.17f ), (1 - age) * 2.7f );
        }
        foreach ( PulseBolt bolt in game.Bolts ) {
            PutObject( bolt.Position, 1.0f ); PutDetail( 0, 1, 0, 0 ); Put( 28, new( 0.48f, 1.12f, 2.7f ), 2.8f );
            commands.SetGraphicsConstants( _constants ); commands.DrawMesh( _pulse ); MeshDraws++;
        }

        // Cockpit view is the default. C toggles to a chase camera so the complete
        // procedurally modeled Viper hull and its twin afterburners can be inspected.
        if ( game.ChaseView ) {
            DrawMesh( commands, _exhaust, game.Position, 1, 0, 4, new( 0.35f, 0.78f, 1.25f ), game.Boosting ? 3.1f : 1.8f );
            DrawMesh( commands, _fighter, game.Position, 1, 0, 4, Vector3.One, 1.0f );
        } else {
            PutObject( game.Position, 1 ); PutDetail( 0, 2, 0, 0 ); Put( 28, Vector3.One, 1 );
            commands.SetGraphicsConstants( _constants );
            if ( _importedCockpit.Count == 0 ) { commands.DrawMesh( _cockpit ); MeshDraws++; }
            else foreach ( Mesh mesh in _importedCockpit ) { commands.DrawMesh( mesh ); MeshDraws++; }
        }
        game.DrawCount = (int)MeshDraws;

        void DrawMesh( GraphicsCommandList list, Mesh mesh, Vector3 position, float scale, float rotationY,
                       int mode, Vector3 tint, float intensity, float rotationX = 0, float rotationZ = 0 ) {
            PutObject( position, scale ); PutDetail( rotationY, mode, rotationX, rotationZ ); Put( 28, tint, intensity );
            list.SetGraphicsConstants( _constants ); list.DrawMesh( mesh ); MeshDraws++;
        }
        void PutObject( Vector3 position, float scale ) => Put( 20, position, scale );
        void PutDetail( float rotationY, int mode, float rotationX, float rotationZ ) {
            Put( 24, rotationY ); Put( 25, mode ); Put( 26, rotationX ); Put( 27, rotationZ );
        }
    }

    public void Dispose( ) {
        foreach ( Mesh mesh in _importedCockpit ) mesh.Dispose( );
        _depth?.Dispose( ); _exhaust.Dispose( ); _fighter.Dispose( ); _ring.Dispose( ); _planet.Dispose( ); _sun.Dispose( ); _cockpit.Dispose( );
        _pulse.Dispose( ); _asteroid.Dispose( ); _meshPipeline.Dispose( ); _backgroundPipeline.Dispose( ); _root.Dispose( );
    }
}
