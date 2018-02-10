using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ownzone
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = readConfiguration();
            var provider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder.AddConfiguration(config.GetSection("Logging"))
                    .AddConsole()
                    .AddDebug();
                })
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IMqttService, MqttService>()
                .AddSingleton<IRepository, Repository>()
                .AddSingleton<IStateRegistry, StateRegistry>()
                .AddSingleton<IFilterService, FilterService>()
                .AddSingleton<IEngine, Engine>()
                .BuildServiceProvider();

            var log = provider.GetService<ILoggerFactory>()
                .CreateLogger<Program>();
            
            log.LogInformation("Starting OwnZone...");
            var engine = provider.GetService<IEngine>();
            engine.Run();
        }

        private static IConfiguration readConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");
            
            return builder.Build();
        }
    }
}
