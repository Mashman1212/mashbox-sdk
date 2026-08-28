#if UNITY_EDITOR

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using MashBoxSDK.ContentTools.Editor;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Exporting;

namespace MashBoxSDK.SDKMain
{
    class MashBoxSetupWindow : EditorWindow
    {
        private enum SetupTab
        {
            SDK,
            PatchNotes,
            Game,
            Login,
            Samples
        }

        private const string PREF_KEY_SETUP_TAB = "MashBoxSDK.SelectedSetupTab";
        private const string SDK_PACKAGE_NAME = "com.mg.mashbox.sdk";
        private const string SDK_PACKAGE_GIT_URL = "https://github.com/Mashman1212/mashbox-sdk.git?path=com.mg.mashbox.sdk#main";
        private const string SDK_CHANGELOG_FILE = "CHANGELOG.md";
        private const string SDK_CHANGELOG_ASSET_PATH = "Packages/" + SDK_PACKAGE_NAME + "/" + SDK_CHANGELOG_FILE;
        private const string SDK_CHANGELOG_URL = "https://raw.githubusercontent.com/Mashman1212/mashbox-sdk/main/com.mg.mashbox.sdk/CHANGELOG.md";
        private const string SAMPLES_PACKAGE_NAME = "com.mg.mashbox.samples";
        private const string SAMPLES_PACKAGE_GIT_URL = "https://github.com/Mashman1212/mashbox-sdk-samples.git?path=com.mg.mashbox.samples#main";
        
        // -------------------------------
        // GAME TARGET
        // -------------------------------
        private List<GameTarget> _installedGames = new List<GameTarget>();
        private Dictionary<string, Texture2D> _gameLogoCache = new();
        private long _lastChosenAppId;

        private const string PREF_KEY_LAST_APP = "ContentPackBuilder.LastChosenAppId";

        // -------------------------------
        // STEAM
        // -------------------------------
        private string _steamRoot = "";
        private const string PREF_STEAM_ROOT = "MashBoxSDK.SteamRootOverride";
        
        // -------------------------------
        // MOD.IO
        // -------------------------------
        private string _emailInput = "";
        private const string PREF_KEY_MODIO_EMAIL = "MashBoxSDK.ModIoLoginEmail";
        //private string _codeInput = "";
        private string _statusMsg = "";
        private Texture2D _modioLogo;

        // -------------------------------
        // PACKAGE STATE
        // -------------------------------
        private int _setupTab;
        private AddRequest _sdkAddRequest;
        private string _sdkStatus = "";
        private ListRequest _samplesListRequest;
        private AddRequest _samplesAddRequest;
        private RemoveRequest _samplesRemoveRequest;
        private double _samplesRequestStartedAt;
        private string _samplesInstalledVersion = "Unknown";
        private string _samplesStatus = "Checking package state...";
        private bool _samplesInstalled;
        private string _samplesPackageId = "";
        private string _legacySamplesStatus = "";
        private string _inputSystemStatus = "";
        private string _patchNotesText = "";
        private string _patchNotesStatus = "";
        private string _patchNotesLoadedPath = "";
        private bool _patchNotesFetchInFlight;
        private bool _showPatchNotesOnSdkTab = true;
        private Vector2 _scrollPosition;
        
        // -------------------------------
        // INIT
        // -------------------------------
        public void OnEnable()
        {
            _setupTab = EditorPrefs.GetInt(PREF_KEY_SETUP_TAB, 0);
            _installedGames = GameTargetResolver.GetAllGames();

            _lastChosenAppId = long.TryParse( EditorPrefs.GetString(PREF_KEY_LAST_APP, "0"),
                out var v) ? v : 0;
            
            _buildLocation = EditorPrefs.GetString("BuildLocation", "");
            
            _steamRoot = EditorPrefs.GetString(PREF_STEAM_ROOT, "");
            _emailInput = EditorPrefs.GetString(PREF_KEY_MODIO_EMAIL, "");

            if (string.IsNullOrEmpty(_steamRoot))
            {
                _steamRoot = SteamLocator.GetSteamRoot();
            }

            _modioLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(MashBoxEditorResources.MODIO);
            
            MashBoxSDKState.CheckForSdkUpdate();
            
            string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "");

            if (!string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase))
            {
                RefreshBuildLocationFromTarget();
            }

