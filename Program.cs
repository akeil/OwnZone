using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ownzone
{
    class Program
    {
        public static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            ReadConfiguration();

            var settings = new MQTTSettings();
            Configuration.GetSection("MQTT").Bind(settings);
            var service = new MQTTService(settings);
            service.Connect();

            var subs = Configuration.GetSection("Subscriptions").Get<Subscription[]>();
            foreach (var s in subs)
            {
                s.Setup(service);
            }
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
