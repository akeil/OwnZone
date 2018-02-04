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
        // Event handler for incoming messages
        event EventHandler<MessageReceivedEventArgs> MessageReceived;

        // Connect to the MQTT broker.
        void Connect();

        void AddSubscription(Subscription subscription);

        // Publish a string message to the given MQTT topic.
        void Publish(string topic, string payload);
    }

    class MqttSettings
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

            var settings = new MqttSettings();
            Program.Configuration.GetSection("MQTT").Bind(settings);
            client = new MqttClient(settings.Host);
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

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

            var args = new MessageReceivedEventArgs();
            args.Topic = evt.Topic;
            args.Message = Encoding.UTF8.GetString(evt.Message);
            OnMessageReceived(args);
        }

        protected virtual void OnMessageReceived(MessageReceivedEventArgs args)
        {
            log.LogDebug("Dispatch message for topic {0}", args.Topic);
            var handler = MessageReceived;
            if (handler != null) {
                handler(this, args);
            }
        }

        public void Subscribe(string topic)
        {

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

    public class MessageReceivedEventArgs : EventArgs
    {
        public string Topic { get; set; }

        public string Message { get; set; }
    }
}