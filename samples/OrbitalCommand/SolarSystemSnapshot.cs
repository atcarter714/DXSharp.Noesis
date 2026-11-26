using System.Globalization;

namespace OrbitalCommand;

public sealed record PlanetState( int Id, string Name, double JulianDateTdb, double X, double Y, double Z,
                                  double Vx, double Vy, double Vz );

/// <summary>Geometric heliocentric J2000 ecliptic state vectors, stored in AU and AU/day.</summary>
public static class SolarSystemSnapshot {
    private static readonly (int Id, string Name)[] Expected = [
        (199, "Mercury"), (299, "Venus"), (399, "Earth"), (499, "Mars"),
        (599, "Jupiter"), (699, "Saturn"), (799, "Uranus"), (899, "Neptune") ];
    internal static List<PlanetState> ReadRows( string path ) {
        using var stream = File.OpenText( path );
        if ( stream.ReadLine( ) != "id,name,jd_tdb,x_au,y_au,z_au,vx_au_day,vy_au_day,vz_au_day" )
            throw new InvalidDataException( "Unexpected Horizons cache schema." );
        var states = new List<PlanetState>( );
        string? line;
        while ( (line = stream.ReadLine( )) != null ) {
            string[] fields = line.Split( ',' );
            if ( fields.Length != 9 || !int.TryParse( fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id ) ||
                 !Expected.Contains( (id, fields[1]) ) ) throw new InvalidDataException( "Invalid Horizons body." );
            double[] values = fields.Skip( 2 ).Select( field => double.Parse( field, CultureInfo.InvariantCulture ) ).ToArray( );
            if ( values.Any( value => !double.IsFinite( value ) ) ) throw new InvalidDataException( "Invalid Horizons state." );
            states.Add( new( id, fields[1], values[0], values[1], values[2], values[3], values[4], values[5], values[6] ) );
        }
        return states;
    }
    public static IReadOnlyList<PlanetState> Load(string path) {
        var states=ReadRows(path);
        if(states.Count!=Expected.Length||states.Select(s=>s.Id).Distinct().Count()!=Expected.Length||states.Select(s=>s.JulianDateTdb).Distinct().Count()!=1)
            throw new InvalidDataException("Horizons cache must contain all eight planets at one epoch.");
        return states.OrderBy(s=>s.Id).ToArray();
    }
}

public sealed record PlanetMarker( string Name, float Left, float Top, string Distance, float Diameter, string Color, float LabelLeft, float LabelTop ) { public Noesis.BitmapImage? Artwork => SolarArtwork.Load(Name.ToLowerInvariant()+".png");
    public Noesis.Visibility RingVisibility => Name=="Saturn" ? Noesis.Visibility.Visible : Noesis.Visibility.Collapsed;
    public float RingWidth => Diameter*2.2f;
    public float RingHeight => Diameter*.7f;
    public float RingLeft => Left+Diameter*.5f-RingWidth*.5f;
    public float RingTop => Top+Diameter*.5f-RingHeight*.5f;
    public float RingStroke => Diameter*.11f;
}
public sealed record OrbitSegment(float X1,float Y1,float X2,float Y2);

/// <summary>NASA/JPL SSD approximate planetary elements, Table 1 (1800–2050).
/// Earth row is the Earth–Moon barycenter. Coordinates are heliocentric J2000 ecliptic AU.
/// https://ssd.jpl.nasa.gov/planets/approx_pos.html</summary>
public static class ApproximateEphemeris {
    private static readonly double[][] Elements = [
        [0.38709927,0.20563593,7.00497902,252.25032350,77.45779628,48.33076593,0.00000037,0.00001906,-0.00594749,149472.67411175,0.16047689,-0.12534081],
        [0.72333566,0.00677672,3.39467605,181.97909950,131.60246718,76.67984255,0.00000390,-0.00004107,-0.00078890,58517.81538729,0.00268329,-0.27769418],
        [1.00000261,0.01671123,-0.00001531,100.46457166,102.93768193,0,0.00000562,-0.00004392,-0.01294668,35999.37244981,0.32327364,0],
        [1.52371034,0.09339410,1.84969142,-4.55343205,-23.94362959,49.55953891,0.00001847,0.00007882,-0.00813131,19140.30268499,0.44441088,-0.29257343],
        [5.20288700,0.04838624,1.30439695,34.39644051,14.72847983,100.47390909,-0.00011607,-0.00013253,-0.00183714,3034.74612775,0.21252668,0.20469106],
        [9.53667594,0.05386179,2.48599187,49.95424423,92.59887831,113.66242448,-0.00125060,-0.00050991,0.00193609,1222.49362201,-0.41897216,-0.28867794],
        [19.18916464,0.04725744,0.77263783,313.23810451,170.95427630,74.01692503,-0.00196176,-0.00004397,-0.00242939,428.48202785,0.40805281,0.04240589],
        [30.06992276,0.00859048,1.77004347,-55.12002969,44.96476227,131.78422574,0.00026291,0.00005105,0.00035372,218.45945325,-0.32241464,-0.00508664]
    ];
    public static readonly string[] Names=["Mercury","Venus","Earth","Mars","Jupiter","Saturn","Uranus","Neptune"];
    public static readonly double[] RadiiKm=[2439.7,6051.8,6371.0,3389.5,69911,58232,25362,24622];
    private const double Rad=Math.PI/180;
    public static (double X,double Y,double Z) Position(int index,double jd,double? anomaly=null) {
        double t=(jd-2451545.0)/36525.0;
        var p=Elements[index]; double a=p[0]+t*p[6],e=p[1]+t*p[7],i=(p[2]+t*p[8])*Rad;
        double l=(p[3]+t*p[9])*Rad,w=(p[4]+t*p[10])*Rad,node=(p[5]+t*p[11])*Rad;
        double m=Math.IEEERemainder(l-w,2*Math.PI), E=anomaly??m;
        if(anomaly is null) for(int k=0;k<12;k++) { double d=(E-e*Math.Sin(E)-m)/(1-e*Math.Cos(E)); E-=d; if(Math.Abs(d)<1e-13)break; }
        double x=a*(Math.Cos(E)-e),y=a*Math.Sqrt(1-e*e)*Math.Sin(E),arg=w-node;
        double u=x*Math.Cos(arg)-y*Math.Sin(arg),v=x*Math.Sin(arg)+y*Math.Cos(arg);
        return (u*Math.Cos(node)-v*Math.Cos(i)*Math.Sin(node),u*Math.Sin(node)+v*Math.Cos(i)*Math.Cos(node),v*Math.Sin(i));
    }
    public static IReadOnlyList<PlanetState> At(double jd) {
        var result=new List<PlanetState>(8);
        for(int i=0;i<8;i++) { var p=Position(i,jd); var a=Position(i,jd-.005);var b=Position(i,jd+.005);result.Add(new(199+100*i,Names[i],jd,p.X,p.Y,p.Z,(b.X-a.X)/.01,(b.Y-a.Y)/.01,(b.Z-a.Z)/.01)); }
        return result;
    }
}

