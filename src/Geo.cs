using System;

namespace ownzone
{

    public interface ILocation
    {
        double Lat { get; set; }

        double Lon { get; set; }
    }

    public class Geo
    {
        private const double EARTH_RADIUS = 6371.0 * 1000.0;

        // The distance between two locations in meters.
        public static double Distance(ILocation loc0, ILocation loc1)
        {
            // http://www.movable-type.co.uk/scripts/latlong.html
            // https://github.com/tkrajina/gpxpy/blob/master/gpxpy/geo.py

            var lat0 = loc0.Lat;
            var lon0 = loc0.Lon;
            var lat1 = loc1.Lat;
            var lon1 = loc1.Lon;

            var d_lat = rad(lat0 - lat1);
            var d_lon = rad(lon0 - lon1);

            var s0 = Math.Sin(d_lat / 2) * Math.Sin(d_lat / 2);
            var p0 = Math.Sin(d_lon / 2) * Math.Sin(d_lon / 2);
            var p1 = Math.Cos(rad(lat0)) * Math.Cos(rad(lat1));
            var s1 = p0 * p1;
            var a = s0 + s1;

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return Geo.EARTH_RADIUS * c;
        }

        private static double rad(double angle)
        {
            return Math.PI * angle / 180.0;
        }
    }
    
}