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
    public interface IEngine
    {
        void Run();
    }

    public class Engine : IEngine
    {
        private readonly ILogger<Engine> log;

        private readonly IMqttService service;

        private readonly IZoneRepository zoneRepo;

        private List<Subscription> subscriptions;

        private Dictionary<string, string> currentZone;

        private Dictionary<string, bool> zoneStatus;

        public Engine(ILoggerFactory loggerFactory, IMqttService mqtt,
            IZoneRepository zoneRepository)
        {
            log = loggerFactory.CreateLogger<Engine>();
            service = mqtt;
            zoneRepo = zoneRepository;
            subscriptions = new List<Subscription>();
            currentZone = new Dictionary<string, string>();
            zoneStatus = new Dictionary<string, bool>();
        }

        public event EventHandler<LocationUpdatedEventArgs> LocationUpdated;

        public event EventHandler<CurrentZoneChangedEventArgs> CurrentZoneChanged;

        public event EventHandler<ZoneStatusChangedEventArgs> ZoneStatusChanged;

        public void Run()
        {
            log.LogDebug("Engine start");
            service.Connect();
            service.MessageReceived += messageReceived;
            readSubs();

            // listen to our own events
            LocationUpdated += locationUpdated;
            ZoneStatusChanged += zoneStatusChanged;
            CurrentZoneChanged += currentZoneChanged;
        }

        // Raw Messages --------------------------------------------------------

        private void messageReceived(object sender, MessageReceivedEventArgs evt)
        {
            log.LogDebug("Got message for {0}", evt.Topic);
            // TODO: throws
            var baseargs = convertOwnTracksMessage(evt.Message);

            // dispatch LocationUpdated events for each affected subscription
            var names = lookupSubscriptionNames(evt.Topic);
            foreach (var name in names)
            {
                var args = baseargs.CopyForName(name);
                OnLocationUpdated(args);
            }
        }

        protected virtual void OnLocationUpdated(LocationUpdatedEventArgs args)
        {
            log.LogDebug("Dispatch location update for {0}", args.Name);
            var handler = LocationUpdated;
            if (handler != null) {
                handler(this, args);
            }
        }

        private LocationUpdatedEventArgs convertOwnTracksMessage(string jsonString)
        {
            try
            {
                var raw = JsonConvert.DeserializeObject<OwnTracksMessage>(jsonString);
                if (!raw.IsValid())
                {
                    throw new Exception("Invalid Message");
                }
                return raw.ToLocationUpdate();
            }
            catch (JsonReaderException)
            {
                log.LogWarning("Failed to parse JSON from message body");
                throw new Exception("Invalid Message");
            }
        }

        // find the subscriptions that are interested in the given topic.
        private List<string> lookupSubscriptionNames(string topic)
        {
            var result = new List<string>();

            foreach (var sub in subscriptions)
            {
                if (sub.Topic == topic)
                {
                    result.Add(sub.Name);
                }
            }

            return result;
        }

        // Location Update Events ----------------------------------------------

        private void locationUpdated(object sender, LocationUpdatedEventArgs evt)
        {
            log.LogDebug("Got location update for {0}", evt.Name);

            var zones = zoneRepo.GetZones(evt.Name);

            // check all zones against the updated location
            // and compose a list of zones where we are "in"
            var matches = new List<(double, IZone)>();
            foreach (var zone in zones)
            {
                var match = zone.Match(evt);
                updateZoneStatus(evt.Name, zone.Name, match.contains);
                if (match.contains)
                {
                    matches.Add((match.distance, zone));
                }
            }

            // find the best match
            var currentZoneName = "";
            if (matches.Count != 0)
            {
                matches.Sort(byRelevance);
                currentZoneName = matches[0].Item2.Name;
            }
            updateCurrentZone(evt.Name, currentZoneName);
        }

        // delegate to sort a list of matches by relevance.
        private static int byRelevance((double, IZone) a, (double, IZone) b)
        {
            if (a.Item1 > b.Item1)
            {
                return 1;
            } else if (a.Item1 < b.Item1) {
                return -1;
            } else {
                return 0;
            }
        }

        private void updateZoneStatus(string subName, string zoneName, bool status)
        {
            var key = subName + "." + zoneName;
            var changed = false;
            try
            {
                changed = status != zoneStatus[key];
            }
            catch (KeyNotFoundException)
            {
                changed = true;
            }

            zoneStatus[key] = status;

            if (changed)
            {
                var args = new ZoneStatusChangedEventArgs()
                {
                    SubName=subName,
                    ZoneName=zoneName,
                    Status=status
                };
                OnZoneStatusChanged(args);
            }
        }

        protected virtual void OnZoneStatusChanged(ZoneStatusChangedEventArgs args)
        {
            log.LogDebug("Dispatch status change for {0}.{1} to {2}",
                args.SubName, args.ZoneName, args.Status);
            var handler = ZoneStatusChanged;
            if (handler != null) {
                handler(this, args);
            }
        }

        private void updateCurrentZone(string subName, string zoneName)
        {
            if (String.IsNullOrEmpty(zoneName))
            {
                zoneName = null;
            }

            var changed = false;
            try
            {
                changed = zoneName != currentZone[subName];
            }
            catch (KeyNotFoundException)
            {
                changed = true;
            }

            currentZone[subName] = zoneName;

            if (changed)
            {
                var args = new CurrentZoneChangedEventArgs()
                {
                    SubName = subName,
                    ZoneName = zoneName
                };
                OnCurrentZoneChanged(args);
            }
        }

        protected virtual void OnCurrentZoneChanged(CurrentZoneChangedEventArgs args)
        {
            log.LogDebug("Dispatch zone change for {0} to {1}",
                args.SubName, args.ZoneName);
            var handler = CurrentZoneChanged;
            if (handler != null) {
                handler(this, args);
            }
        }

        // Zone change events --------------------------------------------------

        private void currentZoneChanged(object sender, CurrentZoneChangedEventArgs evt)
        {
            var topic = String.Format("ownzone/{0}/current", evt.SubName);
            var message = evt.ZoneName;
            service.Publish(topic, message);
        }

        private void zoneStatusChanged(object sender, ZoneStatusChangedEventArgs evt)
        {
            var topic = String.Format("ownzone/{0}/status/{1}",
                evt.SubName, evt.ZoneName);
            var message = evt.Status ? "in" : "out";
            service.Publish(topic, message);
        }

        // Subscriptions -------------------------------------------------------

        private void readSubs()
        {
            var section = Program.Configuration.GetSection("Subscriptions");
            var subs = section.Get<Subscription[]>();
            foreach (var s in subs)
            {
                service.Subscribe(s.Topic);
                subscriptions.Add(s);
            }
        }
    }

    // Message for a location update.
    public class LocationUpdatedEventArgs : EventArgs, ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        public string Name { get; set; }

        public LocationUpdatedEventArgs CopyForName(string name)
        {
            var copy = (LocationUpdatedEventArgs)MemberwiseClone();
            copy.Name = name;
            return copy;
        }
    }

    public class ZoneStatusChangedEventArgs : EventArgs
    {
        public string SubName { get; set; }

        public string ZoneName { get; set; }

        public bool Status { get; set; }
    }

    public class CurrentZoneChangedEventArgs : EventArgs
    {
        public string SubName { get; set; }

        public string ZoneName { get; set; }
    }

    // Deserialization helper for OwnTrack messages.
    // JSON Messages look like this:
    // {
    //   "_type":"location",
    //   "tid":"et",
    //   "acc":12,
    //   "batt":92,
    //   "conn":"w",
    //   "doze":false,
    //   "lat":50.9326135,
    //   "lon":6.9464344,
    //   "tst":1489529135
    // }
    class OwnTracksMessage
    {
        public string _type { get; set; }

        public double lat { get; set; }

        public double lon { get; set; }

        public int acc { get; set; }

        // Check if all required fields are set.
        //
        // Used after mapping the JSON object to make sure that the JSON message
        // did contain all of the expected fields.
        public bool IsValid()
        {
            return lat != 0 && lon != 0;
        }

        // convert to OwnZone message
        public LocationUpdatedEventArgs ToLocationUpdate(){
            return new LocationUpdatedEventArgs() {Lat=lat, Lon = lon};
        }
    }

    public class Subscription
    {
        public string Name { get; set; }

        public string Topic { get; set; }
    }
}