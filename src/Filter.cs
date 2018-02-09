using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ownzone
{
    public interface IFilterService
    {
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

        public FilterService()
        {
            var config = Program.Configuration.GetSection("Filters");
            var settings = new FilterSettings();
            config.Bind(settings);

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