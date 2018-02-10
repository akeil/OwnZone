using System;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        Task ConnectAsync();

        // Subscribe to the given MQTT topic
        void Subscribe(string topic);

        Task SubscribeAsync(string topic);

        // Publish a string message to the given MQTT topic.
        void Publish(string topic, string payload);

        Task PublishAsync(string topic, string payload);
    }

    class MqttConfig
    {
        public const string DEFAULT_CLIENT_ID_PREFIX = "ownzone";

        // Hostname or IP address of the MQTT broker.
        public string Host { get; set; }

        // Port for the MQTT broker.
        public int Port { get; set; }

        // Username for MQTT authentication.
        public string Username { get; set; }

        // Password for MQTT authentication.
        public string Password { get; set; }
    }

    public class MqttService : IMqttService
    {
        private readonly ILogger<MqttService> log;

        private readonly MqttConfig config;
        
        private readonly MqttClient client;

        private readonly string clientId;

        public MqttService(ILoggerFactory loggerFactory,
            IConfiguration cfg)
        {
            log = loggerFactory.CreateLogger<MqttService>();

            config = new MqttConfig();
            cfg.GetSection("MQTT").Bind(config);

            var port = config.Port != 0
                ? config.Port
                : MqttSettings.MQTT_BROKER_DEFAULT_PORT;
            client = new MqttClient(config.Host, port, false, null, null,
                MqttSslProtocols.None);

            client.MqttMsgPublishReceived += messageReceived;

            var machine = Environment.MachineName;
            var user = Environment.UserName;
            clientId = String.Format("{0}-{1}-{2}",
                MqttConfig.DEFAULT_CLIENT_ID_PREFIX, machine, user);
        }

        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        public void Connect()
        {
            ConnectAsync().Wait();
        }

        public async Task ConnectAsync()
        {
            await Task.Run( () =>
                client.Connect(clientId, config.Username, config.Password)
            );

            log.LogInformation("Connected to MQTT broker at {0} as {1}.",
                config.Host, clientId);
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
            SubscribeAsync(topic).Wait();
        }

        public async Task SubscribeAsync(string topic)
        {
            var topics = new string[] { topic };
            var qosLevels = new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE };

            await Task.Run(() => client.Subscribe(topics, qosLevels));

            log.LogInformation("Subscribed to topic {0}.", topic);
        }

        public void Publish(string topic, string payload)
        {
            PublishAsync(topic, payload).Wait();
        }

        public async Task PublishAsync(string topic, string payload)
        {
            var message = Encoding.UTF8.GetBytes(payload);

            await Task.Run( () => client.Publish(topic, message));

            log.LogDebug("Published to topic {0}.", topic);
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public string Topic { get; set; }

        public string Message { get; set; }
    }
}
