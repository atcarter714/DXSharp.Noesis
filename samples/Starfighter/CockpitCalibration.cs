using System.Globalization;
using System.Numerics;
namespace Starfighter;

/// <summary>Rebases a baked cockpit around a pilot eye without changing flight coordinates.</summary>
public readonly record struct CockpitCalibration(Vector3 Eye,float YawDegrees,float Scale) {
    public static CockpitCalibration FromEnvironment() {
        // The previously baked asset is not centered. Default remains explicitly
        // provisional; select another eye and forward direction without rebaking.
        string? text=Environment.GetEnvironmentVariable("DXSHARP_COCKPIT_EYE");
        Vector3 eye=new(1.1f,.65f,-.65f);
        if(!string.IsNullOrWhiteSpace(text)) {
            string[] fields=text.Split(',');
            if(fields.Length!=3)throw new FormatException("DXSHARP_COCKPIT_EYE expects x,y,z.");
            eye=new(Parse(fields[0]),Parse(fields[1]),Parse(fields[2]));
        }
        float yaw=Parse(Environment.GetEnvironmentVariable("DXSHARP_COCKPIT_YAW")??"0");
        float scale=Parse(Environment.GetEnvironmentVariable("DXSHARP_COCKPIT_SCALE")??"1");
        if(scale<=0||scale>100)throw new ArgumentOutOfRangeException(nameof(scale));
        return new(eye,yaw,scale);
        static float Parse(string v) {
            float f=float.Parse(v,CultureInfo.InvariantCulture);
            if(!float.IsFinite(f))throw new FormatException("Cockpit calibration values must be finite.");
            return f;
        }
    }
    public MeshData Apply(MeshData source) {
        var rotation=Matrix4x4.CreateRotationY(YawDegrees*MathF.PI/180);
        return new(source.Vertices.Select(v=>new Vertex(Vector3.Transform(v.Position-Eye,rotation)*Scale,
            Vector3.Normalize(Vector3.TransformNormal(v.Normal,rotation)),v.Color)).ToArray(),source.Indices);
    }
}