            RefreshSamplesPackageState();
            RefreshPatchNotes();
        }
        
        private void RefreshBuildLocationFromTarget()
        {
            // ✅ THIS is the correct source of truth
            if (_lastChosenAppId == 0)
                return;

            var game = _installedGames.FirstOrDefault(g =>
                g.Definition.SteamAppId == _lastChosenAppId);

            if (game == null || string.IsNullOrEmpty(game.InstallPath))
                return;

            var sa = StreamingAssetsResolver.TryResolve(game.InstallPath);

            if (string.IsNullOrEmpty(sa))
                return;

            var final = StreamingAssetsResolver.AppendSubfolder(sa, STREAMING_SUBPATH);

            _buildLocation = final;

            EditorPrefs.SetString("BuildLocation", _buildLocation);
        }
        // -------------------------------
        // MAIN DRAW
        // -------------------------------
        public void Draw()
        {
            MashBoxSDKState.Update();
            UpdateSdkPackageRequests();
            UpdateSamplesPackageRequests();
#if UNITY_EDITOR
            MashBoxInputSystemSetup.UpdateRequests();
            _inputSystemStatus = MashBoxInputSystemSetup.LastStatusMessage;
#endif

            if (IsSamplesBusy() || IsSdkBusy()
#if UNITY_EDITOR
                || MashBoxInputSystemSetup.IsBusy
#endif
               )
                Repaint();

            DrawSetupTabs();
            GUILayout.Space(6);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch ((SetupTab)_setupTab)
            {
                case SetupTab.SDK:
                    DrawSdkSetupTab();
                    break;
                case SetupTab.PatchNotes:
                    DrawPatchNotesSetupTab();
                    break;
                case SetupTab.Game:
                    DrawGameSetupTab();
                    break;
                case SetupTab.Login:
                    DrawLoginSetupTab();
                    break;
                case SetupTab.Samples:
                    DrawSamplesSetupTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSetupTabs()
        {
            int newTab = MashBoxTabDrawer.DrawTabs(_setupTab, new[]
            {
                "SDK",
                "Patch Notes",
                "Game",
                "Mod.io Login",
                "Samples"
            }, MashBoxTabDrawer.TabVisualStyle.Secondary, new[]
            {
#if UNITY_EDITOR
                MashBoxSDKState.UpdateAvailable || !MashBoxInputSystemSetup.HasInputSystemReady,
#else
                MashBoxSDKState.UpdateAvailable,
#endif
                false,
                false,
                false,
                HasDeprecatedSampleFolders()
            });

            if (newTab == _setupTab)
                return;

            _setupTab = newTab;
            EditorPrefs.SetInt(PREF_KEY_SETUP_TAB, _setupTab);
        }

        private void DrawSdkSetupTab()
        {
            DrawThemeSection();
            GUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("SDK Package", EditorStyles.boldLabel);
                DrawSdkUpdaterUI();
                GUILayout.Space(6);

                DrawCookerStatus();
                GUILayout.Space(6);

                EditorGUILayout.LabelField("Package", SDK_PACKAGE_NAME, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Source", SDK_PACKAGE_GIT_URL, EditorStyles.wordWrappedMiniLabel);

                if (!string.IsNullOrEmpty(_sdkStatus))
                    EditorGUILayout.HelpBox(_sdkStatus, MashBoxSDKState.UpdateAvailable ? MessageType.Warning : MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(MashBoxSDKState.CheckingSdk || IsSdkBusy() || !MashBoxSDKState.UpdateAvailable))
                    {
                        if (GUILayout.Button("Update SDK", GUILayout.Height(24f)))
                        {
                            _sdkStatus = "Updating SDK package from Git...";
                            _sdkAddRequest = Client.Add(SDK_PACKAGE_GIT_URL);
                        }
                    }

                    using (new EditorGUI.DisabledScope(MashBoxSDKState.CheckingSdk || IsSdkBusy()))
                    {
                        if (GUILayout.Button("Check For Updates", GUILayout.Width(140f)))
                        {
                            _sdkStatus = "Checking SDK package...";
                            MashBoxSDKState.CheckForSdkUpdate();
                        }
                    }
                }

                if (GUILayout.Button("Open Package Manager"))
                {
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");
                }
            }

            GUILayout.Space(6f);
            DrawPatchNotesSection(compact: true);

            GUILayout.Space(6f);
#if UNITY_EDITOR
            DrawInputSystemSection();
#endif
        }

        private void DrawPatchNotesSetupTab()
        {
            DrawPatchNotesSection(compact: false);
        }

        private void DrawPatchNotesSection(bool compact)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (compact)
                {
                    _showPatchNotesOnSdkTab = EditorGUILayout.Foldout(_showPatchNotesOnSdkTab, "Patch Notes", true);
                    if (!_showPatchNotesOnSdkTab)
                        return;
                }
                else
                {
                    EditorGUILayout.LabelField("MashBox SDK Patch Notes", EditorStyles.boldLabel);
                }

                EditorGUILayout.LabelField($"Installed: {MashBoxSDKState.InstalledVersion}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"GitHub Main: {MashBoxSDKState.LatestVersion}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Notes are loaded from the package CHANGELOG.md that ships with each SDK version.", EditorStyles.wordWrappedMiniLabel);

                GUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_patchNotesFetchInFlight))
                    {
                        if (GUILayout.Button("Refresh Installed", GUILayout.Height(22f)))
                            RefreshPatchNotes();

                        if (GUILayout.Button("Fetch GitHub Notes", GUILayout.Height(22f)))
                            BeginFetchLatestPatchNotes();
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_patchNotesLoadedPath)))
                    {
                        if (GUILayout.Button("Select Changelog", GUILayout.Width(130f), GUILayout.Height(22f)))
                            SelectPatchNotesAsset();
                    }
                }
            }

            GUILayout.Space(6f);

            if (_patchNotesFetchInFlight)
                EditorGUILayout.HelpBox("Fetching latest patch notes from GitHub...", MessageType.None);

            if (!string.IsNullOrEmpty(_patchNotesStatus))
                EditorGUILayout.HelpBox(_patchNotesStatus, string.IsNullOrEmpty(_patchNotesText) ? MessageType.Warning : MessageType.Info);

            if (string.IsNullOrEmpty(_patchNotesText))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPatchNotesMarkdown(_patchNotesText);
            }
        }

        private void RefreshPatchNotes()
        {
            _patchNotesText = "";
            _patchNotesLoadedPath = "";

            foreach (var path in GetPatchNotesCandidatePaths())
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                try
                {
                    _patchNotesText = File.ReadAllText(path);
                    _patchNotesLoadedPath = path;
                    _patchNotesStatus = $"Loaded installed patch notes from {Path.GetFileName(path)}.";
                    return;
                }
                catch (Exception ex)
                {
                    _patchNotesStatus = $"Could not read {SDK_CHANGELOG_FILE}: {ex.Message}";
                    return;
                }
            }

            _patchNotesStatus = $"No {SDK_CHANGELOG_FILE} found in the MashBox SDK package.";
        }

        private void BeginFetchLatestPatchNotes()
        {
            if (_patchNotesFetchInFlight)
                return;

            _patchNotesFetchInFlight = true;
            _patchNotesStatus = "";
            _ = FetchLatestPatchNotes();
        }

        private async System.Threading.Tasks.Task FetchLatestPatchNotes()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MashBoxSDKPatchNotes");

                var response = await http.GetAsync(SDK_CHANGELOG_URL);
                if (!response.IsSuccessStatusCode)
                {
                    _patchNotesStatus = $"Could not fetch GitHub patch notes. HTTP {(int)response.StatusCode}.";
                    return;
                }

                _patchNotesText = await response.Content.ReadAsStringAsync();
                _patchNotesLoadedPath = "";
                _patchNotesStatus = "Loaded latest patch notes from GitHub main.";
            }
            catch (Exception ex)
            {
                _patchNotesStatus = $"Could not fetch GitHub patch notes: {ex.Message}";
            }
            finally
            {
                _patchNotesFetchInFlight = false;
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                        Repaint();
                };
            }
        }

        private static IEnumerable<string> GetPatchNotesCandidatePaths()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + SDK_PACKAGE_NAME + "/package.json");
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                yield return Path.Combine(packageInfo.resolvedPath, SDK_CHANGELOG_FILE);

            var projectPackagePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", SDK_PACKAGE_NAME, SDK_CHANGELOG_FILE));
            yield return projectPackagePath;
        }

        private static void DrawPatchNotesMarkdown(string markdown)
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                wordWrap = true
            };

            var releaseStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                wordWrap = true
            };

            var sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                wordWrap = true
            };

            var bodyStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                wordWrap = true
            };

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var drewRelease = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    GUILayout.Space(4f);
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    EditorGUILayout.LabelField(trimmed.Substring(2).Trim(), titleStyle);
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (drewRelease)
                        DrawPatchNotesDivider();

                    EditorGUILayout.LabelField(trimmed.Substring(3).Trim(), releaseStyle);
                    drewRelease = true;
                    continue;
                }

                if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                {
                    EditorGUILayout.LabelField(trimmed.Substring(4).Trim(), sectionStyle);
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    EditorGUILayout.LabelField("- " + trimmed.Substring(2).Trim(), bodyStyle);
                    continue;
                }

                EditorGUILayout.LabelField(trimmed, bodyStyle);
            }
        }

        private static void DrawPatchNotesDivider()
        {
            GUILayout.Space(4f);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, MashBoxEditorTheme.Border(false));
            GUILayout.Space(4f);
        }

        private void SelectPatchNotesAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SDK_CHANGELOG_ASSET_PATH);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return;
            }

            if (!string.IsNullOrEmpty(_patchNotesLoadedPath))
                EditorUtility.RevealInFinder(_patchNotesLoadedPath);
        }

        private void DrawThemeSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("UI Theme", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    int current = MashBoxEditorTheme.SelectedIndex;
                    EditorGUI.BeginChangeCheck();
                    int selected = EditorGUILayout.Popup("Theme", current, MashBoxEditorTheme.Names);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MashBoxEditorTheme.SelectedIndex = selected;
                        Repaint();
                        EditorWindow.focusedWindow?.Repaint();
                    }

                    DrawThemeSwatch(MashBoxEditorTheme.Current.Primary, GUILayout.Width(28f), GUILayout.Height(18f));
                    DrawThemeSwatch(MashBoxEditorTheme.Current.Secondary, GUILayout.Width(28f), GUILayout.Height(18f));
                    DrawThemeSwatch(MashBoxEditorTheme.Current.Accent, GUILayout.Width(28f), GUILayout.Height(18f));
                }

                EditorGUILayout.LabelField(MashBoxEditorTheme.CurrentDescription, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawThemeSwatch(Color color, params GUILayoutOption[] options)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, options);
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), MashBoxEditorTheme.WithAlpha(Color.white, 0.25f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), MashBoxEditorTheme.WithAlpha(Color.black, 0.35f));
        }

        private void DrawGameSetupTab()
        {
#if MashBoxDev
            DrawProjectSettingsSection();
            GUILayout.Space(6);
#endif

            DrawSteamRootSection();
            GUILayout.Space(6);

            DrawGameSelection();
        }

        private void DrawLoginSetupTab()
        {
            DrawModIoSection();
        }

        private void DrawSamplesSetupTab()
        {
            DrawDeprecatedSamplesWarning();
            GUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Samples Package", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "After installing this package, open Package Manager, select MashBox Samples, then use its Samples tab to import the sample packs you want.",
                    MessageType.Info);

                EditorGUILayout.LabelField("Package", SAMPLES_PACKAGE_NAME, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Source", SAMPLES_PACKAGE_GIT_URL, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Installed Version", _samplesInstalled ? _samplesInstalledVersion : "Not Installed", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(_samplesStatus))
                {
                    var statusType = _samplesInstalled ? MessageType.Info : MessageType.None;
                    EditorGUILayout.HelpBox(_samplesStatus, statusType);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(IsSamplesPackageMutationBusy()))
                    {
                        var buttonLabel = _samplesInstalled ? "Update / Reinstall" : "Install Samples";

                        if (GUILayout.Button(buttonLabel, GUILayout.Height(24)))
                        {
                            if (TrySetManifestDependency(SAMPLES_PACKAGE_NAME, SAMPLES_PACKAGE_GIT_URL, out var error))
                            {
                                _samplesStatus = "Added samples package to Packages/manifest.json. Unity may take a moment to resolve it.";
                                _samplesInstalledVersion = "Resolving...";
                                _samplesInstalled = true;
                                _samplesRequestStartedAt = EditorApplication.timeSinceStartup;
                                RefreshSamplesPackageState(forceStatusMessage: false);
                            }
                            else
                            {
                                _samplesStatus = $"Failed to update Packages/manifest.json: {error}";
                            }
                        }
                    }

                    using (new EditorGUI.DisabledScope(_samplesListRequest != null && !_samplesListRequest.IsCompleted))
                    {
                        if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                            RefreshSamplesPackageState(forceStatusMessage: false);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Package Manager"))
                    {
                        EditorApplication.ExecuteMenuItem("Window/Package Manager");
                    }

                    using (new EditorGUI.DisabledScope(!_samplesInstalled || IsSamplesPackageMutationBusy()))
                    {
                        if (GUILayout.Button("Remove Package"))
                        {
                            _samplesStatus = "Removing samples package...";
                            _samplesRequestStartedAt = EditorApplication.timeSinceStartup;
                            _samplesRemoveRequest = Client.Remove(SAMPLES_PACKAGE_NAME);
                        }
                    }
                }
            }

        }

        private void DrawDeprecatedSamplesWarning()
        {
            var deprecatedFolders = GetDeprecatedSampleFolderPaths();
            if (deprecatedFolders.Count == 0)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var headerRect = EditorGUILayout.BeginVertical();
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, headerRect.width, headerRect.height), new Color(0.4f, 0.08f, 0.08f, 0.16f));

                var headerStyle = new GUIStyle(EditorStyles.boldLabel);
                headerStyle.normal.textColor = new Color(1f, 0.45f, 0.45f, 1f);
                EditorGUILayout.LabelField("Deprecated Sample Import Detected", headerStyle);
                EditorGUILayout.EndVertical();

                EditorGUILayout.HelpBox(
                    "The old Assets/Samples style import is no longer supported for MashBox SDK samples. Please remove these legacy sample folders and use the package Samples workflow instead.",
                    MessageType.Error);

                foreach (var folder in deprecatedFolders)
                    EditorGUILayout.LabelField(folder, EditorStyles.wordWrappedMiniLabel);

                if (!string.IsNullOrEmpty(_legacySamplesStatus))
                    EditorGUILayout.HelpBox(_legacySamplesStatus, MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fix And Delete Legacy Samples", GUILayout.Height(24f)))
                        DeleteDeprecatedSampleFolders(deprecatedFolders);

                    if (GUILayout.Button("Select Legacy Folder", GUILayout.Height(24f)))
                        SelectDeprecatedSampleFolder(deprecatedFolders[0]);
                }
            }
        }

        private bool IsSamplesBusy()
        {
            return (_samplesListRequest != null && !_samplesListRequest.IsCompleted)
                   || (_samplesAddRequest != null && !_samplesAddRequest.IsCompleted)
                   || (_samplesRemoveRequest != null && !_samplesRemoveRequest.IsCompleted);
        }

        private bool IsSamplesPackageMutationBusy()
        {
            return _samplesRemoveRequest != null && !_samplesRemoveRequest.IsCompleted;
        }

        private bool IsSdkBusy()
        {
            return _sdkAddRequest != null && !_sdkAddRequest.IsCompleted;
        }

        private static bool HasDeprecatedSampleFolders()
        {
            return GetDeprecatedSampleFolderPaths().Count > 0;
        }

        private static List<string> GetDeprecatedSampleFolderPaths()
        {
            var folders = new List<string>();
            var samplesRoot = Path.Combine(Application.dataPath, "Samples");
            if (!Directory.Exists(samplesRoot))
                return folders;

            var matches = Directory.GetDirectories(samplesRoot, "MashBox SDK", SearchOption.AllDirectories);
            foreach (var match in matches)
            {
                var assetPath = "Assets" + match.Substring(Application.dataPath.Length).Replace("\\", "/");
                if (AssetDatabase.IsValidFolder(assetPath))
                    folders.Add(assetPath);
            }

            return folders.Distinct().OrderBy(path => path).ToList();
        }

        private void DeleteDeprecatedSampleFolders(List<string> folderPaths)
        {
            if (folderPaths == null || folderPaths.Count == 0)
                return;

            var message = "This will delete the deprecated legacy sample folder(s):\n\n"
                          + string.Join("\n", folderPaths)
                          + "\n\nUse this only for the old Assets/Samples import style. Continue?";

            if (!EditorUtility.DisplayDialog("Delete Legacy Samples", message, "Delete", "Cancel"))
                return;

            var deletedAny = false;
            foreach (var folderPath in folderPaths.OrderByDescending(path => path.Length))
            {
                if (!AssetDatabase.IsValidFolder(folderPath))
                    continue;

                if (AssetDatabase.DeleteAsset(folderPath))
                    deletedAny = true;
            }

            AssetDatabase.Refresh();
            _legacySamplesStatus = deletedAny
                ? "Deleted deprecated MashBox SDK sample folders from Assets/Samples."
                : "No legacy MashBox SDK sample folders were deleted.";
        }

        private void SelectDeprecatedSampleFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            if (folder == null)
                return;

            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

