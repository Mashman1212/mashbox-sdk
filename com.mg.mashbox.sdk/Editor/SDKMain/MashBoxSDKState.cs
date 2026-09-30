
#if UNITY_EDITOR

using System;
using System.Linq;
using UnityEditor;

namespace MashBoxSDK.SDKMain
{

    public static class MashBoxSDKState
    {
        
        // ============================
        // SDK VERSION STATE
        // ============================
        public static string InstalledVersion = "Unknown";
        public static string LatestVersion = "Unknown";
        public static bool UpdateAvailable = false;
        public static bool CheckingSdk = false;

        private static bool _sdkCheckInFlight;
        private const string SDK_PACKAGE = "com.mg.mashbox.sdk";
        private const string VERSION_URL = "https://raw.githubusercontent.com/Mashman1212/mashbox-sdk/main/com.mg.mashbox.sdk/package.json";

        
        public enum CookerStatus
        {
            Unknown,
            Online,
            Stale,
            Offline,
            Error
        }

        private static CookerStatus _cooker = CookerStatus.Unknown;
        public static PublisherService.Publisher[] Publishers = Array.Empty<PublisherService.Publisher>();
        private static DateTimeOffset _fleetChecked;
        public static CookerStatus Cooker
        {
            get
            {
                if (_cooker != CookerStatus.Online) return _cooker;
                if ((DateTimeOffset.UtcNow - _fleetChecked).TotalSeconds > 45) return CookerStatus.Stale;
                var game = MashBoxSDK.Exporting.GameRegistry.Find(EditorPrefs.GetString("ModIo.CurrentGame", ""));
                return game != null && Publishers.Any(p => p.available && p.platform == "PC" && p.unityVersion == game.UnityEditorVersion) ? CookerStatus.Online : CookerStatus.Offline;
            }
            private set { _cooker = value; }
        }
        public static string CookerNote = "checking...";
        public static bool CheckingCooker => _inFlight;

        private static bool _inFlight;
        private static double _nextPoll;

        private const int HeartbeatFreshnessSeconds = 30;
        private const string URL = "https://ugccooker.blob.core.windows.net/status/heartbeat.json";

        public static void Update()
        {
            if (_inFlight) return;
            if (EditorApplication.timeSinceStartup < _nextPoll) return;

            _inFlight = true;
            CookerNote = "checking...";
            _ = Poll();
        }

        public static void RefreshCookerStatus()
        {
            _ = RefreshCookerStatusAsync();
        }

        public static async System.Threading.Tasks.Task RefreshCookerStatusAsync()
        {
            if (_inFlight)
            {
                while (_inFlight)
                    await System.Threading.Tasks.Task.Delay(50);

                return;
            }

            Cooker = CookerStatus.Unknown;
            CookerNote = "checking...";
            _inFlight = true;
            _nextPoll = 0.0;

            await Poll();
        }
        
        public static void CheckForSdkUpdate()
        {
            if (_sdkCheckInFlight) return;

            _ = RefreshSdkVersionStateAsync();
        }

        public static async System.Threading.Tasks.Task RefreshSdkVersionStateAsync()
        {
            if (_sdkCheckInFlight)
            {
                while (_sdkCheckInFlight)
                    await System.Threading.Tasks.Task.Delay(50);

                return;
            }

            _sdkCheckInFlight = true;
            CheckingSdk = true;

            await PollSdk();
        }

