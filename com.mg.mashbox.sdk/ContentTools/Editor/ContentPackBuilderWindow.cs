
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


using Content_Icon_Capture.Editor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

using MashBoxSDK.Clothing;
using MashBoxSDK.EditorResources;
using MashBoxSDK.Exporting;
using MashBoxSDK.SDKMain;

namespace MashBoxSDK.ContentTools.Editor
{
    [Serializable]
    public class ContentPackGroupInfo
    {
        public string Name = "Ungrouped";
        public Color Color = new Color(0f, 0f, 0f, 0.35f);
    }

    public class ContentPackGroupSettings : ScriptableObject
    {
        public List<ContentPackGroupInfo> Groups = new List<ContentPackGroupInfo>();
    }

    /// <summary>
    /// Content Pack Manager:
    ///  • Forced pack folder: Assets/ContentPacks
    ///  • Read-only Validation Rules panel + inline item issues
    ///  • Build hooks capture 2K icons per item before Addressables build
    ///  • Duplicate name checks only on "Create Pack" click
    ///  • Drag & Drop prefabs into packs (Project or Hierarchy)
    /// </summary>
    public class ContentPackBuilderWindow : EditorWindow
    {
        private string _installedRevision;
        private string _remoteRevision;
        private bool _updateAvailable;
        private bool _checkingUpdate;
        private const string _sdkPackageName = "com.mg.mashbox.sdk";
        private bool _sdkFoldout = true;
        private const string PREF_KEY_SDK_FOLDOUT = "ContentPackBuilder.SdkFoldout";
        
        
        private const string STREAMING_SUBPATH = "Addressables/Customization/Local Custom"; // under StreamingAssets


        private long _lastChosenAppId = 0; // persisted for UX
        private const string PREF_KEY_LAST_APP = "ContentPackBuilder.LastChosenAppId";


        private const string FORCED_PACKS_FOLDER = "Assets/Content/Content Pack Data";
        private const string PREF_KEY_BUILD_LOCATION = "BuildLocation";

        private const string PREF_KEY_PROXY_BASE = "ModIo.ProxyBase";

        private const string DEFAULT_PROXY_BASE =
            "https://modio-proxy-cgf2e7hvc6fggsh6.centralus-01.azurewebsites.net/modio";

        private const string UGC_REQUEST_PATH =
            "https://ugc-remote-cook-func-node-fecqe4asaabhcddn.centralus-01.azurewebsites.net/api/request-upload";
        

        private static string ProxyHostBase => EditorPrefs.GetString(PREF_KEY_PROXY_BASE, DEFAULT_PROXY_BASE)
            .TrimEnd('/').Replace("/modio", "");

        private static string UploaderEndpoint => UGC_REQUEST_PATH;
        private const string DefaultPackGroup = "Ungrouped";
        private static string PackGroupSettingsPath => $"{FORCED_PACKS_FOLDER}/ContentPackGroups.asset";
        
        private readonly List<ContentPackDefinition> _packs = new List<ContentPackDefinition>();
        [SerializeField] private ContentPackDefinition _selectedPack;
        [SerializeField] private ContentPackGroupSettings _groupSettings;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _packGroupFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> _metaFoldouts = new Dictionary<string, bool>();

        // NEW: foldout memory for the rules section grouped by SuperType
        private readonly Dictionary<string, bool> _rulesSuperFoldouts = new Dictionary<string, bool>();

        private AddressableAssetSettings _settings;
        private string _buildLocation = string.Empty;

        private string _newPackName = "NewContentPack";
        private string _packsFolder = FORCED_PACKS_FOLDER;

        [SerializeField] private ContentValidationRules _rules;

        private readonly Dictionary<GameObject, List<ContentPackValidator.Issue>> _itemIssues = new Dictionary<GameObject, List<ContentPackValidator.Issue>>();
        private static readonly Dictionary<ContentPackDefinition, List<ContentPackValidator.Issue>> _packIssues = new Dictionary<ContentPackDefinition, List<ContentPackValidator.Issue>>();

        private static bool _warnedNoRules;

        private static GUIStyle _helpWrap, _errStyle,_okStyle, _warnStyle, _miniHeader, _dropZoneStyle;

// UI state: Build Output Target panel foldout (default open)
        private bool _targetFoldout = true;

// Cache Steam library images (capsules/headers) by AppID
        private readonly Dictionary<long, Texture2D> _steamCapsuleCache = new Dictionary<long, Texture2D>();
        private bool _rulesFoldout = false;
        private Vector2 _scroll;

        private const string HEADER_RESOURCE_NAME = "ContentManager_Header";
        private Texture2D _headerTex;
        private const long MaxContentPublishPackageBytes = 100L * 1024L * 1024L;

        private Dictionary<string, bool> _packFoldoutState = new();
        private readonly HashSet<string> _batchSelectedPackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _captureIconsOnBuild = true;
        private const string PREF_KEY_CAPTURE_ICONS = "ContentPackBuilder.CaptureIconsOnBuild";
        
// Footer (button bar) height
        private const float FOOTER_H = 40f;

        //[MenuItem("MashBox/Content Manager")]
        public static void Open()
        {
            GetWindow<ContentPackBuilderWindow>( "MG Content Manager");

            EditorPrefs.SetString(PREF_KEY_PROXY_BASE, DEFAULT_PROXY_BASE);

            System.Net.ServicePointManager.Expect100Continue = false;
            System.Net.ServicePointManager.DefaultConnectionLimit = 64;
            SharedHttp.DefaultRequestHeaders.ExpectContinue = false;
        }

        private static readonly HttpClient SharedHttp = CreateSharedHttp();

        private static HttpClient CreateSharedHttp()
        {
            var http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            http.DefaultRequestHeaders.ExpectContinue = false;
            return http;
        }

        public void OnEnable()
        {
            EditorPrefs.SetString(PREF_KEY_PROXY_BASE, DEFAULT_PROXY_BASE);

            System.Net.ServicePointManager.Expect100Continue = false;
            System.Net.ServicePointManager.DefaultConnectionLimit = 64;
            SharedHttp.DefaultRequestHeaders.ExpectContinue = false;
            
            _headerTex = Resources.Load<Texture2D>(HEADER_RESOURCE_NAME);
            _settings = AddressableAssetSettingsDefaultObject.Settings;
            _buildLocation = EditorPrefs.GetString(PREF_KEY_BUILD_LOCATION, DefaultBuildFolderRel);

            _lastChosenAppId = long.TryParse(EditorPrefs.GetString(PREF_KEY_LAST_APP, "0"), out var v) ? v : 0;
            _sdkFoldout = EditorPrefs.GetBool(PREF_KEY_SDK_FOLDOUT, true);
            if (string.IsNullOrWhiteSpace(_buildLocation))
            {
                _buildLocation = DefaultBuildFolderRel; 
            }

            _packsFolder = FORCED_PACKS_FOLDER;
            EnsureFolderExists(_packsFolder);
            EnsurePackGroupSettings();
            
            _captureIconsOnBuild = EditorPrefs.GetBool(PREF_KEY_CAPTURE_ICONS, true);
            EditorApplication.projectChanged += OnProjectChanged;
            RefreshBuildLocation();
            
            EditorApplication.delayCall += DelayedInit;
        }
        private void DelayedInit()
        {
            ReloadRulesAlways();
            RefreshPacks();
            AutoSyncCorePack();
            CheckForSdkUpdate();
            RefreshBuildLocation();
            Repaint();
        }
        
        private void OnFocus()
        {
            RefreshBuildLocation();
        }

        private void RefreshBuildLocation()
        {
            var latest = EditorPrefs.GetString(PREF_KEY_BUILD_LOCATION, "");

            if (!string.IsNullOrEmpty(latest))
            {
                _buildLocation = latest.Replace("\\", "/");
            }
        }
        
        private void OnDisable()
        {
            //EditorPrefs.SetString(PREF_KEY_BUILD_LOCATION, _buildLocation ?? string.Empty);
            //EditorPrefs.SetString(PREF_KEY_LAST_APP, _lastChosenAppId.ToString());
            EditorPrefs.SetBool(PREF_KEY_SDK_FOLDOUT, _sdkFoldout);
            EditorPrefs.SetBool(PREF_KEY_CAPTURE_ICONS, _captureIconsOnBuild);
            EditorApplication.projectChanged -= OnProjectChanged;
        }
        private static string ShortString(string value, int max = 7)
        {
            if (string.IsNullOrEmpty(value))
                return "Unknown";

            return value.Length > max
                ? value.Substring(0, max)
                : value;
        }
        
        private void AutoSyncCorePack()
        {
            var corePack = FindCorePack();

            if (corePack == null)
            {
                Debug.LogError($"MashBox could not find Core Pack");
                return;
            }

            try
            {
                Debug.Log("[CorePack] Auto-syncing Core Pack on tool open...");

                corePack.SyncToAddressables();

                Debug.Log("[CorePack] Sync complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CorePack] Auto-sync failed: {ex.Message}");
            }
        }
        
        private async void CheckForSdkUpdate()
        {
            if (_checkingUpdate) return;
            _checkingUpdate = true;

            try
            {
                // Get installed package info
                var listRequest = UnityEditor.PackageManager.Client.List(true);
                while (!listRequest.IsCompleted)
                    await System.Threading.Tasks.Task.Delay(50);

                var pkg = listRequest.Result
                    .FirstOrDefault(p => p.name == _sdkPackageName);

                if (pkg == null)
                {
                    Debug.LogWarning("MashBox SDK package not found in PackageManager list.");
                    _installedRevision = "Not Installed";
                    _checkingUpdate = false;
                    Repaint();
                    return;
                }

                if (!string.IsNullOrEmpty(pkg.version))
                {
                    _installedRevision = pkg.version;
                }
                else if (pkg.git != null && !string.IsNullOrEmpty(pkg.git.revision))
                {
                    _installedRevision = pkg.git.revision;
                }
                else
                {
                    _installedRevision = "Unknown";
                }
                // Fetch latest commit from GitHub
                string repoOwner = "Mashman1212";
                string repoName  = "mashbox-sdk";
                string branch    = "main";

                string url = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/{branch}/com.mg.mashbox.sdk/package.json";

                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("UnitySDKUpdater");

                    var response = await http.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.LogWarning("Failed to fetch remote package.json");
                        _checkingUpdate = false;
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();

                    var match = System.Text.RegularExpressions.Regex.Match(
                        json,
                        "\"version\"\\s*:\\s*\"([^\"]+)\""
                    );

                    if (match.Success)
                    {
                        _remoteRevision = match.Groups[1].Value;

                        _updateAvailable = _remoteRevision != _installedRevision;
                    }
                }

                
                foreach (var p in listRequest.Result)
                {
                    if (p.name.Contains("mashbox"))
                    {
                        Debug.Log($"FOUND PACKAGE:");
                        Debug.Log($"Name: {p.name}");
                        Debug.Log($"Version: {p.version}");
                        Debug.Log($"Source: {p.source}");
                        Debug.Log($"ResolvedPath: {p.resolvedPath}");
                        Debug.Log($"Git revision: {p.git?.revision}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SDK update check failed: " + ex.Message);
            }

            
            
            _checkingUpdate = false;
            Repaint();
        }
        
        private void DrawSdkUpdaterUI()
        {
            Color originalBg = GUI.backgroundColor;

            if (_checkingUpdate)
                GUI.backgroundColor = originalBg;
            else if (_updateAvailable)
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f); // 🔴 red
            else
                GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f); // 🟢 green

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = originalBg; // reset immediately for children

                string headerLabel = "MashBox SDK";

                if (!_checkingUpdate && _updateAvailable)
                {
                    headerLabel += " (UPDATE AVAILABLE)";
                }
                else if (!_checkingUpdate && !_updateAvailable && 
                         !string.IsNullOrEmpty(_installedRevision) && 
                         !string.IsNullOrEmpty(_remoteRevision))
                {
                    headerLabel += " (Up To Date)";
                }

                _sdkFoldout = EditorGUILayout.Foldout(_sdkFoldout, headerLabel, true);

                
                if (!_sdkFoldout)
                    return;

                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("Installed:",
                    string.IsNullOrEmpty(_installedRevision)
                        ? "Unknown"
                        : ShortString(_installedRevision));

                EditorGUILayout.LabelField("Latest:",
                    string.IsNullOrEmpty(_remoteRevision)
                        ? "Checking..."
                        : ShortString(_remoteRevision));

                EditorGUILayout.Space(6);

                if (_checkingUpdate)
                {
                    EditorGUILayout.LabelField("Checking for updates...");
                }
                else if (_updateAvailable)
                {
                    EditorGUILayout.HelpBox("Update Available!", MessageType.Warning);


                }
                else
                {
                    EditorGUILayout.HelpBox("SDK is up to date.", MessageType.Info);
                }

                //if (GUILayout.Button("Update SDK"))
                //    ForceUpdateSdk();
                