#if UNITY_EDITOR
        private void DrawInputSystemSection()
        {
            var hasInputSystemReady = MashBoxInputSystemSetup.HasInputSystemReady;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var headerRect = EditorGUILayout.BeginVertical();
                if (!hasInputSystemReady && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, headerRect.width, headerRect.height), new Color(0.4f, 0.08f, 0.08f, 0.16f));

                var headerStyle = new GUIStyle(EditorStyles.boldLabel);
                if (!hasInputSystemReady)
                    headerStyle.normal.textColor = new Color(1f, 0.45f, 0.45f, 1f);

                EditorGUILayout.LabelField("Input System", headerStyle);
                EditorGUILayout.EndVertical();

                if (hasInputSystemReady)
                {
                    EditorGUILayout.HelpBox(
                        "The new Unity Input System is installed and enabled. Freecam playtesting tools are ready to use.",
                        MessageType.Info);
                }
                else
                {
                    var statusSummary = MashBoxInputSystemSetup.GetStatusSummary();
                    EditorGUILayout.HelpBox(
                        $"The SDK freecam tools require the Unity Input System package and Active Input Handling set to Input System Package or Both.\n\nCurrent status: {statusSummary}.",
                        MessageType.Warning);
                }

                if (!string.IsNullOrEmpty(_inputSystemStatus))
                {
                    var statusStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                    if (!hasInputSystemReady)
                        statusStyle.normal.textColor = new Color(1f, 0.6f, 0.6f, 1f);

                    EditorGUILayout.LabelField(_inputSystemStatus, statusStyle);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(MashBoxInputSystemSetup.IsBusy || MashBoxInputSystemSetup.IsInputSystemPackageInstalled))
                    {
                        if (GUILayout.Button("Install Input System", GUILayout.Height(24f)))
                        {
                            MashBoxInputSystemSetup.InstallInputSystemPackage();
                            _inputSystemStatus = MashBoxInputSystemSetup.LastStatusMessage;
                        }
                    }

                    using (new EditorGUI.DisabledScope(MashBoxInputSystemSetup.IsBusy || !MashBoxInputSystemSetup.IsInputSystemPackageInstalled || MashBoxInputSystemSetup.IsInputSystemEnabled))
                    {
                        if (GUILayout.Button("Enable In Project Settings", GUILayout.Height(24f)))
                        {
                            MashBoxInputSystemSetup.EnableInputSystem();
                            _inputSystemStatus = MashBoxInputSystemSetup.LastStatusMessage;
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Player Settings"))
                        SettingsService.OpenProjectSettings("Project/Player");

                    using (new EditorGUI.DisabledScope(MashBoxInputSystemSetup.IsBusy))
                    {
                        if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                            MashBoxInputSystemSetup.RefreshPackageState();
                    }
                }
            }
        }
