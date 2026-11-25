using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Starfighter;

/// <summary>
/// Tiny native WinMM streaming mixer. It synthesizes the engine bed and combat
/// effects in real time, so the sample ships with sound and no third-party audio
/// runtime or copyrighted sound assets.
/// </summary>
public sealed class ProceduralAudio : IDisposable {
    private const int SampleRate = 48000, Channels = 2, BlockFrames = 1024, BufferCount = 4;
    private const uint WaveMapper = 0xFFFFFFFF, HeaderDone = 0x00000001;
    private const ushort FormatPcm = 1;
    private readonly ConcurrentQueue<SoundCue> _pending = new( );
    private readonly List<Voice> _voices = [];
    private readonly List<AudioBuffer> _buffers = [];
    private readonly AutoResetEvent _wake = new( false );
    private readonly Thread _thread;
    private IntPtr _device;
    private volatile bool _stopping;
    private volatile bool _enabled = true;
    private volatile float _speed = 8;
    private volatile bool _boost;
    private bool _available;
    private double _enginePhase, _subPhase, _turbinePhase, _boostPhase;
    private float _filteredNoise;
    private uint _noise = 0xA341316C;

    public bool Available => _available;
    public string Status { get; private set; } = "Audio unavailable";
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public ProceduralAudio( ) {
        _thread = new Thread( Pump ) { IsBackground = true, Name = "VANGUARD procedural audio" };
        try {
            var format = new WaveFormat {
                FormatTag = FormatPcm, Channels = Channels, SamplesPerSecond = SampleRate,
                AverageBytesPerSecond = SampleRate * Channels * 2, BlockAlign = Channels * 2, BitsPerSample = 16, ExtraSize = 0
            };
            uint result = WaveOutOpen( out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0 );
            if ( result != 0 ) { Status = $"waveOutOpen returned {result}"; return; }
            for ( int i = 0; i < BufferCount; i++ ) {
                var buffer = new AudioBuffer( );
                buffer.Pin = GCHandle.Alloc( buffer.Samples, GCHandleType.Pinned );
                buffer.Header = Marshal.AllocHGlobal( Marshal.SizeOf<WaveHeader>( ) );
                var header = new WaveHeader { Data = buffer.Pin.AddrOfPinnedObject( ), BufferLength = (uint)(BlockFrames * Channels * sizeof(short)) };
                Marshal.StructureToPtr( header, buffer.Header, false );
                _buffers.Add( buffer );
                result = WaveOutPrepareHeader( _device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>( ) );
                if ( result != 0 ) throw new InvalidOperationException( $"waveOutPrepareHeader returned {result}" );
                Fill( buffer.Samples );
                result = WaveOutWrite( _device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>( ) );
                if ( result != 0 ) throw new InvalidOperationException( $"waveOutWrite returned {result}" );
            }
            _available = true; Status = "48 kHz stereo procedural mixer"; _thread.Start( );
        } catch ( Exception ex ) {
            Status = $"Audio disabled: {ex.Message}";
            StopDevice( );
        }
    }

    public void Play( SoundCue cue ) { if ( _available && _enabled ) { _pending.Enqueue( cue ); _wake.Set( ); } }
    public void SetFlightState( float speed, bool boost, bool enabled ) {
        _speed = Math.Clamp( speed, 0, 100 ); _boost = boost; _enabled = enabled;
    }

    private void Pump( ) {
        while ( !_stopping ) {
            bool queued = false;
            foreach ( AudioBuffer buffer in _buffers ) {
                WaveHeader header = Marshal.PtrToStructure<WaveHeader>( buffer.Header );
                if ( (header.Flags & HeaderDone) == 0 ) continue;
                Fill( buffer.Samples );
                uint result = WaveOutWrite( _device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>( ) );
                if ( result != 0 ) { Status = $"Audio stream stopped ({result})"; _stopping = true; break; }
                queued = true;
            }
            if ( !queued ) _wake.WaitOne( 4 );
        }
    }

