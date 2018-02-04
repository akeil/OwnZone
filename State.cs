using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ownzone
{
    // Holds status values for zones.
    public interface IStateRegistry
    {
        // Update the current zone for a subscription.
        // This may trigger a CurrentZoneChanged event.
        void UpdateCurrentZone(string name, string zone);

        Task UpdateCurrentZoneAsync(string name, string zone);

        // Update the status for a zone.
        // This may trigger a ZoneStatusChanged event.
        void UpdateZoneStatus(string name, string zone, bool Status);

        Task UpdateZoneStatusAsync(string name, string zone, bool Status);

        event EventHandler<CurrentZoneChangedEventArgs> CurrentZoneChanged;

        event EventHandler<ZoneStatusChangedEventArgs> ZoneStatusChanged;
    }

    public class StateRegistry : IStateRegistry
    {
        private readonly ILogger<StateRegistry> log;

        private Dictionary<string, string> currentZone;

        private Dictionary<string, bool> zoneStatus;

        public StateRegistry(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<StateRegistry>();
            currentZone = new Dictionary<string, string>();
            zoneStatus = new Dictionary<string, bool>();
        }

        public event EventHandler<CurrentZoneChangedEventArgs> CurrentZoneChanged;

        public event EventHandler<ZoneStatusChangedEventArgs> ZoneStatusChanged;

        public void UpdateZoneStatus(string subName, string zoneName, bool status)
        {
            UpdateZoneStatusAsync(subName, zoneName, status).Wait();
        }

        public async Task UpdateZoneStatusAsync(string subName, string zoneName, bool status)
        {
            var key = subName + "." + zoneName;
            var changed = false;
            try
            {
                changed = status != zoneStatus[key];
            }
            catch (KeyNotFoundException)
            {
                changed = true;
            }

            zoneStatus[key] = status;

            if (changed)
            {
                var args = new ZoneStatusChangedEventArgs()
                {
                    SubName=subName,
                    ZoneName=zoneName,
                    Status=status
                };
                OnZoneStatusChanged(args);
            }
        }

        protected virtual void OnZoneStatusChanged(ZoneStatusChangedEventArgs args)
        {
            log.LogDebug("Dispatch status change for {0}.{1}.",
                args.SubName, args.ZoneName);
            var handler = ZoneStatusChanged;
            if (handler != null) {
                handler(this, args);
            }
        }

        public void UpdateCurrentZone(string subName, string zoneName)
        {
            UpdateCurrentZoneAsync(subName, zoneName).Wait();
        }

        public async Task UpdateCurrentZoneAsync(string subName, string zoneName)
        {
            if (String.IsNullOrEmpty(zoneName))
            {
                zoneName = null;
            }

            var changed = false;
            try
            {
                changed = zoneName != currentZone[subName];
            }
            catch (KeyNotFoundException)
            {
                changed = true;
            }

            currentZone[subName] = zoneName;

            if (changed)
            {
                var args = new CurrentZoneChangedEventArgs()
                {
                    SubName = subName,
                    ZoneName = zoneName
                };
                OnCurrentZoneChanged(args);
            }
        }

        protected virtual void OnCurrentZoneChanged(CurrentZoneChangedEventArgs args)
        {
            log.LogDebug("Dispatch zone change for {0}.", args.SubName);
            var handler = CurrentZoneChanged;
            if (handler != null) {
                handler(this, args);
            }
        }
    }

    public class ZoneStatusChangedEventArgs : EventArgs
    {
        public string SubName { get; set; }

        public string ZoneName { get; set; }

        public bool Status { get; set; }
    }

    public class CurrentZoneChangedEventArgs : EventArgs
    {
        public string SubName { get; set; }

        public string ZoneName { get; set; }
    }
}