#endif

        private void UpdateSdkPackageRequests()
        {
            if (_sdkAddRequest == null || !_sdkAddRequest.IsCompleted)
                return;

            _sdkStatus = _sdkAddRequest.Status == StatusCode.Success
                ? "SDK package updated. Unity may take a moment to reimport scripts."
                : $"Failed to update SDK package: {_sdkAddRequest.Error?.message}";

            _sdkAddRequest = null;
            MashBoxSDKState.CheckForSdkUpdate();
            Repaint();
        }

        private void RefreshSamplesPackageState(bool forceStatusMessage = true)
        {
            if (_samplesListRequest != null && !_samplesListRequest.IsCompleted)
                return;

            if (forceStatusMessage)
                _samplesStatus = "Checking package state...";
            _samplesListRequest = Client.List(true);
        }

        private void UpdateSamplesPackageRequests()
        {
            if (_samplesListRequest != null && _samplesListRequest.IsCompleted)
            {
                if (_samplesListRequest.Status == StatusCode.Success)
                {
                    var pkg = _samplesListRequest.Result.FirstOrDefault(p => p.name == SAMPLES_PACKAGE_NAME);
                    _samplesInstalled = pkg != null;
                    _samplesInstalledVersion = pkg?.version ?? "Unknown";
                    _samplesPackageId = pkg?.packageId ?? "";

                    _samplesStatus = pkg == null
                        ? "Samples package is not installed."
                        : $"Samples package is installed{(string.IsNullOrEmpty(_samplesPackageId) ? "." : $": {_samplesPackageId}")}";
                }
                else
                {
                    _samplesInstalled = false;
                    _samplesInstalledVersion = "Unknown";
                    _samplesPackageId = "";
                    _samplesStatus = $"Unable to check packages: {_samplesListRequest.Error?.message}";
                }

                _samplesListRequest = null;
                Repaint();
            }

            if (_samplesRemoveRequest != null && _samplesRemoveRequest.IsCompleted)
            {
                _samplesStatus = _samplesRemoveRequest.Status == StatusCode.Success
                    ? "Samples package removed."
                    : $"Failed to remove samples package: {_samplesRemoveRequest.Error?.message}";

                _samplesRemoveRequest = null;
                _samplesRequestStartedAt = 0d;
                RefreshSamplesPackageState();
                Repaint();
            }

            if (_samplesListRequest != null && !_samplesListRequest.IsCompleted)
            {
                var elapsedSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - _samplesRequestStartedAt);
                if (elapsedSeconds >= 8d)
                {
                    _samplesStatus = $"Waiting for Unity to resolve samples from Packages/manifest.json ({Mathf.FloorToInt((float)elapsedSeconds)}s).";
                }
            }
        }

        private static bool TrySetManifestDependency(string packageName, string packageValue, out string error)
        {
            error = null;

            try
            {
                var manifestPath = GetProjectManifestPath();
                if (!File.Exists(manifestPath))
                {
                    error = $"Manifest not found at {manifestPath}";
                    return false;
                }

                var original = File.ReadAllText(manifestPath);
                var updated = UpsertManifestDependency(original, packageName, packageValue);
                if (string.Equals(original, updated, StringComparison.Ordinal))
                    return true;

                File.WriteAllText(manifestPath, updated);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string UpsertManifestDependency(string manifestJson, string packageName, string packageValue)
        {
            var dependencyPattern = $"\"{Regex.Escape(packageName)}\"\\s*:\\s*\"[^\"]*\"";
            if (Regex.IsMatch(manifestJson, dependencyPattern))
            {
                return new Regex(dependencyPattern).Replace(
                    manifestJson,
                    $"\"{packageName}\": \"{packageValue}\"",
                    1);
            }

            var dependenciesMatch = Regex.Match(
                manifestJson,
                "\"dependencies\"\\s*:\\s*\\{(?<body>.*?)\\}",
                RegexOptions.Singleline);

            if (!dependenciesMatch.Success)
                throw new InvalidOperationException("Could not find a dependencies block in Packages/manifest.json.");

            var body = dependenciesMatch.Groups["body"].Value;
            var indentMatch = Regex.Match(body, "\\r?\\n(?<indent>\\s+)\"");
            var indent = indentMatch.Success ? indentMatch.Groups["indent"].Value : "    ";
            var newline = manifestJson.Contains("\r\n") ? "\r\n" : "\n";
            var trimmedBody = body.TrimEnd();
            var needsComma = trimmedBody.Any(c => !char.IsWhiteSpace(c)) && !trimmedBody.TrimEnd().EndsWith(",");
            var insertion = $"{(needsComma ? "," : string.Empty)}{newline}{indent}\"{packageName}\": \"{packageValue}\"";
            var updatedBody = body + insertion;

            return manifestJson.Remove(dependenciesMatch.Groups["body"].Index, dependenciesMatch.Groups["body"].Length)
                .Insert(dependenciesMatch.Groups["body"].Index, updatedBody);
        }

        private static string GetProjectManifestPath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "Packages", "manifest.json");
        }
        
        private void DrawProjectSettingsSection()
        {
            var report = MashBoxProjectSettingsSync.GetSyncReport();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Project Settings", EditorStyles.boldLabel);

                if (!report.profileFound)
                {
                    EditorGUILayout.HelpBox(
                        "No SDK project settings template was found. Capture the canonical project's tags, layers, collisions, and time settings into the SDK resources first.",
                        MessageType.Warning);
                }
                else if (!report.profileConfigured)
                {
                    EditorGUILayout.HelpBox(
                        "The SDK project settings profile exists but is empty. Capture the canonical project's tags, layers, collisions, and time settings, then apply them for SDK users.",
                        MessageType.Warning);
                }
                else if (report.IsInSync)
                {
                    EditorGUILayout.HelpBox(
                        "This project's tags, layers, collisions, and time settings match the SDK profile.",
                        MessageType.Info);
                }
                else
                {
                    var message = "This project is out of sync with the SDK profile.\n";

                    if (report.missingTags.Count > 0)
                        message += $"\nMissing tags: {string.Join(", ", report.missingTags)}";

                    if (report.missingLayers.Count > 0)
                        message += $"\nMissing layers: {string.Join(", ", report.missingLayers)}";

                    if (report.conflictingLayers.Count > 0)
                        message += $"\nConflicting layers: {string.Join(" | ", report.conflictingLayers)}";

                    if (report.collisionMismatches.Count > 0)
                        message += $"\nCollision matrix mismatches: {report.collisionMismatches.Count}";

                    if (report.timeMismatches.Count > 0)
                        message += $"\nTime setting mismatches: {report.timeMismatches.Count}";

                    EditorGUILayout.HelpBox(message, MessageType.Warning);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!report.profileFound || !report.profileConfigured))
                    {
                        if (GUILayout.Button("Apply SDK Project Settings"))
                        {
                            MashBoxProjectSettingsSync.ApplyProfile(false);
                        }

                        if (GUILayout.Button("Force SDK Project Settings"))
                        {
                            if (EditorUtility.DisplayDialog(
                                    "Force SDK Project Settings",
                                    "This will overwrite the project's TagManager, physics settings, and time settings to match the SDK templates.\n\nContinue?",
                                    "Force",
                                    "Cancel"))
                            {
                                MashBoxProjectSettingsSync.ApplyProfile(true);
                            }
                        }
                    }
                }

