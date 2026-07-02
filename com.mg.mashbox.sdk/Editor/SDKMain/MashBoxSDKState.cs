
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

        public static CookerStatus Cooker = CookerStatus.Unknown;
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
                using var client = new System.Net.Http.HttpClient();
                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    $"{URL}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                request.Headers.Pragma.ParseAdd("no-cache");

                var res = await client.SendAsync(request);

                if (!res.IsSuccessStatusCode)
                {
                    Cooker = CookerStatus.Error;
                    CookerNote = $"HTTP {(int)res.StatusCode}";
                }
                else
                {
                    var body = await res.Content.ReadAsStringAsync();

                    bool online = System.Text.RegularExpressions.Regex.IsMatch(
                        body,
                        "\"online\"\\s*:\\s*true",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

          
                    DateTimeOffset utc = DateTimeOffset.MinValue;
                    int i = body.IndexOf("\"utc\":", StringComparison.OrdinalIgnoreCase);

                    if (i >= 0)
                    {
                        int q1 = body.IndexOf('"', i + 6);
                        int q2 = (q1 >= 0) ? body.IndexOf('"', q1 + 1) : -1;

                        if (q1 >= 0 && q2 > q1)
                        {
                            var iso = body.Substring(q1 + 1, q2 - q1 - 1);
                            DateTimeOffset.TryParse(
                                iso,
                                null,
                                System.Globalization.DateTimeStyles.AssumeUniversal |
                                System.Globalization.DateTimeStyles.AdjustToUniversal,
                                out utc
                            );
                        }
                    }

                    var referenceUtc = res.Headers.Date ?? DateTimeOffset.UtcNow;
                    double age = (utc == DateTimeOffset.MinValue)
                        ? double.MaxValue
                        : (referenceUtc.ToUniversalTime() - utc.ToUniversalTime()).TotalSeconds;

                    if (age < 0)
                        age = 0;

           
                    if (!online)
                    {
                        Cooker = CookerStatus.Offline;
                        CookerNote = "offline";
                    }
                    else if (age > HeartbeatFreshnessSeconds)
                    {
                        Cooker = CookerStatus.Stale;
                        CookerNote = $"stale ({(int)age}s)";
                    }
                    else
                    {
                        Cooker = CookerStatus.Online;
                        CookerNote = $"ok ({(int)age}s)";
                    }
                }
            }
            catch (Exception ex)
            {
                Cooker = CookerStatus.Error;
                CookerNote = ex.Message;
            }
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
