﻿using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ownzone
{
    class Program
    {
        public static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            ReadConfiguration();

            var provider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.AddConfiguration(Configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddDebug();
                })
                .AddSingleton<IMqttService, MqttService>()
                .AddSingleton<IEngine, Engine>()
                .BuildServiceProvider();

            var log = provider.GetService<ILoggerFactory>()
                .CreateLogger<Program>();
            log.LogDebug("Starting OwnZone");

            var engine = provider.GetService<IEngine>();
            engine.Run();
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