#if MashBoxDev
                if (GUILayout.Button("Capture Current Project As SDK Defaults"))
                {
                    MashBoxProjectSettingsSync.SaveCurrentProjectAsProfile();
                }
#endif
            }
        }
        
        private void DrawSteamRootSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Steam Installation", EditorStyles.boldLabel);

                bool found = !string.IsNullOrEmpty(_steamRoot);

                var style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = found ? Color.gray : Color.red;

                EditorGUILayout.LabelField(
                    found ? "Steam Found" : "Steam Not Found",
                    style
                );

                EditorGUILayout.TextField("Steam Root", _steamRoot);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Locate Automatically"))
                    {
                        _steamRoot = SteamLocator.GetSteamRoot();
                        EditorPrefs.SetString(PREF_STEAM_ROOT, _steamRoot ?? "");
                        Repaint();
                    }

                    if (GUILayout.Button("Set Manually"))
                    {
                        var path = EditorUtility.OpenFolderPanel("Select Steam Folder", "", "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            _steamRoot = path.Replace("\\", "/");
                            EditorPrefs.SetString(PREF_STEAM_ROOT, _steamRoot);
                        }
                    }
                }
            }
        }
        private void DrawSdkUpdaterUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Installed: {MashBoxSDKState.InstalledVersion}", GUILayout.MaxWidth(200));
                EditorGUILayout.LabelField($"GitHub Main: {MashBoxSDKState.LatestVersion}", GUILayout.MaxWidth(220));

                string text;
                Color color;

                if (MashBoxSDKState.CheckingSdk)
                {
                    text = "Checking...";
                    color = Color.gray;
                }
                else if (MashBoxSDKState.UpdateAvailable)
                {
                    text = "Update Available";
                    color = new Color(1f, 0.3f, 0.3f);
                }
                else
                {
                    text = "Up To Date";
                    color = new Color(0.3f, 1f, 0.4f);
                }

                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = color;

                GUILayout.Label(text, style, GUILayout.Width(140));
                GUILayout.FlexibleSpace();
            }
        }
        // =====================================================
        // GAME SELECTION
        // =====================================================

        private string _buildLocation = "";

        private const string STREAMING_SUBPATH = "Addressables/Customization/Local Custom";

        private void DrawGameSelection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Game Target", EditorStyles.boldLabel);


