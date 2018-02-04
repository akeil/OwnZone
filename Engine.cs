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

        public Engine(ILoggerFactory loggerFactory, IMqttService mqtt,
            IZoneRepository zoneRepo)
        {
            log = loggerFactory.CreateLogger<Engine>();
            service = mqtt;
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
                s.Setup(log, service);
                service.AddSubscription(s);
            }
        }
    }


    public class Subscription
    {
        private IMqttService service;

        private ILogger<Engine> log;

        private List<IZone> zones;

        public string Topic { get; set; }

        public string OutTopic { get; set; }

        public string ZonePath { get; set; }

        public Subscription()
        {
            zones = new List<IZone>();
        }

        public void Setup(ILogger<Engine> logger, IMqttService mqttService)
        {
            log = logger;
            service = mqttService;
            try
            {
                readZones();
            }
            catch (FileNotFoundException)
            {
                log.LogWarning("Could not find {0}", ZonePath);
            }
        }

        // Tell if the given MQTT topic matches this subscription
        public bool TopicMatches(string candidate)
        {
            return candidate == Topic;
        }

        // Handle a location update
        public void HandleLocationUpdate(LocationUpdate update)
        {
            log.LogInformation("Location update");

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

        // Read the list of Zones from the JSON file at ZonePath.
        private void readZones()
        {
            // Expect a JSON array like this:
            // [
            //   {
            //     "Kind": "Point",
            //     <other properties>
            //   },
            //   {...}
            // ]
            // 
            // Depending on the Kind, a different concrete class needs
            // to be instantiated:
            // - Point
            // - Box
            using (StreamReader f = new StreamReader(ZonePath, Encoding.UTF8))
            using(JsonTextReader reader = new JsonTextReader(f))
            {
                var arr = JToken.ReadFrom(reader);
                if (arr.Type != JTokenType.Array)
                {
                    throw new Exception("Unexpected Type, not an array.");
                }
                foreach (var child in arr.Children())
                {
                    if (child.Type != JTokenType.Object)
                    {
                        throw new Exception("Unexpected type, not an object.");
                    }

                    JObject obj = (JObject) child;
                    var kind = obj.Value<string>("Kind");
                    if (kind == "Point")
                    {
                        zones.Add(obj.ToObject<Point>());
                    }
                    else if (kind == "Bounds")
                    {
                        zones.Add(obj.ToObject<Bounds>());
                    }
                    else
                    {
                        throw new Exception("Unsupported Zone kind.");
                    }
                }
            }
        }
    }

}