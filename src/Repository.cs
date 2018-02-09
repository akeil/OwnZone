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

    // Holds zone definitions
    public interface IRepository
    {
        // List all account names.
        IEnumerable<string> GetAccountNames();

        // Get an Account by name.
        Account GetAccount(string name);

        // Get the list of zones associated to the given account name.
        IEnumerable<IZone> GetZones(string account);

        Task<IEnumerable<string>> GetAccountNamesAsync();

        Task<Account> GetAccountAsync(string name);

        Task<IEnumerable<IZone>> GetZonesAsync(string name);
    }

    // Configuration settings for the Zone Repository.
    class RepoSettings
    {
        public string BaseDirectory { get; set; }
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

        private readonly RepoSettings settings;

        private readonly Dictionary<string, Account> accounts;

        private bool accountsRead;

        public Repository(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<Repository>();
            settings = new RepoSettings();

            var config = Program.Configuration.GetSection("Repository");
            config.Bind(settings);

            log.LogInformation("Init Repository, basedir is {0}.",
                settings.BaseDirectory);

            accounts = new Dictionary<string, Account>();
            accountsRead = false;
        }

        public IEnumerable<string> GetAccountNames()
        {
            var task = GetAccountNamesAsync();
            task.Wait();
            return task.Result;
        }

        public async Task<IEnumerable<string>> GetAccountNamesAsync()
        {
            await lazyReadAccountsAsync();
            return accounts.Keys;
        }

        public Account GetAccount(string name)
        {
            var task = GetAccountAsync(name);
            task.Wait();
            return task.Result;
        }

        public async Task<Account> GetAccountAsync(string name)
        {
            await lazyReadAccountsAsync();
            return accounts[name];
        }

        public IEnumerable<IZone> GetZones(string name)
        {
            var task = GetZonesAsync(name);
            task.Wait();
            return task.Result;
        }

        public async Task<IEnumerable<IZone>> GetZonesAsync(string name)
        {
            await lazyReadAccountsAsync();
            var account = await GetAccountAsync(name);
            return account.GetZones();
        }

        private async Task lazyReadAccountsAsync()
        {
            if (!accountsRead)
            {
                try
                {
                    await readAccountsAsync();
                }
                catch(Exception ex)
                {
                    log.LogError(ex, "Error reading account");
                }
                accountsRead = true;
            }
        }

        private async Task readAccountsAsync()
        {
            var path = settings.BaseDirectory;
            foreach (var filename in Directory.EnumerateFiles(path, "*.json"))
            {
                try
                {
                    var account = await readAccountAsync(filename);
                    accounts[account.Name] = account;
                }
                catch (AccountReadException ex)
                {
                    // skip invalid account definitions
                    log.LogError(ex, "Failed to load account from {0}.",
                        filename);
                }
            }
        }

        private async Task<Account> readAccountAsync(string path)
        {
            log.LogInformation("Read account info from {0}.", path);

            var json = "";
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync();
            }
            var account = JsonConvert.DeserializeObject<Account>(json);

            var name = Path.GetFileNameWithoutExtension(path);
            account.Name = name;

            return account;
        }
    }

    public class Account : FeatureCollection
    {
        public string Name { get; set; }

        [JsonProperty(PropertyName="topic", Required = Required.Always)]
        public string Topic { get; set; }

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
        protected readonly Feature feature;

        public ZoneAdapter(Feature ft)
        {
            feature = ft;
        }

        public string Name
        {
            get
            {
                // InvalidCastException
                // NullReferenceException
                // KeyNotFoundException
                return (string)feature.Properties["name"];
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
                object raw = feature.Properties["radius"];
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
            var p = (Point)feature.Geometry;
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
                object raw = feature.Properties["padding"];
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
            var l = (LineString)feature.Geometry;
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