                if (GUILayout.Button("Check Again"))
                    CheckForSdkUpdate();
            }
        }

        private async void ForceUpdateSdk()
        {
            try
            {
                EditorUtility.DisplayProgressBar("MashBox SDK", "Updating SDK...", 0.3f);

                // Remove package first
                var removeRequest = UnityEditor.PackageManager.Client.Remove(_sdkPackageName);
                while (!removeRequest.IsCompleted)
                    await Task.Delay(100);

                if (removeRequest.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    throw new Exception(removeRequest.Error.message);

                EditorUtility.DisplayProgressBar("MashBox SDK", "Reinstalling latest version...", 0.6f);

                // Re-add from Git
                string gitUrl = "https://github.com/Mashman1212/mashbox-sdk.git#main";
//
                var addRequest = UnityEditor.PackageManager.Client.Add(gitUrl);
                while (!addRequest.IsCompleted)
                    await Task.Delay(100);

                if (addRequest.Status == UnityEditor.PackageManager.StatusCode.Failure)
                    throw new Exception(addRequest.Error.message);

                EditorUtility.ClearProgressBar();

                Debug.Log("MashBox SDK updated successfully.");

                // Refresh version display
                CheckForSdkUpdate();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError("SDK Update failed: " + ex.Message);
            }
        }


        private void OnProjectChanged()
        {
            foreach (var p in _packs)
            {
                if (p == null) continue;

                var path = AssetDatabase.GetAssetPath(p);
                if (string.IsNullOrEmpty(path)) continue;

                // Only revalidate expanded packs
                if (GetPackFoldout(path))
                {
                    RevalidatePack(p);
                }

                p.RemoveMissingReferences();
            }

            Repaint();
        }

        bool GetPackFoldout(string packGuid)
        {
            if (!_packFoldoutState.TryGetValue(packGuid, out var state))
            {
                state = false; // 👈 collapsed by default
                _packFoldoutState[packGuid] = state;
            }

            return state;
        }

        void SetPackFoldout(string packGuid, bool value)
        {
            _packFoldoutState[packGuid] = value;
        }

//  — smaller, tighter cap
        private const float HEADER_MIN = 56f;
        private const float HEADER_MAX = 80f;
        private float _headerMeasuredH = 64f;

        private void DrawHeaderBanner()
        {
            if (_headerTex == null)
                _headerTex = Resources.Load<Texture2D>(HEADER_RESOURCE_NAME);

            if (_headerTex != null)
            {
                float vw = EditorGUIUtility.currentViewWidth;
                float aspect = (float)_headerTex.height / Mathf.Max(1, _headerTex.width);
                float desiredH = Mathf.Clamp(vw * aspect, HEADER_MIN, HEADER_MAX);

                // This reserves layout height and returns the rect we draw into
                Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(desiredH), GUILayout.ExpandWidth(true));

                _headerMeasuredH = r.height; // <-- cache the height layout actually used

                // background + image + divider (unchanged)
                var card = new Rect(r.x, r.y, r.width, r.height);
                var cardCol = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.035f) : new Color(0f, 0f, 0f, 0.05f);
                EditorGUI.DrawRect(card, cardCol);
                GUI.DrawTexture(card, _headerTex, ScaleMode.ScaleAndCrop, true);
                var div = new Rect(card.x, card.yMax, card.width, 1f);
                EditorGUI.DrawRect(div,
                    EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.12f));
            }
        }



        private void RevalidateAllItems()
        {
            if (_packs == null) return;
            foreach (var p in _packs)
            {
                if (p == null || p._items == null) continue;
                foreach (var go in p._items)
                {
                    if (!go) continue;
                    _itemIssues[go] = ContentPackValidator.ValidateItem(go, _rules);
                }
            }
        }
        
        private void RevalidatePack(ContentPackDefinition pack)
        {
            if (pack == null || pack._items == null) return;
            
            
            foreach (var go in pack._items)
            {
                if (!go) continue;
                _itemIssues[go] = ContentPackValidator.ValidateItem(go, _rules);
            }
            
            _packIssues[pack] = ValidatePackWithExportChecks(pack, _rules);
        }

        private void ReloadRulesAlways()
        {
            string RULES_PATH = "Packages/com.mg.mashbox.sdk/ContentTools/ContentValidationRules.asset";
            
            _rules = AssetDatabase.LoadAssetAtPath<ContentValidationRules>(RULES_PATH);

            if (_rules == null)
            {
                Debug.LogError($"[ContentPackBuilder] Could not load validation rules at:\n{RULES_PATH}");
            }
        }

        private void AutoLoadRulesIfNeeded()
        {
            if (_rules != null) return;

            var guids = AssetDatabase.FindAssets("t:ContentValidationRules");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _rules = AssetDatabase.LoadAssetAtPath<ContentValidationRules>(path);
            }
            else if (!_warnedNoRules)
            {
                _warnedNoRules = true;
                Debug.LogWarning(
                    "No ContentValidationRules asset found. Create one via Tools → MashBox → Create Prefilled Validation Rules, or Assets → Create → Content → Validation Rules.");
            }
        }

        private const string DefaultBuildFolderRel = "Assets/StreamingAssets/Addressables/Customization/Local Custom";

        private static string ToProjectAbsolutePath(string path)
        {
            // Accept absolute paths as-is
            if (Path.IsPathRooted(path)) return path.Replace("\\", "/");

            // Accept project-relative "Assets/..." paths
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Application.dataPath; // "<project>/Assets"
                var abs = Path.Combine(Path.GetDirectoryName(projectRoot) ?? "",
                    path.Replace("/", Path.DirectorySeparatorChar.ToString()));
                return Path.GetFullPath(abs).Replace("\\", "/");
            }

            // Anything else must be absolute; return as-is (will fail validation)
            return path.Replace("\\", "/");
        }

        private Vector2 _mainScroll;

        private string _currentGameName;

        private void OnGUI()
        {
            Draw();
        }

        public void Draw()
        {
            _currentGameName = EditorPrefs.GetString("ModIo.CurrentGame", "this game");
            RefreshBuildLocation();

            MashBoxSDKState.Update();

            // 1) Header (fixed, layout-managed)
            DrawHeaderBanner();
            
            // 2) Middle = one scroll, sized by remaining window height
           // float bodyH = Mathf.Max(0f, position.height - FOOTER_H - _headerMeasuredH);

            using (var sv = new EditorGUILayout.ScrollViewScope(_mainScroll))
            {
                _mainScroll = sv.scrollPosition;

                using (new EditorGUILayout.VerticalScope())
                {
                    Header();
                    GUILayout.Space(6);
                    
                    // Validation Rules auto-expands (no inner scroll)
                    
                    EditorGUILayout.Space(4);
                    
                    
                    DrawRulesOverview();
                    GUILayout.Space(6);

                    //_captureIconsOnBuild = EditorGUILayout.ToggleLeft("Capture Icons During Build", _captureIconsOnBuild );
                    
                    DrawCreateSection();
                    GUILayout.Space(6);

#if MashBoxDev
                    if (GUILayout.Button("Build Core Pack", GUILayout.Height(30)))
                    {
                        var corePack = FindCorePack();
                        if (corePack != null)
                        {
                            BuildCorePack(corePack);
                        }
                    }
#endif
                    
                    EditorGUILayout.LabelField("Active Target:", _buildLocation, EditorStyles.miniLabel);
                    
                    // Ensure DrawPacksList() has no inner ScrollViewScope
                    DrawPacksList();
                    GUILayout.Space(8);

                    // 👇 NEW: bottom spacer so the last items don’t sit under the footer
                    GUILayout.Space(FOOTER_H + 8f);
                }
            }

            // 3) Footer (fixed): draw in a bottom area so it never scrolls
            var footerRect = new Rect(0, position.height - FOOTER_H, position.width, FOOTER_H);
            using (new GUILayout.AreaScope(footerRect))
            {
                // (optional) subtle bg + divider
                //if (Event.current.type == EventType.Repaint)
                //{
                //    var bg = EditorGUIUtility.isProSkin ? new Color(1,1,1,0.035f) : new Color(0,0,0,0.06f);
                //    EditorGUI.DrawRect(new Rect(0,0,footerRect.width,footerRect.height), bg);
                //    EditorGUI.DrawRect(new Rect(0,0,footerRect.width,1f),
                //        EditorGUIUtility.isProSkin ? new Color(1,1,1,0.08f) : new Color(0,0,0,0.12f));
                //}

                GUILayout.Space(4);
                //DrawBuildRow(); // your centered, larger buttons
                GUILayout.Space(2);
            }
        }


        private void Header()
        {
            if (_helpWrap == null) _helpWrap = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, richText = true };
            if (_errStyle == null)
            {
                _errStyle = new GUIStyle(EditorStyles.boldLabel);
                _errStyle.normal.textColor = new Color(.9f, 0.25f, .25f);
            }
            if (_okStyle == null)
            {
                _okStyle = new GUIStyle(EditorStyles.boldLabel);
                _okStyle.normal.textColor = new Color(0.2f, 0.8f, 0.3f); // green
            }

            if (_warnStyle == null)
            {
                _warnStyle = new GUIStyle(EditorStyles.label);
                _warnStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            }

            if (_miniHeader == null)
                _miniHeader = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            if (_dropZoneStyle == null)
            {
                _dropZoneStyle = new GUIStyle(EditorStyles.helpBox)
                    { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
                _dropZoneStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            }
        }

        private void DrawCreateSection()
        {
            EditorGUILayout.Space(4);

            //if (!AssetDatabase.IsValidFolder(FORCED_PACKS_FOLDER))
            //{
            //    if (GUILayout.Button("Create Folder", GUILayout.Width(140)))
            //    {
            //        EnsureFolderExists(FORCED_PACKS_FOLDER);
            //        AssetDatabase.Refresh();
            //    }
            //    return;
            //}

            if (GUILayout.Button("Create New Pack", GUILayout.Height(28)))
            {
                CreatePackPopup.Show((name) =>
                {
                    string safe = SanitizePackName(name);

                    if (string.IsNullOrWhiteSpace(safe))
                    {
                        EditorUtility.DisplayDialog(
                            "Invalid Name",
                            "Name may contain only letters, digits, and spaces.",
                            "OK");
                        return;
                    }

                    if (PackNameExists(safe, out var existingPath))
                    {
                        EditorUtility.DisplayDialog(
                            "Duplicate Name",
                            $"A ContentPackDefinition named '{safe}' already exists:\n{existingPath}",
                            "OK");
                        return;
                    }

                    if (AddressablesGroupExists(safe))
                    {
                        EditorUtility.DisplayDialog(
                            "Duplicate Group",
                            $"An Addressables Group named '{safe}' already exists.",
                            "OK");
                        return;
                    }

                    CreatePackWithName(safe);
                });
            }

#if MashBoxDev
            if (GUILayout.Button("Build All", GUILayout.Height(28)))
                BuildAllPacks();
#endif
        }

        void EnsureWrapStyle()
        {
            if (_wrapLabel != null) return;
            _wrapLabel = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = false
            };
        }

        /// Draw a wrapped line that adapts to the current window width.
        /// leftPadding lets you indent bullets under the header nicely.
        void DrawWrappedLine(string text, float leftPadding = 16f, float rightPadding = 8f)
        {
            EnsureWrapStyle();
            if (string.IsNullOrEmpty(text)) text = "";

            // How much width we actually have inside the current view/helpbox
            float full = EditorGUIUtility.currentViewWidth;
            // Unity adds margins; a small cushion prevents accidental clipping
            float contentWidth = Mathf.Max(80f, full - leftPadding - rightPadding - 20f);

            var gc = new GUIContent(text);
            float h = _wrapLabel.CalcHeight(gc, contentWidth);

            // Reserve a rect and draw
            var r = GUILayoutUtility.GetRect(contentWidth, h, _wrapLabel, GUILayout.ExpandWidth(true));
            r.x += leftPadding;
            r.width = contentWidth;
            EditorGUI.LabelField(r, gc, _wrapLabel);
        }

        // Scroll position for the Part Hierarchy Rules panel
        private Vector2 _rulesHierarchyScroll;


// Subsection foldouts
        private bool _allowedPairsFoldout = true;
        private bool _colorsFoldout = true;

        private void DrawRulesOverview()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _rulesFoldout = EditorGUILayout.Foldout(_rulesFoldout, "Validation Rules", true);
                if (!_rulesFoldout) return;

                if (_rules == null)
                {
                    EditorGUILayout.HelpBox(
                        "No ContentValidationRules asset found. Create one via Tools → MashBox → Create Prefilled Validation Rules, or Assets → Create → Content → Validation Rules.",
                        MessageType.Warning);
                    return;
                }

                // --- SuperTypes ---
                EditorGUILayout.LabelField("SuperTypes", _miniHeader);
                if (_rules.SuperTypes != null && _rules.SuperTypes.Length > 0)
                    EditorGUILayout.LabelField(string.Join(", ", _rules.SuperTypes));
                else
                    EditorGUILayout.LabelField("<none>");

                EditorGUILayout.Space(3);

                // --- Allowed Pairs (foldout, expands naturally) ---
                _allowedPairsFoldout =
                    EditorGUILayout.Foldout(_allowedPairsFoldout, "Allowed Pairs (SuperType → Types)", true);
                if (_allowedPairsFoldout)
                {
                    if (_rules.AllowedPairs != null && _rules.AllowedPairs.Count > 0)
                    {
                        foreach (var pair in _rules.AllowedPairs)
                        {
                            if (pair == null) continue;
                            var types = (pair.Types != null && pair.Types.Length > 0)
                                ? string.Join(", ", pair.Types)
                                : "<none>";
                            DrawWrappedLine($"• {pair.SuperType} → {types}", leftPadding: 16f);
                        }
                    }
                    else
                    {
                        DrawWrappedLine("<none>", leftPadding: 16f);
                    }
                }

                EditorGUILayout.Space(3);

                // --- Colors (foldout, expands naturally) ---
                _colorsFoldout = EditorGUILayout.Foldout(_colorsFoldout, "Colors", true);
                if (_colorsFoldout)
                {
                    if (_rules.Colors != null && _rules.Colors.Length > 0)
                        DrawWrappedLine(string.Join(", ", _rules.Colors), leftPadding: 16f);
                    else
                        EditorGUILayout.LabelField("<none>");
                }

                EditorGUILayout.Space(3);

                // --- Part Hierarchy Rules (NO inner scroll; expands with content) ---
                EditorGUILayout.LabelField("Part Hierarchy Rules", _miniHeader);

                if (_rules.ItemRules != null && _rules.ItemRules.Count > 0)
                {
                    var grouped = _rules.ItemRules
                        .Where(r => r != null)
                        .GroupBy(r => r.AppliesToSuperType)
                        .OrderBy(g => g.Key);

                    foreach (var g in grouped)
                    {
                        var superKey = g.Key;
                        var foldKey = $"rules.super.{superKey}";
                        if (!_rulesSuperFoldouts.ContainsKey(foldKey))
                            _rulesSuperFoldouts[foldKey] = false;

                        _rulesSuperFoldouts[foldKey] = EditorGUILayout.Foldout(
                            _rulesSuperFoldouts[foldKey],
                            $"{superKey}  ({g.Count()} rule{(g.Count() == 1 ? "" : "s")})",
                            true);

                        if (!_rulesSuperFoldouts[foldKey]) continue;

                        var byType = g.GroupBy(r => r.AppliesToType)
                            .OrderBy(x => x.Key.ToDisplayName());

                        foreach (var typeGroup in byType)
                        {
                            var typeKey = typeGroup.Key.ToDisplayName();
                            var typeFoldKey = $"{foldKey}.type.{typeKey}";
                            if (!_rulesRuleFoldouts.ContainsKey(typeFoldKey))
                                _rulesRuleFoldouts[typeFoldKey] = true;

                            if (!_rulesRuleFoldouts[typeFoldKey]) continue;

                            EditorGUI.indentLevel++;
                            foreach (var r in typeGroup.OrderBy(r => r.AppliesToBrand))
                            {
                                string brandTok = string.IsNullOrEmpty(r.AppliesToBrand) ? "*" : r.AppliesToBrand;
                                string ruleFoldKey = $"{typeFoldKey}.brand.{brandTok}";
                                if (!_rulesRuleFoldouts.ContainsKey(ruleFoldKey))
                                    _rulesRuleFoldouts[ruleFoldKey] = false;

                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    var budgetLabel = FormatTextureBudgetLabel(r.MaxTextureDataMB.MB);
                                    _rulesRuleFoldouts[ruleFoldKey] = EditorGUILayout.Foldout(
                                        _rulesRuleFoldouts[ruleFoldKey],
                                        $"[{superKey}_{typeKey}] {budgetLabel}",
                                        true);
                                }

                                if (!_rulesRuleFoldouts[ruleFoldKey]) continue;

                                EditorGUI.indentLevel++;
                                EditorGUILayout.LabelField("Shaders", ContentValidationRules.GetAllowedShaderLabel(r), EditorStyles.miniLabel);
                                var tree = BuildRuleTree(r);
                                bool hasAny = (r.RequiredChildren != null && r.RequiredChildren.Length > 0) ||
                                              (r.RequiredDescendantsAnywhere != null && r.RequiredDescendantsAnywhere.Length > 0);

                                if (!hasAny)
                                {
                                    EditorGUILayout.LabelField("└─ <none>", EditorStyles.miniLabel);
                                }
                                else
                                {
                                    DrawRuleTree(tree);
                                }

                                EditorGUI.indentLevel--;
                            }

                            EditorGUI.indentLevel--;
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("<none>");
                }
            }

            EditorGUI.indentLevel = 0;
        }

        private static string FormatTextureBudgetLabel(float mb)
        {
            return $"({mb:0.##} MB)";
        }



// Remember per-rule foldouts (SuperType/Type/Brand)
        private readonly Dictionary<string, bool> _rulesRuleFoldouts = new Dictionary<string, bool>();

// Simple tree for showing RequiredChildren & Pattern scopes
        private class RuleTreeNode
        {
            public string Name;

            public SortedDictionary<string, RuleTreeNode> Children =
                new SortedDictionary<string, RuleTreeNode>(StringComparer.Ordinal);

            public List<string> Annotations = new List<string>(); // e.g., pattern summaries at this node
            public bool IsLeaf;

            public RuleTreeNode(string name)
            {
                Name = name;
            }

            public RuleTreeNode GetOrAdd(string key)
            {
                if (!Children.TryGetValue(key, out var n))
                    Children[key] = n = new RuleTreeNode(key);
                return n;
            }
        }

        // Build a tree from one rule's RequiredChildren + RequiredPatterns
        private RuleTreeNode BuildRuleTree(ContentValidationRules.ItemRule  r)
        {
            var root = new RuleTreeNode("<root>");

            // Exact children (root-relative paths)
            if (r.RequiredChildren != null)
            {
                foreach (var path in r.RequiredChildren)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var cur = root;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        cur = cur.GetOrAdd(parts[i]);
                        if (i == parts.Length - 1) cur.IsLeaf = true;
                    }
                }
            }

            if (r.RequiredDescendantsAnywhere != null)
            {
                foreach (var name in r.RequiredDescendantsAnywhere)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    root.Annotations.Add($"required anywhere: {name}");
                }
            }

            return root;
        }

// Draw the tree nicely with branch marks
        private void DrawRuleTree(RuleTreeNode node, int indent = 0, bool isRoot = true)
        {
            // Show pattern annotations at this node (above its children)
            if (!isRoot && (node.Annotations.Count > 0 || node.IsLeaf))
            {
                // line for the node itself
                DrawTreeLine(node.Name, indent, isLast: false);
            }

            // If this node has annotations, render them beneath it
            if (node.Annotations.Count > 0)
            {
                foreach (var a in node.Annotations)
                    DrawTreeLine($"({a})", indent + (isRoot ? 0 : 1), isLast: false, muted: true);
            }

            // Children
            var kids = node.Children.Values.ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                var child = kids[i];
                bool last = (i == kids.Count - 1);

                // Render the child label with branch lines
                string label = child.Name;
                DrawTreeLine(label, indent + (isRoot ? 0 : 1), last);

                // Recurse — draw annotations and grandchildren under this child
                if (child.Annotations.Count > 0 || child.Children.Count > 0)
                {
                    // For grandchildren, increase indent
                    DrawRuleTreeChildren(child, indent + (isRoot ? 0 : 1), last);
                }
            }
        }

// Helper to draw grandchildren with proper connector lines
        private void DrawRuleTreeChildren(RuleTreeNode node, int indent, bool parentLast)
        {
            // annotations
            foreach (var a in node.Annotations)
                DrawTreeLine($"({a})", indent + 1, isLast: false, muted: true);

            // grandchildren
            var kids = node.Children.Values.ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                var child = kids[i];
                bool last = (i == kids.Count - 1);

                DrawTreeLine(child.Name, indent + 1, last);
                if (child.Annotations.Count > 0 || child.Children.Count > 0)
                    DrawRuleTreeChildren(child, indent + 1, last);
            }
        }

