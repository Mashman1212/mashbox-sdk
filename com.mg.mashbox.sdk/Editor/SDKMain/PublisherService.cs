#if UNITY_EDITOR
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MashBoxSDK.Exporting;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public static class PublisherService
    {
        private const string BaseUrl = "https://ugc-remote-cook-func-node-fecqe4asaabhcddn.centralus-01.azurewebsites.net/api/";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        [Serializable] public sealed class Publisher
        {
            public string id, label, platform, unityVersion, mashBoxSdkVersion, family, status, message, serviceStatus;
            public bool enabled, available, online, busy;
            public string[] regions;
        }
        [Serializable] public sealed class Fleet { public bool enabled; public string utc, message; public Publisher[] workers; }
        [Serializable] public sealed class Route
        {
            public int protocol = 2;
            public string game, unityVersion, sdkVersion, region;
            // One identity per submission, shared across its platform copies and upload retries.
            public string submissionId;
        }
        [Serializable] private sealed class UploadRequest
        {
            public int protocol;
            public string fileName, container, game, unityVersion, sdkVersion, region;
        }
        [Serializable] private sealed class UploadResponse { public string jobId, uploadUrl, message; }
        public static Route CreateRoute(string gameName, string region)
        {
            GameTargetUnityVersionValidator.ThrowIfInvalidForPublishing(gameName);
            if (region != "us" && region != "eu") throw new ArgumentException("Invalid upload region.");
            return new Route { game = GameRegistry.Find(gameName).DisplayName, unityVersion = Application.unityVersion,
                sdkVersion = MashBoxSDKState.InstalledVersion, region = region, submissionId = Guid.NewGuid().ToString("N") };
        }
        public static async Task<Fleet> GetFleetAsync()
        {
            using var response = await Http.GetAsync(BaseUrl + "publisher-status");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Publisher status could not be verified. Try Refresh Servers shortly.");
            var fleet = JsonUtility.FromJson<Fleet>(await response.Content.ReadAsStringAsync());
            if (fleet == null || fleet.workers == null) throw new InvalidOperationException("Invalid publisher status response.");
            return fleet;
        }
        public static async Task<string[]> AvailableContainersAsync(Route route, string[] selected = null)
        {
            var fleet = await GetFleetAsync();
            var workers = fleet.workers.Where(w => w.unityVersion == route.unityVersion).ToArray();
            var containers = workers.Where(w => fleet.enabled && w.available && w.regions != null && w.regions.Contains(route.region))
                .Select(w => w.platform == "PC" ? "inbox-windows" : w.platform == "Xbox Series" ? "inbox-xbox" : w.platform == "PlayStation 5" ? "inbox-ps5" : null)
                .Where(c => c != null).Distinct().OrderBy(c => c == "inbox-windows" ? 0 : 1).ToArray();
            if (!containers.Contains("inbox-windows")) throw new InvalidOperationException(!string.IsNullOrEmpty(fleet.message) ? fleet.message : workers.FirstOrDefault(w => !string.IsNullOrEmpty(w.message))?.message ?? "No compatible PC publisher is accepting submissions for this game and region.");
            if (selected != null && selected.Any(c => !containers.Contains(c)))
                throw new InvalidOperationException("One or more selected platforms are unavailable. Check publisher status in MashBox Setup and select available platforms.");
            return selected ?? containers;
        }
        public static async Task<(string jobId, string uploadUrl)> RequestUploadAsync(string fileName, string container, Route route)
        {
            // Route was captured before asynchronous export/upload; never re-read EditorPrefs during retries.
            var request = new UploadRequest { protocol = route.protocol, game = route.game, unityVersion = route.unityVersion,
                sdkVersion = route.sdkVersion, region = route.region, container = container, fileName = route.submissionId + "_" + fileName };
            using var content = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(BaseUrl + "request-upload-v2", content).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonUtility.FromJson<UploadResponse>(body);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(result?.message ?? "Publisher could not accept the upload.");
            if (string.IsNullOrEmpty(result?.uploadUrl)) throw new InvalidOperationException("Publisher returned an invalid upload response.");
            return (result.jobId, result.uploadUrl);
        }
    }
}
#endif
