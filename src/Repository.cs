using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;

namespace ownzone
{
    // Common interface for zones.
    public interface IZone
    {   
        string Name { get; }

        // Tell if this Zone contains the given location.
        bool Contains(ILocation location);

        // Get the distance (meters) of the given location to the Zone center.
        double Distance(ILocation location);
    }

    // Holds zone definitions.
    public interface IRepository
    {
        Task<IEnumerable<IZone>> GetZonesAsync(string name);
    }

    class AccountReadException: Exception
    {
        public AccountReadException(string message)
            : base(message)
        {
        }
    }

    public class Repository : IRepository
    {
        private readonly ILogger<Repository> log;

        public string BaseDirectory { get; set; }

        public Repository(ILoggerFactory loggerFactory,
            IConfiguration config)
        {
            log = loggerFactory.CreateLogger<Repository>();

            config.GetSection("Repository").Bind(this);

            log.LogInformation("Init Repository, basedir is {0}.",
                BaseDirectory);
        }

        public async Task<IEnumerable<IZone>> GetZonesAsync(string name)
        {
            var account = await readAccountAsync(name);
            return account.GetZones();
        }

        private async Task<Account> readAccountAsync(string name)
        {
            var path = Path.Combine(BaseDirectory, name + ".json");
            log.LogInformation("Read account {0} from {1}.", name, path);

            var jsonString = "";
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                jsonString = await reader.ReadToEndAsync();
            }
            var account = JsonConvert.DeserializeObject<Account>(jsonString);

            return account;
        }
    }

    public class Account : FeatureCollection
    {

        public IEnumerable<IZone> GetZones()
        {
            var result = new List<IZone>();
            foreach (var feature in Features)
            {
                var kind = feature.Geometry.Type;
                if (kind == GeoJSONObjectType.Point)
                {
                    result.Add(new PointAdapter(feature));
                }
                else if (kind == GeoJSONObjectType.LineString)
                {
                    result.Add(new LineStringAdapter(feature));
                }
            }
            return result;
        }
    }

    abstract class ZoneAdapter : IZone
    {
        protected readonly Feature Feature;

        public ZoneAdapter(Feature ft)
        {
            Feature = ft;
        }

        public string Name
        {
            get
            {
                return Feature.Id;
            }
        }

        public abstract bool Contains(ILocation location);

        public abstract double Distance(ILocation location);

        protected ILocation asLocation(IPosition pos)
        {
            return new Location()
            {
                Lat = pos.Latitude,
                Lon = pos.Longitude
            };
        }
    }

    class PointAdapter : ZoneAdapter
    {
        public PointAdapter(Feature ft) : base(ft)
        {
        }

        private double radius
        {
            get
            {
                // NullReferenceException
                // KeyNotFoundException
                double value;
                object raw = Feature.Properties["radius"];
                try
                {
                    value = (double)raw;
                }
                catch (InvalidCastException)
                {
                    // may again throw InvalidCastException
                    var l = (long)raw;
                    value = Convert.ToDouble(l);
                }
                return value;
            }
        }

        public override bool Contains(ILocation location)
        {
            return Distance(location) <= radius;
        }

        public override double Distance(ILocation location)
        {
            var p = (Point)Feature.Geometry;
            return Geo.Distance(location, asLocation(p.Coordinates));
        }

    }

    class LineStringAdapter : ZoneAdapter
    {
        public LineStringAdapter(Feature ft) : base(ft)
        {
        }

        private double padding
        {
            get
            {
                // NullReferenceException
                // KeyNotFoundException
                double value;
                object raw = Feature.Properties["padding"];
                try
                {
                    value = (double)raw;
                }
                catch (InvalidCastException)
                {
                    // may again throw InvalidCastException
                    var l = (long)raw;
                    value = Convert.ToDouble(l);
                }
                return value;
            }
        }

        public override bool Contains(ILocation location)
        {
            return Distance(location) <= padding;
        }

        public override double Distance(ILocation location)
        {
            var l = (LineString)Feature.Geometry;
            var path = new List<ILocation>();
            foreach (var coordinate in l.Coordinates)
            {
                path.Add(asLocation(coordinate));
            }
            return Geo.DistanceToPath(location, path);
        }

    }

    class Location : ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }
    }
}