// Draw a single line with tree glyphs and indentation.
// We keep this simple and robust in IMGUI.
        private void DrawTreeLine(string text, int indent, bool isLast, bool muted = false)
        {
            // Build prefix like "│  " / "├─ " / "└─ "
            string prefix = "";
            if (indent > 0)
            {
                // The last-level connector:
                prefix = (isLast ? "└─ " : "├─ ");
                // Add padding for previous levels
                prefix = new string(' ', (indent - 1) * 2) + prefix;
            }

            var style = muted ? EditorStyles.miniLabel : EditorStyles.label;
            EditorGUILayout.LabelField(prefix + text, style);
        }


        private void DrawPacksList()
        {
            if (_packs == null || _packs.Count == 0)
            {
                EditorGUILayout.HelpBox("No content packs found.", MessageType.Info);
                _selectedPack = null;
                return;
            }

            if (_selectedPack == null || !_packs.Contains(_selectedPack))
                _selectedPack = _packs.FirstOrDefault();

            DrawBatchPackToolbar();
            GUILayout.Space(4);
            DrawPackGroupToolbar();
            GUILayout.Space(4);
            DrawPackGroups();
            GUILayout.Space(8);
            DrawSelectedPackPanel(_selectedPack);
        }

        private void DrawBatchPackToolbar()
        {
            var selectedPacks = GetBatchSelectedPacks();
            int selectedCount = selectedPacks.Count;
            string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Unknown");
            bool hasSelection = selectedCount > 0;
            bool canPublish = hasSelection &&
                              ModIoAuth.IsAuthorizedForCurrentGame() &&
                              !string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Selected: {selectedCount}", EditorStyles.boldLabel, GUILayout.Width(90));

                using (new EditorGUI.DisabledScope(_packs.Count == 0))
                {
                    if (GUILayout.Button("Select All", GUILayout.Width(85)))
                        SelectAllBatchPacks();
                }

                using (new EditorGUI.DisabledScope(!hasSelection))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(65)))
                        ClearBatchPackSelection();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!hasSelection))
                {
                    if (GUILayout.Button($"Build Selected To {currentGame} ({selectedCount})", GUILayout.Width(230)))
                        BuildBatchSelectedPacks(selectedPacks);
                }

                using (new EditorGUI.DisabledScope(!canPublish))
                {
                    if (GUILayout.Button($"Publish Selected To {currentGame} Mod.io ({selectedCount})", GUILayout.Width(285)))
                        PublishBatchSelectedPacksAsync(selectedPacks, currentGame);
                }
            }
        }

        private List<ContentPackDefinition> GetBatchSelectedPacks()
        {
            return _packs
                .Where(pack => pack != null && _batchSelectedPackPaths.Contains(GetPackSelectionKey(pack)))
                .OrderBy(pack => pack.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SelectAllBatchPacks()
        {
            _batchSelectedPackPaths.Clear();
            foreach (var pack in _packs.Where(pack => pack != null))
                _batchSelectedPackPaths.Add(GetPackSelectionKey(pack));

            Repaint();
        }

        private void ClearBatchPackSelection()
        {
            _batchSelectedPackPaths.Clear();
            Repaint();
        }

        private bool IsPackBatchSelected(ContentPackDefinition pack)
        {
            return pack != null && _batchSelectedPackPaths.Contains(GetPackSelectionKey(pack));
        }

        private void SetPackBatchSelected(ContentPackDefinition pack, bool selected)
        {
            if (pack == null)
                return;

            var key = GetPackSelectionKey(pack);
            if (selected)
                _batchSelectedPackPaths.Add(key);
            else
                _batchSelectedPackPaths.Remove(key);

            Repaint();
        }

        private static string GetPackSelectionKey(ContentPackDefinition pack)
        {
            if (pack == null)
                return string.Empty;

            var path = AssetDatabase.GetAssetPath(pack);
            return string.IsNullOrEmpty(path) ? pack.GetInstanceID().ToString(CultureInfo.InvariantCulture) : path;
        }

        private void PruneBatchPackSelection()
        {
            var validKeys = new HashSet<string>(
                _packs.Where(pack => pack != null).Select(GetPackSelectionKey),
                StringComparer.OrdinalIgnoreCase);

            _batchSelectedPackPaths.RemoveWhere(path => !validKeys.Contains(path));
        }

        private void DrawPackGroupToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel, GUILayout.Width(70));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Create Group", GUILayout.Width(110)))
                    ShowCreateGroupDialog();
            }
        }

        private void DrawPackGroups()
        {
            EnsurePackGroupSettings();
            EnsureGroupsForPacks();

            var packsByGroup = _packs
                .Where(pack => pack != null)
                .GroupBy(GetPackGroup)
                .ToDictionary(group => group.Key, group => group.OrderBy(pack => pack.name, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in GetOrderedGroupInfos())
            {
                var groupName = NormalizePackGroup(group.Name);
                packsByGroup.TryGetValue(groupName, out var packs);
                if (packs == null)
                    packs = new List<ContentPackDefinition>();

                string foldoutKey = "pack_group_" + groupName;
                if (!_packGroupFoldouts.TryGetValue(foldoutKey, out var expanded))
                {
                    expanded = false;
                    _packGroupFoldouts[foldoutKey] = expanded;
                }

                var groupRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (Event.current.type == EventType.Repaint)
                {
                    var color = group.Color;
                    color.a = Mathf.Clamp(color.a, 0.12f, 0.55f);
                    EditorGUI.DrawRect(groupRect, color);
                }

                try
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _packGroupFoldouts[foldoutKey] = EditorGUILayout.Foldout(expanded, $"{groupName} ({packs.Count})", true);
                        GUILayout.FlexibleSpace();
                        DrawPackGroupColor(group);
                        DrawPackGroupStatus(packs);

                        if (GUILayout.Button("Rename", GUILayout.Width(70)))
                            ShowRenameGroupDialog(groupName);

                        using (new EditorGUI.DisabledScope(string.Equals(groupName, DefaultPackGroup, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (GUILayout.Button("Delete", GUILayout.Width(60)))
                                DeletePackGroup(groupName);
                        }
                    }

                    if (_packGroupFoldouts[foldoutKey])
                    {
                        GUILayout.Space(4);
                        DrawPackTiles(packs);
                    }
                }
                finally
                {
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawPackGroupColor(ContentPackGroupInfo group)
        {
            EditorGUI.BeginChangeCheck();
            var color = EditorGUILayout.ColorField(GUIContent.none, group.Color, false, false, true, GUILayout.Width(50));
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(_groupSettings, "Change Content Pack Group Color");
            group.Color = color;
            EditorUtility.SetDirty(_groupSettings);
        }

        private void DrawPackGroupStatus(IReadOnlyList<ContentPackDefinition> packs)
        {
            int errors = 0;
            int warnings = 0;

            foreach (var pack in packs)
            {
                GetPackIssueCounts(pack, out var packErrors, out var packWarnings);
                errors += packErrors;
                warnings += packWarnings;
            }

            var status = errors > 0
                ? $"{errors} error{(errors == 1 ? "" : "s")}"
                : warnings > 0
                    ? $"{warnings} warning{(warnings == 1 ? "" : "s")}"
                    : "ready";

            EditorGUILayout.LabelField(status, EditorStyles.miniLabel, GUILayout.Width(90));
        }

        private void DrawPackTiles(IReadOnlyList<ContentPackDefinition> packs)
        {
            const float tileWidth = 210f;
            const float tileHeight = 62f;
            float availableWidth = Mathf.Max(220f, EditorGUIUtility.currentViewWidth - 42f);
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / tileWidth));

            for (int i = 0; i < packs.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int col = 0; col < columns; col++)
                    {
                        int index = i + col;
                        if (index >= packs.Count)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }

                        DrawPackTile(packs[index], tileWidth - 6f, tileHeight);
                    }
                }
            }
        }

        private void DrawPackTile(ContentPackDefinition pack, float width, float height)
        {
            if (pack == null)
                return;

            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            bool selected = pack == _selectedPack;
            bool batchSelected = IsPackBatchSelected(pack);
            bool hovered = rect.Contains(Event.current.mousePosition);
            var toggleRect = new Rect(rect.xMax - 22f, rect.y + 5f, 18f, 18f);

            if (Event.current.type == EventType.Repaint)
            {
                var border = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.10f)
                    : new Color(0f, 0f, 0f, 0.14f);
                var fill = selected
                    ? GetSelectedPackFillColor()
                    : hovered
                        ? new Color(1f, 1f, 1f, 0.12f)
                        : new Color(0f, 0f, 0f, EditorGUIUtility.isProSkin ? 0.10f : 0.04f);

                EditorGUI.DrawRect(rect, selected || batchSelected ? GetSelectedPackBorderColor() : border);
                EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 6, 6),
                richText = true
            };

            GetPackIssueCounts(pack, out var errors, out var warnings);
            string status = errors > 0
                ? $"<color=#ff6060>{errors} error{(errors == 1 ? "" : "s")}</color>"
                : warnings > 0
                    ? $"<color=#ffaa40>{warnings} warning{(warnings == 1 ? "" : "s")}</color>"
                    : _packIssues.ContainsKey(pack)
                        ? "<color=#66cc66>valid</color>"
                        : "<color=#aaaaaa>not validated</color>";

            int itemCount = pack._items != null ? pack._items.Count : 0;
            var label = $"<b>{pack.name}</b>\n{itemCount} item{(itemCount == 1 ? "" : "s")}  |  {status}";

            GUI.Label(rect, label, style);

            EditorGUI.BeginChangeCheck();
            bool nextBatchSelected = GUI.Toggle(toggleRect, batchSelected, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
            {
                SetPackBatchSelected(pack, nextBatchSelected);
                GUI.FocusControl(null);
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                rect.Contains(Event.current.mousePosition) &&
                !toggleRect.Contains(Event.current.mousePosition))
            {
                _selectedPack = pack;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }

        private static Color GetSelectedPackFillColor()
        {
            return MashBoxEditorTheme.SelectedFill();
        }

        private static Color GetSelectedPackBorderColor()
        {
            return MashBoxEditorTheme.SelectedBorder();
        }

        private static string GetPackGroup(ContentPackDefinition pack)
        {
            return NormalizePackGroup(pack != null ? pack.PackGroup : null);
        }

        private static string NormalizePackGroup(string group)
        {
            return string.IsNullOrWhiteSpace(group) ? DefaultPackGroup : group.Trim();
        }

        private void ShowCreateGroupDialog()
        {
            RenameDialog.Show(
                "Create Content Pack Group",
                "Enter a name for the new group:",
                "New Group",
                (newName) =>
                {
                    var normalized = NormalizePackGroup(newName);
                    if (GroupNameExists(normalized))
                    {
                        EditorUtility.DisplayDialog("Group Exists", $"A group named '{normalized}' already exists.", "OK");
                        return;
                    }

                    AddPackGroup(normalized);
                    _packGroupFoldouts["pack_group_" + normalized] = true;

                    Repaint();
                });
        }

        private void ShowRenameGroupDialog(string oldGroup)
        {
            RenameDialog.Show(
                "Rename Content Pack Group",
                $"Rename group '{oldGroup}':",
                oldGroup,
                (newName) =>
                {
                    var normalized = NormalizePackGroup(newName);
                    if (string.Equals(oldGroup, normalized, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (GroupNameExists(normalized))
                    {
                        EditorUtility.DisplayDialog("Group Exists", $"A group named '{normalized}' already exists.", "OK");
                        return;
                    }

                    RenamePackGroup(oldGroup, normalized);
                });
        }

        private bool GroupNameExists(string group)
        {
            EnsurePackGroupSettings();
            return _groupSettings.Groups.Any(info => string.Equals(NormalizePackGroup(info.Name), group, StringComparison.OrdinalIgnoreCase));
        }

        private void RenamePackGroup(string oldGroup, string newGroup)
        {
            EnsurePackGroupSettings();

            var groupInfo = GetGroupInfo(oldGroup);
            if (groupInfo != null)
            {
                Undo.RecordObject(_groupSettings, "Rename Content Pack Group");
                groupInfo.Name = newGroup;
                EditorUtility.SetDirty(_groupSettings);
            }

            var packsInGroup = _packs.Where(pack => pack != null && string.Equals(GetPackGroup(pack), oldGroup, StringComparison.OrdinalIgnoreCase)).ToList();
            if (packsInGroup.Count > 0)
            {
                Undo.RecordObjects(packsInGroup.Cast<Object>().ToArray(), "Rename Content Pack Group");
                foreach (var pack in packsInGroup)
                {
                    pack.PackGroup = newGroup;
                    EditorUtility.SetDirty(pack);
                }
            }

            _packGroupFoldouts.Remove("pack_group_" + oldGroup);
            _packGroupFoldouts["pack_group_" + newGroup] = true;
            Repaint();
        }

        private void DeletePackGroup(string groupName)
        {
            if (string.Equals(groupName, DefaultPackGroup, StringComparison.OrdinalIgnoreCase))
                return;

            if (!EditorUtility.DisplayDialog(
                    "Delete Content Pack Group",
                    $"Delete group '{groupName}'?\n\nPacks in this group will move to '{DefaultPackGroup}'.",
                    "Delete",
                    "Cancel"))
                return;

            EnsurePackGroupSettings();

            var packsInGroup = _packs.Where(pack => pack != null && string.Equals(GetPackGroup(pack), groupName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (packsInGroup.Count > 0)
            {
                Undo.RecordObjects(packsInGroup.Cast<Object>().ToArray(), "Move Packs To Ungrouped");
                foreach (var pack in packsInGroup)
                {
                    pack.PackGroup = DefaultPackGroup;
                    EditorUtility.SetDirty(pack);
                }
            }

            Undo.RecordObject(_groupSettings, "Delete Content Pack Group");
            _groupSettings.Groups.RemoveAll(info => string.Equals(NormalizePackGroup(info.Name), groupName, StringComparison.OrdinalIgnoreCase));
            EnsureDefaultPackGroup();
            EditorUtility.SetDirty(_groupSettings);

            _packGroupFoldouts.Remove("pack_group_" + groupName);
            _packGroupFoldouts["pack_group_" + DefaultPackGroup] = true;
            Repaint();
        }

        private ContentPackGroupSettings EnsurePackGroupSettings()
        {
            if (_groupSettings == null)
                _groupSettings = AssetDatabase.LoadAssetAtPath<ContentPackGroupSettings>(PackGroupSettingsPath);

            if (_groupSettings == null)
            {
                EnsureFolderExists(FORCED_PACKS_FOLDER);
                _groupSettings = ScriptableObject.CreateInstance<ContentPackGroupSettings>();
                _groupSettings.Groups.Add(new ContentPackGroupInfo
                {
                    Name = DefaultPackGroup,
                    Color = new Color(0f, 0f, 0f, 0.35f)
                });
                AssetDatabase.CreateAsset(_groupSettings, PackGroupSettingsPath);
                AssetDatabase.SaveAssets();
            }

            EnsureDefaultPackGroup();
            return _groupSettings;
        }

        private void EnsureDefaultPackGroup()
        {
            if (_groupSettings == null)
                return;

            if (_groupSettings.Groups == null)
                _groupSettings.Groups = new List<ContentPackGroupInfo>();

            if (_groupSettings.Groups.Any(info => string.Equals(NormalizePackGroup(info.Name), DefaultPackGroup, StringComparison.OrdinalIgnoreCase)))
                return;

            _groupSettings.Groups.Insert(0, new ContentPackGroupInfo
            {
                Name = DefaultPackGroup,
                Color = new Color(0f, 0f, 0f, 0.35f)
            });
            EditorUtility.SetDirty(_groupSettings);
        }

        private void EnsureGroupsForPacks()
        {
            EnsurePackGroupSettings();

            foreach (var pack in _packs)
            {
                if (pack == null)
                    continue;

                AddPackGroup(GetPackGroup(pack), false);
            }
        }

        private ContentPackGroupInfo AddPackGroup(string groupName, bool recordUndo = true)
        {
            EnsurePackGroupSettings();

            var normalized = NormalizePackGroup(groupName);
            var existing = GetGroupInfo(normalized);
            if (existing != null)
                return existing;

            if (recordUndo)
                Undo.RecordObject(_groupSettings, "Create Content Pack Group");

            var info = new ContentPackGroupInfo
            {
                Name = normalized,
                Color = GetDefaultPackGroupColor()
            };
            _groupSettings.Groups.Add(info);
            EditorUtility.SetDirty(_groupSettings);
            return info;
        }

        private ContentPackGroupInfo GetGroupInfo(string groupName)
        {
            if (_groupSettings == null || _groupSettings.Groups == null)
                return null;

            return _groupSettings.Groups.FirstOrDefault(info =>
                string.Equals(NormalizePackGroup(info.Name), NormalizePackGroup(groupName), StringComparison.OrdinalIgnoreCase));
        }

        private List<ContentPackGroupInfo> GetOrderedGroupInfos()
        {
            EnsurePackGroupSettings();

            return _groupSettings.Groups
                .Where(info => info != null)
                .GroupBy(info => NormalizePackGroup(info.Name), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(info => string.Equals(NormalizePackGroup(info.Name), DefaultPackGroup, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(info => NormalizePackGroup(info.Name), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Color GetDefaultPackGroupColor()
        {
            return new Color(0f, 0f, 0f, 0.35f);
        }

        private static void GetPackIssueCounts(ContentPackDefinition pack, out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;

            if (pack == null || !_packIssues.TryGetValue(pack, out var issues) || issues == null)
                return;

            errors = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
            warnings = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);
        }

        private void DrawSelectedPackPanel(ContentPackDefinition p)
        {
            if (p == null)
                return;

            var panelRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(panelRect, GetSelectedPackBorderColor());
                EditorGUI.DrawRect(new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, panelRect.height - 2f), GetSelectedPackFillColor());
            }

            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(p.name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"{(p._items != null ? p._items.Count : 0)} item(s)", EditorStyles.miniLabel, GUILayout.Width(70));
                }

                DrawSelectedPackMoveGroup(p);
                DrawInsiderVanillaToggle(p);
                GUILayout.Space(4);
                DrawSelectedPackActions(p);
                GUILayout.Space(6);
                DrawSelectedPackDetails(p);
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawInsiderVanillaToggle(ContentPackDefinition p)
        {
#if MashBoxInsider
            EditorGUI.BeginChangeCheck();
            bool isVanilla = EditorGUILayout.ToggleLeft(
                "Vanilla SDK Content",
                p.IsVanillaContent,
                GUILayout.Width(180));
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(p, "Toggle Vanilla SDK Content");
            p.IsVanillaContent = isVanilla;
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
#endif
        }

        private void DrawSelectedPackMoveGroup(ContentPackDefinition p)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Group", GUILayout.Width(45));
                EditorGUILayout.LabelField(GetPackGroup(p), EditorStyles.miniLabel);

                var groups = GetExistingPackGroups();
                int currentIndex = groups.FindIndex(group => string.Equals(group, GetPackGroup(p), StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0)
                    currentIndex = 0;

                using (new EditorGUI.DisabledScope(groups.Count <= 1))
                {
                    int nextIndex = EditorGUILayout.Popup(currentIndex, groups.ToArray(), GUILayout.Width(160));
                    if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < groups.Count)
                        SetPackGroup(p, groups[nextIndex]);
                }
            }
        }

        private List<string> GetExistingPackGroups()
        {
            EnsurePackGroupSettings();
            EnsureGroupsForPacks();

            return GetOrderedGroupInfos()
                .Select(info => NormalizePackGroup(info.Name))
                .ToList();
        }

        private void SetPackGroup(ContentPackDefinition p, string group)
        {
            if (p == null)
                return;

            string normalized = NormalizePackGroup(group);
            if (string.Equals(GetPackGroup(p), normalized, StringComparison.Ordinal))
                return;

            AddPackGroup(normalized, false);
            Undo.RecordObject(p, "Change Content Pack Group");
            p.PackGroup = normalized;
            EditorUtility.SetDirty(p);
            Repaint();
        }

        private void DrawSelectedPackActions(ContentPackDefinition p)
        {
            string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Unknown");
            bool cookerOk = MashBoxSDKState.Cooker == MashBoxSDKState.CookerStatus.Online;
#if MashBoxDev
            cookerOk = true;
#endif

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build To " + _currentGameName, GUILayout.Height(24)))
                    BuildSinglePack(p);

                if (GUILayout.Button("Validate", GUILayout.Width(90), GUILayout.Height(24)))
                    ValidateSelectedPack(p);

                if (GUILayout.Button($"Publish to {currentGame} Mod.io", GUILayout.Width(190), GUILayout.Height(24)))
                    PublishSelectedPack(p, currentGame, cookerOk);

                if (!cookerOk)
                    DrawCookerRefreshButton(130f, 24f);

#if MashBoxDev
                if (GUILayout.Button("Debug Export .unitypackage", GUILayout.Width(190), GUILayout.Height(24)))
                    ExportDebugUnityPackageForPack(p);
#endif

                if (GUILayout.Button("Rename", GUILayout.Width(90), GUILayout.Height(24)))
                    RenamePack(p);

                if (GUILayout.Button("Delete", GUILayout.Width(70), GUILayout.Height(24)))
                {
                    DeletePack(p);
                    _selectedPack = _packs.FirstOrDefault(pack => pack != null && pack != p);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawSelectedPackDetails(ContentPackDefinition p)
        {
            DrawPackIssuesUI(p);
            GUILayout.Space(6);

            string metaKey = p.name + "_meta";
            if (!_metaFoldouts.ContainsKey(metaKey))
                _metaFoldouts[metaKey] = false;

            _metaFoldouts[metaKey] = EditorGUILayout.Foldout(_metaFoldouts[metaKey], "Pack Metadata", true);
            if (_metaFoldouts[metaKey])
                DrawPackMetadata(p);

            GUILayout.Space(8);
            DrawPackItems(p);

            GUILayout.Space(8);
            var dropRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag prefabs here", _dropZoneStyle);
            HandleDragAndDropForPack(p, dropRect);
        }

        private void DrawPackMetadata(ContentPackDefinition p)
        {
            GUILayout.Space(6);
            EditorGUILayout.LabelField("Mod.io Mod IDs", EditorStyles.boldLabel);

            DrawContentPackModIdMappings(p);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Summary");
                EditorGUI.BeginChangeCheck();
                string newSummary = EditorGUILayout.TextArea(p.summary, GUILayout.MinHeight(40));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(p, "Edit Pack Summary");
                    p.summary = newSummary;
                    EditorUtility.SetDirty(p);
                }
            }
        }

        private void DrawContentPackModIdMappings(ContentPackDefinition p)
        {
            foreach (var def in GameRegistry.Games)
            {
                string existingId = p.GetModIdForGame(def.DisplayName) ?? string.Empty;
                bool isPublishTarget = p.GameModMappings != null &&
                                       p.GameModMappings.Any(g =>
                                           string.Equals(g.GameName, def.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                                           g.IsPublishTarget);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel($"{def.DisplayName} Mod ID");
                    string newValue = EditorGUILayout.TextField(existingId);

                    if (newValue != existingId)
                        ApplyContentPackModId(p, def.DisplayName, newValue);

                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Toggle(
                            isPublishTarget,
                            new GUIContent("Publish Target", "Set automatically to the active Setup game when publishing."),
                            GUILayout.Width(105f));
                    }

                    ModIoModCreator.DrawCreateButton(
                        def.DisplayName,
                        p != null ? p.name : string.Empty,
                        p != null ? p.summary : string.Empty,
                        createdId => ApplyContentPackModId(p, def.DisplayName, createdId),
                        this);
                }
            }
        }

        private static void ApplyContentPackModId(ContentPackDefinition p, string gameName, string modId)
        {
            if (p == null)
                return;

            Undo.RecordObject(p, "Edit Mod ID");
            modId = (modId ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(modId))
                p.SetModIdForGame(gameName, modId);
            else
                p.GameModMappings.RemoveAll(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
        }

        private void DrawPackItems(ContentPackDefinition p)
        {
            EditorGUILayout.LabelField("Items", EditorStyles.boldLabel);

            if (p._items == null || p._items.Count == 0)
            {
                EditorGUILayout.LabelField("<no items>", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < p._items.Count; i++)
            {
                var original = p._items[i];
                var edited = original;

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawItemIssuesUI(original, true);
                    edited = DrawItemWithIconField(ref edited, 40f);
                    DrawClothingControlsIfNeeded(edited);

                    if (GUILayout.Button("X", GUILayout.Width(26)))
                    {
                        Undo.RecordObject(p, "Remove Item From Pack");
                        p._items.RemoveAt(i);
                        EditorUtility.SetDirty(p);
                        AssetDatabase.SaveAssets();
                        i--;
                        RevalidatePack(p);
                        Repaint();
                        GUIUtility.ExitGUI();
                    }
                    else if (edited != original)
                    {
                        Undo.RecordObject(p, "Change Pack Item");
                        p._items[i] = edited;
                        EditorUtility.SetDirty(p);
                        AssetDatabase.SaveAssets();
                        RevalidatePack(p);
                    }
                }

                if (i >= 0 && i < p._items.Count)
                    DrawItemIssuesUI(p._items[i], false);
            }
        }

        private void BuildSinglePack(ContentPackDefinition p)
        {
            var issues = ValidatePackWithExportChecks(p, _rules);
            _packIssues[p] = issues;
            bool isCustom = _currentGameName.ToLowerInvariant().Equals("custom folder");

            if (!isCustom && issues.Any(i => i.severity == ContentPackValidator.Severity.Error))
            {
                ContentPackValidator.LogReport(p, issues, "Build blocked");
                EditorUtility.DisplayDialog("Build blocked", $"'{p.name}' has validation errors. See Console.", "OK");
                return;
            }

            if (isCustom && issues.Any(i => i.severity == ContentPackValidator.Severity.Error))
                Debug.LogWarning($"[ContentPackBuilder] Building '{p.name}' with validation errors (custom folder).");

            BuildPacks(new List<ContentPackDefinition> { p }, cleanMissing: true, isCustom);
        }

#if MashBoxDev
        private void BuildAllPacks()
        {
            RefreshPacks();

            var packsToBuild = _packs
                .Where(pack => pack != null)
                .OrderBy(pack => pack.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (packsToBuild.Count == 0)
            {
                EditorUtility.DisplayDialog("Build All", "No content packs were found to build.", "OK");
                return;
            }

            bool isCustom = string.Equals(_currentGameName, "Custom Folder", StringComparison.OrdinalIgnoreCase);
            string targetLabel = string.IsNullOrWhiteSpace(_currentGameName) ? "the active target" : _currentGameName;
            string message =
                $"Build all {packsToBuild.Count} content pack{(packsToBuild.Count == 1 ? "" : "s")} to {targetLabel}?\n\n" +
                "This will run the normal per-pack build flow for every content pack and may take a while.";

            if (!EditorUtility.DisplayDialog("Build All Content Packs?", message, "Build All", "Cancel"))
                return;

            var blockedPacks = new List<ContentPackDefinition>();

            foreach (var pack in packsToBuild)
            {
                var issues = ValidatePackWithExportChecks(pack, _rules);
                _packIssues[pack] = issues;

                bool hasErrors = issues.Any(i => i.severity == ContentPackValidator.Severity.Error);
                if (!hasErrors)
                    continue;

                if (isCustom)
                {
                    Debug.LogWarning($"[ContentPackBuilder] Building '{pack.name}' with validation errors (custom folder).");
                }
                else
                {
                    blockedPacks.Add(pack);
                    ContentPackValidator.LogReport(pack, issues, "Build All blocked");
                }
            }

            if (blockedPacks.Count > 0)
            {
                string packList = string.Join("\n", blockedPacks.Take(12).Select(pack => "- " + pack.name));
                if (blockedPacks.Count > 12)
                    packList += $"\n- ...and {blockedPacks.Count - 12} more";

                EditorUtility.DisplayDialog(
                    "Build All Blocked",
                    $"Build All was blocked because {blockedPacks.Count} content pack{(blockedPacks.Count == 1 ? "" : "s")} have validation errors.\n\n{packList}\n\nSee Console for details.",
                    "OK");
                return;
            }

            BuildPacks(packsToBuild, cleanMissing: true, isCustom);
        }
#endif

        private void BuildBatchSelectedPacks(List<ContentPackDefinition> selectedPacks)
        {
            selectedPacks = selectedPacks?.Where(pack => pack != null).OrderBy(pack => pack.name, StringComparer.OrdinalIgnoreCase).ToList()
                            ?? new List<ContentPackDefinition>();

            if (selectedPacks.Count == 0)
            {
                EditorUtility.DisplayDialog("Build Selected", "Choose at least one content pack first.", "OK");
                return;
            }

            bool isCustom = string.Equals(_currentGameName, "Custom Folder", StringComparison.OrdinalIgnoreCase);
            string targetLabel = string.IsNullOrWhiteSpace(_currentGameName) ? "the active target" : _currentGameName;

            if (!ValidatePacksForBatchBuild(selectedPacks, isCustom, "Build Selected"))
                return;

            if (!EditorUtility.DisplayDialog(
                    "Build Selected Content Packs?",
                    $"Build {selectedPacks.Count} selected content pack{(selectedPacks.Count == 1 ? "" : "s")} to {targetLabel}?",
                    "Build Selected",
                    "Cancel"))
            {
                return;
            }

            BuildPacks(selectedPacks, cleanMissing: true, isCustom);
        }

        private bool ValidatePacksForBatchBuild(IReadOnlyList<ContentPackDefinition> packs, bool isCustom, string context)
        {
            var blockedPacks = new List<ContentPackDefinition>();

            foreach (var pack in packs)
            {
                var issues = ValidatePackWithExportChecks(pack, _rules);
                _packIssues[pack] = issues;

                bool hasErrors = issues.Any(i => i.severity == ContentPackValidator.Severity.Error);
                if (!hasErrors)
                    continue;

                if (isCustom)
                    Debug.LogWarning($"[ContentPackBuilder] Building '{pack.name}' with validation errors (custom folder).");
                else
                {
                    blockedPacks.Add(pack);
                    ContentPackValidator.LogReport(pack, issues, $"{context} blocked");
                }
            }

            if (blockedPacks.Count == 0)
                return true;

            string packList = string.Join("\n", blockedPacks.Take(12).Select(pack => "- " + pack.name));
            if (blockedPacks.Count > 12)
                packList += $"\n- ...and {blockedPacks.Count - 12} more";

            EditorUtility.DisplayDialog(
                $"{context} Blocked",
                $"{context} was blocked because {blockedPacks.Count} selected content pack{(blockedPacks.Count == 1 ? "" : "s")} have validation errors.\n\n{packList}\n\nSee Console for details.",
                "OK");
            return false;
        }

        private async void PublishBatchSelectedPacksAsync(List<ContentPackDefinition> selectedPacks, string currentGame)
        {
            selectedPacks = selectedPacks?.Where(pack => pack != null).OrderBy(pack => pack.name, StringComparer.OrdinalIgnoreCase).ToList()
                            ?? new List<ContentPackDefinition>();

            if (selectedPacks.Count == 0)
            {
                EditorUtility.DisplayDialog("Publish Selected", "Choose at least one content pack first.", "OK");
                return;
            }

            if (!EnsureCorrectUnityVersionForPublishing(currentGame))
                return;

            bool cookerOk = MashBoxSDKState.Cooker == MashBoxSDKState.CookerStatus.Online;
#if MashBoxDev
            cookerOk = true;
#endif

            if (!cookerOk)
            {
                EditorUtility.DisplayDialog("Cooker Offline", $"The content cooking server is {MashBoxSDKState.Cooker}.", "OK");
                return;
            }

            if (!ModIoAuth.IsAuthorizedForCurrentGame())
            {
                EditorUtility.DisplayDialog(
                    "mod.io Login Required",
                    $"Please log in to mod.io for '{currentGame}' in Setup > Mod.io Login before publishing.",
                    "OK");
                return;
            }

            foreach (var pack in selectedPacks)
            {
                pack.SetPublishTargetGame(currentGame);
                EditorUtility.SetDirty(pack);
            }
            AssetDatabase.SaveAssets();

            if (!ValidatePacksForBatchPublish(selectedPacks, currentGame))
                return;

            if (!EditorUtility.DisplayDialog(
                    "Publish Selected Content Packs?",
                    $"Publish {selectedPacks.Count} selected content pack{(selectedPacks.Count == 1 ? "" : "s")} to {currentGame} Mod.io?",
                    "Publish Selected",
                    "Cancel"))
            {
                return;
            }

            var (ok, err) = await ValidateModioTokenAsync();
            if (!ok)
            {
                ModIoAuth.ClearForCurrentGame();
                Debug.LogWarning($"[ContentPackBuilder] Mod.io token invalid for {currentGame}. Logged out. Details: {err}");
                Repaint();

                EditorUtility.DisplayDialog(
                    "mod.io Login Expired",
                    "Your mod.io session has expired or was revoked.\n\n" +
                    "You've been logged out for this game. Please re-login in the Mod.io section, then try publishing again.",
                    "OK");
                return;
            }

            if (!await EnsureLatestSdkForPublishingAsync())
                return;

            int published = 0;
            for (int i = 0; i < selectedPacks.Count; i++)
            {
                var pack = selectedPacks[i];
                try
                {
                    await PublishToModioPackageAsync(
                        pack,
                        currentGame,
                        $"Publish Selected ({i + 1}/{selectedPacks.Count})");
                    published++;
                }
                catch (OperationCanceledException)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Publish Cancelled", $"Publishing stopped after {published} content pack{(published == 1 ? "" : "s")}.", "OK");
                    return;
                }
                catch (Exception ex)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogError($"[ContentPackBuilder] Batch publish failed on '{pack.name}': {ex}");
                    EditorUtility.DisplayDialog("Publish Failed", $"Publishing failed on '{pack.name}'.\n\n{ex.Message}", "OK");
                    return;
                }
            }

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog(
                "Processing Submissions",
                $"Submitted {published} content pack{(published == 1 ? "" : "s")} to {currentGame}.\n\nYou will be emailed when each one is ready on mod.io.",
                "OK");
        }

        private bool ValidatePacksForBatchPublish(IReadOnlyList<ContentPackDefinition> packs, string currentGame)
        {
            var blocked = new List<string>();

            foreach (var pack in packs)
            {
                if (string.IsNullOrEmpty(pack.GetModIdForGame(currentGame)))
                    blocked.Add($"- {pack.name}: missing Mod ID for {currentGame}");

                RevalidatePack(pack);
                var packIssues = ValidatePackWithExportChecks(pack, _rules);
                _packIssues[pack] = packIssues;
                var (itemsValid, errCount, warnCount) = ComputePackValidation(pack, _rules);
                bool packHasErrors = packIssues.Any(i => i.severity == ContentPackValidator.Severity.Error);

                if (!packHasErrors && itemsValid)
                    continue;

                blocked.Add($"- {pack.name}: pack errors {packIssues.Count(i => i.severity == ContentPackValidator.Severity.Error)}, item errors {errCount}, warnings {warnCount}");
            }

            if (blocked.Count == 0)
                return true;

            string message = string.Join("\n", blocked.Take(14));
            if (blocked.Count > 14)
                message += $"\n- ...and {blocked.Count - 14} more";

            EditorUtility.DisplayDialog(
                "Fix Selected Packs Before Publishing",
                $"Publishing is blocked until these selected packs are ready:\n\n{message}",
                "OK");
            return false;
        }

        private void ValidateSelectedPack(ContentPackDefinition p)
        {
            RevalidatePack(p);
            var issues = ValidatePackWithExportChecks(p, _rules);
            _packIssues[p] = issues;
            int errors = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
            int warnings = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);

            ContentPackValidator.LogReport(p, issues, "Manual validation");

            if (errors == 0 && warnings == 0)
                EditorUtility.DisplayDialog("Validation Passed", $"'{p.name}' has no validation issues.", "OK");
            else
                EditorUtility.DisplayDialog("Validation Results", $"Errors: {errors}\nWarnings: {warnings}\n\nSee Console for details.", "OK");
        }

        private void PublishSelectedPack(ContentPackDefinition p, string currentGame, bool cookerOk)
        {
            if (!EnsureCorrectUnityVersionForPublishing(currentGame))
                return;

            if (!MashBoxSDK.ContentTools.Editor.ModIoAuth.IsAuthorizedForCurrentGame())
            {
                EditorUtility.DisplayDialog(
                    "mod.io Login Required",
                    $"Please log in to mod.io for '{currentGame}' in Setup > Mod.io Login before publishing.",
                    "OK");
                return;
            }

            if (!cookerOk)
            {
                EditorUtility.DisplayDialog("Cooker Offline", $"The content cooking server is {MashBoxSDKState.Cooker}.", "OK");
                return;
            }

            p.SetPublishTargetGame(currentGame);
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();

            var modId = p.GetModIdForGame(currentGame);
            if (string.IsNullOrEmpty(modId))
            {
                EditorUtility.DisplayDialog(
                    "Missing Mod ID",
                    $"This pack does not have a Mod ID configured for '{currentGame}'.\n\nOpen Pack Metadata and enter the Mod ID before publishing.",
                    "OK");
                return;
            }

            RevalidatePack(p);
            var packIssues = ValidatePackWithExportChecks(p, _rules);
            var (itemsValid, errCount, warnCount) = ComputePackValidation(p, _rules);
            bool packHasErrors = packIssues.Any(i => i.severity == ContentPackValidator.Severity.Error);
            bool allValid = !packHasErrors && itemsValid;

            if (!allValid)
            {
                EditorUtility.DisplayDialog(
                    "Fix Validation Before Publishing",
                    $"Pack Errors: {packIssues.Count(i => i.severity == ContentPackValidator.Severity.Error)}\nItem Errors: {errCount}\nWarnings: {warnCount}",
                    "OK");
                return;
            }

            PreflightAndPublishAsync(p, currentGame);
        }

        private void DrawCookerRefreshButton(float width, float height = 0f)
        {
            var options = height > 0f
                ? new[] { GUILayout.Width(width), GUILayout.Height(height) }
                : new[] { GUILayout.Width(width) };

            using (new EditorGUI.DisabledScope(MashBoxSDKState.CheckingCooker))
            {
                if (GUILayout.Button(MashBoxSDKState.CheckingCooker ? "Checking Servers..." : "Refresh Servers", options))
                {
                    MashBoxSDKState.RefreshCookerStatus();
                    Repaint();
                }
            }
        }

        private void RenamePack(ContentPackDefinition p)
        {
            RenameDialog.Show(
                "Rename Content Pack",
                $"Enter a new name for '{p.name}':",
                p.name,
                (newName) =>
                {
                    var safe = SanitizePackName(newName);
                    if (string.IsNullOrWhiteSpace(safe) || safe == p.name)
                        return;

                    if (PackNameExists(safe, out var existingPath))
                    {
                        EditorUtility.DisplayDialog("Duplicate Name", $"A ContentPackDefinition named '{safe}' already exists:\n{existingPath}", "OK");
                        return;
                    }

                    if (AddressablesGroupExists(safe))
                    {
                        EditorUtility.DisplayDialog("Duplicate Group", $"An Addressables Group named '{safe}' already exists.", "OK");
                        return;
                    }

                    string oldName = p.name;
                    string assetPath = AssetDatabase.GetAssetPath(p);
                    AssetDatabase.RenameAsset(assetPath, safe);
                    AssetDatabase.SaveAssets();

                    var settings = AddressableAssetSettingsDefaultObject.Settings;
                    var oldGroup = settings.groups.FirstOrDefault(g => g != null && g.name == oldName);
                    if (oldGroup != null)
                        settings.RemoveGroup(oldGroup);

                    CleanAddressableData();
                    p.SyncToAddressables();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    _selectedPack = p;
                });
        }

        private void DrawPacksListLegacy()
        {
            if (_packs == null || _packs.Count == 0)
                return;

            for (int j = 0; j < _packs.Count; j++)
            {
                var p = _packs[j];////
                if (p == null)
                    continue;

                var key = AssetDatabase.GetAssetPath(p);
                if (string.IsNullOrEmpty(key))
                    continue;

                bool expanded = GetPackFoldout(key);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // ===== PACK HEADER =====
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool wasExpanded = GetPackFoldout(key);
                        expanded = EditorGUILayout.Foldout(wasExpanded, p.name, true);

                        if (!wasExpanded && expanded)
                        {
                            // Pack was just opened
                            RevalidatePack(p);
                        }
                        GUILayout.FlexibleSpace();

                        // Build this single pack to game
                        if (GUILayout.Button("Build To " + _currentGameName, GUILayout.Width(140)))
                        {
                            var issues = ValidatePackWithExportChecks(p, _rules);
                            _packIssues[p] = issues;
                            bool isCustom = _currentGameName.ToLowerInvariant().Equals("custom folder");

                            if (!isCustom && issues.Any(i => i.severity == ContentPackValidator.Severity.Error))
                            {
                                // Only block for real game targets
                                ContentPackValidator.LogReport(p, issues, "Build blocked");
                                EditorUtility.DisplayDialog(
                                    "Build blocked",
                                    $"'{p.name}' has validation errors. See Console.",
                                    "OK");
                            }
                            else
                            {
                                if (isCustom && issues.Any(i => i.severity == ContentPackValidator.Severity.Error))
                                {
                                    Debug.LogWarning($"[ContentPackBuilder] Building '{p.name}' with validation errors (custom folder).");
                                }

                                BuildPacks(new List<ContentPackDefinition> { p }, cleanMissing: true, isCustom);
                            }
                        }

// Optional: Build to custom target

                        bool modIoAuthorized = MashBoxSDK.ContentTools.Editor.ModIoAuth.IsAuthorizedForCurrentGame();
#if MashBoxDev
                        bool allowDevPublish = true;
#else
                        bool allowDevPublish = false;
#endif

                        if (modIoAuthorized || allowDevPublish)
                        {
                            string currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Unknown");

                            bool cookerOk = MashBoxSDKState.Cooker == MashBoxSDKState.CookerStatus.Online;
                        
                            if (GUILayout.Button("Validate", GUILayout.Width(90)))
                            {
                                RevalidatePack(p);  
                                
                                var issues = ValidatePackWithExportChecks(p, _rules);
                                _packIssues[p] = issues;
                                int errors = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
                                int warnings = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);

                                ContentPackValidator.LogReport(p, issues, "Manual validation");

                                if (errors == 0 && warnings == 0)
                                {
                                    EditorUtility.DisplayDialog(
                                        "Validation Passed",
                                        $"'{p.name}' has no validation issues.",
                                        "OK");
                                }
                                else
                                {
                                    EditorUtility.DisplayDialog(
                                        "Validation Results",
                                        $"Errors: {errors}\nWarnings: {warnings}\n\nSee Console for details.",
                                        "OK");
                                }
                            }
                            
                            #if MashBoxDev
                            cookerOk =  true;
                            #endif
                            
                            using (new EditorGUI.DisabledScope(!cookerOk))
                            {
                                
                                if (GUILayout.Button($"Publish to {currentGame} Mod.io", GUILayout.Width(190)))
                                {
                                    if (!cookerOk)
                                    {
                                        EditorUtility.DisplayDialog(
                                            "Cooker Offline",
                                            $"The content cooking server is {MashBoxSDKState.Cooker}.",
                                            "OK");
                                        return;
                                    }

                        
                                    var modId = p.GetModIdForGame(currentGame);

                                    if (string.IsNullOrEmpty(modId))
                                    {
                                        EditorUtility.DisplayDialog(
                                            "Missing Mod ID",
                                            $"This pack does not have a Mod ID configured for '{currentGame}'.\n\n" +
                                            "Open Pack Metadata and enter the Mod ID before publishing.",
                                            "OK"
                                        );
                                        return;
                                    }

                                    // 1. Ensure everything is freshly validated (UI + cache)
                                    RevalidatePack(p);
                                    
                                    var packIssues = ValidatePackWithExportChecks(p, _rules);
                                    
                                    var (itemsValid, errCount, warnCount) = ComputePackValidation(p, _rules);
                                    
                                    bool packHasErrors = packIssues.Any(i => i.severity == ContentPackValidator.Severity.Error);
                                    bool allValid = !packHasErrors && itemsValid;

                                    if (!allValid)
                                    {
                                        EditorUtility.DisplayDialog(
                                            "Fix Validation Before Publishing",
                                            $"Pack Errors: {packIssues.Count(i => i.severity == ContentPackValidator.Severity.Error)}\n" +
                                            $"Item Errors: {errCount}\nWarnings: {warnCount}",
                                            "OK"
                                        );
                                        return;
                                    }

                                    PublishToModioAsync(p, currentGame);
                                }

                            }

                            if (!cookerOk)
                                DrawCookerRefreshButton(130f);

#if MashBoxDev
                            if (GUILayout.Button("Debug Export .unitypackage", GUILayout.Width(190)))
                            {
                                ExportDebugUnityPackageForPack(p);
                            }
#endif
                        }
                        else
                        {
                            GUILayout.Label("🔒 Not authorized with Mod.io", GUILayout.Width(190));
                        }


                        if (GUILayout.Button("Rename", GUILayout.Width(90)))
                        {
                            RenameDialog.Show(
                                "Rename Content Pack",
                                $"Enter a new name for '{p.name}':",
                                p.name,
                                (newName) =>
                                {
                                    var safe = SanitizePackName(newName);
                                    if (string.IsNullOrWhiteSpace(safe) || safe == p.name)
                                        return;

                                    if (PackNameExists(safe, out var existingPath))
                                    {
                                        EditorUtility.DisplayDialog(
                                            "Duplicate Name",
                                            $"A ContentPackDefinition named '{safe}' already exists:\n{existingPath}",
                                            "OK");
                                        return;
                                    }

                                    if (AddressablesGroupExists(safe))
                                    {
                                        EditorUtility.DisplayDialog(
                                            "Duplicate Group",
                                            $"An Addressables Group named '{safe}' already exists.",
                                            "OK");
                                        return;
                                    }

                                    string oldName = p.name;
                                    string assetPath = AssetDatabase.GetAssetPath(p);

                                    // 1. Rename asset
                                    AssetDatabase.RenameAsset(assetPath, safe);
                                    AssetDatabase.SaveAssets();

                                    var settings = AddressableAssetSettingsDefaultObject.Settings;

                                    // 2. Remove OLD group
                                    var oldGroup = settings.groups
                                        .FirstOrDefault(g => g != null && g.name == oldName);

                                    if (oldGroup != null)
                                    {
                                        settings.RemoveGroup(oldGroup);
                                    }

                                    // 3. Force cleanup of schemas + groups
                                    CleanAddressableData();

                                    // 4. Re-sync (creates new group with new name)
                                    p.SyncToAddressables();

                                    AssetDatabase.SaveAssets();
                                    AssetDatabase.Refresh();
                                });
                        }

                        if (GUILayout.Button("Delete", GUILayout.Width(70)))
                        {
                            DeletePack(p);
                            continue;
                        }
                    }

                    // save foldout state
                    SetPackFoldout(key, expanded);
                    
                    if (!expanded)
                        continue;

                    GUILayout.Space(10);

                    // ===== PACK METADATA =====
                    if (!expanded)
                        continue;

                    GUILayout.Space(10);

                    DrawPackIssuesUI(p);
                    
// ===== PACK METADATA =====
                    string metaKey = p.name + "_meta";
                    if (!_metaFoldouts.ContainsKey(metaKey))
                        _metaFoldouts[metaKey] = false;

                    _metaFoldouts[metaKey] =
                        EditorGUILayout.Foldout(_metaFoldouts[metaKey], "Pack Metadata", true);

                    if (_metaFoldouts[metaKey])
                    {
                        // ---- Mod.io Mod IDs Per Game ----
                        GUILayout.Space(8);
                        EditorGUILayout.LabelField("Mod.io Mod IDs", EditorStyles.boldLabel);

                        DrawContentPackModIdMappings(p);
                        
                        
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUI.indentLevel++;

                            // Summary
                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.LabelField("Summary");
                            string newSummary =
                                EditorGUILayout.TextArea(p.summary, GUILayout.MinHeight(40));
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(p, "Edit Pack Summary");
                                p.summary = newSummary;
                                EditorUtility.SetDirty(p);
                            }

                            // Screenshot UI
                            // (paste the rest of your existing screenshot code here)

                            EditorGUI.indentLevel--;
                        }
                    }

                    GUILayout.Space(10);

// ===== ITEMS =====


                    GUILayout.Space(10);

                    // ===== ITEMS =====
                    if (p._items != null && p._items.Count > 0)
                    {
                        for (int i = 0; i < p._items.Count; i++)
                        {
                            var original = p._items[i];
                            var edited = original;

                            using (new EditorGUILayout.HorizontalScope())
                            {
                                DrawItemIssuesUI(original, true);

                                edited = DrawItemWithIconField(ref edited, 40f);

                                // 👕 CLOTHING CONTROLS
                                DrawClothingControlsIfNeeded(edited);

                                if (GUILayout.Button("X", GUILayout.Width(26)))
                                {
                                    Undo.RecordObject(p, "Remove Item From Pack");
                                    p._items.RemoveAt(i);
                                    EditorUtility.SetDirty(p);
                                    AssetDatabase.SaveAssets();
                                    i--;
                                    Repaint();
                                    GUIUtility.ExitGUI();
                                }
                                else if (edited != original)
                                {
                                    Undo.RecordObject(p, "Change Pack Item");
                                    p._items[i] = edited;

                                    EditorUtility.SetDirty(p);
                                    AssetDatabase.SaveAssets();

                                    RevalidatePack(p); // keeps validation UI in sync
                                }
                            }

                            DrawItemIssuesUI(p._items[i], false);
                        }
                    }

                    else
                    {
                        EditorGUILayout.LabelField("<no items>", EditorStyles.miniLabel);
                    }

                    GUILayout.Space(8);

                    // ===== DRAG & DROP =====
                    var dropRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                    GUI.Box(dropRect, "Drag prefabs here", _dropZoneStyle);
                    HandleDragAndDropForPack(p, dropRect);
                }
            }
        }


        private void DrawBuildRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                    {
                        RefreshPacks();
                    }
                }
            }
        }
        
        private void DrawPackIssuesUI(ContentPackDefinition pack)
        {
            if (!_packIssues.TryGetValue(pack, out var issues) || issues == null || issues.Count == 0)
                return;

            int errorCount = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
            int warnCount  = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);

            // Header summary
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (errorCount > 0)
                    EditorGUILayout.LabelField($"✗ {errorCount} Error(s)", _errStyle);
                else if (warnCount > 0)
                    EditorGUILayout.LabelField($"⚠ {warnCount} Warning(s)", _warnStyle);
                else
                    EditorGUILayout.LabelField("✓ Valid", _okStyle);

                // Details
                foreach (var issue in issues)
                {
                    var style = issue.severity == ContentPackValidator.Severity.Error
                        ? _errStyle
                        : _warnStyle;

                    EditorGUILayout.LabelField("• " + issue.message, style);
                }
            }
        }
        
        private async void PublishToModioAsync(ContentPackDefinition p, string currentGame)
        {
            if (!EnsureCorrectUnityVersionForPublishing(currentGame))
                return;

            if (!await EnsureLatestSdkForPublishingAsync())
                return;

            try
            {
                await PublishToModioPackageAsync(p, currentGame, "Publish to Mod.io");
                EditorUtility.ClearProgressBar();

                EditorUtility.DisplayDialog(
                    "Processing Submission",
                    $"Your pack '{p.name}' is processing for {currentGame}.\n\n" +
                    "You will be emailed when it is ready on mod.io.\n\n" +
                    "This may take a few minutes or a few hours.",
                    "OK"
                );
            }
            catch (OperationCanceledException)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Publish Cancelled", "The content pack upload was cancelled.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[ContentPackBuilder] Publish failed: {ex.Message}");
                EditorUtility.DisplayDialog("Publish Failed", ex.Message, "OK");
            }
        }

        private async Task PublishToModioPackageAsync(ContentPackDefinition p, string currentGame, string progressTitle)
        {
            GameTargetUnityVersionValidator.ThrowIfInvalidForPublishing(currentGame);

            var publishIssues = ValidatePackWithExportChecks(p, _rules);
            _packIssues[p] = publishIssues;
            var blockingIssues = publishIssues
                .Where(issue => issue.severity == ContentPackValidator.Severity.Error)
                .ToList();
            if (blockingIssues.Count > 0)
            {
                ContentPackValidator.LogReport(p, publishIssues, "Publish blocked");
                var shownIssues = string.Join("\n", blockingIssues.Take(8).Select(issue => "- " + issue.message));
                var hiddenCount = blockingIssues.Count - 8;
                if (hiddenCount > 0)
                    shownIssues += $"\n- ...and {hiddenCount} more";

                throw new InvalidOperationException(
                    $"Publishing '{p.name}' was blocked by validation errors:\n\n{shownIssues}");
            }

            p.PublisingToGameName = _currentGameName;
            p.modioUserToken = MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentToken;
            p.publisherEmail = MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentEmail;
            p.SetPublishTargetGame(currentGame);

            //EnsureModIoMarkerOnPrefabs(p);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            using var cts = new CancellationTokenSource();
            DisplayCancelableProgress(progressTitle, $"Exporting {p.name} unitypackage...", 0.25f, cts);
            var packagePath = BuildUnityPackageForPack(p);
            if (!p.IsVanillaContent)
                EnsurePackageSizeWithinLimit(packagePath, MaxContentPublishPackageBytes, "content pack");

            float[] progresses = new float[3];

            void ReportCombinedProgress()
            {
                float combined = (progresses[0] + progresses[1] + progresses[2]) / 3f;

                DisplayCancelableProgress(
                    progressTitle,
                    $"Uploading {p.name}... {(int)(combined * 100f)}%",
                    0.45f + combined * 0.50f,
                    cts
                );
            }

            var tasks = new[]
            {
                UploadToContainer(packagePath, "inbox-windows",
                    new Progress<float>(progress => { progresses[0] = progress; ReportCombinedProgress(); }), cts.Token),

                UploadToContainer(packagePath, "inbox-xbox",
                    new Progress<float>(progress => { progresses[1] = progress; ReportCombinedProgress(); }), cts.Token),

                UploadToContainer(packagePath, "inbox-ps5",
                    new Progress<float>(progress => { progresses[2] = progress; ReportCombinedProgress(); }), cts.Token)
            };

            await Task.WhenAll(tasks);
            DisplayCancelableProgress(progressTitle, $"Finalizing {p.name}...", 0.98f, cts);
        }
        private static async Task<bool> EnsureLatestSdkForPublishingAsync()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Checking SDK Version",
                    "Verifying latest MashBox SDK version...",
                    0.15f);

                await MashBoxSDKState.RefreshSdkVersionStateAsync();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (MashBoxSDKState.CanPublishWithInstalledSdk())
                return true;

            EditorUtility.DisplayDialog(
                "SDK Update Required",
                MashBoxSDKState.GetPublishBlockedMessage(),
                "OK");
            return false;
        }

        private static bool EnsureCorrectUnityVersionForPublishing(string currentGame)
        {
            if (GameTargetUnityVersionValidator.IsValidForPublishing(currentGame, out var message))
                return true;

            EditorUtility.DisplayDialog("Correct Unity Version Required", message, "OK");
            return false;
        }

        private async Task UploadToContainer(string packagePath, string container, IProgress<float> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(packagePath);

            var (jobId, uploadUrl) = await RequestUploadUrlAsync(fileName, container);

            try
            {
                await UploadFileToSasAsync(packagePath, uploadUrl, progress, cancellationToken);
            }
            catch (Exception ex) when (LooksLikeSasClockWindowIssue(ex))
            {
                Debug.LogWarning($"[ContentPackBuilder] Upload URL had an invalid SAS time window. Requesting a fresh upload URL and retrying once. Details: {ex.Message}");
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                var (_, refreshedUploadUrl) = await RequestUploadUrlAsync(fileName, container);
                await UploadFileToSasAsync(packagePath, refreshedUploadUrl, progress, cancellationToken);
            }

           // Debug.Log($"[UGC] Uploaded to {container} (job {jobId})");
        }
        [Serializable]
        class UploadRequest {
            public string fileName;
            public string container;
            public string region;
        }

        private static readonly HashSet<string> UsUploadCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AG", "AI", "AR", "AW", "BB", "BL", "BM", "BO", "BQ", "BR", "BS", "BZ",
            "CA", "CL", "CO", "CR", "CU", "CW", "DM", "DO", "EC", "FK", "GD", "GF",
            "GL", "GP", "GT", "GY", "HN", "HT", "JM", "KN", "KY", "LC", "MF", "MQ",
            "MS", "MX", "NI", "PA", "PE", "PM", "PR", "PY", "SR", "SV", "SX", "TC",
            "TT", "US", "UY", "VC", "VE", "VG", "VI"
        };

        private static readonly HashSet<string> EuUploadCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AD", "AE", "AF", "AL", "AM", "AO", "AT", "AZ", "BA", "BD", "BE", "BF",
            "BG", "BH", "BI", "BJ", "BY", "CD", "CF", "CG", "CH", "CI", "CM", "CN",
            "CY", "CZ", "DE", "DJ", "DK", "DZ", "EE", "EG", "EH", "ER", "ES", "ET",
            "FI", "FO", "FR", "GA", "GB", "GE", "GH", "GI", "GM", "GN", "GQ", "GR",
            "GW", "HK", "HR", "HU", "ID", "IE", "IL", "IN", "IQ", "IR", "IS", "IT",
            "JO", "JP", "KE", "KG", "KH", "KM", "KP", "KR", "KW", "KZ", "LA", "LB",
            "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME",
            "MG", "MK", "ML", "MM", "MN", "MO", "MR", "MT", "MU", "MV", "MW", "MY",
            "MZ", "NA", "NE", "NG", "NL", "NO", "NP", "OM", "PH", "PK", "PL", "PS",
            "PT", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SC", "SD", "SE", "SG",
            "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "ST", "SY", "SZ", "TD",
            "TF", "TG", "TH", "TJ", "TM", "TN", "TR", "TW", "TZ", "UA", "UG", "UZ",
            "VA", "VN", "XK", "YE", "YT", "ZA", "ZM", "ZW"
        };

        private static readonly string[] UsTimeZoneHints =
        {
            "alaska", "atlantic", "australia", "canada", "caribbean", "central", "eastern",
            "fiji", "hawaii", "mexico", "mountain", "new zealand", "newfoundland", "pacific",
            "samoa", "tasmania"
        };

        private static readonly string[] EuTimeZoneHints =
        {
            "africa", "arab", "asia", "china", "europe", "gmt", "greenwich", "india", "israel",
            "japan", "korea", "russia", "singapore", "tokyo", "turkey", "utc", "w. europe"
        };

        private static async Task<(string jobId, string uploadUrl)> RequestUploadUrlAsync(string fileName, string container)
        {
            var region = DetermineUploadRegion();
            var json = JsonUtility.ToJson(new UploadRequest {
                fileName = fileName,
                container = container,
                region = region
            });

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            using var req = new HttpRequestMessage(HttpMethod.Post, UploaderEndpoint)
            {
                Content = content
            };

            using var res = await SharedHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Proxy request failed: {(int)res.StatusCode} {res.ReasonPhrase}");

            var data = JsonUtility.FromJson<UploadResponse>(body);
            if (data == null || string.IsNullOrEmpty(data.uploadUrl))
                throw new Exception("Invalid proxy response (missing uploadUrl).");

            return (data.jobId, data.uploadUrl);
        }

        private static string DetermineUploadRegion()
        {
            if (TryGetRegionFromCulture(out var region))
                return region;

            return DetermineUploadRegionFromTimeZone();
        }

        private static bool TryGetRegionFromCulture(out string region)
        {
            var cultures = new[]
            {
                CultureInfo.CurrentCulture,
                CultureInfo.CurrentUICulture,
                CultureInfo.InstalledUICulture
            };

            foreach (var culture in cultures)
            {
                if (culture == null || string.IsNullOrWhiteSpace(culture.Name))
                    continue;

                try
                {
                    var countryCode = new RegionInfo(culture.Name).TwoLetterISORegionName;
                    if (UsUploadCountries.Contains(countryCode))
                    {
                        region = "us";
                        return true;
                    }

                    if (EuUploadCountries.Contains(countryCode))
                    {
                        region = "eu";
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Neutral cultures like "en" do not map cleanly to a region.
                }
            }

            region = null;
            return false;
        }

        private static string DetermineUploadRegionFromTimeZone()
        {
            try
            {
                var localTimeZone = TimeZoneInfo.Local;
                var descriptor = $"{localTimeZone.Id} {localTimeZone.DisplayName}".ToLowerInvariant();

                if (UsTimeZoneHints.Any(hint => descriptor.Contains(hint)))
                    return "us";

                if (EuTimeZoneHints.Any(hint => descriptor.Contains(hint)))
                    return "eu";

                var offset = localTimeZone.BaseUtcOffset;
                if (offset <= TimeSpan.FromHours(-2))
                    return "us";

                if (offset >= TimeSpan.Zero && offset <= TimeSpan.FromHours(10))
                    return "eu";
            }
            catch
            {
                // Fall through to the default region.
            }

            return "us";
        }



// Content that reports progress while HttpClient streams it
        private sealed class ProgressStreamContent : HttpContent
        {
            private readonly Stream _source;
            private readonly int _bufferSize;
            private readonly IProgress<float> _progress; // 0..1
            private readonly long _contentLength;
            private readonly int _minMsBetweenReports;

            public ProgressStreamContent(Stream source, int bufferSize, IProgress<float> progress,
                int minMsBetweenReports = 150)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _bufferSize = Mathf.Max(8 * 1024, bufferSize);
                _progress = progress;
                _minMsBetweenReports = minMsBetweenReports;
                _contentLength = source.CanSeek ? source.Length : -1;
                if (_contentLength >= 0) Headers.ContentLength = _contentLength;
                Headers.Add("x-ms-blob-type", "BlockBlob"); // Azure Blob requirement for simple PUT
            }

            protected override async Task SerializeToStreamAsync(Stream target, TransportContext ctx)
            {
                var buffer = new byte[_bufferSize];
                long total = 0;
                int read;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                while ((read = await _source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await target.WriteAsync(buffer, 0, read);
                    total += read;

                    if (_contentLength > 0 && sw.ElapsedMilliseconds >= _minMsBetweenReports)
                    {
                        _progress?.Report((float)total / _contentLength);
                        sw.Restart();
                    }
                }

                _progress?.Report(1f);
            }

            protected override bool TryComputeLength(out long length)
            {
                if (_contentLength >= 0)
                {
                    length = _contentLength;
                    return true;
                }

                length = 0;
                return false;
            }
        }

        private static async Task UploadFileToSasAsync(string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            // Optional: per-host limit bump that actually works on Mono
            var sp = System.Net.ServicePointManager.FindServicePoint(new Uri(uploadUrl));
            sp.ConnectionLimit = Math.Max(sp.ConnectionLimit, 64);
            sp.Expect100Continue = false;

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Upload file not found.", filePath);

            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.ExpectContinue = false;

            const long simplePutThreshold = 128L * 1024L * 1024L;
            if (fileInfo.Length <= simplePutThreshold)
            {
                await UploadFileSinglePutWithRetryAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await UploadFileAsBlockBlobAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (LooksLikeBlockUploadSasIssue(ex))
            {
                Debug.LogWarning($"[ContentPackBuilder] Block upload was rejected by the SAS URL. Falling back to a direct blob upload. Details: {ex.Message}");
                await UploadFileSinglePutWithRetryAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (LooksLikeTransientUploadFailure(ex))
            {
                Debug.LogWarning($"[ContentPackBuilder] Block upload hit a transient connection issue. Falling back to a direct blob upload retry path. Details: {ex.Message}");
                await UploadFileSinglePutWithRetryAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task UploadFileSinglePutAsync(HttpClient http, string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            using var fs = File.OpenRead(filePath);
            using var content = new ProgressStreamContent(fs, 4 * 1024 * 1024, progress, minMsBetweenReports: 300);
            using var req = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = content };

            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new Exception($"Upload failed: {(int)res.StatusCode} {res.ReasonPhrase}");
            }
        }

        private static async Task UploadFileSinglePutWithRetryAsync(HttpClient http, string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            var delaysMs = new[] { 0, 1500, 4000 };

            for (var attempt = 0; attempt < delaysMs.Length; attempt++)
            {
                if (delaysMs[attempt] > 0)
                    await Task.Delay(delaysMs[attempt], cancellationToken).ConfigureAwait(false);

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UploadFileSinglePutAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (!LooksLikeTransientUploadFailure(ex) && !LooksLikeBlockUploadSasIssue(ex))
                        throw BuildFriendlyUploadException(ex);
                }
            }

            throw BuildFriendlyUploadException(lastError);
        }

        private static async Task UploadFileAsBlockBlobAsync(HttpClient http, string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            const int blockSize = 8 * 1024 * 1024;
            const int maxParallelUploads = 6;
            var blockIds = new List<string>();
            var inFlightUploads = new List<Task>(maxParallelUploads);

            using var fs = File.OpenRead(filePath);
            var totalLength = fs.Length;
            var buffer = new byte[blockSize];
            long uploaded = 0;
            var blockIndex = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockIndex.ToString("D6")));
                blockIds.Add(blockId);
                blockIndex++;

                var blockData = new byte[read];
                Buffer.BlockCopy(buffer, 0, blockData, 0, read);

                inFlightUploads.Add(UploadBlockAsync(http, uploadUrl, blockData, blockId, cancellationToken,
                    bytesUploaded =>
                    {
                        var completed = Interlocked.Add(ref uploaded, bytesUploaded);
                        progress?.Report((float)completed / totalLength);
                    }));

                if (inFlightUploads.Count >= maxParallelUploads)
                {
                    await Task.WhenAll(inFlightUploads).ConfigureAwait(false);
                    inFlightUploads.Clear();
                }
            }

            if (inFlightUploads.Count > 0)
            {
                await Task.WhenAll(inFlightUploads).ConfigureAwait(false);
                inFlightUploads.Clear();
            }

            var xml = BuildAzureBlockListXml(blockIds);
            using var finalizeContent = new StringContent(xml, Encoding.UTF8, "application/xml");
            var finalizeUrl = AppendQueryString(uploadUrl, "comp=blocklist");
            using var finalizeReq = new HttpRequestMessage(HttpMethod.Put, finalizeUrl) { Content = finalizeContent };
            using var finalizeRes = await http.SendAsync(finalizeReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!finalizeRes.IsSuccessStatusCode)
            {
                throw new Exception($"Block list commit failed: {(int)finalizeRes.StatusCode} {finalizeRes.ReasonPhrase}");
            }

            progress?.Report(1f);
        }

        private static async Task UploadBlockAsync(
            HttpClient http,
            string uploadUrl,
            byte[] data,
            string blockId,
            CancellationToken cancellationToken,
            Action<int> onUploaded)
        {
            using var content = new ByteArrayContent(data);
            content.Headers.Add("x-ms-blob-type", "BlockBlob");

            var blockUrl = AppendQueryString(uploadUrl, $"comp=block&blockid={Uri.EscapeDataString(blockId)}");
            using var req = new HttpRequestMessage(HttpMethod.Put, blockUrl) { Content = content };
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new Exception($"Block upload failed: {(int)res.StatusCode} {res.ReasonPhrase}");
            }

            onUploaded?.Invoke(data.Length);
        }

        private static string AppendQueryString(string url, string query)
        {
            return url.Contains("?") ? $"{url}&{query}" : $"{url}?{query}";
        }

        private static string BuildAzureBlockListXml(IEnumerable<string> blockIds)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
            foreach (var blockId in blockIds)
                sb.Append("<Latest>").Append(blockId).Append("</Latest>");
            sb.Append("</BlockList>");
            return sb.ToString();
        }

        private static bool LooksLikeBlockUploadSasIssue(Exception ex)
        {
            var message = ex?.ToString() ?? string.Empty;
            return message.Contains("AuthenticationFailed", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Signed expiry time", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Server failed to authenticate the request", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("403", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeSasClockWindowIssue(Exception ex)
        {
            var message = ex?.ToString() ?? string.Empty;
            return message.Contains("Signed expiry time", StringComparison.OrdinalIgnoreCase) &&
                   message.Contains("signed start time", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeTransientUploadFailure(Exception ex)
        {
            var message = ex?.ToString() ?? string.Empty;
            return ex is HttpRequestException ||
                   ex is IOException ||
                   message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("transport connection", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("connection was closed", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        private static Exception BuildFriendlyUploadException(Exception ex)
        {
            var message = ex?.ToString() ?? "Unknown upload error.";

            if (LooksLikeBlockUploadSasIssue(ex))
            {
                if (LooksLikeSasClockWindowIssue(ex))
                {
                    return new Exception(
                        "The upload URL from the backend has an invalid time window.\n\n" +
                        "Azure reported that the SAS expiry time is earlier than the SAS start time. The SDK retried once with a fresh upload URL, but the backend is still issuing malformed SAS tokens.\n\n" +
                        "This needs a backend fix in the upload URL generator.",
                        ex);
                }

                return new Exception(
                    "The upload URL from the backend was rejected for this package upload.\n\n" +
                    "The SDK tried the chunked upload path and then a direct blob upload fallback, but the SAS authorization still failed.\n\n" +
                    "This usually means the backend needs a longer-lived SAS token or different blob permissions.\n\n" +
                    "See the Editor log for the full Azure response.",
                    ex);
            }

            if (LooksLikeTransientUploadFailure(ex))
            {
                return new Exception(
                    "The upload connection was interrupted while sending the package.\n\n" +
                    "The SDK retried the direct upload path, but the connection still failed. Please try again, and if it keeps happening we should inspect the backend/blob upload limits.\n\n" +
                    "See the Editor log for the full network error.",
                    ex);
            }

            return ex ?? new Exception("Upload failed for an unknown reason.");
        }

        private static void DisplayCancelableProgress(string title, string info, float progress, CancellationTokenSource cts)
        {
            if (cts != null && !cts.IsCancellationRequested &&
                EditorUtility.DisplayCancelableProgressBar(title, info, progress))
            {
                cts.Cancel();
                throw new OperationCanceledException();
            }
        }




        

        private bool EnsureValidBuildFolder(out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(_buildLocation))
            {
                error = "No build output folder set.";
                return false;
            }

            // Normalize slashes
            _buildLocation = _buildLocation.Replace("\\", "/");

            // Must be an absolute path
            if (!Path.IsPathRooted(_buildLocation))
            {
                error = $"Build output folder must be an absolute path:\n{_buildLocation}";
                return false;
            }

            try
            {
                if (!Directory.Exists(_buildLocation))
                    Directory.CreateDirectory(_buildLocation);

                // Verify write access with a quick temp file probe
                var probe = Path.Combine(_buildLocation, ".write_probe.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (System.Exception ex)
            {
                error = $"Build output folder is not writable:\n{_buildLocation}\n\n{ex.Message}";
                return false;
            }
        }
        
        
        private ContentPackDefinition FindCorePack()
        {
            string[] guids = AssetDatabase.FindAssets("t:ContentPackDefinition");

            ContentPackDefinition found = null;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pack = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(path);

                if (pack == null)
                    continue;

                if (pack.IsCorePack)
                {
                    if (found != null)
                    {
                        Debug.LogError("[CorePack] Multiple Core Packs found! Only one is allowed.");
                        return null;
                    }

                    found = pack;
                }
            }

            if (found == null)
            {
                Debug.LogWarning("[CorePack] No Core Pack found. Create one and enable IsCorePack.");
            }

            return found;
        }
        
        private void BuildCorePack(ContentPackDefinition corePack)
        {
            if (corePack == null)
                return;

            Debug.Log("[CorePack] Building core content bundle...");
            

            var list = new List<ContentPackDefinition> { corePack };

            corePack.SyncToAddressables();

            bool isCustomFolder = true;
            BuildPacks(list, cleanMissing: true, true);
        }

        private void BuildPacks(List<ContentPackDefinition> list, bool cleanMissing, bool customFolder)
        {
            RefreshBuildLocation();

            _settings = AddressableAssetSettingsDefaultObject.Settings;
            if (_settings == null)
            {
                Debug.LogError("Addressables settings not found.");
                return;
            }

            if (list == null || list.Count == 0)
            {
                Debug.LogWarning("No packs selected to build.");
                return;
            }


            string buildErr;
            if (!EnsureValidBuildFolder(out buildErr))
            {
                EditorUtility.DisplayDialog("Invalid Build Output Folder", buildErr, "OK");
                return;
            }

            RefreshPacks();

            if (cleanMissing)
            {
                foreach (var p in list)
                {
                    if (p == null || p._items == null) continue;
                    int before = p._items.Count;
                    if (before == 0) continue;

                    Undo.RecordObject(p, "Clean Missing Items");
                    p._items.RemoveAll(x => x == null);
                    //p.GameName = _currentGameName;
                    p.modioUserToken = MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentToken;
                    p.publisherEmail = MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentEmail;

                    p.BuildToCustomFolder = customFolder;
                    
                    if (p._items.Count != before)
                    {
                        EditorUtility.SetDirty(p);
                        p.SyncToAddressables();
                    }

                }

                AssetDatabase.SaveAssets();
            }

            string buildLocation = _buildLocation;

            if (_currentGameName == "Custom Folder")
            {
                string target = EditorUserBuildSettings.activeBuildTarget.ToString();
                buildLocation = Path.Combine(buildLocation, target).Replace("\\", "/");
            }

            var opts = new AddressablesPackBuilder.BuildOptions
            {
                profileId = _settings.activeProfileId,
                rebuildPlayerContent = true,
                enableRemoteCatalog = true,
                disableOtherGroups = true,
                writeManifestJson = true,
                manifestFileName = null,
                setPlayerVersionOverride = true,

                sessionRemoteBuildRootOverride = string.IsNullOrEmpty(buildLocation) ? null : buildLocation.Replace("\\", "/"),
                sessionRemoteLoadRootOverride = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}",
            };

            int built = 0;
            
            foreach (var p in list)
            {
                if (p == null) continue;
                if (!p.IsCorePack)
                {
                    // Capture 2K icons before building
                    try
                    {
                        var items = (p._items ?? new List<GameObject>()).Where(x => x != null);
                        ContentIconCaptureUtility.CaptureIconsForPrefabs(
                            items,
                            renderSize: 2048,
                            outputSize: 2048,
                            imageType: ContentIconCaptureUtility.ImageType.PNG
                        );
                        Debug.Log($"[ContentPackBuilder] Captured 2K icons for pack '{p.name}'.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ContentPackBuilder] Icon capture failed for '{p?.name}': {ex.Message}");
                    }
                }
                

                try
                {
                    p._icons = new List<Texture2D>();
                    bool changedIcons = false;
                    
                    if (!p.IsCorePack)
                    foreach (var go in (p._items ?? new List<GameObject>()).Where(x => x != null))
                    {
                        string folder;
                        var iconPath = ComputeIconPathForPrefab(go, out folder);
                        if (string.IsNullOrEmpty(iconPath)) continue;

                        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                        if (tex != null && !p._icons.Contains(tex))
                        {
                            p._icons.Add(tex);
                            changedIcons = true;
                        }
                    }
                    

                    if (changedIcons)
                    {
                        EditorUtility.SetDirty(p);
                        AssetDatabase.SaveAssets();
                    }

                    // ensure the _Icons group is updated before the addressables build for this pack
                    p.SyncToAddressables();
                }
                catch (System.Exception exCollect)
                {
                    Debug.LogError(
                        $"[ContentPackBuilder] Icon registration failed for '{p?.name}': {exCollect.Message}");
                }

                AddressablesPackBuilder.BuildPack(p, opts);
                built++;
            }

            Debug.Log(built > 0 ? $"Built {built} content pack(s)." : "Nothing to build.");
        }

        private void DeletePack(ContentPackDefinition p)
        {
            if (p == null) return;

            var packPath = AssetDatabase.GetAssetPath(p);
            if (string.IsNullOrEmpty(packPath)) return;

            AddressableAssetGroup matchingGroup = null;
            if (_settings != null)
            {
                matchingGroup = _settings.groups
                    .FirstOrDefault(g => g != null
                                         && g.name == p.name
                                         && !g.ReadOnly
                                         && g != _settings.DefaultGroup);
            }

            string msg = matchingGroup != null
                ? $"Delete content pack '{p.name}' and its Addressables group with the same name?\nThis cannot be undone."
                : $"Delete content pack '{p.name}' from the project?\n(This pack has no matching writable Addressables group.)\nThis cannot be undone.";

            bool confirm = EditorUtility.DisplayDialog("Delete Content Pack?", msg, "Delete", "Cancel");
            if (!confirm) return;

            if (Selection.activeObject == p) Selection.activeObject = null;

            AssetDatabase.DeleteAsset(packPath);

            if (matchingGroup != null)
                _settings.RemoveGroup(matchingGroup);

            CleanAddressablesGroups(_settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshPacks();
            
            CleanAddressableData();
        }

        // ---------- Drag & Drop support ----------
        private void HandleDragAndDropForPack(ContentPackDefinition pack, Rect dropRect)
        {
            var e = Event.current;
            if (!dropRect.Contains(e.mousePosition)) return;

            // Accept GameObjects / prefab assets
            bool HasValidObject(Object o)
            {
                if (o is GameObject go)
                {
                    // If it's a scene object, try to resolve to prefab asset
                    var path = AssetDatabase.GetAssetPath(go);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                        return true;

                    // Try linked prefab
                    var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (prefab != null)
                    {
                        var p = AssetDatabase.GetAssetPath(prefab);
                        return !string.IsNullOrEmpty(p) && p.EndsWith(".prefab");
                    }
                }

                return false;
            }

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                var anyValid = DragAndDrop.objectReferences.Any(HasValidObject);
                DragAndDrop.visualMode = anyValid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (e.type == EventType.DragPerform && anyValid)
                {
                    DragAndDrop.AcceptDrag();

                    Undo.RecordObject(pack, "Add Items To Pack");
                    if (pack._items == null) pack._items = new List<GameObject>();

                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (!(obj is GameObject go)) continue;

                        // Prefer prefab asset version
                        GameObject assetGo = null;
                        var assetPath = AssetDatabase.GetAssetPath(go);
                        if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab"))
                        {
                            assetGo = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        }
                        else
                        {
                            var fromSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
                            if (fromSource != null)
                            {
                                var srcPath = AssetDatabase.GetAssetPath(fromSource);
                                if (!string.IsNullOrEmpty(srcPath) && srcPath.EndsWith(".prefab"))
                                    assetGo = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                            }
                        }

                        if (assetGo == null) continue;
                        if (!pack._items.Contains(assetGo))
                            pack._items.Add(assetGo);

                        // validate on add
                        _itemIssues[assetGo] = ContentPackValidator.ValidateItem(assetGo, _rules);
                        _packIssues[pack] = ValidatePackWithExportChecks(pack, _rules);
                        
                        pack.SyncToAddressables();
                    }

                    EditorUtility.SetDirty(pack);
                    AssetDatabase.SaveAssets();
                    Repaint();
                }

                e.Use();
            }
        }

        private async void PreflightAndPublishAsync(ContentPackDefinition p, string currentGame)
        {
            // 0) Quick “/me” check
            var (ok, err) = await ValidateModioTokenAsync();
            if (!ok)
            {
                // Auto-logout for this game and inform the user
                MashBoxSDK.ContentTools.Editor.ModIoAuth.ClearForCurrentGame();
                Debug.LogWarning($"[ContentPackBuilder] Mod.io token invalid for {currentGame}. Logged out. Details: {err}");
                Repaint();

                EditorUtility.DisplayDialog(
                    "mod.io Login Expired",
                    "Your mod.io session has expired or was revoked.\n\n" +
                    "You’ve been logged out for this game. Please re-login in the Mod.io section, then try publishing again.",
                    "OK"
                );
                return;
            }

            // 1) Token is valid → carry on with your existing flow
            PublishToModioAsync(p, currentGame);

        }


        // ---------- Validation helpers (live inline UI) ----------
        private void ValidateItemLive(GameObject go)
        {
            if (!go) return;
            var issues = ContentPackValidator.ValidateItem(go, _rules);
            _itemIssues[go] = issues;
            Repaint();
        }

        // Returns true only if there are NO errors and NO warnings across all items in the pack.
        private static (bool allValid, int errors, int warnings) ComputePackValidation(ContentPackDefinition pack, ContentValidationRules rules)
        {
            var issues = ValidatePackWithExportChecks(pack, rules);
            _packIssues[pack] = issues;
            int err = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
            int warn = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);
            return (err == 0 && warn == 0, err, warn);
        }

        private static List<ContentPackValidator.Issue> ValidatePackWithExportChecks(ContentPackDefinition pack, ContentValidationRules rules)
        {
            var issues = ContentPackValidator.ValidatePack(pack, rules);
            AddBakeryLightmapValidationIssue(pack, issues);
            return issues;
        }

        private static async Task<(bool ok, string error)> ValidateModioTokenAsync()
        {
            var token = MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentToken;
            var apiBase = ResolveCurrentGameModIoApiBase();

            if (string.IsNullOrEmpty(token))
                return (false, "No mod.io token is set for the current game.");

            if (string.IsNullOrWhiteSpace(apiBase))
                return (false, "No mod.io API base is configured for the current game.");

            try
            {
                var url = $"{apiBase}/me";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var res = await http.GetAsync(url);
                var body = await res.Content.ReadAsStringAsync();

                if ((int)res.StatusCode == 200)
                    return (true, null);

                // Treat any non-200 as invalid; common: 401 expired/revoked/malformed
                return (false, $"HTTP {(int)res.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                return (false, $"Token check failed: {ex.Message}");
            }
        }

        private static string ResolveCurrentGameModIoApiBase()
        {
            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", string.Empty);
            foreach (var game in GameRegistry.Games)
            {
                if (!string.Equals(game.DisplayName, currentGame, StringComparison.OrdinalIgnoreCase))
                    continue;

                var registeredApiBase = game.ModIoApiBase ?? string.Empty;
                if (IsModApiBase(registeredApiBase))
                {
                    EditorPrefs.SetString("ModIo.ApiBase", registeredApiBase);
                    return registeredApiBase.TrimEnd('/');
                }
            }

            var apiBase = EditorPrefs.GetString("ModIo.ApiBase", string.Empty);
            if (IsModApiBase(apiBase))
                return apiBase.TrimEnd('/');

            return string.IsNullOrWhiteSpace(apiBase) ? string.Empty : apiBase.TrimEnd('/');
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

        

        private void DrawItemIssuesUI(GameObject go, bool headerOnly)
        {
            if (!go) return;
            if (!_itemIssues.TryGetValue(go, out var issues) || issues == null)
                return;

            int errorCount = issues.Count(i => i.severity == ContentPackValidator.Severity.Error);
            int warnCount  = issues.Count(i => i.severity == ContentPackValidator.Severity.Warning);

            // ===== HEADER STATUS =====
            if (headerOnly)
            {
                if (errorCount == 0 && warnCount == 0)
                {
                    GUILayout.Label("✓", _okStyle, GUILayout.Width(20));
                }
                else
                {
                    GUILayout.Label($"✗ {errorCount} {(errorCount == 1 ? "Error" : "Errors")}", _errStyle, GUILayout.Width(80)
                    );
                }

                return;
            }

            // ===== INLINE DETAILS =====
            if (errorCount == 0 && warnCount == 0)
                return;

            EditorGUI.indentLevel++;

            foreach (var issue in issues)
            {
                var style = issue.severity == ContentPackValidator.Severity.Error
                    ? _errStyle
                    : _warnStyle;

                EditorGUILayout.LabelField("• " + issue.message, style);
            }

            EditorGUI.indentLevel--;
        }



        // ---------- Pack list + Addressables helpers ----------
        private void CreatePackWithName(string safeName)
        {
            EnsureFolderExists(FORCED_PACKS_FOLDER);

            string assetPath = $"{FORCED_PACKS_FOLDER}/{safeName}.asset";
            var asset = ScriptableObject.CreateInstance<ContentPackDefinition>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            asset.SyncToAddressables();
            //EditorGUIUtility.PingObject(asset);
            RefreshPacks();

            CleanAddressableData();
        }

        private void RefreshPacks()
        {
            var previous = new Dictionary<string, bool>(_foldouts);
            _packs.Clear();
            _foldouts.Clear();

            // Find ALL ContentPackDefinition assets in the project
            string[] guids = AssetDatabase.FindAssets("t:ContentPackDefinition");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pack = AssetDatabase.LoadAssetAtPath<ContentPackDefinition>(path);
                if (pack != null && !pack.IsCorePack)
                    _packs.Add(pack);
            }

            foreach (var pack in _packs)
            {
                pack.RemoveMissingReferences();
                var path = AssetDatabase.GetAssetPath(pack);
                _foldouts[path] = previous.ContainsKey(path) ? previous[path] : true;
            }

            EnsureGroupsForPacks();
            PruneBatchPackSelection();
            Repaint();
        }


        private static void EnsureFolderExists(string folder)
        {
            var parts = folder.Split('/');
            string cur = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0 && parts[i] == "Assets")
                {
                    cur = "Assets";
                    continue;
                }

                var parent = string.IsNullOrEmpty(cur) ? "Assets" : cur;
                var name = parts[i];
                if (!AssetDatabase.IsValidFolder(Path.Combine(parent, name)))
                    AssetDatabase.CreateFolder(parent, name);
                cur = Path.Combine(parent, name).Replace("\\", "/");
            }
        }

// REPLACE the existing SanitizePackName with this:
        private static string SanitizePackName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ')
                    sb.Append(c); // allow A–Z a–z 0–9 and space
                // everything else is dropped
            }

            // collapse multiple spaces and trim again
            var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
            return s;
        }

        private static bool PackNameExists(string packName, out string path)
        {
            path = null;
            var guids = AssetDatabase.FindAssets($"t:ContentPackDefinition {packName}");
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(p);
                if (string.Equals(name, packName, System.StringComparison.OrdinalIgnoreCase))
                {
                    path = p;
                    return true;
                }
            }

            return false;
        }

        private bool AddressablesGroupExists(string groupName)
        {
            var s = AddressableAssetSettingsDefaultObject.Settings;
            if (s == null) return false;
            return s.groups.Any(g => g != null && g.name == groupName);
        }

        private static void CleanAddressablesGroups(AddressableAssetSettings s)
        {
            if (s == null) return;
            var empties = s.groups.Where(g =>
                    g != null && g != s.DefaultGroup && !g.ReadOnly && (g.entries == null || g.entries.Count == 0))
                .ToList();
            foreach (var g in empties)
                s.RemoveGroup(g);
        }

        private static List<ContentPackValidator.Issue> ValidatePack(ContentPackDefinition pack, ContentValidationRules rules)
        {
            var all = new List<ContentPackValidator.Issue>();
            if (pack == null) return all;
            if (pack._items == null) return all;
            foreach (var go in pack._items)
            {
                if (!go) continue;
                var issues = ContentPackValidator.ValidateItem(go, rules);
                _packIssues[pack] = issues;
                all.AddRange(issues);
            }

            //if (String.IsNullOrEmpty(pack.summary))
            //{
            //    ContentPackValidator.Issue issue = new ContentPackValidator.Issue();
            //    issue.severity = ContentPackValidator.Severity.Error;
            //    issue.message = "No Pack Summary";
            //    all.Add(issue);
            //}

            return all;
        }

        private void OpenBuildOutputFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Open Folder", "Build Output Folder is empty.", "OK");
                return;
            }

            // Ensure it exists so Explorer/Finder opens cleanly
            path = path.Replace("\\", "/");
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (System.Exception ex)
                {
                    EditorUtility.DisplayDialog("Open Folder", $"Could not create folder:\n{path}\n\n{ex.Message}",
                        "OK");
                    return;
                }
            }

            // Cross-platform open