#if MashBoxDev
                // =============================
                // 🔧 CUSTOM TARGET
                // =============================
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Custom Build Target", EditorStyles.boldLabel);

                    if (GUILayout.Button("Set Custom Target Folder...", GUILayout.Height(24)))
                    {
                        string path = EditorUtility.OpenFolderPanel("Select Build Folder", _buildLocation, "");

                        string previousGame = EditorPrefs.GetString("ModIo.CurrentGame", "");

                        if (!string.Equals(previousGame, "Custom Folder", StringComparison.OrdinalIgnoreCase))
                        {
                            ModIoAuth.ClearForCurrentGame();
                            _statusMsg = "Switched to Custom Folder. Please log in again.";
                        }

                        if (!string.IsNullOrEmpty(path))
                        {
                            _buildLocation = path.Replace("\\", "/");
                            _lastChosenAppId = 0;

                            EditorPrefs.SetString("BuildLocation", _buildLocation);
                            EditorPrefs.SetString(PREF_KEY_LAST_APP, "0");
                            EditorPrefs.SetString("ModIo.CurrentGame", "Custom Folder");

                            Repaint();
                        }
                    }
                    
                }
#endif
                
                var activeGame = _installedGames.FirstOrDefault(g =>
                    g.Definition.SteamAppId == _lastChosenAppId);

                if (activeGame != null || !string.IsNullOrEmpty(_buildLocation))
                {
                    if (activeGame != null)
                    {
                        EditorGUILayout.LabelField("Active Game:", activeGame.Definition.DisplayName, EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("Unity Editor:", FormatUnityEditorVersion(activeGame.Definition.UnityEditorVersion), EditorStyles.miniLabel);
                    }

                    EditorGUILayout.LabelField(
                        "Build Folder:",
                        string.IsNullOrEmpty(_buildLocation) ? "Unavailable (install not detected or not ready)" : _buildLocation,
                        EditorStyles.miniLabel);

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_buildLocation)))
                    {
                        if (GUILayout.Button("Open Folder", GUILayout.Width(110)))
                        {
                            OpenBuildOutputFolder(_buildLocation);
                        }
                    }
                }

                GUILayout.Space(6);

                // =============================
                // 🎮 GAMES
                // =============================
                foreach (var game in _installedGames)
                {
                    string install = game.InstallPath;
                    bool detected = !string.IsNullOrEmpty(install);
                    bool isActive = _lastChosenAppId == game.Definition.SteamAppId;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {

                            var rect = GUILayoutUtility.GetRect(1, 72, GUILayout.ExpandWidth(true));
                            if (Event.current.type == EventType.Repaint)
                            {
                                var bg = isActive
                                    ? new Color(0, 0, 0, 0.2f)
                                    : new Color(0, 0, 0, 0.05f);

                                EditorGUI.DrawRect(rect, bg);
                            }

                            GUILayout.BeginHorizontal();


                            Texture2D tex = null;
                            var key = game.Definition.SteamAppId.ToString();

                            if (!_gameLogoCache.TryGetValue(key, out tex))
                            {
                                var path = MashBoxEditorResources.GetGameLogo(game.Definition.DisplayName);
                                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                                _gameLogoCache[key] = tex;
                            }

                            var imgRect = GUILayoutUtility.GetRect(96, 48, GUILayout.Width(96), GUILayout.Height(48));
                            if (tex != null)
                                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleAndCrop);


                            GUILayout.BeginVertical();

                            var nameStyle = new GUIStyle(EditorStyles.label);
                            if (isActive) nameStyle.normal.textColor = Color.green;

                            GUILayout.Label(game.Definition.DisplayName, nameStyle);
                            GUILayout.Label($"Unity {FormatUnityEditorVersion(game.Definition.UnityEditorVersion)}", EditorStyles.miniLabel);

                            var statusStyle = new GUIStyle(EditorStyles.miniLabel);
                            statusStyle.normal.textColor = detected ? Color.gray : Color.red;

                            GUILayout.Label(detected ? "Installed" : "Not Installed", statusStyle);

                            if (isActive)
                                GUILayout.Label("✓ Active Target", EditorStyles.miniBoldLabel);

                            GUILayout.EndVertical();

                            GUILayout.FlexibleSpace();


                            if (!detected)
                            {
                                using (new EditorGUILayout.VerticalScope(GUILayout.Width(120)))
                                {
                                    if (GUILayout.Button("Locate Install"))
                                    {
                                        var path = EditorUtility.OpenFolderPanel("Select Game Install Folder", "", "");
                                        if (!string.IsNullOrEmpty(path))
                                        {
                                            game.InstallPath = path;
                                        }
                                    }

                                    if (GUILayout.Button("Set Target"))
                                        SetGameTarget(game);
                                }
                            }
                            else
                            {
                                if (GUILayout.Button("Set Target", GUILayout.Width(120)))
                                    SetGameTarget(game);
                            }

                            using (new EditorGUI.DisabledScope(!detected))
                            {
                                if (GUILayout.Button("Open Folder", GUILayout.Width(110)))
                                {
                                    var sa = StreamingAssetsResolver.TryResolve(install);
                                    var final = StreamingAssetsResolver.AppendSubfolder(sa, STREAMING_SUBPATH);
                                    OpenBuildOutputFolder(final);
                                }
                            }

                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
        }

        private void SetGameTarget(GameTarget game)
        {
            if (game?.Definition == null)
                return;

            string previousGame = EditorPrefs.GetString("ModIo.CurrentGame", "");
            bool gameChanged = !string.Equals(
                previousGame,
                game.Definition.DisplayName,
                StringComparison.OrdinalIgnoreCase);

            var streamingAssets = string.IsNullOrEmpty(game.InstallPath)
                ? null
                : StreamingAssetsResolver.TryResolve(game.InstallPath);

            _buildLocation = string.IsNullOrEmpty(streamingAssets)
                ? string.Empty
                : StreamingAssetsResolver.AppendSubfolder(streamingAssets, STREAMING_SUBPATH);
            _lastChosenAppId = game.Definition.SteamAppId;

            EditorPrefs.SetString("BuildLocation", _buildLocation);
            EditorPrefs.SetString(PREF_KEY_LAST_APP, _lastChosenAppId.ToString());
            EditorPrefs.SetString("ModIo.ApiBase", game.Definition.ModIoApiBase ?? string.Empty);
            EditorPrefs.SetString("ModIo.CurrentGame", game.Definition.DisplayName);

            if (gameChanged)
            {
                ModIoAuth.ClearForCurrentGame();
                _statusMsg = $"Switched to {game.Definition.DisplayName}. Please log in again.";
            }

            Repaint();
        }

        private static string FormatUnityEditorVersion(string unityEditorVersion)
        {
            return string.IsNullOrWhiteSpace(unityEditorVersion) ? "Unknown" : unityEditorVersion;
        }

        private void OpenBuildOutputFolder(string path)
        {
#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", path.Replace("/", "\\"));
#else
    EditorUtility.RevealInFinder(path);
#endif
        }

