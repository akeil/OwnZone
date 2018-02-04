using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
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

        private bool currentZoneLoaded;

        private Dictionary<string, bool> zoneStatus;

        private bool zoneStatusLoaded;

        public string BaseDirectory { get; set; }

        public StateRegistry(ILoggerFactory loggerFactory)
        {
            log = loggerFactory.CreateLogger<StateRegistry>();
            currentZone = new Dictionary<string, string>();
            zoneStatus = new Dictionary<string, bool>();
            zoneStatusLoaded = false;
            currentZoneLoaded = false;

            var config = Program.Configuration.GetSection("StateRegistry");
            config.Bind(this);
        }

        public event EventHandler<CurrentZoneChangedEventArgs> CurrentZoneChanged;

        public event EventHandler<ZoneStatusChangedEventArgs> ZoneStatusChanged;

        public void UpdateZoneStatus(string subName, string zoneName, bool status)
        {
            UpdateZoneStatusAsync(subName, zoneName, status).Wait();
        }

        public async Task UpdateZoneStatusAsync(string subName, string zoneName, bool status)
        {
            await lazyLoadStatusAsync();

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

                await storeStatusAsync();
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
            await lazyLoadZonesAsync();

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

                await storeZonesAsync();
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

        private async Task lazyLoadZonesAsync()
        {
            if (!currentZoneLoaded)
            {
                await loadZonesAsync();
                currentZoneLoaded = true;
            }
        }

        private async Task lazyLoadStatusAsync()
        {
            if (!zoneStatusLoaded)
            {
                await loadStatusAsync();
                zoneStatusLoaded = true;
            }
        }

        private async Task loadZonesAsync()
        {
            var path = Path.Combine(BaseDirectory, "state.zones.json");
            var obj = await loadAsync(path);
            if (obj != null)
            {
                currentZone = obj.ToObject<Dictionary<string, string>>();
            }
        }

        private async Task loadStatusAsync()
        {
            var path = Path.Combine(BaseDirectory, "state.status.json");
            var obj = await loadAsync(path);
            if (obj != null)
            {
                zoneStatus = obj.ToObject<Dictionary<string, bool>>();
            }
        }

        private async Task<JObject> loadAsync(string path)
        {
            log.LogInformation("Load state from {0}.", path);

            JObject root = null;
            try
            {
                using (StreamReader f = new StreamReader(path, Encoding.UTF8))
                using(JsonTextReader reader = new JsonTextReader(f))
                {
                    root = await JObject.LoadAsync(reader);
                }
            }
            catch (IOException)
            {
                log.LogWarning("Could not open state file {0}", path);
            }

            return root;
        }

        private async Task storeZonesAsync()
        {
            var path = Path.Combine(BaseDirectory, "state.zones.json");
            await storeAsync(currentZone, path);
        }

        private async Task storeStatusAsync()
        {
            var path = Path.Combine(BaseDirectory, "state.status.json");
            await storeAsync(zoneStatus, path);
        }

        private async Task storeAsync(object obj, string path)
        {
            log.LogDebug("Write state to {0}.", path);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var jsonStr = JsonConvert.SerializeObject(obj);
                var append = false;
                using(StreamWriter writer = new StreamWriter(path, append, Encoding.UTF8))
                {
                    await writer.WriteAsync(jsonStr);
                }
            }
            catch (IOException ex)
            {
                log.LogError(ex, "Failed to save state to {0}", path);
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