using System;
using System.Collections.Generic;

namespace ownzone
{
    public interface IFilterService
    {
        bool Accept(LocationUpdatedEventArgs evt);
    }

    public class FilterService : IFilterService
    {
        private readonly List<IFilter> filters;

        public FilterService()
        {
            filters = new List<IFilter>();
            filters.Add(new AgeFilter());
            filters.Add(new AccuracyFilter());
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
        private const int MAX_ACCURACY = 25;

        public bool Accept(LocationUpdatedEventArgs evt)
        {
            if (evt.Accuracy != 0)
            {
                return evt.Accuracy < MAX_ACCURACY;
            }
            else{
                return true;
            }
        }
    }

    class AgeFilter : IFilter
    {
        private readonly TimeSpan MAX_AGE = new TimeSpan(1, 30, 0);
        public bool Accept(LocationUpdatedEventArgs evt)
        {
            var now = DateTime.UtcNow;
            var then = evt.Timestamp;
            return (now - then) <= MAX_AGE;
        }
    }
}