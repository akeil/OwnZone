using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        private readonly IRepository repo;

        private readonly IStateRegistry states;

        private readonly IFilterService filters;

        // OwnTracks MQTT base topic to subscribe to.
        public string TopicPrefixIn { get; set; }

        // MQTT base topic to publish to.
        public string TopicPrefixOut { get; set; }

        public Engine(ILoggerFactory loggerFactory,
            IConfiguration config,
            IMqttService mqttService,
            IRepository repository,
            IStateRegistry stateRegistry,
            IFilterService filterService)
        {
            log = loggerFactory.CreateLogger<Engine>();
            mqtt = mqttService;
            repo = repository;
            states = stateRegistry;
            filters = filterService;

            config.GetSection("Engine").Bind(this);
        }

        public event EventHandler<LocationUpdatedEventArgs> LocationUpdated;

        public void Run()
        {
            // register event handlers
            this.LocationUpdated += locationUpdated;
            states.ZoneStatusChanged += zoneStatusChanged;
            states.CurrentZoneChanged += currentZoneChanged;
            mqtt.MessageReceived += messageReceived;

            // subscriptions require completed connection
            mqtt.ConnectAsync().Wait();
            subscribe();

            log.LogInformation("Engine started.");
        }

        // Raw Messages --------------------------------------------------------

        // event handler for raw mqtt messages
        private async void messageReceived(object sender, MessageReceivedEventArgs evt)
        {
            log.LogDebug("Handle message for {0}.", evt.Topic);
            // TODO: throws
            var args = convertOwnTracksMessage(evt.Message);

            var userAndDevice = parseTopic(evt.Topic);
            args.Name = userAndDevice.Item1;
            args.Device = userAndDevice.Item2;

            OnLocationUpdated(args);
        }

        // Trigger a LocationUpdateEvent.
        protected virtual void OnLocationUpdated(LocationUpdatedEventArgs args)
        {
            if (filters.Accept(args))
            {
                log.LogDebug("Dispatch location update for {0}.", args.Name);
                var handler = LocationUpdated;
                if (handler != null) {
                    handler(this, args);
                }
            }
            else
            {
                log.LogDebug("Filtered location update for {0}.", args.Name);
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

        // Location Update Events ----------------------------------------------

        // Event handler for location updated events.
        private async void locationUpdated(object sender, LocationUpdatedEventArgs evt)
        {
            log.LogDebug("Handle location update for {0}.", evt.Name);

            var zones = await repo.GetZonesAsync(evt.Name);
            var zoneUpdates = new List<Task>();
            // check all zones against the updated location
            // and compose a list of zones where we are "in"
            var matches = new List<(double, IZone)>();
            foreach (var zone in zones)
            {
                var contained = zone.Contains(evt);
                var distance = zone.Distance(evt);

                zoneUpdates.Add(states.UpdateZoneStatusAsync(evt.Name,
                    zone.Name, contained));
                if (contained)
                {
                    matches.Add((distance, zone));
                }
            }

            // find the best match
            var currentZoneName = "";
            if (matches.Count != 0)
            {
                matches.Sort(byRelevance);
                currentZoneName = matches[0].Item2.Name;
            }
            await states.UpdateCurrentZoneAsync(evt.Name, currentZoneName);
            await Task.WhenAll(zoneUpdates);
        }

        // Delegate to sort a list of matches by relevance.
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

        // Event handler for Current Zone Changed events.
        private async void currentZoneChanged(object sender, CurrentZoneChangedEventArgs evt)
        {
            var topic = String.Format("{0}/{1}/current",
                TopicPrefixOut, evt.SubName);
            var message = evt.ZoneName != null ? evt.ZoneName : "";
            await mqtt.PublishAsync(topic, message);
        }

        // Event handler for Zone Status Changed events.
        private async void zoneStatusChanged(object sender, ZoneStatusChangedEventArgs evt)
        {
            var topic = String.Format("{0}/{1}/status/{2}",
                TopicPrefixOut, evt.SubName, evt.ZoneName);
            var message = evt.Status ? "in" : "out";
            await mqtt.PublishAsync(topic, message);
        }

        // Subscriptions -------------------------------------------------------

        private void subscribe()
        {
            // OwnTracks topics are build like this:
            //
            //  [prefix]/[user]/[device]
            //
            // see: http://owntracks.org/booklet/guide/topics/
            var topic = TopicPrefixIn + "/+/+";
            mqtt.Subscribe(topic);
        }

        private (string, string) parseTopic(string topic)
        {
            var prefix = TopicPrefixIn + "/";
            if (topic.StartsWith(prefix))
            {
                var parts = topic.Remove(0, prefix.Length).Split("/", 2);
                return (parts[0], parts[1]);
            }

            throw new ArgumentException(String.Format("Invalid topic {0}",
                topic));
        }
    }

    // Message for a location update.
    public class LocationUpdatedEventArgs : EventArgs, ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        public string Name { get; set; }

        public string Device { get; set; }

        public int Accuracy { get; set; }

        public DateTime Timestamp { get; set; }
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
        private static readonly DateTime EPOCH = new DateTime(
            1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public string _type { get; set; }

        public double lat { get; set; }

        public double lon { get; set; }

        // accuracy in meters
        public int acc { get; set; }

        public long tst { get; set; }

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
            DateTime timestamp;
            if (tst != 0)
            {
                timestamp = OwnTracksMessage.EPOCH.AddSeconds(tst);
            }
            else{
                timestamp = DateTime.UtcNow;
            }

            return new LocationUpdatedEventArgs()
            {
                Lat = lat,
                Lon = lon,
                Accuracy = acc,
                Timestamp = timestamp
            };
        }
    }
}
