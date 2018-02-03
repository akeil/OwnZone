using System;
using System.Text;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ownzone
{
    public interface IMqttService
    {
        void Connect();

        void AddSubscription(Subscription subscription);

        void Publish(string topic, string payload);
    }

    public class MQTTSettings
    {
        public string Host { get; set; }
    }

    public class MqttService : IMqttService
    {
        private const string CLIENT_ID = "ownzone";

        private readonly ILogger<MqttService> log;
        
        private MqttClient client;

        private List<Subscription> subscriptions;

        public MqttService(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<MqttService>();
            subscriptions = new List<Subscription>();

            var settings = new MQTTSettings();
            Program.Configuration.GetSection("MQTT").Bind(settings);
            client = new MqttClient(settings.Host);
        }

        // Connect to the MQTT service
        public void Connect()
        {
            client.MqttMsgPublishReceived += messageReceived;
            client.Connect(CLIENT_ID);
        }

        // handle incoming MQTT message
        private void messageReceived(object sender, MqttMsgPublishEventArgs evt)
        {
            var matching = subscriptions.FindAll(
                x => x.TopicMatches(evt.Topic));
            if (matching.Count == 0)
            {
                log.LogWarning("No subscription matches topic {0}", evt.Topic);
                return;
            }

            Message message = null;
            var json = Encoding.UTF8.GetString(evt.Message);
            try
            {
                var raw = JsonConvert.DeserializeObject<RawMessage>(json);
                if (raw.Accept())
                {
                    message = raw.AsMessage();
                }
            }
            catch (JsonReaderException)
            {
                log.LogWarning("Failed to parse JSON from message body ({0}",
                    evt.Topic);
            }

            if (message != null)
            {
                foreach (var sub in matching)
                {
                    sub.LocationUpdate(message);
                }
            }
        }

        public void AddSubscription(Subscription subscription)
        {
            var topics = new string[] { subscription.Topic };
            var qosLevels = new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE };
            client.Subscribe(topics, qosLevels);
            subscriptions.Add(subscription);
        }

        public void Publish(string topic, string payload)
        {
            byte[] message = Encoding.UTF8.GetBytes(payload);
            client.Publish(topic, message);
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
    class RawMessage
    {
        public string _type { get; set; }

        public double lat { get; set; }

        public double lon { get; set; }

        public int acc { get; set; }

        // Check if all required fields are set.
        //
        // Used after mapping the JSON object to make sure that the JSON message
        // did contain all of the expected fields.
        public bool Accept()
        {
            return lat != 0 && lon != 0;
        }

        // convert to OwnZone message
        public Message AsMessage(){
            return new Message(lat, lon);
        }
    }


    public class Message : ILocation
    {
        
        public Message(double lat, double lon)
        {
            Lat = lat;
            Lon = lon;
        }

        public double Lat { get; set; }

        public double Lon { get; set; }
    }
}