    private void Fill( short[] samples ) {
        while ( _pending.TryDequeue( out SoundCue cue ) ) AddVoice( cue );
        float speed = _speed, speedNorm = Math.Clamp( speed / 58f, 0, 1.4f );
        double engineHz = 43 + speedNorm * 36 + (_boost ? 27 : 0);
        double turbineHz = 118 + speedNorm * 96 + (_boost ? 140 : 0);
        for ( int frame = 0; frame < BlockFrames; frame++ ) {
            // Layered turbine harmonics plus a filtered noise bed make a continuous engine
            // presence, rather than a single audible sine-wave test tone.
            float noise = NextNoise( );
            _filteredNoise += (noise - _filteredNoise) * (_boost ? 0.085f : 0.052f);
            float fundamental = (float)Math.Sin( _enginePhase );
            float engine = fundamental * 0.028f
                + (float)Math.Sin( _enginePhase * 2.013 ) * 0.011f
                + (float)Math.Sin( _subPhase ) * 0.023f
                + (float)Math.Sin( _turbinePhase ) * (_boost ? 0.010f : 0.004f)
                + _filteredNoise * (_boost ? 0.015f : 0.006f);
            float pulse = _boost ? (float)Math.Sin( _boostPhase ) * 0.017f + (float)Math.Sin( _boostPhase * 0.5 ) * 0.009f : 0;
            _enginePhase += Math.Tau * engineHz / SampleRate;
            _subPhase += Math.Tau * (31 + speedNorm * 8) / SampleRate;
            _turbinePhase += Math.Tau * turbineHz / SampleRate;
            _boostPhase += Math.Tau * (8.5 + speedNorm * 5.0) / SampleRate;
            if ( _enginePhase > Math.Tau ) _enginePhase -= Math.Tau;
            if ( _subPhase > Math.Tau ) _subPhase -= Math.Tau;
            if ( _turbinePhase > Math.Tau ) _turbinePhase -= Math.Tau;
            if ( _boostPhase > Math.Tau ) _boostPhase -= Math.Tau;
            float left = _enabled ? engine + pulse : 0, right = left;
            for ( int i = _voices.Count - 1; i >= 0; i-- ) {
                Voice voice = _voices[i];
                if ( voice.Age >= voice.Duration ) { _voices.RemoveAt( i ); continue; }
                float t = voice.Age / voice.Duration;
                float envelope = voice.Type switch {
                    SoundCue.Explosion => MathF.Pow( 1 - t, 1.8f ),
                    SoundCue.HullHit => MathF.Pow( 1 - t, 2.5f ),
                    _ => MathF.Sin( MathF.PI * Math.Clamp( t, 0, 1 ) )
                };
                float sample = VoiceSample( voice, t, envelope );
                float pan = voice.Pan;
                left += sample * MathF.Sqrt( 1 - pan ); right += sample * MathF.Sqrt( 1 + pan );
                voice.Age += 1f / SampleRate;
            }
            samples[frame * 2] = (short)(Math.Clamp( MathF.Tanh( left * 1.55f ), -1, 1 ) * 32760);
            samples[frame * 2 + 1] = (short)(Math.Clamp( MathF.Tanh( right * 1.55f ), -1, 1 ) * 32760);
        }
    }

    private float VoiceSample( Voice voice, float t, float envelope ) {
        double phase = voice.Phase;
        float sweep = voice.StartHz + (voice.EndHz - voice.StartHz) * t;
        voice.Phase += Math.Tau * sweep / SampleRate;
        float sine = (float)Math.Sin( phase ), second = (float)Math.Sin( phase * 1.997 );
        float noise = NextNoise( );
        return voice.Type switch {
            SoundCue.Cannon => (sine * 0.68f + second * 0.18f) * envelope * 0.21f,
            SoundCue.Impact => (noise * 0.56f + sine * 0.44f) * envelope * 0.22f,
            SoundCue.Explosion => (noise * 0.76f + sine * 0.24f) * envelope * 0.28f,
            SoundCue.BoostOn => (sine * 0.65f + second * 0.35f) * envelope * 0.16f,
            SoundCue.BoostOff => sine * envelope * 0.12f,
            SoundCue.HullHit => (noise * 0.42f + sine * 0.58f) * envelope * 0.3f,
            SoundCue.Lock => sine * envelope * 0.13f,
            SoundCue.Overheat => (sine * 0.7f + second * 0.3f) * envelope * 0.15f,
            _ => 0
        };
    }

