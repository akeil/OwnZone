using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ownzone
{
    public class Subscription
    {
        private MQTTService service;

        private List<IZone> zones;

        public string Topic { get; set; }

        public string OutTopic { get; set; }

        public string ZonePath { get; set; }

        public Subscription()
        {
            zones = new List<IZone>();
        }

        public void Setup(MQTTService mqttService)
        {
            service = mqttService;
            try{
                readZones();
            }
            catch (FileNotFoundException)
            {
                // TODO log warning
            }
        }

        // Tell if the given MQTT topic matches this subscription
        public bool TopicMatches(string candidate)
        {
            return candidate == Topic;
        }

        // Handle a location update
        public void LocationUpdate(Message message)
        {
            // list all zones that contain the current location
            var matches = new List<(double, IZone)>();
            foreach (var zone in zones)
            {
                var match = zone.Match(message);
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

    // Common interface for all zones.
    public interface IZone
    {   
        string Name { get; set; }

        bool Active { get; set; }

        (bool contains, double distance) Match(ILocation loc);
    }

    // Zone defined by a single coordinate pair and a radius.
    class Point : IZone, ILocation
    {
        public double Lat { get; set; }

        public double Lon { get; set; }

        public int Radius { get; set; }

        public string Name { get; set; }

        public bool Active { get; set; }

        public (bool contains, double distance) Match(ILocation loc)
        {
            var distance = Geo.Distance(loc, this);
            var contains = distance < Radius;
            return (contains, distance);
        }
    }

    class Bounds : IZone
    {
        public double MinLat { get; set; }

        public double MinLon { get; set; }

        public double MaxLat { get; set; }

        public double MaxLon { get; set; }

        public string Name { get; set; }

        public bool Active { get; set; }

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