        public static bool CanPublishWithInstalledSdk()
        {
            if (UpdateAvailable)
                return false;

            if (string.IsNullOrWhiteSpace(InstalledVersion) ||
                string.Equals(InstalledVersion, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(InstalledVersion, "Not Installed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(LatestVersion) ||
                string.Equals(LatestVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !IsRemoteVersionNewer(InstalledVersion, LatestVersion);
        }

        public static string GetPublishBlockedMessage()
        {
            if (UpdateAvailable)
            {
                return $"You need to update MashBox SDK before publishing.\n\nInstalled: {InstalledVersion}\nLatest: {LatestVersion}";
            }

            return $"MashBox SDK version could not be verified, so publishing is blocked.\n\nInstalled: {InstalledVersion}\nLatest: {LatestVersion}\n\nPlease update to the latest SDK version and try again.";
        }

        private static async System.Threading.Tasks.Task PollSdk()
        {
            try
            {
                // -------------------------
                // Installed version
                // -------------------------
                var list = UnityEditor.PackageManager.Client.List(true);
                while (!list.IsCompleted)
                    await System.Threading.Tasks.Task.Delay(50);

                var pkg = list.Result.FirstOrDefault(p => p.name == SDK_PACKAGE);

                if (pkg == null)
                {
                    InstalledVersion = "Not Installed";
                    LatestVersion = "Unknown";
                    UpdateAvailable = false;
                    return;
                }

                if (!string.IsNullOrEmpty(pkg.version))
                    InstalledVersion = pkg.version;
                else if (pkg.git != null && !string.IsNullOrEmpty(pkg.git.revision))
                    InstalledVersion = pkg.git.revision;
                else
                    InstalledVersion = "Unknown";

                // -------------------------
                // Remote version (GitHub)
                // -------------------------
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("UnitySDKUpdater");

                var res = await http.GetAsync(VERSION_URL);

                if (!res.IsSuccessStatusCode)
                {
                    LatestVersion = "Unknown";
                    UpdateAvailable = false;
                    return;
                }

                var json = await res.Content.ReadAsStringAsync();

                var match = System.Text.RegularExpressions.Regex.Match(
                    json,
                    "\"version\"\\s*:\\s*\"([^\"]+)\""
                );

                if (match.Success)
                {
                    LatestVersion = match.Groups[1].Value;
                    UpdateAvailable = IsRemoteVersionNewer(InstalledVersion, LatestVersion);
                }
                else
                {
                    LatestVersion = "Unknown";
                    UpdateAvailable = false;
                }
            }
            catch
            {
                InstalledVersion = "Unknown";
                LatestVersion = "Unknown";
                UpdateAvailable = false;
            }
            finally
            {
                CheckingSdk = false;
                _sdkCheckInFlight = false;

                EditorApplication.delayCall += () => { EditorWindow.focusedWindow?.Repaint(); };
            }
        }

        private static async System.Threading.Tasks.Task Poll()
        {
            try
            {
                var fleet = await PublisherService.GetFleetAsync();
                Publishers = fleet.workers;
                _fleetChecked = DateTimeOffset.UtcNow;
                Cooker = fleet.enabled ? CookerStatus.Online : CookerStatus.Offline;
                CookerNote = string.IsNullOrEmpty(fleet.message) ? "Availability depends on game, Unity and region" : fleet.message;
            }
            catch (Exception ex) { Cooker = CookerStatus.Error; Publishers = Array.Empty<PublisherService.Publisher>(); CookerNote = ex.Message; }
            finally
            {
                _inFlight = false;
                _nextPoll = EditorApplication.timeSinceStartup + 15.0;
                EditorApplication.delayCall += () => { EditorWindow.focusedWindow?.Repaint(); };
            }
        }

        private static bool IsRemoteVersionNewer(string installedVersion, string latestVersion)
        {
            if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(latestVersion))
                return false;

            if (string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            if (TryParseComparableVersion(installedVersion, out var installed) &&
                TryParseComparableVersion(latestVersion, out var latest))
            {
                return latest > installed;
            }

            return false;
        }

        private static bool TryParseComparableVersion(string rawVersion, out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(rawVersion))
                return false;

            var numericPrefix = System.Text.RegularExpressions.Regex.Match(rawVersion, @"^\d+(\.\d+){0,3}");
            if (!numericPrefix.Success)
                return false;

            return Version.TryParse(numericPrefix.Value, out version);
        }
    }
}

#endif
