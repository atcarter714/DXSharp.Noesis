using System.Globalization;

namespace OrbitalCommand;

public sealed record PlanetState( int Id, string Name, double JulianDateTdb, double X, double Y, double Z,
                                  double Vx, double Vy, double Vz );

/// <summary>Geometric heliocentric J2000 ecliptic state vectors, stored in AU and AU/day.</summary>
public static class SolarSystemSnapshot {
    private static readonly (int Id, string Name)[] Expected = [
        (199, "Mercury"), (299, "Venus"), (399, "Earth"), (499, "Mars"),
        (599, "Jupiter"), (699, "Saturn"), (799, "Uranus"), (899, "Neptune") ];
    public static IReadOnlyList<PlanetState> Load( string path ) {
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
        if ( states.Count != Expected.Length || states.Select( s => s.Id ).Distinct( ).Count( ) != Expected.Length ||
             states.Select( s => s.JulianDateTdb ).Distinct( ).Count( ) != 1 )
            throw new InvalidDataException( "Horizons cache must contain all eight planets at one epoch." );
        return states.OrderBy( s => s.Id ).ToArray( );
    }
}

public sealed record PlanetMarker( string Name, float Left, float Top, string Distance );
