using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ownzone
{
    // Common interface for all zones.
    public interface IZone
    {   
        string Name { get; set; }

        bool Active { get; set; }

        (bool contains, double distance) Match(ILocation loc);
    }

    // Holds zone definitions
    public interface IZoneRepository
    {
        // Get the list of zones associated to the given name.
        List<IZone> GetZones(string name);
    }

    // Configuration settings for the Zone Repository
    class ZoneRepoSettings
    {
        public string BaseDirectory { get; set; }
    }

    public class ZoneRepository : IZoneRepository
    {
        private readonly ILogger<ZoneRepository> log;

        private readonly ZoneRepoSettings settings;

        public ZoneRepository(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<ZoneRepository>();
            settings = new ZoneRepoSettings();

            var config = Program.Configuration.GetSection("ZoneRepository");
            config.Bind(settings);

            log.LogInformation("Init ZoneRepository, basedir is {0}.",
                settings.BaseDirectory);
        }

        public List<IZone> GetZones(string name)
        {
            var path = zoneFilePath(name);
            log.LogDebug("Read zones for {0} from {1}.", name, path);
            return readZoneFile(path);
        }

        private string zoneFilePath(string name)
        {
            return Path.Combine(settings.BaseDirectory, name + ".zones.json");
        }

        // Read the list of Zones from the JSON file at ZonePath.
        private static List<IZone> readZoneFile(string path)
        {
            var result = new List<IZone>();

            // Expect a JSON array like this:
            // [
            //   {
            //     "Kind": "Point",
            //     <other properties>
            //   },
            //   {...}
            // ]
            // 
            // Depending on the Kind, a different concrete class needs
            // to be instantiated:
            // - Point
            // - Box
            using (StreamReader f = new StreamReader(path, Encoding.UTF8))
            using(JsonTextReader reader = new JsonTextReader(f))
            {
                var arr = JToken.ReadFrom(reader);
                if (arr.Type != JTokenType.Array)
                {
                    throw new Exception("Unexpected Type, not an array.");
                }

                foreach (var child in arr.Children())
                {
                    if (child.Type != JTokenType.Object)
                    {
                        throw new Exception("Unexpected type, not an object.");
                    }

                    JObject obj = (JObject) child;
                    var kind = obj.Value<string>("Kind");
                    if (kind == "Point")
                    {
                        result.Add(obj.ToObject<Point>());
                    }
                    else if (kind == "Bounds")
                    {
                        result.Add(obj.ToObject<Bounds>());
                    }
                    else
                    {
                        throw new Exception("Unsupported Zone kind.");
                    }
                }
            }

            return result;
        }
    }

    // Zone Types --------------------------------------------------------------

    // Zone defined by a single coordinate pair and a radius.
    class Point : IZone, ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        public int Radius { get; set; }

        public string Name { get; set; }

        public bool Active { get; set; }

        public (bool contains, double distance) Match(ILocation loc)
        {
            var distance = Geo.Distance(loc, this);
            var contains = distance < Radius;
            return (contains, distance);
        }
    }

    // Rectangular zone defined by two lat/lon pairs.
    class Bounds : IZone
    {
        public double MinLat { get; set; }

        public double MinLon { get; set; }

        public double MaxLat { get; set; }

        public double MaxLon { get; set; }

        public string Name { get; set; }

        public bool Active { get; set; }

        public (bool contains, double distance) Match(ILocation loc)
        {
            var inLat = loc.Lat <= MaxLat && loc.Lat >= MinLat;
            var inLon = loc.Lon <= MaxLon && loc.Lon >= MinLon;

            var center = new Point();
            center.Lat = MinLat + (MaxLat - MinLat) / 2;
            center.Lon = MinLon + (MaxLon - MinLon) / 2;
            var distance = Geo.Distance(loc, center);

            return (inLat && inLon, distance);
        }
    }
}