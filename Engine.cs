using System;
using System.Collections.Generic;
using Newtonsoft.Json;
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

        private readonly IMqttService mqtt;

        private readonly IZoneRepository zones;

        private readonly IStateRegistry states;

        private List<Subscription> subscriptions;

        public Engine(ILoggerFactory loggerFactory, IMqttService mqttService,
            IZoneRepository zoneRepository, IStateRegistry stateRegistry)
        {
            log = loggerFactory.CreateLogger<Engine>();
            mqtt = mqttService;
            zones = zoneRepository;
            states = stateRegistry;
            subscriptions = new List<Subscription>();
        }

        public event EventHandler<LocationUpdatedEventArgs> LocationUpdated;

        public void Run()
        {
            mqtt.MessageReceived += messageReceived;
            mqtt.Connect();
            readSubs();

            // register event handler
            this.LocationUpdated += locationUpdated;
            states.ZoneStatusChanged += zoneStatusChanged;
            states.CurrentZoneChanged += currentZoneChanged;

            log.LogInformation("Engine started.");
        }

        // Raw Messages --------------------------------------------------------

        private void messageReceived(object sender, MessageReceivedEventArgs evt)
        {
            log.LogDebug("Handle message for {0}.", evt.Topic);
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
            log.LogDebug("Dispatch location update for {0}.", args.Name);
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
            log.LogDebug("Handle location update for {0}.", evt.Name);

            var zonelist = zones.GetZones(evt.Name);

            // check all zones against the updated location
            // and compose a list of zones where we are "in"
            var matches = new List<(double, IZone)>();
            foreach (var zone in zonelist)
            {
                var match = zone.Match(evt);
                states.UpdateZoneStatus(evt.Name, zone.Name, match.contains);
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
            states.UpdateCurrentZone(evt.Name, currentZoneName);
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

        // Zone change events --------------------------------------------------

        private void currentZoneChanged(object sender, CurrentZoneChangedEventArgs evt)
        {
            var topic = String.Format("ownzone/{0}/current", evt.SubName);
            var message = evt.ZoneName;
            mqtt.Publish(topic, message);
        }

        private void zoneStatusChanged(object sender, ZoneStatusChangedEventArgs evt)
        {
            var topic = String.Format("ownzone/{0}/status/{1}",
                evt.SubName, evt.ZoneName);
            var message = evt.Status ? "in" : "out";
            mqtt.Publish(topic, message);
        }

        // Subscriptions -------------------------------------------------------

        private void readSubs()
        {
            var section = Program.Configuration.GetSection("Subscriptions");
            var subs = section.Get<Subscription[]>();
            foreach (var s in subs)
            {
                mqtt.Subscribe(s.Topic);
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