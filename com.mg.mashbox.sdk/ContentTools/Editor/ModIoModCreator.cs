#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Exporting;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    public static class ModIoModCreator
    {
        private const int MaxModNameLength = 80;
        private const int MaxSummaryLength = 250;

        public static void DrawCreateButton(
            string gameName,
            string defaultName,
            string defaultSummary,
            Action<string> onCreated,
            EditorWindow owner,
            Texture2D defaultCoverImage = null)
        {
            var canCreate = CanCreateForGame(gameName, out var unavailableReason);
            var tooltip = canCreate
                ? $"Create a new mod.io mod for {gameName} and save the returned Mod ID."
                : unavailableReason;

            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button(new GUIContent("Create", tooltip), GUILayout.Width(68f)))
                {
                    ModIoCreateModDialog.Show(gameName, defaultName, defaultSummary, defaultCoverImage, onCreated, owner);
                }
            }
        }

        public static bool CanCreateForGame(string gameName)
        {
            return CanCreateForGame(gameName, out _);
        }

        private static bool CanCreateForGame(string gameName, out string unavailableReason)
        {
            if (!TryPrepareCurrentGameApiBase(gameName, out _, out unavailableReason))
                return false;

            if (!ModIoAuth.IsAuthorizedForCurrentGame())
            {
                unavailableReason = $"Log in to mod.io for {gameName} before creating a Mod ID.";
                return false;
            }

            return true;
        }

        private static bool TryPrepareCurrentGameApiBase(string gameName, out string apiBase, out string unavailableReason)
        {
            apiBase = string.Empty;
            unavailableReason = string.Empty;

            if (string.IsNullOrWhiteSpace(gameName))
            {
                unavailableReason = "Select a game before creating a Mod ID.";
                return false;
            }

            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
            if (!string.Equals(currentGame, gameName, StringComparison.OrdinalIgnoreCase))
            {
                unavailableReason = $"Select {gameName} as the active game before creating a Mod ID.";
                return false;
            }

            if (!TryGetRegisteredApiBase(gameName, out apiBase))
            {
                unavailableReason = $"No mod.io API base is configured for {gameName}.";
                return false;
            }

            var activeApiBase = EditorPrefs.GetString("ModIo.ApiBase", string.Empty);
            if (!string.Equals(activeApiBase?.TrimEnd('/'), apiBase.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                EditorPrefs.SetString("ModIo.ApiBase", apiBase);
                Debug.Log($"[mod.io][CreateMod] Restored ModIo.ApiBase for {gameName}: {apiBase}");
            }

            return true;
        }

        public static async Task<CreatedMod> CreateModAsync(string gameName, string name, string summary, Texture2D coverImage = null)
        {
            if (!TryPrepareCurrentGameApiBase(gameName, out var gameApiBase, out var unavailableReason))
                throw new InvalidOperationException(unavailableReason);

            name = NormalizeRequiredText(name, "Mod name", 3, MaxModNameLength);
            summary = NormalizeRequiredText(summary, "Summary", 3, MaxSummaryLength);

            var token = ModIoAuth.CurrentToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException($"Log in to mod.io for '{gameName}' before creating a Mod ID.");

            gameApiBase = gameApiBase.TrimEnd('/');
            var gameId = ExtractGameIdFromApiBase(gameApiBase);
            if (string.IsNullOrWhiteSpace(gameApiBase) || string.IsNullOrWhiteSpace(gameId))
                throw new InvalidOperationException($"No mod.io API base is configured for '{gameName}'.");

            var userId = await ResolveUserIdAsync(gameApiBase, token);
            var urls = BuildCreateModUrls(gameApiBase, gameId, userId);
            var errors = new List<string>();
            foreach (var url in urls)
            {
                try
                {
                    return await PostCreateModAsync(url, token, gameName, gameId, name, summary, coverImage);
                }
                catch (Exception ex)
                {
                    errors.Add($"{url}: {ex.Message}");
                    Debug.LogWarning($"[mod.io][CreateMod] Create mod failed for {url}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                "mod.io could not create the mod.\n\n" +
                string.Join("\n\n", errors));
        }

        private static async Task<CreatedMod> PostCreateModAsync(
            string url,
            string token,
            string gameName,
            string gameId,
            string name,
            string summary,
            Texture2D coverImage)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var form = new MultipartFormDataContent();
            AddFormValue(form, "game_id", gameId);
            AddFormValue(form, "name", name);
            AddFormValue(form, "summary", summary);
            AddFormValue(form, "description", summary);
            AddFormValue(form, "visible", "0");
            AddCoverImageFormValue(form, coverImage, gameName);

            using var response = await http.PostAsync(url, form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 401)
                    ModIoAuth.ClearForCurrentGame();

                throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            var id = ExtractId(body);
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"mod.io created a response without a readable id: {body}");

            return new CreatedMod(id, body);
        }

        private static void AddFormValue(MultipartFormDataContent form, string key, string value)
        {
            form.Add(new StringContent(value ?? string.Empty, Encoding.UTF8), key);
        }

        private static void AddCoverImageFormValue(MultipartFormDataContent form, Texture2D coverImage, string gameName)
        {
            var assetPath = ResolveCoverImageAssetPath(coverImage, gameName);
            if (!TryReadAssetBytes(assetPath, out var bytes, out var fileName, out var contentType))
                throw new InvalidOperationException($"Could not read a cover image for mod.io creation. Tried: {assetPath}");

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, "logo", fileName);
        }

        private static string ResolveCoverImageAssetPath(Texture2D coverImage, string gameName)
        {
            if (coverImage != null)
            {
                var texturePath = AssetDatabase.GetAssetPath(coverImage);
                if (!string.IsNullOrWhiteSpace(texturePath))
                    return texturePath;
            }

            var gameLogoPath = MashBoxEditorResources.GetGameLogo(gameName);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(gameLogoPath) != null)
                return gameLogoPath;

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.MODIO) != null)
                return MashBoxEditorResources.MODIO;

            return MashBoxEditorResources.HEADER;
        }

        private static bool TryReadAssetBytes(string assetPath, out byte[] bytes, out string fileName, out string contentType)
        {
            bytes = null;
            fileName = string.IsNullOrWhiteSpace(assetPath) ? "logo.png" : Path.GetFileName(assetPath);
            contentType = GetImageContentType(assetPath);

            var fullPath = ResolveAssetFullPath(assetPath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return false;

            bytes = File.ReadAllBytes(fullPath);
            return bytes.Length > 0;
        }

        private static string ResolveAssetFullPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return string.Empty;

            assetPath = assetPath.Replace("\\", "/");
            if (Path.IsPathRooted(assetPath))
                return assetPath;

            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return string.IsNullOrWhiteSpace(projectRoot)
                    ? string.Empty
                    : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            }

            if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                {
                    var packageAssetRoot = (packageInfo.assetPath ?? string.Empty).Replace("\\", "/").TrimEnd('/');
                    var relativePath = assetPath;
                    if (!string.IsNullOrWhiteSpace(packageAssetRoot) &&
                        assetPath.StartsWith(packageAssetRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = assetPath.Substring(packageAssetRoot.Length).TrimStart('/', '\\');
                    }

                    return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relativePath));
                }
            }

            return Path.GetFullPath(assetPath);
        }

        private static string GetImageContentType(string assetPath)
        {
            var extension = Path.GetExtension(assetPath ?? string.Empty);
            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : "image/png";
        }

        private static List<string> BuildCreateModUrls(string gameApiBase, string gameId, string userId)
        {
            var urls = new List<string>();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var userApiBase = $"https://u-{Uri.EscapeDataString(userId)}.modapi.io/v1";
                AddUnique(urls, $"{userApiBase}/games/{Uri.EscapeDataString(gameId)}/mods");
                AddUnique(urls, $"{userApiBase}/mods");
            }

            AddUnique(urls, $"{gameApiBase}/games/{Uri.EscapeDataString(gameId)}/mods");
            AddUnique(urls, $"{gameApiBase}/mods");
            return urls;
        }

        private static async Task<string> ResolveUserIdAsync(string gameApiBase, string token)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.GetAsync($"{gameApiBase}/me");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Could not read mod.io user id: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

            var userId = ExtractJsonNumber(body, "user_id");
            if (string.IsNullOrWhiteSpace(userId))
                userId = ExtractJsonNumber(body, "id");

            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException($"Could not read mod.io user id from /me response: {body}");

            return userId;
        }

        private static bool TryGetRegisteredApiBase(string gameName, out string apiBase)
        {
            apiBase = string.Empty;
            foreach (var game in GameRegistry.Games)
            {
                if (!string.Equals(game.DisplayName, gameName, StringComparison.OrdinalIgnoreCase))
                    continue;

                apiBase = game.ModIoApiBase ?? string.Empty;
                return IsModApiBase(apiBase);
            }

            return false;
        }

        private static bool IsModApiBase(string apiBase)
        {
            if (string.IsNullOrWhiteSpace(apiBase) ||
                !Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Host.EndsWith(".modapi.io", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractGameIdFromApiBase(string apiBase)
        {
            if (string.IsNullOrWhiteSpace(apiBase) ||
                !Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var match = Regex.Match(uri.Host, @"(?:^|\.)g-(\d+)\.", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractId(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var modIdMatch = Regex.Match(json, "\"mod_id\"\\s*:\\s*\"?(\\d+)\"?", RegexOptions.IgnoreCase);
            if (modIdMatch.Success)
                return modIdMatch.Groups[1].Value;

            var idMatch = Regex.Match(json, "\"id\"\\s*:\\s*\"?(\\d+)\"?", RegexOptions.IgnoreCase);
            return idMatch.Success ? idMatch.Groups[1].Value : string.Empty;
        }

        private static string ExtractJsonNumber(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"?(\\d+)\"?", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string NormalizeRequiredText(string value, string label, int minLength, int maxLength)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length < minLength)
                throw new ArgumentException($"{label} must be at least {minLength} characters.");

            return value.Length <= maxLength ? value : value.Substring(0, maxLength).Trim();
        }

        private static void AddUnique(List<string> urls, string url)
        {
            if (!urls.Contains(url))
                urls.Add(url);
        }

        public readonly struct CreatedMod
        {
            public CreatedMod(string id, string rawJson)
            {
                Id = id;
                RawJson = rawJson;
            }

            public string Id { get; }
            public string RawJson { get; }
        }

        private sealed class ModIoCreateModDialog : EditorWindow
        {
            private string gameName;
            private string modName;
            private string summary;
            private Texture2D coverImage;
            private Action<string> onCreated;
            private EditorWindow owner;
            private bool isCreating;
            private string status;

            public static void Show(
                string gameName,
                string defaultName,
                string defaultSummary,
                Texture2D defaultCoverImage,
                Action<string> onCreated,
                EditorWindow owner)
            {
                var window = CreateInstance<ModIoCreateModDialog>();
                window.titleContent = new GUIContent("Create mod.io Mod");
                window.gameName = gameName;
                window.modName = BuildDefaultName(defaultName);
                window.summary = BuildDefaultSummary(defaultSummary, window.modName);
                window.coverImage = defaultCoverImage != null ? defaultCoverImage : LoadDefaultCoverImage(gameName);
                window.onCreated = onCreated;
                window.owner = owner;
                window.minSize = new Vector2(420f, 190f);
                window.maxSize = new Vector2(620f, 260f);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Game", gameName);

                GUI.SetNextControlName("modNameText");
                modName = EditorGUILayout.TextField("Mod Name", modName);
                summary = EditorGUILayout.TextField("Summary", summary);
                coverImage = (Texture2D)EditorGUILayout.ObjectField("Cover Image", coverImage, typeof(Texture2D), false);

                EditorGUILayout.HelpBox(
                    "This creates a private mod.io profile and saves its Mod ID for publishing. Finish the full mod profile details later in the mod.io web portal before making it public. If no cover image is set, the SDK will use the selected game's editor icon. Terms: https://mod.io/terms",
                    MessageType.Info);

                if (!string.IsNullOrWhiteSpace(status))
                    EditorGUILayout.HelpBox(status, isCreating ? MessageType.Info : MessageType.Warning);

                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(isCreating))
                    {
                        if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
                            Close();
                    }

                    using (new EditorGUI.DisabledScope(isCreating || string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(summary)))
                    {
                        if (GUILayout.Button(isCreating ? "Creating..." : "Create", GUILayout.Width(90f)))
                            CreateAsync();
                    }
                }

                EditorGUI.FocusTextInControl("modNameText");
            }

            private async void CreateAsync()
            {
                isCreating = true;
                status = "Creating mod profile...";
                Repaint();

                try
                {
                    var created = await ModIoModCreator.CreateModAsync(gameName, modName, summary, coverImage);
                    onCreated?.Invoke(created.Id);
                    owner?.Repaint();

                    EditorUtility.DisplayDialog(
                        "Mod ID Created",
                        $"Created private mod profile '{modName}' for {gameName}.\n\nMod ID: {created.Id}\n\nFinish the mod profile details in the mod.io web portal before making it public.",
                        "OK");

                    Close();
                }
                catch (Exception ex)
                {
                    status = ex.Message;
                    Debug.LogError($"[mod.io][CreateMod] {ex}");
                }
                finally
                {
                    isCreating = false;
                    Repaint();
                }
            }

            private static string BuildDefaultName(string value)
            {
                value = (value ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(value) ? "New MashBox Mod" : value;
            }

            private static string BuildDefaultSummary(string value, string modName)
            {
                value = (value ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Length <= MaxSummaryLength ? value : value.Substring(0, MaxSummaryLength).Trim();

                return $"Created from MashBox SDK for {modName}.";
            }

            private static Texture2D LoadDefaultCoverImage(string gameName)
            {
                var gameLogoPath = MashBoxEditorResources.GetGameLogo(gameName);
                var gameLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(gameLogoPath);
                if (gameLogo != null)
                    return gameLogo;

                var modioLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.MODIO);
                if (modioLogo != null)
                    return modioLogo;

                return AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.HEADER);
            }
        }
    }
}
#endif
