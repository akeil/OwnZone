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

        public Engine(ILoggerFactory loggerFactory, IMqttService mqtt,
            IZoneRepository zoneRepository)
        {
            log = loggerFactory.CreateLogger<Engine>();
            service = mqtt;
            zoneRepo = zoneRepository;
        }

        public void Run()
        {
            log.LogDebug("Engine start");
            service.Connect();
            readSubs();
        }

        private void readSubs()
        {
            var section = Program.Configuration.GetSection("Subscriptions");
            var subs = section.Get<Subscription[]>();
            foreach (var s in subs)
            {
                s.Setup(log, service, zoneRepo);
                service.AddSubscription(s);
            }
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
        public void HandleLocationUpdate(LocationUpdate update)
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