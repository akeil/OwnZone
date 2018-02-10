using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace OwnZone
{
    public interface IFilterService
    {
        // Tell if the given Location Update should be processed.
        bool Accept(LocationUpdatedEventArgs evt);
    }

    class FilterSettings
    {
        public string MaxAge { get; set; }

        public int MaxAccuracy { get; set; }
    }

    public class FilterService : IFilterService
    {
        private readonly List<IFilter> filters;

        public FilterService(IConfiguration config)
        {
            var settings = new FilterSettings();
            config.GetSection("Filters").Bind(settings);

            filters = new List<IFilter>();
            filters.Add(new AgeFilter(TimeSpan.Parse(settings.MaxAge)));
            filters.Add(new AccuracyFilter(settings.MaxAccuracy));
        }

        public bool Accept(LocationUpdatedEventArgs evt)
        {
            foreach (var filter in filters)
            {
                if (!filter.Accept(evt))
                {
                    return false;
                }
            }
            return true;
        }
    }

    interface IFilter
    {
        bool Accept(LocationUpdatedEventArgs evt);
    }

    // Filter events by their `Accuracy` (if that field is set).
    class AccuracyFilter : IFilter
    {
        private readonly int maxAccuracy;

        public AccuracyFilter(int maxAcc)
        {
            maxAccuracy = maxAcc;
        }

        public bool Accept(LocationUpdatedEventArgs evt)
        {
            if (evt.Accuracy != 0)
            {
                return evt.Accuracy < maxAccuracy;
            }
            else{
                return true;
            }
        }
    }

    // Accept only recent events.
    class AgeFilter : IFilter
    {
        private readonly TimeSpan maxAge;

        public AgeFilter(TimeSpan t)
        {
            maxAge = t;
        }
        public bool Accept(LocationUpdatedEventArgs evt)
        {
            var now = DateTime.UtcNow;
            var then = evt.Timestamp;
            return (now - then) <= maxAge;
        }
    }
}
