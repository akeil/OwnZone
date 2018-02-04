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

        // Subscribe to the given MQTT topic
        void Subscribe(string topic);

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
        
        private readonly MqttClient client;

        public MqttService(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<MqttService>();

            var settings = new MqttSettings();
            Program.Configuration.GetSection("MQTT").Bind(settings);
            client = new MqttClient(settings.Host);
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        public void Connect()
        {
            client.MqttMsgPublishReceived += messageReceived;
            client.Connect(CLIENT_ID);

            log.LogInformation("Connected to MQTT broker.");
        }

        // handle incoming MQTT message
        private void messageReceived(object sender, MqttMsgPublishEventArgs evt)
        {
            log.LogDebug("Received message for topic {0}.", evt.Topic);

            var args = new MessageReceivedEventArgs();
            args.Topic = evt.Topic;
            args.Message = Encoding.UTF8.GetString(evt.Message);
            OnMessageReceived(args);
        }

        protected virtual void OnMessageReceived(MessageReceivedEventArgs args)
        {
            log.LogDebug("Dispatch event for topic {0}.", args.Topic);
            var handler = MessageReceived;
            if (handler != null) {
                handler(this, args);
            }
        }

        public void Subscribe(string topic)
        {
            var topics = new string[] { topic };
            var qosLevels = new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE };
            client.Subscribe(topics, qosLevels);

            log.LogInformation("Subscribed to topic {0}.", topic);
        }

        public void Publish(string topic, string payload)
        {
            byte[] message = Encoding.UTF8.GetBytes(payload);
            client.Publish(topic, message);

            log.LogDebug("Published to topic {0}.", topic);
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public string Topic { get; set; }

        public string Message { get; set; }
    }
}