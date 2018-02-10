using System;
using System.Collections.Generic;

namespace Ownzone
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

        public static double DistanceToPath(ILocation location, IEnumerable<ILocation> path)
        {
            //TODO: make sure that the path contains at least 2 points?

            ILocation prev = null;
            var min = -1.0;
            foreach (var current in path)
            {
                if (prev != null)
                {
                    var d = Math.Abs(crossTrackDistance(prev, current, location));
                    if (min < 0 || d < min)
                    {
                        min = d;
                    }
                }

                prev = current;
            }

            return min;
        }

        // Shortest distance of a point (location) to a path (start-end).
        private static double crossTrackDistance(ILocation start, ILocation end, ILocation location)
        {
            // see:
            // http://www.movable-type.co.uk/scripts/latlong.html

            var startDistance = Distance(start, location);
            var startBearing = bearing(start, location);
            var pathBearing = bearing(start, end);

            var a = Math.Sin(startDistance / EARTH_RADIUS);
            var b = Math.Sin(startBearing - pathBearing);
            var c = a * b;

            return Math.Asin(c) * EARTH_RADIUS;

        }

        // Bearing from a start point towards an end point in *degrees*.
        private static double bearing(ILocation start, ILocation end)
        {
            // see:
            // http://www.movable-type.co.uk/scripts/latlong.html
            var lat0 = rad(start.Lat);
            var lat1 = rad(end.Lat);
            var dLon = rad(start.Lon) - rad(end.Lon);

            var a = Math.Sin(dLon) * Math.Cos(lat1);
            var b0 = Math.Cos(lat0) * Math.Sin(lat1);
            var b1 = Math.Sin(lat0) * Math.Cos(lat1) * Math.Cos(dLon);
            var b = b0 - b1;

            var bearingInRadians = Math.Atan2(a, b);
            return deg(bearingInRadians);
        }

        private static double rad(double angleInDegrees)
        {
            return Math.PI * angleInDegrees / 180.0;
        }

        private static double deg(double angleInRadians)
        {
            return angleInRadians * 180.0 / Math.PI;
        }
    }
    
}
