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

        public string TopicPrefix { get; set; }

        public Engine(ILoggerFactory loggerFactory, IMqttService mqttService,
            IRepository repository, IStateRegistry stateRegistry)
        {
            log = loggerFactory.CreateLogger<Engine>();
            mqtt = mqttService;
            repo = repository;
            states = stateRegistry;

            var config = Program.Configuration.GetSection("Engine");
            config.Bind(this);
        }

        public event EventHandler<LocationUpdatedEventArgs> LocationUpdated;

        public void Run()
        {
            // register event handlers
            this.LocationUpdated += locationUpdated;
            states.ZoneStatusChanged += zoneStatusChanged;
            states.CurrentZoneChanged += currentZoneChanged;
            mqtt.MessageReceived += messageReceived;

            var connected = mqtt.ConnectAsync();
            // subscriptions require completed connection
            connected.Wait();
            subscribeForAccounts();

            log.LogInformation("Engine started.");
        }

        // Raw Messages --------------------------------------------------------

        // event handler for raw mqtt messages
        private async void messageReceived(object sender, MessageReceivedEventArgs evt)
        {
            log.LogDebug("Handle message for {0}.", evt.Topic);
            // TODO: throws
            var baseargs = convertOwnTracksMessage(evt.Message);

            // dispatch LocationUpdated events for each affected subscription
            var names = await lookupAccountsAsync(evt.Topic);
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

        // find the accounts that are interested in the given topic.
        private async Task<List<string>> lookupAccountsAsync(string topic)
        {
            var result = new List<string>();

            var names = await repo.GetAccountNamesAsync();
            foreach (var name in names)
            {
                var account = await repo.GetAccountAsync(name);
                if (account.Topic == topic)
                {
                    result.Add(account.Name);
                }
            }

            return result;
        }

        // Location Update Events ----------------------------------------------

        private async void locationUpdated(object sender, LocationUpdatedEventArgs evt)
        {
            log.LogDebug("Handle location update for {0}.", evt.Name);

            var zones = await repo.GetZonesAsync(evt.Name);

            // check all zones against the updated location
            // and compose a list of zones where we are "in"
            var matches = new List<(double, IZone)>();
            foreach (var zone in zones)
            {
                var match = zone.Match(evt);
                await states.UpdateZoneStatusAsync(evt.Name, zone.Name,
                    match.contains);
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
            await states.UpdateCurrentZoneAsync(evt.Name, currentZoneName);
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

        private async void currentZoneChanged(object sender, CurrentZoneChangedEventArgs evt)
        {
            var topic = String.Format("{0}/{1}/current",
                TopicPrefix, evt.SubName);
            var message = evt.ZoneName != null ? evt.ZoneName : "";
            mqtt.Publish(topic, message);
        }

        private async void zoneStatusChanged(object sender, ZoneStatusChangedEventArgs evt)
        {
            var topic = String.Format("{0}/{1}/status/{2}",
                TopicPrefix, evt.SubName, evt.ZoneName);
            var message = evt.Status ? "in" : "out";
            mqtt.Publish(topic, message);
        }

        // Subscriptions -------------------------------------------------------

        private async Task subscribeForAccounts()
        {
            foreach (var name in repo.GetAccountNames())
            {
                log.LogInformation("Subscribe for account {0}", name);
                var account = repo.GetAccount(name);
                await mqtt.SubscribeAsync(account.Topic);
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
}