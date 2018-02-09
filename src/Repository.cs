using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GeoJSON.Net.Feature;

namespace ownzone
{
    // Common interface for all zones.
    public interface IZone
    {   
        string Name { get; }

        // Check internal state and raise InvalidZoneException if invalid.
        void Validate();

        // Tell if this Zone contains the given location
        // and how far the location is from the Zone's center.
        (bool contains, double distance) Match(ILocation loc);
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

    class InvalidZoneException: Exception
    {
        public InvalidZoneException(string message)
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
            return account.Zones;
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

        private async Task<Account> xreadAccountAsync(string path)
        {
            log.LogInformation("Read account from {0}.", path);

            JObject root = null;
            using (StreamReader f = new StreamReader(path, Encoding.UTF8))
            using(JsonTextReader reader = new JsonTextReader(f))
            {
                root = await JObject.LoadAsync(reader);
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var topic = root.Value<string>("Topic");
            if(String.IsNullOrEmpty(topic))
            {
                var msg = String.Format("Missing topic in account file {0}.",
                    path);
                throw new AccountReadException(msg);
            }
            var account = new Account(){ Name=name, Topic=topic};

            var zonesToken = root["Zones"];
            if(zonesToken == null || zonesToken.Type != JTokenType.Object)
            {
                throw new AccountReadException("Invalid zone definition."
                    + " \"Zones\" is not a dictionary.");
            }

            var zones = (JObject)zonesToken;
            foreach (JProperty prop in zones.Properties())
            {
                try
                {
                    var zone = readZone(prop);
                    account.Zones.Add(zone);
                    log.LogDebug("Add zone {0} to {1}", zone.Name, account.Name);
                }
                catch (InvalidZoneException ex)
                {
                    log.LogError(ex, "Skip invalid zone for {0}", account.Name);
                }
            }

            return account;
        }

        private IZone readZone(JProperty prop)
        {
            var data = prop.Value;
            var kind = data.Value<string>("Kind");

            IZone zone = null;
            if (kind == "Point")
            {
                zone = data.ToObject<Point>();
            }
            else if (kind == "Bounds")
            {
                zone = data.ToObject<Bounds>();
            }
            else if (kind == "Path")
            {
                zone = data.ToObject<PaddedPath>();
            }
            else
            {
                var msg = String.Format("Unsupported Zone kind {0}.", kind);
                throw new InvalidZoneException(msg);
            }

            //zone.Name = prop.Name;
            zone.Validate();
            return zone;
        }

        private async Task<Account> readAccountAsync(string path)
        {
            log.LogInformation("Read account info from {0}.", path);

            var name = Path.GetFileNameWithoutExtension(path);
            log.LogInformation("Account name is {0}.", name);

            var json = "";
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync();
            }
            var account = JsonConvert.DeserializeObject<Account>(json);

            log.LogInformation("Topic is {0}.", account.Topic);
            log.LogInformation("Features: {0}", account.Features);

            account.Foo();

            account.Name = name;
            return account;
        }
    }

    public class Account : FeatureCollection
    {
        public List<IZone> Zones;

        public string Name { get; set; }

        [JsonProperty(PropertyName="topic", Required = Required.Always)]
        public string Topic { get; set; }

        //[JsonProperty(PropertyName="features", Required = Required.Always)]
        //public FeatureCollection Features { get; set; }

        public Account()
        {
            Zones = new List<IZone>();
        }

        public void Foo()
        {
            foreach (var feature in Features)
            {
                var wrapper = new ZoneWrapper(feature);
                Console.WriteLine("Feature name {0}", wrapper.Name);
            }
        }

    }

    class ZoneWrapper : IZone
    {
        private readonly Feature feature;

        public ZoneWrapper(Feature ft)
        {
            feature = ft;
        }

        public string Name
        {
            get
            {
                var name = feature.Properties["name"];
                if (name != null)
                {
                    return name.ToString();
                }
                else
                {
                    return "";
                }
            }
        }

        public (bool contains, double distance) Match(ILocation loc)
        {
            return (false, 0.0);
        }

        public void Validate()
        {

        }

        public bool Contains(ILocation location)
        {
            return false;
        }

        public double Distance(ILocation location)
        {
            return 0.0;
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

        public (bool contains, double distance) Match(ILocation loc)
        {
            var distance = Geo.Distance(loc, this);
            var contains = distance < Radius;
            return (contains, distance);
        }

        public void Validate()
        {
            if (Lat == 0 || Lon == 0)
            {
                throw new InvalidZoneException(String.Format(
                    "Invalid Lat/Lon {0},{1}", Lat, Lon));
            }

            if (Radius <= 0)
            {
                throw new InvalidZoneException(String.Format(
                    "Invalid radius {0}", Radius));
            }
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

        public void Validate()
        {
            if (MinLat == 0 || MinLon == 0)
            {
                throw new InvalidZoneException(String.Format(
                    "Invalid MinLat/MinLon {0},{1}", MinLat, MinLon));
            }

            if (MaxLat == 0 || MaxLon == 0)
            {
                throw new InvalidZoneException(String.Format(
                    "Invalid MaxLat/MaxLon {0},{1}", MaxLat, MaxLon));
            }
        }
    }

    class Location : ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }
    }

    class PaddedPath : IZone
    {
        public List<Location> Points { get; set; }

        public string Name { get; set; }

        public int Padding { get; set; }

        public (bool contains, double distance) Match(ILocation loc)
        {
            var distance = Geo.DistanceToPath(loc, Points);
            return (distance <= Padding, distance);
        }

        public void Validate()
        {
            if (Points == null || Points.Count < 2)
            {
                throw new InvalidZoneException(
                    "A path must have at least two points.");
            }

            if (Padding <= 0)
            {
                throw new InvalidZoneException(String.Format(
                    "Invalid padding {0}", Padding));
            }
        }
    }
}