    private void AddVoice( SoundCue cue ) {
        (float duration, float start, float end, float pan) = cue switch {
            SoundCue.Cannon => (0.105f, 1120, 235, _voices.Count % 2 == 0 ? -0.72f : 0.72f),
            SoundCue.Impact => (0.15f, 150, 58, ((float)_voices.Count % 3 - 1) * 0.42f),
            SoundCue.Explosion => (0.78f, 112, 34, ((float)_voices.Count % 5 - 2) * 0.22f),
            SoundCue.BoostOn => (0.42f, 220, 820, 0),
            SoundCue.BoostOff => (0.28f, 520, 145, 0),
            SoundCue.HullHit => (0.34f, 95, 42, 0),
            SoundCue.Lock => (0.13f, 760, 1320, 0),
            SoundCue.Overheat => (0.42f, 510, 180, 0),
            _ => (0.1f, 440, 220, 0)
        };
        _voices.Add( new Voice { Type = cue, Duration = duration, StartHz = start, EndHz = end, Pan = Math.Clamp( pan, -0.9f, 0.9f ) } );
    }

    private float NextNoise( ) { _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5; return (_noise / (float)uint.MaxValue) * 2 - 1; }

    public void Dispose( ) {
        _stopping = true; _wake.Set( );
        if ( _thread.IsAlive ) _thread.Join( );
        StopDevice( ); _wake.Dispose( );
    }

    private void StopDevice( ) {
        if ( _device != IntPtr.Zero ) {
            WaveOutReset( _device );
            foreach ( AudioBuffer buffer in _buffers ) {
                WaveOutUnprepareHeader( _device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>( ) );
                Marshal.FreeHGlobal( buffer.Header ); buffer.Pin.Free( );
            }
            _buffers.Clear( ); WaveOutClose( _device ); _device = IntPtr.Zero;
        }
        _available = false;
    }

    private sealed class AudioBuffer { public readonly short[] Samples = new short[BlockFrames * Channels]; public GCHandle Pin; public IntPtr Header; }
    private sealed class Voice { public SoundCue Type; public float Age, Duration, StartHz, EndHz, Pan; public double Phase; }

    [StructLayout( LayoutKind.Sequential, Pack = 2 )]
    private struct WaveFormat {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout( LayoutKind.Sequential )]
    private struct WaveHeader {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }

    [DllImport( "winmm.dll", EntryPoint = "waveOutOpen" )]
    private static extern uint WaveOutOpen( out IntPtr device, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags );
    [DllImport( "winmm.dll", EntryPoint = "waveOutPrepareHeader" )]
    private static extern uint WaveOutPrepareHeader( IntPtr device, IntPtr header, uint size );
    [DllImport( "winmm.dll", EntryPoint = "waveOutUnprepareHeader" )]
    private static extern uint WaveOutUnprepareHeader( IntPtr device, IntPtr header, uint size );
    [DllImport( "winmm.dll", EntryPoint = "waveOutWrite" )]
    private static extern uint WaveOutWrite( IntPtr device, IntPtr header, uint size );
    [DllImport( "winmm.dll", EntryPoint = "waveOutReset" )]
    private static extern uint WaveOutReset( IntPtr device );
    [DllImport( "winmm.dll", EntryPoint = "waveOutClose" )]
    private static extern uint WaveOutClose( IntPtr device );
}