public static class SolarArtwork {
    private static bool _enabled;
    private static readonly Dictionary<string,Noesis.BitmapImage> Images=new(StringComparer.Ordinal);
    public static void Initialize() => _enabled=true;
    public static void Clear() { Images.Clear(); _enabled=false; }
    public static string Status {
        get {
            try {
                using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Ui","Solar","provenance.json")));
                bool measured=doc.RootElement.GetProperty("milkyway").GetString()?.StartsWith("https://svs.gsfc.nasa.gov/",StringComparison.Ordinal)==true;
                return measured ? "NASA SVS sky · illustrative planet lighting · reference orbits" : "Illustrative sky and globes · download source maps for observed artwork";
            } catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException) {
                return "Artwork provenance unavailable · reference orbits";
            }
        }
    }
    public static Noesis.BitmapImage? Load(string file) {
        if(!_enabled || !File.Exists(Path.Combine(AppContext.BaseDirectory,"Ui","Solar",file)))return null;
        if(!Images.TryGetValue(file,out var image))Images.Add(file,image=new Noesis.BitmapImage(new Uri("Solar/"+file,UriKind.Relative)));
        return image;
    }
}


/// <summary>Bounded interpolation of geometric JPL positions and AU/day velocities.
/// Outside the validated cache this class refuses to extrapolate.</summary>
public sealed class HorizonsSeries {
    private readonly PlanetState[][] _frames;
    private readonly double[] _epochs;
    public double Start=>_epochs[0];
    public double End=>_epochs[^1];
    private HorizonsSeries(PlanetState[][] frames) {
        _frames=frames; _epochs=frames.Select(f=>f[0].JulianDateTdb).ToArray();
    }
    public static HorizonsSeries Load(string path) {
        var frames=SolarSystemSnapshot.ReadRows(path).GroupBy(s=>s.JulianDateTdb).OrderBy(g=>g.Key)
            .Select(g=>g.OrderBy(s=>s.Id).ToArray()).ToArray();
        if(frames.Length<2||frames.Length>3651||frames.Any(f=>f.Length!=8||f.Select(s=>s.Id).Distinct().Count()!=8))
            throw new InvalidDataException("Horizons series requires all eight bodies at each epoch.");
        for(int i=1;i<frames.Length;i++) {
            double h=frames[i][0].JulianDateTdb-frames[i-1][0].JulianDateTdb;
            if(!(h>0&&h<=7.000001))throw new InvalidDataException("Horizons series spacing exceeds seven days.");
        }
        return new(frames);
    }
    public bool TryAt(double jd,out IReadOnlyList<PlanetState> states) {
        states=[];
        if(!double.IsFinite(jd)||jd<Start||jd>End)return false;
        int exact=Array.BinarySearch(_epochs,jd);
        if(exact>=0){states=_frames[exact];return true;}
        int hi=~exact,lo=hi-1;
        double h=_epochs[hi]-_epochs[lo],u=(jd-_epochs[lo])/h;
        var values=new PlanetState[8];
        for(int i=0;i<8;i++)values[i]=Interpolate(_frames[lo][i],_frames[hi][i],jd,u,h);
        states=values; return true;
    }
    internal static PlanetState Interpolate(PlanetState a,PlanetState b,double jd,double u,double h) {
        double u2=u*u,u3=u2*u;
        (double p,double v) axis(double p0,double v0,double p1,double v1) => (
            (2*u3-3*u2+1)*p0+(u3-2*u2+u)*h*v0+(-2*u3+3*u2)*p1+(u3-u2)*h*v1,
            ((6*u2-6*u)*p0+(-6*u2+6*u)*p1)/h+(3*u2-4*u+1)*v0+(3*u2-2*u)*v1);
        var x=axis(a.X,a.Vx,b.X,b.Vx);var y=axis(a.Y,a.Vy,b.Y,b.Vy);var z=axis(a.Z,a.Vz,b.Z,b.Vz);
        return new(a.Id,a.Name,jd,x.p,y.p,z.p,x.v,y.v,z.v);
    }
}