// =====================================================
        // MOD.IO
        // =====================================================
        private void DrawModIoSection()
        {
            bool hasGameTarget = _lastChosenAppId != 0 ||
                                 EditorPrefs.GetString("ModIo.CurrentGame", "") == "Custom Folder";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawModIoHeader();
                string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "None");
                EditorGUILayout.LabelField($"Game: {currentGame}", EditorStyles.miniLabel);
                if (!hasGameTarget)
                {
                    EditorGUILayout.HelpBox(
                        "Select a Game Target before logging into mod.io.",
                        MessageType.Warning);

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Email", "");
                        GUILayout.Button("Send Code");
                    }

                    return;
                }

                if (!ModIoAuth.IsAuthorizedForCurrentGame())
                {
                    EditorGUI.BeginChangeCheck();
                    _emailInput = EditorGUILayout.TextField("Email", _emailInput);
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetString(PREF_KEY_MODIO_EMAIL, _emailInput ?? string.Empty);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = !string.IsNullOrWhiteSpace(_emailInput);

                        if (GUILayout.Button("Send Code"))
                        {
                            EditorPrefs.SetString(PREF_KEY_MODIO_EMAIL, _emailInput ?? string.Empty);

                            ModIoAuth.BeginEmailRequest(_emailInput, s =>
                            {
                                _statusMsg = s;
                                Repaint();
                            });

                            ModIoCodePopup.Show(_emailInput, (code) =>
                            {
                                ModIoAuth.ExchangeCode(_emailInput, code, s =>
                                {
                                    _statusMsg = s;
                                    Repaint();
                                });
                            });
                        }

                        GUI.enabled = true;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Authorized", EditorStyles.boldLabel);

                    if (GUILayout.Button("Logout"))
                    {
                        ModIoAuth.ClearForCurrentGame();
                        _statusMsg = "Logged out";
                    }
                }

                if (!string.IsNullOrEmpty(_statusMsg))
                    EditorGUILayout.HelpBox(_statusMsg, MessageType.None);
            }
        }

        private void DrawModIoHeader()
        {
            if (_modioLogo != null)
            {
                var width = Mathf.Min(EditorGUIUtility.currentViewWidth - 48f, 220f);
                var height = width * (_modioLogo.height / (float)Mathf.Max(1, _modioLogo.width));
                var rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
                GUI.DrawTexture(rect, _modioLogo, ScaleMode.ScaleToFit, true);
                GUILayout.Space(4f);
            }
            else
            {
                EditorGUILayout.LabelField("mod.io Login", EditorStyles.boldLabel);
            }
        }



        private void DrawCookerStatus()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Publishing Servers:", GUILayout.Width(130f));
                var status = MashBoxSDKState.Cooker;

                string text;
                Color color;

                switch (status)
                {
                    case MashBoxSDKState.CookerStatus.Online:
                        text = "Online";
                        color = new Color(0.3f, 1f, 0.4f);
                        break;

                    case MashBoxSDKState.CookerStatus.Stale:
                    case MashBoxSDKState.CookerStatus.Offline:
                        text = "Offline";
                        color = new Color(1f, 0.3f, 0.3f);
                        break;

                    case MashBoxSDKState.CookerStatus.Error:
                        text = "Error";
                        color = new Color(1f, 0.5f, 0.2f);
                        break;

                    default:
                        text = "Checking...";
                        color = Color.gray;
                        break;
                }

                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = color;

                GUILayout.Label(text, style, GUILayout.Width(80f));
                GUILayout.Label(MashBoxSDKState.CookerNote, EditorStyles.miniLabel, GUILayout.Width(110f));

                using (new EditorGUI.DisabledScope(MashBoxSDKState.CheckingCooker))
                {
                    if (GUILayout.Button(MashBoxSDKState.CheckingCooker ? "Checking..." : "Refresh", GUILayout.Width(90f)))
                    {
                        MashBoxSDKState.RefreshCookerStatus();
                        Repaint();
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }
        
        private void OnFocus()
        {
            string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "");

            if (!string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase))
            {
                RefreshBuildLocationFromTarget();
            }
        }
    }



    class ModIoCodePopup : EditorWindow
    {
        private string _code = "";
        private Action<string> _onSubmit;
        private string _email;

        public static void Show(string email, Action<string> onSubmit)
        {
            var window = CreateInstance<ModIoCodePopup>();
            window.titleContent = new GUIContent("Enter Code");
            window._onSubmit = onSubmit;
            window._email = email;

            window.position = new Rect(Screen.width / 2f, Screen.height / 2f, 300, 120);
            window.ShowUtility();
        }

        
        
        private void OnGUI()
        {
            GUILayout.Label("Enter Verification Code", EditorStyles.boldLabel);
            GUILayout.Label(_email, EditorStyles.miniLabel);

            GUILayout.Space(6);

            GUI.SetNextControlName("CodeField");
            _code = EditorGUILayout.TextField("Code", _code);

            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                GUI.enabled = !string.IsNullOrWhiteSpace(_code);

                if (GUILayout.Button("Verify"))
                {
                    _onSubmit?.Invoke(_code);
                    Close();
                }

                GUI.enabled = true;
            }

            EditorGUI.FocusTextInControl("CodeField");

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return &&
                !string.IsNullOrWhiteSpace(_code))
            {
                _onSubmit?.Invoke(_code);
                Close();
                Event.current.Use();
            }
        }
    }
}

#endif
