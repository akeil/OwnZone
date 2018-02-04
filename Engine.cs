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

        public Engine(ILoggerFactory loggerFactory, IMqttService mqtt,
            IZoneRepository zoneRepository)
        {
            log = loggerFactory.CreateLogger<Engine>();
            service = mqtt;
            zoneRepo = zoneRepository;
            subscriptions = new List<Subscription>();
        }

        public event EventHandler<LocationUpdatedEventArgs> LocationUpdated;

        public void Run()
        {
            log.LogDebug("Engine start");
            service.Connect();
            service.MessageReceived += messageReceived;
            readSubs();

            // listen to our own events
            LocationUpdated += locationUpdated;
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

        // location Update Events ----------------------------------------------

        private void locationUpdated(object sender, LocationUpdatedEventArgs evt)
        {
            log.LogDebug("Got location update for {0}", evt.Name);

        }


        // Subscriptions -------------------------------------------------------

        private void readSubs()
        {
            var section = Program.Configuration.GetSection("Subscriptions");
            var subs = section.Get<Subscription[]>();
            foreach (var s in subs)
            {
                s.Setup(log, service, zoneRepo);
                service.AddSubscription(s);
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
        private IMqttService service;

        private IZoneRepository zoneRepo;

        private ILogger<Engine> log;

        public string Name { get; set; }

        public string Topic { get; set; }

        public string OutTopic { get; set; }

        public string ZonePath { get; set; }

        public Subscription()
        {

        }

        public void Setup(ILogger<Engine> logger, IMqttService mqttService,
            IZoneRepository zoneRepository)
        {
            log = logger;
            service = mqttService;
            zoneRepo = zoneRepository;
            log.LogDebug("Setup subscription {0}", Name);
        }

        // Tell if the given MQTT topic matches this subscription
        public bool TopicMatches(string candidate)
        {
            return candidate == Topic;
        }

        // Handle a location update
        public void HandleLocationUpdate(LocationUpdatedEventArgs update)
        {
            log.LogInformation("Location update for {0}", Name);

            var zones = zoneRepo.GetZones(Name);

            // list all zones that contain the current location
            var matches = new List<(double, IZone)>();
            foreach (var zone in zones)
            {
                var match = zone.Match(update);
                if (match.contains)
                {
                    matches.Add((match.distance, zone));
                    if (!zone.Active)
                    {
                        zone.Active = true;
                        publishZoneStatus(zone.Name, "in");
                    }
                    
                }
                else
                {
                    if (zone.Active)
                    {
                        zone.Active = false;
                        publishZoneStatus(zone.Name, "out");
                    }
                }
            }

            matches.Sort(byRelevance);

            publishZones(matches);
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

        // publish the top zone and the list of matches
        private void publishZones(List<(double, IZone)> matches)
        {
            var bestName = "";
            var allNames = "";
            if (matches.Count != 0)
            {
                bestName = matches[0].Item2.Name;

                var namelist = matches.ConvertAll(m => m.Item2.Name);
                allNames = String.Join("\n", namelist);
            }

            var bestTopic = OutTopic + "/current";
            service.Publish(bestTopic, bestName);

            var allTopic = OutTopic + "/list";
            service.Publish(allTopic, allNames);
        }

        // publish the status ("in" or "out") for a zone
        private void publishZoneStatus(string name, string status)
        {
            var topic = OutTopic + "/at/" + name;
            service.Publish(topic, status);
        }
    }
}