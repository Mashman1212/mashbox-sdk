#if UNITY_EDITOR
using System;
using System.Linq;

namespace MashBoxSDK.SDKMain
{
    // Display aggregation only; upload authorization remains on the server.
    public sealed class PublisherStatusSummary
    {
        public string Status, Detail;
        public bool Available;
        public PublisherService.Publisher[] Workers;

        public static PublisherStatusSummary ForPlatform(PublisherService.Publisher[] publishers,
            string version, string platform, bool hasSnapshot, bool fresh, bool checking, bool failed, bool globallyEnabled)
        {
            var workers = (publishers ?? Array.Empty<PublisherService.Publisher>())
                .Where(w => w != null && w.unityVersion == version && w.platform == platform).ToArray();
            var result = new PublisherStatusSummary { Workers = workers };
            if (!hasSnapshot || !fresh)
            {
                result.Status = failed ? "Status unavailable" : !hasSnapshot ? "Checking..." : "Status outdated";
                result.Detail = failed ? "Cannot reach the status service." : checking ? "Checking publisher status..." : "Refresh to check availability.";
                return result;
            }
            if (workers.Length == 0)
            {
                result.Status = "Not connected";
                result.Detail = "No publisher has reported for this platform and Unity version.";
                return result;
            }
            var available = workers.Where(w => globallyEnabled && w.enabled && w.online && w.available).ToArray();
            var reasons = workers.Select(w => w.message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();
            result.Available = available.Length > 0;
            if (result.Available)
            {
                result.Status = available.All(w => w.busy) ? "Cooking" : "Online";
                var regions = available.SelectMany(w => w.regions ?? Array.Empty<string>()).Distinct()
                    .Select(r => r == "us" ? "Americas" : r == "eu" ? "Europe" : r);
                result.Detail = $"{available.Length} of {workers.Length} publishers available";
                var regionText = string.Join(" + ", regions);
                if (!string.IsNullOrEmpty(regionText)) result.Detail += " · " + regionText;
            }
            else
            {
                result.Status = !globallyEnabled || workers.All(w => !w.enabled) ? "Paused"
                    : workers.Any(w => w.enabled && w.online) ? "Not accepting" : "Offline";
                result.Detail = reasons.Length > 0 ? string.Join(" · ", reasons)
                    : result.Status == "Offline" ? "No fresh heartbeat from an enabled publisher."
                    : result.Status == "Paused" ? "New publishing work is paused."
                    : "Connected, but not ready to accept publishing work.";
            }
            return result;
        }
    }
}
#endif
