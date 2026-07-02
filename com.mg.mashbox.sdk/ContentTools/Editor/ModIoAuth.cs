// ModIoAuth.cs (proxy-based email flow)
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    public static class ModIoAuth
    {
        // Selected game's mod.io base that you already save in EditorPrefs when "Set Game" is clicked.
        static string ApiBase => ResolveApiBase();

        // Your backend base (set this once in your tool init, or via EditorPrefs).
        static string ProxyBase => EditorPrefs.GetString("ModIo.ProxyBase", "https://YOUR_BACKEND/modio");

        // Per-game token/email storage (namespaced by api base).
        // SessionState forces a fresh mod.io login each Unity editor session,
        // while still surviving normal domain reloads during that session.
        static string TK(string apiBase) => $"modio_access_token::{apiBase}";
        static string EK(string apiBase) => $"modio_email::{apiBase}";

        public static string CurrentToken => SessionState.GetString(TK(ApiBase), "");
        public static string CurrentEmail => SessionState.GetString(EK(ApiBase), "");
        public static bool IsAuthorizedForCurrentGame() => !string.IsNullOrEmpty(CurrentToken);

        public static void ClearForCurrentGame()
        {
            SessionState.SetString(TK(ApiBase), "");
            SessionState.SetString(EK(ApiBase), "");

            // Remove tokens written by older SDK versions so they cannot be reused silently.
            EditorPrefs.DeleteKey(TK(ApiBase));
            EditorPrefs.DeleteKey(EK(ApiBase));
        }

        public static void BeginEmailRequest(string email, Action<string> onStatus)
            => EditorCoroutine.Start(CoProxyEmailRequest(email, onStatus));

        public static void ExchangeCode(string email, string code, Action<string> onStatus)
            => EditorCoroutine.Start(CoProxyExchange(email, code, onStatus));

        // --- proxy calls (no api_key on the client) ---
        static IEnumerator CoProxyEmailRequest(string email, Action<string> onStatus)
        {
            if (string.IsNullOrEmpty(email))
            {
                onStatus?.Invoke("Missing email.");
                yield break;
            }

            onStatus?.Invoke("Requesting login code...");
            var form = new List<KeyValuePair<string, string>>
            {
                new("email", email)
            };

            Debug.Log($"[mod.io][Auth] Sending EmailRequest via proxy: ProxyBase={ProxyBase}, {BuildProxyGameDebugLabel()}");

            var task = PostToProxyWithFallbackAsync("emailrequest", form);
            while (!task.IsCompleted)
                yield return null;

            if (task.IsCanceled)
            {
                onStatus?.Invoke("mod.io login request was cancelled.");
                yield break;
            }

            if (task.IsFaulted)
            {
                onStatus?.Invoke($"Failed: {task.Exception?.GetBaseException().Message ?? "mod.io login request failed."}");
                yield break;
            }

            onStatus?.Invoke(task.Result.Contains("error") ? $"Failed: {task.Result}" : "Code sent! Check your email.");
        }

        static IEnumerator CoProxyExchange(string email, string code, Action<string> onStatus)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code))
            {
                onStatus?.Invoke("Missing email or code.");
                yield break;
            }

            onStatus?.Invoke("Exchanging code for token...");
            var form = new List<KeyValuePair<string, string>>
            {
                new("email", email),
                new("security_code", code),
            };

            Debug.Log($"[mod.io][Auth] Sending Code Exchange via proxy: ProxyBase={ProxyBase}, {BuildProxyGameDebugLabel()}");

            var task = PostToProxyWithFallbackAsync("emailexchange", form);
            while (!task.IsCompleted)
                yield return null;

            if (task.IsCanceled)
            {
                onStatus?.Invoke("mod.io code exchange was cancelled.");
                yield break;
            }

            if (task.IsFaulted)
            {
                onStatus?.Invoke($"Failed: {task.Exception?.GetBaseException().Message ?? "mod.io code exchange failed."}");
                yield break;
            }

            // Expect your backend to return the raw mod.io JSON with access_token.
            string json = task.Result;
            string token = ExtractJsonValue(json, "access_token");
            if (!string.IsNullOrEmpty(token))
            {
                SessionState.SetString(TK(ApiBase), token);
                SessionState.SetString(EK(ApiBase), email);

                // Do not persist mod.io auth across Unity editor sessions.
                EditorPrefs.DeleteKey(TK(ApiBase));
                EditorPrefs.DeleteKey(EK(ApiBase));
                onStatus?.Invoke("Connected to mod.io for this game!");
            }
            else
            {
                onStatus?.Invoke($"Failed: {json}");
            }
        }

        static async Task<string> PostToProxyWithFallbackAsync(string route, IEnumerable<KeyValuePair<string, string>> form)
        {
            Exception lastException = null;

            foreach (var url in BuildProxyAuthUrls(route))
            {
                try
                {
                    return await ModioHttp.PostUrlEncodedAsync(url, form);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Debug.LogWarning($"[mod.io][Auth] Proxy {route} failed for {url}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                BuildProxyFailureMessage(route, lastException),
                lastException);
        }

        static string BuildProxyFailureMessage(string route, Exception lastException)
        {
            var lastError = lastException?.Message ?? "unknown error";
            if (lastError.IndexOf("Unknown game", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return
                    $"mod.io proxy {route} does not have this game configured ({BuildProxyGameDebugLabel()}). " +
                    "Hidden/unlaunched mod.io games can still authenticate, but the proxy must know the game id/API base and have the game's API key configured. " +
                    $"Last error: {lastError}";
            }

            return $"mod.io proxy {route} failed for {BuildProxyGameDebugLabel()}. Last error: {lastError}";
        }

        static IEnumerable<string> BuildProxyAuthUrls(string route)
        {
            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "BMXS");
            var apiBase = ApiBase.TrimEnd('/');
            var gameId = ExtractGameIdFromApiBase(apiBase);
            var preferGameId = !string.IsNullOrWhiteSpace(gameId) &&
                               string.Equals(currentGame, "ProjectX", StringComparison.OrdinalIgnoreCase);

            if (preferGameId)
                yield return BuildProxyAuthUrl(route, gameId, currentGame, apiBase, gameId);

            yield return BuildProxyAuthUrl(route, currentGame, currentGame, apiBase, gameId);

            if (!preferGameId &&
                !string.IsNullOrWhiteSpace(gameId) &&
                !string.Equals(currentGame, gameId, StringComparison.OrdinalIgnoreCase))
            {
                yield return BuildProxyAuthUrl(route, gameId, currentGame, apiBase, gameId);
            }
        }

        static string BuildProxyAuthUrl(string route, string game, string gameName, string apiBase, string gameId)
        {
            var query = new List<string>
            {
                $"game={Uri.EscapeDataString(game ?? string.Empty)}"
            };

            if (!string.IsNullOrWhiteSpace(gameName))
                query.Add($"gameName={Uri.EscapeDataString(gameName)}");

            if (!string.IsNullOrWhiteSpace(gameId))
            {
                query.Add($"gameId={Uri.EscapeDataString(gameId)}");
                query.Add($"game_id={Uri.EscapeDataString(gameId)}");
            }

            if (!string.IsNullOrWhiteSpace(apiBase))
            {
                query.Add($"apiBase={Uri.EscapeDataString(apiBase)}");
                query.Add($"api_base={Uri.EscapeDataString(apiBase)}");
            }

            return $"{ProxyBase.TrimEnd('/')}/{route}?{string.Join("&", query)}";
        }

        static string ResolveApiBase()
        {
            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
            if (TryGetRegisteredApiBase(currentGame, out var registeredApiBase))
            {
                var activeApiBase = EditorPrefs.GetString("ModIo.ApiBase", string.Empty);
                if (!string.Equals(activeApiBase?.TrimEnd('/'), registeredApiBase.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    EditorPrefs.SetString("ModIo.ApiBase", registeredApiBase);
                    Debug.Log($"[mod.io][Auth] Restored ModIo.ApiBase for {currentGame}: {registeredApiBase}");
                }

                return registeredApiBase;
            }

            var apiBase = EditorPrefs.GetString("ModIo.ApiBase", string.Empty);
            if (IsModApiBase(apiBase))
                return apiBase;

            return "https://api.mod.io/v1";
        }

        static bool TryGetRegisteredApiBase(string gameName, out string apiBase)
        {
            apiBase = string.Empty;
            if (string.IsNullOrWhiteSpace(gameName))
                return false;

            foreach (var game in GameRegistry.Games)
            {
                if (!string.Equals(game.DisplayName, gameName, StringComparison.OrdinalIgnoreCase))
                    continue;

                apiBase = game.ModIoApiBase ?? string.Empty;
                return IsModApiBase(apiBase);
            }

            return false;
        }

        static bool IsModApiBase(string apiBase)
        {
            if (string.IsNullOrWhiteSpace(apiBase) ||
                !Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase);
        }

        static string ExtractGameIdFromApiBase(string apiBase)
        {
            if (string.IsNullOrWhiteSpace(apiBase) ||
                !Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var host = uri.Host;
            var markerIndex = host.IndexOf("g-", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return string.Empty;

            var start = markerIndex + 2;
            var end = start;
            while (end < host.Length && char.IsDigit(host[end]))
                end++;

            return end > start ? host.Substring(start, end - start) : string.Empty;
        }

        static string BuildProxyGameDebugLabel()
        {
            var apiBase = ApiBase.TrimEnd('/');
            var gameId = ExtractGameIdFromApiBase(apiBase);
            return $"CurrentGame={EditorPrefs.GetString("ModIo.CurrentGame", "")}, GameId={gameId}, ApiBase={apiBase}";
        }

        // Tiny JSON value extractor.
        static string ExtractJsonValue(string json, string key)
        {
            var marker = $"\"{key}\":";
            int i = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += marker.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\"')) i++;
            int start = i;
            while (i < json.Length && json[i] != '\"' && json[i] != ',' && json[i] != '}') i++;
            return json.Substring(start, i - start).Trim('\"');
        }
    }
}
#endif
