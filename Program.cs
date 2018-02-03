using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ownzone
{
    class Program
    {
        public static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            Console.WriteLine("Start");
            ReadConfiguration();

            var settings = new MQTTSettings();
            Configuration.GetSection("MQTT").Bind(settings);
            var service = new MQTTService(settings);
            service.Connect();

            var subscription = new Subscription(service);
            Configuration.GetSection("Subscription").Bind(subscription);
            subscription.Setup();

            service.AddSubscription(subscription);
        }

        static void ReadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");
            
            Configuration = builder.Build();
        }
    }
}
