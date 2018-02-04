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
    }

    // Configuration settings for the Zone Repository.
    class RepoSettings
    {
        public string BaseDirectory { get; set; }
    }

    public class Repository : IRepository
    {
        private readonly ILogger<Repository> log;

        private readonly RepoSettings settings;

        private readonly Dictionary<string, Account> accounts;

        public Repository(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<Repository>();
            settings = new RepoSettings();

            var config = Program.Configuration.GetSection("Repository");
            config.Bind(settings);

            log.LogInformation("Init Repository, basedir is {0}.",
                settings.BaseDirectory);

            accounts = new Dictionary<string, Account>();
            readAccounts();
        }

        public IEnumerable<string> GetAccountNames()
        {
            return accounts.Keys;
        }

        public Account GetAccount(string name)
        {
            return accounts[name];
        }

        public IEnumerable<IZone> GetZones(string name)
        {
            var account = GetAccount(name);
            return account.Zones;
        }

        private void readAccounts()
        {
            var path = settings.BaseDirectory;
            foreach (var filename in Directory.EnumerateFiles(path, "*.json"))
            {
                var account = readAccount(filename);
                accounts[account.Name] = account;
            }
        }

        private Account readAccount(string path)
        {
            log.LogInformation("Read account from {0}.", path);

            JObject root = null;
            using (StreamReader f = new StreamReader(path, Encoding.UTF8))
            using(JsonTextReader reader = new JsonTextReader(f))
            {
                root = JObject.Load(reader);
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var topic = root.Value<string>("Topic");
            // TODO: make sure name and topic are set
            var account = new Account(){ Name=name, Topic=topic};

            // TODO: make sure "Zones" is an object
            var zones = (JObject)root["Zones"];
            foreach (JProperty prop in zones.Properties())
            {
                var zone = readZone(prop);
                account.Zones.Add(zone);
                log.LogDebug("add zone {0} to {1}", zone.Name, account.Name);
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
            else
            {
                throw new Exception("Unsupported Zone kind.");
            }

            zone.Name = prop.Name;
            return zone;
        }
    }

    public class Account
    {
        public List<IZone> Zones;

        public string Name { get; set; }

        public string Topic { get; set; }

        public Account()
        {
            Zones = new List<IZone>();
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
    }
}