#if UNITY_EDITOR_WIN
            Process.Start(new ProcessStartInfo("explorer.exe", path.Replace("/", "\\")) { UseShellExecute = true });
#elif UNITY_EDITOR_OSX
    EditorUtility.RevealInFinder(path); // opens Finder at the folder
#else
    EditorUtility.RevealInFinder(path); // Linux/editor support
#endif
        }

// --- Thumbnail cache for icons to keep UI fast ---
        private readonly Dictionary<string, Texture2D> _iconThumbCache = new Dictionary<string, Texture2D>();

        private static string ComputeIconPathForPrefab(GameObject prefab, out string folder)
        {
            folder = null;
            if (!prefab) return null;
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath)) return null;

            // Mirror "…/Prefabs/Foo.prefab" → "…/Icons/Foo_Icon.png"
            var dir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/") ?? "Assets";
            folder = dir.Replace("/Prefabs", "/Icons");
            var fileNameNoExt = Path.GetFileNameWithoutExtension(prefabPath) + "_Icon";
            return Path.Combine(folder, fileNameNoExt + ".png").Replace("\\", "/");
        }

        private Texture2D GetIconTextureForPrefab(GameObject prefab)
        {
            if (prefab == null) return null;


            string folder;
            var iconPath = ComputeIconPathForPrefab(prefab, out folder);
            if (string.IsNullOrEmpty(iconPath)) return null;

            if (_iconThumbCache.TryGetValue(iconPath, out var cached) && cached != null)
                return cached;

            // Try the generated icon first
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

            // Fall back to Unity's preview if not generated yet
            if (tex == null)
                tex = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab) as Texture2D;

            _iconThumbCache[iconPath] = tex;
            return tex;
        }


        static GUIStyle _wrapLabel;

        private void DrawWrappedHelpBox(string text, float leftPadding = 6f, float rightPadding = 6f,
            float topPadding = 6f, float bottomPadding = 6f)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Container styled like the existing “Validation Rules” box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Calculate width inside the helpbox
            float fullWidth = EditorGUIUtility.currentViewWidth; // the window’s usable width
            float contentWidth = fullWidth - leftPadding - rightPadding - 20f; // a bit of slack for margins/scrollbars

            // Measure required height for the wrapped text
            var gc = new GUIContent(text);
            float height = _wrapLabel.CalcHeight(gc, contentWidth);

            // Reserve and draw
            var r = GUILayoutUtility.GetRect(contentWidth, height, _wrapLabel, GUILayout.ExpandWidth(true));
            r.x += leftPadding;
            r.width = contentWidth;
            r.y += topPadding;
            r.height = height;

            EditorGUI.LabelField(r, gc, _wrapLabel);

            GUILayout.Space(bottomPadding);
            EditorGUILayout.EndVertical();
        }

        private GameObject DrawItemWithIconField(ref GameObject itemRef, float iconSize = 40f)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // Reserve a rect for the icon
                var iconRect = GUILayoutUtility.GetRect(iconSize, iconSize, GUILayout.Width(iconSize),
                    GUILayout.Height(iconSize));

                // Figure out which asset we can ping (icon if present, else prefab)
                Texture2D tex = null;
                Object pingTarget = null;

                if (itemRef != null)
                {
                    string folder;
                    var iconPath = ComputeIconPathForPrefab(itemRef, out folder);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                        if (tex != null) pingTarget = tex;
                    }

                    if (tex == null)
                    {
                        // fallback preview for display only
                        tex = AssetPreview.GetAssetPreview(itemRef) ??
                              AssetPreview.GetMiniThumbnail(itemRef) as Texture2D;
                        pingTarget = itemRef; // fallback ping target = prefab
                    }
                }

                // Draw subtle background + thumbnail
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(iconRect,
                        EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.05f) : new Color(0, 0, 0, 0.06f));
                if (tex != null) GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit);

                // Make it feel clickable
                EditorGUIUtility.AddCursorRect(iconRect, MouseCursor.Link);

                // Click = Ping (left click). Alt+Click also reveals in Finder/Explorer.
                if (GUI.Button(iconRect, GUIContent.none, GUIStyle.none) && pingTarget != null)
                {
                    EditorGUIUtility.PingObject(pingTarget);
                    Selection.activeObject = pingTarget;

                    if (Event.current != null && (Event.current.alt ||
                                                  Event.current.control &&
                                                  Application.platform == RuntimePlatform.OSXEditor))
                    {
                        var path = AssetDatabase.GetAssetPath(pingTarget);
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            EditorUtility.RevealInFinder(path);
                    }
                }

                // The object field
                itemRef = (GameObject)EditorGUILayout.ObjectField(itemRef, typeof(GameObject), false);
            }

            return itemRef;
        }


        private void ClearIconThumbsForPack(ContentPackDefinition pack)
        {
            if (pack == null || pack._items == null) return;

            foreach (var go in pack._items)
            {
                if (!go) continue;
                string folder;
                var path = ComputeIconPathForPrefab(go, out folder);
                if (string.IsNullOrEmpty(path)) continue;
                _iconThumbCache.Remove(path);
            }
        }


        private void GenerateIconsForPack(ContentPackDefinition pack)
        {
            if (pack == null) return;

            var items = (pack._items ?? new List<GameObject>()).Where(x => x != null).ToList();
            if (items.Count == 0)
            {
                Debug.LogWarning($"[ContentPackBuilder] No items in '{pack.name}' to capture icons for.");
                return;
            }

            // 1) Capture icons (same utility used by build)
            try
            {
                Content_Icon_Capture.Editor.ContentIconCaptureUtility.CaptureIconsForPrefabs(
                    items,
                    renderSize: 2048,
                    outputSize: 2048,
                    imageType: Content_Icon_Capture.Editor.ContentIconCaptureUtility.ImageType.PNG
                );
                Debug.Log($"[ContentPackBuilder] Generated icons for pack '{pack.name}'.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ContentPackBuilder] Icon capture failed for '{pack?.name}': {ex.Message}");
                return;
            }


            // 2) Load the generated textures and record them on the pack
            bool changed = false;
            pack._icons = new List<Texture2D>();

            foreach (var prefab in items)
            {
                string folder;
                var iconPath = ComputeIconPathForPrefab(prefab, out folder);
                if (string.IsNullOrEmpty(iconPath)) continue;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                if (tex != null && !pack._icons.Contains(tex))
                {
                    pack._icons.Add(tex);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(pack);
                AssetDatabase.SaveAssets();
            }

            // 3) Push the icons into the "{PackName}_Icons" addressable group
            pack.SyncToAddressables();

            ClearIconThumbsForPack(pack);
            Repaint();

        }


        private static string BuildUnityPackageForPack(ContentPackDefinition pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));

            // Make sure latest edits (token/summary/screenshot) are on disk
            pack.StampMashBoxSdkVersion();
            AssetDatabase.SaveAssets();

            // 1) Seed roots with the pack definition itself
            var roots = new List<string>();
            var defPath = AssetDatabase.GetAssetPath(pack); // <-- the .asset file
            if (!string.IsNullOrEmpty(defPath)) roots.Add(defPath);

            // Items
            if (pack._items != null)
            {
                foreach (var obj in pack._items)
                {
                    if (!obj) continue;
                    var p = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(p)) roots.Add(p);
                }
            }

            // Main screenshot
            if (pack.mainScreenshot)
            {
                var p = AssetDatabase.GetAssetPath(pack.mainScreenshot);
                if (!string.IsNullOrEmpty(p)) roots.Add(p);
            }

            AddBakeryLightmapExportRoots(pack, roots);

            // Icons (if you have a list)
            //TryAddTextureListField(pack, "icons", roots);
            //TryAddTextureListField(pack, "_icons", roots);
            //TryAddTextureListField(pack, "iconTextures", roots);
            //TryAddTextureListField(pack, "additionalImages", roots);

            // 2) Expand with dependencies (keep content-only)
            var toExport = AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true)
                .Where(IsExportableUnityPackagePath)
                .Distinct()
                .ToList();

            if (toExport.Count == 0)
                throw new Exception("Nothing to export: no assets/screenshot/icons/definition were found.");

            // 3) Export
            var outPath = GetTempUnityPackagePath(pack.name);
            EditorUtility.DisplayProgressBar("Exporting Package",
                $"Creating {Path.GetFileName(outPath)} ({toExport.Count} assets)…", 0.5f);

            AssetDatabase.ExportPackage(toExport.ToArray(), outPath, ExportPackageOptions.Default);
            AssetDatabase.Refresh();

            if (!File.Exists(outPath))
                throw new FileNotFoundException("Export failed (file not created).", outPath);

            EditorUtility.ClearProgressBar();
            return outPath;
        }

        private void ExportDebugUnityPackageForPack(ContentPackDefinition pack)
        {
            if (pack == null)
            {
                EditorUtility.DisplayDialog("No Pack Selected", "Select a content pack before exporting a debug package.", "OK");
                return;
            }

            try
            {
                var tempPackagePath = BuildUnityPackageForPack(pack);
                var defaultName = Path.GetFileName(tempPackagePath);
                var savePath = EditorUtility.SaveFilePanel(
                    "Save Debug Content Package",
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Path.GetFileNameWithoutExtension(defaultName),
                    "unitypackage");

                if (string.IsNullOrWhiteSpace(savePath))
                    return;

                File.Copy(tempPackagePath, savePath, true);
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ContentPackBuilder] Debug package export failed: {ex}");
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }


// Helper: add Texture2D list field by name if it exists on the pack (reflection)
        private static void TryAddTextureListField(ContentPackDefinition pack, string fieldName, List<string> roots)
        {
            var fi = typeof(ContentPackDefinition).GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fi == null) return;

            var val = fi.GetValue(pack);
            if (val is IEnumerable<Texture2D> texList)
            {
                foreach (var t in texList)
                {
                    if (!t) continue;
                    var p = AssetDatabase.GetAssetPath(t);
                    if (!string.IsNullOrEmpty(p)) roots.Add(p);
                }
            }
        }

// Temp file location (Project/Temp/RemoteCook_<pack>.unitypackage)
        private static string GetTempUnityPackagePath(string packName)
        {
            var projRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var tempDir = Path.Combine(projRoot, "Temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var safe = SanitizeFileName(string.IsNullOrEmpty(packName) ? "ContentPack" : packName);
            return Path.Combine(tempDir, $"RemoteCook_{safe}.unitypackage");
        }

        private static string SanitizeFileName(string raw)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string(raw.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        }



        private void CleanAddressableData()
        {
            const string GROUPS_PATH = "Assets/AddressableAssetsData/AssetGroups";
            const string SCHEMAS_PATH = "Assets/AddressableAssetsData/AssetGroups/Schemas";

            // Keep these by default
            HashSet<string> protectedBaseNames = new HashSet<string>
            {
                "Built In Data",
                "Default Local Group"
            };

            // Add content pack names to the protected list
            foreach (var pack in _packs)
            {
                if (pack == null) continue;
                protectedBaseNames.Add(pack.name);
            }

            void CleanFolder(string folderPath)
            {
                if (!AssetDatabase.IsValidFolder(folderPath))
                    return;

                string[] files =
                    System.IO.Directory.GetFiles(folderPath, "*.*", System.IO.SearchOption.TopDirectoryOnly);

                foreach (string filePath in files)
                {
                    if (filePath.EndsWith(".meta")) continue;

                    string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    bool keep = protectedBaseNames.Any(baseName =>
                        fileName.StartsWith(baseName, System.StringComparison.OrdinalIgnoreCase));

                    if (!keep)
                    {
                        Debug.Log($"[Cleanup] Removing stray Addressables asset: {fileName}");
                        AssetDatabase.DeleteAsset(filePath);
                    }
                }
            }

            // Run cleanup on both folders
            CleanFolder(GROUPS_PATH);
            CleanFolder(SCHEMAS_PATH);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

// Build a unitypackage containing: items + dependencies (+ screenshot + icons)
        private static List<string> CollectExportPaths(ContentPackDefinition pack)
        {
            var roots = new List<string>();
            var packPath = AssetDatabase.GetAssetPath(pack);
            if (!string.IsNullOrEmpty(packPath))
                roots.Add(packPath);

            if (pack?._items != null)
            {
                foreach (var go in pack._items)
                {
                    if (!go) continue;
                    var p = AssetDatabase.GetAssetPath(go);
                    if (!string.IsNullOrEmpty(p)) roots.Add(p);
                }
            }

            // include main screenshot if set
            if (pack.mainScreenshot)
            {
                var p = AssetDatabase.GetAssetPath(pack.mainScreenshot);
                if (!string.IsNullOrEmpty(p)) roots.Add(p);
            }

            // include any generated icons recorded on the pack
            if (pack._icons != null)
            {
                foreach (var t in pack._icons)
                {
                    if (!t) continue;
                    var p = AssetDatabase.GetAssetPath(t);
                    if (!string.IsNullOrEmpty(p)) roots.Add(p);
                }
            }

            AddBakeryLightmapExportRoots(pack, roots);

            // Dependencies
            var deps = AssetDatabase.GetDependencies(roots.ToArray(), true)
                .Where(IsExportableUnityPackagePath)
                .Distinct()
                .ToList();

            return deps;
        }

        private static bool IsExportableUnityPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/Editor/");
        }

        private const string BakeryLightmapsRoot = "Assets/BakeryLightmaps";

        private static void AddBakeryLightmapExportRoots(ContentPackDefinition pack, List<string> roots)
        {
            if (pack == null || roots == null)
                return;

            if (!TryCollectBakeryCandidateNames(pack, out var candidateNames))
                return;

            if (!AssetDatabase.IsValidFolder(BakeryLightmapsRoot))
            {
                Debug.LogWarning(
                    $"[ContentPackBuilder] Bakery lightmap groups were found in '{pack.name}', but {BakeryLightmapsRoot} does not exist. " +
                    "The exported package may be missing baked lighting.");
                return;
            }

            var folders = AssetDatabase.GetSubFolders(BakeryLightmapsRoot);
            if (folders == null || folders.Length == 0)
            {
                Debug.LogWarning(
                    $"[ContentPackBuilder] Bakery lightmap groups were found in '{pack.name}', but {BakeryLightmapsRoot} has no lightmap folders. " +
                    "The exported package may be missing baked lighting.");
                return;
            }

            var matchedFolders = folders
                .Where(folder => candidateNames.Any(name => NamesLikelyReferToSameBakeryFolder(Path.GetFileName(folder), name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matchedFolders.Count == 0)
            {
                Debug.LogWarning(
                    $"[ContentPackBuilder] Bakery lightmap groups were found in '{pack.name}', but no matching folder name was found under {BakeryLightmapsRoot}. " +
                    "No Bakery lightmap folders were added automatically because the match is ambiguous.");
                return;
            }

            int added = 0;
            foreach (var folder in matchedFolders)
            {
                foreach (var assetPath in EnumerateAssetsInFolder(folder))
                {
                    if (!IsExportableUnityPackagePath(assetPath) || roots.Contains(assetPath))
                        continue;

                    roots.Add(assetPath);
                    added++;
                }
            }

            if (added > 0)
            {
                Debug.Log(
                    $"[ContentPackBuilder] Added {added} Bakery lightmap asset(s) from {matchedFolders.Count} folder(s) to '{pack.name}' export.");
            }
        }

        private static void AddBakeryLightmapValidationIssue(ContentPackDefinition pack, List<ContentPackValidator.Issue> issues)
        {
            if (pack == null || issues == null)
                return;

            if (!TryCollectBakeryCandidateNames(pack, out var candidateNames))
                return;

            string message = null;

            if (!AssetDatabase.IsValidFolder(BakeryLightmapsRoot))
            {
                message =
                    $"Pack '{pack.name}' contains Bakery lightmap components, but {BakeryLightmapsRoot} was not found. " +
                    "Bake the map or include the generated Bakery lightmap folder before publishing.";
            }
            else
            {
                var folders = AssetDatabase.GetSubFolders(BakeryLightmapsRoot);
                if (folders == null || folders.Length == 0)
                {
                    message =
                        $"Pack '{pack.name}' contains Bakery lightmap components, but {BakeryLightmapsRoot} has no lightmap folders. " +
                        "Bake the map before publishing.";
                }
                else
                {
                    var matchedFolders = folders
                        .Where(folder => candidateNames.Any(name => NamesLikelyReferToSameBakeryFolder(Path.GetFileName(folder), name)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (matchedFolders.Count == 0)
                    {
                        message =
                            $"Pack '{pack.name}' contains Bakery lightmap components, but no matching folder was found under {BakeryLightmapsRoot}. " +
                            "Rename or move the active Bakery output so it matches the map/pack, then validate again.";
                    }
                    else if (!matchedFolders.Any(FolderContainsExportableAssets))
                    {
                        message =
                            $"Pack '{pack.name}' contains Bakery lightmap components, but the matched Bakery lightmap folder has no exportable assets. " +
                            "Rebake the map before publishing.";
                    }
                }
            }

            if (string.IsNullOrEmpty(message))
                return;

            issues.Add(new ContentPackValidator.Issue
            {
                severity = ContentPackValidator.Severity.Warning,
                message = message,
                context = pack
            });
        }

        private static bool TryCollectBakeryCandidateNames(ContentPackDefinition pack, out HashSet<string> candidateNames)
        {
            candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pack == null)
                return false;

            bool hasBakeryComponents = false;
            AddCandidateName(candidateNames, pack.name);

            if (pack._items == null)
                return false;

            foreach (var item in pack._items)
            {
                if (!item) continue;

                AddCandidateName(candidateNames, item.name);

                var itemPath = AssetDatabase.GetAssetPath(item);
                AddCandidateName(candidateNames, Path.GetFileNameWithoutExtension(itemPath));
                AddCandidateName(candidateNames, Path.GetFileName(Path.GetDirectoryName(itemPath)?.Replace("\\", "/") ?? string.Empty));

                hasBakeryComponents |= CollectBakeryNamesFromHierarchy(item, candidateNames);
            }

            return hasBakeryComponents;
        }

        private static bool CollectBakeryNamesFromHierarchy(GameObject root, HashSet<string> candidateNames)
        {
            if (!root)
                return false;

            bool foundBakeryComponent = false;
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (!component)
                    continue;

                var type = component.GetType();
                if (!IsBakeryComponentType(type))
                    continue;

                foundBakeryComponent = true;
                AddCandidateName(candidateNames, component.gameObject.name);

                try
                {
                    var serializedObject = new SerializedObject(component);
                    var iterator = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.String)
                            continue;

                        var propertyName = iterator.name ?? string.Empty;
                        if (propertyName.IndexOf("scene", StringComparison.OrdinalIgnoreCase) < 0 &&
                            propertyName.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0 &&
                            propertyName.IndexOf("folder", StringComparison.OrdinalIgnoreCase) < 0 &&
                            propertyName.IndexOf("path", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        AddCandidateName(candidateNames, iterator.stringValue);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ContentPackBuilder] Could not inspect Bakery component '{type.Name}' for export names: {ex.Message}");
                }
            }

            return foundBakeryComponent;
        }

        private static bool IsBakeryComponentType(Type type)
        {
            if (type == null)
                return false;

            var name = type.FullName ?? type.Name;
            return name.IndexOf("Bakery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ftLightmapsStorage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ftLightmap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> EnumerateAssetsInFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                yield break;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                    yield return assetPath;
            }
        }

        private static bool FolderContainsExportableAssets(string folder)
        {
            return EnumerateAssetsInFolder(folder).Any(IsExportableUnityPackagePath);
        }

        private static void AddCandidateName(HashSet<string> names, string value)
        {
            if (names == null || string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (trimmed.Length > 0)
                names.Add(trimmed);
        }

        private static bool NamesLikelyReferToSameBakeryFolder(string folderName, string candidateName)
        {
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(candidateName))
                return false;

            var normalizedFolder = NormalizeBakeryName(folderName);
            var normalizedCandidate = NormalizeBakeryName(candidateName);

            if (string.IsNullOrEmpty(normalizedFolder) || string.IsNullOrEmpty(normalizedCandidate))
                return false;

            if (normalizedFolder == normalizedCandidate ||
                normalizedFolder.Contains(normalizedCandidate) ||
                normalizedCandidate.Contains(normalizedFolder))
            {
                return true;
            }

            var folderTokens = TokenizeBakeryName(folderName);
            var candidateTokens = TokenizeBakeryName(candidateName);
            return folderTokens.Overlaps(candidateTokens);
        }

        private static string NormalizeBakeryName(string value)
        {
            var chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray();

            return new string(chars);
        }

        private static HashSet<string> TokenizeBakeryName(string value)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
                return tokens;

            var split = value.Split(new[] { ' ', '_', '-', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawToken in split)
            {
                var token = new string(rawToken.Where(char.IsLetterOrDigit).ToArray());
                if (token.Length >= 4)
                    tokens.Add(token);
            }

            return tokens;
        }


        private static string ProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string TempPackagePathFor(string packName)
        {
            var tempDir = Path.Combine(ProjectRoot(), "Temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            return Path.Combine(tempDir, $"RemoteCook_{SanitizeFile(packName)}.unitypackage");
        }

        private static void EnsurePackageSizeWithinLimit(string packagePath, long maxBytes, string packageLabel)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                throw new FileNotFoundException("Could not find the exported unitypackage to validate size.", packagePath);

            var fileInfo = new FileInfo(packagePath);
            if (fileInfo.Length <= maxBytes)
                return;

            throw new Exception(
                $"The {packageLabel} .unitypackage is too large to publish.\n\n" +
                $"Current size: {FormatBytes(fileInfo.Length)}\n" +
                $"Maximum size: {FormatBytes(maxBytes)}");
        }

        private static string FormatBytes(long bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;

            if (bytes >= gb)
                return $"{bytes / gb:0.##} GB";
            if (bytes >= mb)
                return $"{bytes / mb:0.##} MB";
            if (bytes >= kb)
                return $"{bytes / kb:0.##} KB";

            return $"{bytes} B";
        }

        private static string SanitizeFile(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "ContentPack";
            var bad = Path.GetInvalidFileNameChars();
            return new string(raw.Select(c => bad.Contains(c) ? '_' : c).ToArray());
        }

        private static string ExportUnityPackage(ContentPackDefinition pack)
        {
            pack.StampMashBoxSdkVersion();
            AssetDatabase.SaveAssets();

            var export = CollectExportPaths(pack);
            if (export.Count == 0) throw new Exception("No assets to export.");
            var outPath = TempPackagePathFor(pack.name);

            EditorUtility.DisplayProgressBar("Exporting Package",
                $"Creating {Path.GetFileName(outPath)} ({export.Count} items)…", 0.5f);

            AssetDatabase.ExportPackage(export.ToArray(), outPath, ExportPackageOptions.Default);
            AssetDatabase.Refresh();

            if (!File.Exists(outPath)) throw new FileNotFoundException("Export failed", outPath);
            return outPath;
        }

        [Serializable]
        private class UploadResponse
        {
            public string jobId;
            public string uploadUrl;
        }

        private static async Task UploadUnityPackageAsync(string filePath)
        {
            using (var http = new HttpClient())
            {
                // 1) Ask proxy for an upload URL (pass a name so your function can store nicely)
                var name = Path.GetFileName(filePath);
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("fileName", name),
                });

                var req = await http.PostAsync(UploaderEndpoint, form);
                var body = await req.Content.ReadAsStringAsync();
                if (!req.IsSuccessStatusCode)
                    throw new Exception($"Proxy request failed: {(int)req.StatusCode} {req.ReasonPhrase}");

                var data = JsonUtility.FromJson<UploadResponse>(body);
                if (data == null || string.IsNullOrEmpty(data.uploadUrl))
                    throw new Exception("Invalid proxy response (missing uploadUrl).");

                // 2) Upload the bytes to the SAS URL
                var bytes = File.ReadAllBytes(filePath);
                using var content = new ByteArrayContent(bytes);
                content.Headers.Add("x-ms-blob-type", "BlockBlob");
                var putRes = await http.PutAsync(data.uploadUrl, content);

                if (!putRes.IsSuccessStatusCode)
                    throw new Exception($"Upload failed: {(int)putRes.StatusCode} {putRes.ReasonPhrase}");
            }
        }
        

        

#if MashBoxDev
        [MenuItem("MashBox/Dev/Mod.io/PrintCurrentUserToken")]
        public static void PrintCurrentUserToken()
        {
            Debug.Log(MashBoxSDK.ContentTools.Editor.ModIoAuth.CurrentToken);
        }
#endif
        
        private static bool IsHumanClothing(GameObject prefab)
        {
            if (prefab == null) return false;
            return prefab.name.StartsWith("Human_", System.StringComparison.OrdinalIgnoreCase);
        }

        private static ClothingClipSettings GetOrAddClothingClipSettings(GameObject prefab)
        {
            var settings = prefab.GetComponent<ClothingClipSettings>();
            if (settings != null)
                return settings;

            // Add component safely
            Undo.RecordObject(prefab, "Add Clothing Clip Settings");
            settings = prefab.AddComponent<ClothingClipSettings>();

            EditorUtility.SetDirty(prefab);
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);

            return settings;
        }

        private void DrawClothingControlsIfNeeded(GameObject prefab)
        {
            if (!IsHumanClothing(prefab))
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Clothing Settings", EditorStyles.boldLabel);

            var settings = GetOrAddClothingClipSettings(prefab);

            EditorGUI.BeginChangeCheck();

            // --- Clipping Type ---
            var newClipType = (ClothingClipType)EditorGUILayout.EnumPopup(
                "Clipping Type",
                settings.clipType
            );

            // --- Disable Features (Flags Enum) ---
            var newDisableFeatures = (CharacterFeatureFlags)EditorGUILayout.EnumFlagsField(
                "Disable Features",
                settings.disableFeatures
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Modify Clothing Settings");

                settings.clipType = newClipType;
                settings.disableFeatures = newDisableFeatures;

                EditorUtility.SetDirty(settings);
                PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            }

            EditorGUILayout.EndVertical();
        }
    }
    class CreatePackPopup : EditorWindow
    {
        private string _packName = "";
        private Action<string> _onCreate;

        public static void Show(Action<string> onCreate)
        {
            var window = CreateInstance<CreatePackPopup>();
            window.titleContent = new GUIContent("Create Pack");
            window._onCreate = onCreate;
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 300, 90);
            window.ShowUtility(); // modal-like popup
        }

        private void OnGUI()
        {
            GUILayout.Label("Pack Name", EditorStyles.boldLabel);
            GUI.SetNextControlName("PackNameField");
            _packName = EditorGUILayout.TextField(_packName);

            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                GUI.enabled = !string.IsNullOrWhiteSpace(_packName);

                if (GUILayout.Button("Create"))
                {
                    _onCreate?.Invoke(_packName);
                    Close();
                }

                GUI.enabled = true;
            }

            // Auto-focus text field
            EditorGUI.FocusTextInControl("PackNameField");
        }
    }
}
