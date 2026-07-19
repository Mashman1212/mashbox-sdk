#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MashBoxSDK.ContentTools.Editor;
using MashBoxSDK.Exporting;
using MashBoxSDK.Maps;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.SDKMain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public class MashBoxMapToolsWindow : EditorWindow
    {
        private enum ToolTab { ArtTools, Gameplay, Audio, Performance, Testing, MapExporter }
        private enum AuthoringToolTab { MGBrush, SplineLoft, MeshSculpt, UVSpline }
        private const string PREF_KEY_MAP_TOOL_TAB = "MashBoxSDK.SelectedMapToolTab";
        private const string PREF_KEY_AUTHORING_TOOL_TAB = "MashBoxSDK.SelectedMapAuthoringToolTab";
        private const string PREF_KEY_MAP_TOOL_TAB_ORDER = "MashBoxSDK.SelectedMapToolTab.Order";
        private ToolTab currentToolTab = ToolTab.ArtTools;
        private int authoringToolTab;
        private bool changingAuthoringSelection;
        private bool uvSplineSelectionQueued;
        private UVSpline queuedUvSpline;
        private const string SpawnLocationPrefabPath = "Packages/com.mg.mashbox.sdk/Runtime/Maps/[MashBox] Spawn Location.prefab";
        private const string FreeCamPrefabPath = "Packages/com.mg.mashbox.sdk/Runtime/Maps/Freecam/Free Cam.prefab";
        private const string ChallengesRootName = "Challenges";
        private static readonly Color RaceGateFillColor = new Color(1f, 0.45f, 0.15f, 0.12f);
        private static readonly Color RaceGateWireColor = new Color(1f, 0.45f, 0.15f, 0.95f);
        private static readonly Color RaceGateLabelColor = new Color(1f, 0.9f, 0.72f, 1f);

        private MapContentDatabase db;
        private ReorderableList list;
        private string outputPath = "AssetBundles/";

        private enum CompressionMode
        {
            LZMACompression,
            ChunkBasedCompression,
            None
        }

        private CompressionMode compressionMode = CompressionMode.ChunkBasedCompression;
        private const string MapFolderSuffix = "_map";
        private const long MaxMapPublishPackageBytes = 5L * 1024L * 1024L * 1024L;
        private const long SequentialContainerUploadThresholdBytes = 1024L * 1024L * 1024L;
        private static readonly PublishPlatformOption[] PublishPlatformOptions =
        {
            new PublishPlatformOption("inbox-windows", "Windows"),
            new PublishPlatformOption("inbox-xbox", "Xbox"),
            new PublishPlatformOption("inbox-ps5", "PS5")
        };
        private CancellationTokenSource activeMapPublishCts;
        private bool isMapPublishInProgress;
        private float activeMapPublishProgress;
        private string activeMapPublishStatus = string.Empty;
        private string activeMapPublishRegion = string.Empty;
        private Vector2 mapToolsScrollPosition;
        private string validatedPackInstanceId;
        private List<MapValidationIssue> lastValidationIssues;
        private List<MapValidationIssue> gameplayValidationIssues = new();
        private double nextGameplayValidationRefreshTime;
        [SerializeField] private MapPerformanceScannerPanel performanceScannerPanel = new();
        private double nextSceneToolCacheRefreshTime;
        private bool sceneToolCacheDirty = true;
        private Scene cachedScene;
        private MBSpawnLocation cachedSpawnLocation;
        private MBMapBoundary cachedMapBoundary;
        private MBMapTaskList cachedMapTaskList;
        private GameObject cachedPhotoSpotGroupRoot;
        private GameObject cachedRaceGroupRoot;
        private GameObject cachedSecretGapGroupRoot;
        private GameObject cachedSideHitGroupRoot;
        private GameObject cachedExpertLineGroupRoot;
        private GameObject cachedCollectibleGroupRoot;
        private Transform cachedLettersRoot;
        private readonly List<GameObject> cachedFlyCameraObjects = new();
        private readonly List<MBPhotoSpot> cachedPhotoSpots = new();
        private readonly List<MBRace> cachedRaces = new();
        private readonly List<MBSecretGap> cachedSecretGaps = new();
        private readonly List<MBSideHit> cachedSideHits = new();
        private readonly List<MBExpertLine> cachedExpertLines = new();
        private readonly List<MBCollectible> cachedCollectibles = new();
        private readonly List<MBCollectLetter> cachedLetters = new();
        private readonly Dictionary<string, bool> challengeItemFoldouts = new();
        private readonly Dictionary<string, bool> challengeSectionFoldouts = new();
        private int cachedMissingChallengeGroupScriptCount;
        private bool initialized;
        [NonSerialized] private MGBrushWindow authoringBrushTool;
        [NonSerialized] private MultiSplineLoftWindow authoringLoftTool;
        [NonSerialized] private MeshSculptWindow authoringSculptTool;

        private const string UGC_REQUEST_PATH =
            "https://ugc-remote-cook-func-node-fecqe4asaabhcddn.centralus-01.azurewebsites.net/api/request-upload";

        private static string UploaderEndpoint => UGC_REQUEST_PATH;
        private static readonly HttpClient SharedHttp = CreateSharedHttp();

        //[MenuItem("MashBox/Map Exporter")]
        public static void Open()
        {
            GetWindow<MashBoxMapToolsWindow>("MashBox Map Tools");
        }

        private static string GetObjectStableId(UnityEngine.Object obj)
        {
            if (obj == null)
                return string.Empty;

#if UNITY_6000_0_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            return obj.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#endif
        }

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
            initialized = false;
            authoringToolTab = EditorPrefs.GetInt(PREF_KEY_AUTHORING_TOOL_TAB, 0);
            UVSplineEditor.SceneEditingEnabled = (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline;
            Selection.selectionChanged -= OnAuthoringSelectionChanged;
            Selection.selectionChanged += OnAuthoringSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkSceneToolCacheDirty;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
            Selection.selectionChanged -= OnAuthoringSelectionChanged;
            EditorApplication.delayCall -= ProcessQueuedUvSplineSelection;
            queuedUvSpline = null;
            uvSplineSelectionQueued = false;
            UVSplineEditor.SceneEditingEnabled = true;
            DestroyAuthoringToolInstances();
            initialized = false;
        }

        private static ToolTab GetSavedMapToolTab()
        {
            int savedTab = EditorPrefs.GetInt(PREF_KEY_MAP_TOOL_TAB, (int)ToolTab.ArtTools);
            string savedOrder = EditorPrefs.GetString(PREF_KEY_MAP_TOOL_TAB_ORDER, string.Empty);

            if (!string.Equals(savedOrder, "ArtToolsFirst", StringComparison.Ordinal))
            {
                ToolTab migratedTab = savedTab switch
                {
                    0 => ToolTab.Gameplay,
                    1 => ToolTab.Audio,
                    2 => ToolTab.Performance,
                    3 => ToolTab.Testing,
                    4 => ToolTab.MapExporter,
                    5 => ToolTab.ArtTools,
                    _ => ToolTab.ArtTools
                };

                EditorPrefs.SetInt(PREF_KEY_MAP_TOOL_TAB, (int)migratedTab);
                EditorPrefs.SetString(PREF_KEY_MAP_TOOL_TAB_ORDER, "ArtToolsFirst");
                return migratedTab;
            }

            return Enum.IsDefined(typeof(ToolTab), savedTab) ? (ToolTab)savedTab : ToolTab.ArtTools;
        }

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            currentToolTab = GetSavedMapToolTab();
            authoringToolTab = EditorPrefs.GetInt(PREF_KEY_AUTHORING_TOOL_TAB, 0);
            UVSplineEditor.SceneEditingEnabled = currentToolTab == ToolTab.ArtTools
                && (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline;
            db = MapContentDatabase.GetOrCreate();

            EditorApplication.hierarchyChanged -= MarkSceneToolCacheDirty;
            EditorApplication.hierarchyChanged += MarkSceneToolCacheDirty;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;
            Selection.selectionChanged -= OnAuthoringSelectionChanged;
            Selection.selectionChanged += OnAuthoringSelectionChanged;

            MarkSceneToolCacheDirty();
            RefreshGameplayValidationIssues(force: true);
            EnsureAuthoringToolInstances();

            list = new ReorderableList(db.Packs, typeof(MapContentPackDefinition), true, true, true, true);
            list.drawHeaderCallback = (r) => EditorGUI.LabelField(r, "Maps");
            list.drawElementCallback = DrawElement;
            list.elementHeight = EditorGUIUtility.singleLineHeight * 2f + 8f;
            list.onAddCallback = (l) => CreateEmptyPackAsset();
            list.onRemoveCallback = (l) => RemovePackAt(l.index);

            initialized = true;
            OnAuthoringSelectionChanged();
        }

        private void OnAuthoringSelectionChanged()
        {
            if (changingAuthoringSelection || (AuthoringToolTab)authoringToolTab != AuthoringToolTab.UVSpline)
                return;

            GameObject selected = Selection.activeGameObject;
            UVSpline uvSpline = FindUvSpline(selected, (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline);
            if (uvSpline == null)
                return;

            currentToolTab = ToolTab.ArtTools;
            authoringToolTab = (int)AuthoringToolTab.UVSpline;
            EditorPrefs.SetInt(PREF_KEY_MAP_TOOL_TAB, (int)currentToolTab);
            EditorPrefs.SetString(PREF_KEY_MAP_TOOL_TAB_ORDER, "ArtToolsFirst");
            EditorPrefs.SetInt(PREF_KEY_AUTHORING_TOOL_TAB, authoringToolTab);
            QueueUvSplineSelection(uvSpline);
            UpdateAuthoringSceneToolState();
            Repaint();
        }

        private static UVSpline FindUvSpline(GameObject selected, bool includeChildren)
        {
            if (selected == null)
                return null;

            UVSpline uvSpline = selected.GetComponent<UVSpline>() ?? selected.GetComponentInParent<UVSpline>();
            if (uvSpline != null || !includeChildren)
                return uvSpline;

            uvSpline = selected.GetComponentInChildren<UVSpline>(true);
            if (uvSpline != null)
                return uvSpline;

            MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>()
                ?? selected.GetComponentInParent<MultiSplineLoft>()
                ?? FindSiblingLoftForSpline(selected);
            if (loft == null)
                return null;

            return loft.GeneratedUvSpline != null
                ? loft.GeneratedUvSpline
                : loft.GetComponentInChildren<UVSpline>(true);
        }

        private static MultiSplineLoft FindSiblingLoftForSpline(GameObject selected)
        {
            UnityEngine.Splines.SplineContainer selectedSpline = selected != null ? selected.GetComponent<UnityEngine.Splines.SplineContainer>() : null;
            if (selectedSpline == null)
                return null;

            for (Transform ancestor = selected.transform.parent; ancestor != null; ancestor = ancestor.parent)
            {
                MultiSplineLoft[] lofts = ancestor.GetComponentsInChildren<MultiSplineLoft>(true);
                foreach (MultiSplineLoft loft in lofts)
                {
                    if (loft == null) continue;
                    foreach (MultiSplineLoft.SplineSource source in loft.Sources)
                    {
                        if (source?.container == selectedSpline)
                            return loft;
                    }
                }
            }
            return null;
        }

        private void SelectUvSpline(UVSpline uvSpline)
        {
            if (uvSpline == null)
                return;
            if (Selection.activeGameObject != uvSpline.gameObject)
            {
                changingAuthoringSelection = true;
                Selection.activeGameObject = uvSpline.gameObject;
                changingAuthoringSelection = false;
            }
            InternalEditorUtility.RepaintAllViews();
            SceneView.RepaintAll();
        }

        private void QueueUvSplineSelection(UVSpline uvSpline)
        {
            if (uvSpline == null)
                return;
            queuedUvSpline = uvSpline;
            if (uvSplineSelectionQueued)
                return;
            uvSplineSelectionQueued = true;
            EditorApplication.delayCall -= ProcessQueuedUvSplineSelection;
            EditorApplication.delayCall += ProcessQueuedUvSplineSelection;
        }

        private void ProcessQueuedUvSplineSelection()
        {
            EditorApplication.delayCall -= ProcessQueuedUvSplineSelection;
            uvSplineSelectionQueued = false;
            UVSpline uvSpline = queuedUvSpline;
            queuedUvSpline = null;
            if (this == null || uvSpline == null || (AuthoringToolTab)authoringToolTab != AuthoringToolTab.UVSpline)
                return;
            SelectUvSpline(uvSpline);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void SelectUvSplineForCurrentSelection()
        {
            QueueUvSplineSelection(FindUvSpline(Selection.activeGameObject, true));
        }

        private void DeselectUvSplineForOtherTool()
        {
            UVSpline uvSpline = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<UVSpline>()
                : null;
            if (uvSpline == null)
                return;

            GameObject target = uvSpline.Target != null
                ? uvSpline.Target.gameObject
                : uvSpline.transform.parent != null ? uvSpline.transform.parent.gameObject : null;
            if (target == null)
                return;

            changingAuthoringSelection = true;
            Selection.activeGameObject = target;
            changingAuthoringSelection = false;
        }

        private void OnActiveSceneChangedInEditMode(Scene previousScene, Scene newScene)
        {
            MarkSceneToolCacheDirty();
        }

        private void MarkSceneToolCacheDirty()
        {
            sceneToolCacheDirty = true;
        }

        private void EnsureSceneToolCache(bool force = false)
        {
            var activeScene = SceneManager.GetActiveScene();
            var now = EditorApplication.timeSinceStartup;
            if (!force
                && !sceneToolCacheDirty
                && cachedScene == activeScene
                && now < nextSceneToolCacheRefreshTime)
                return;

            cachedScene = activeScene;
            sceneToolCacheDirty = false;
            nextSceneToolCacheRefreshTime = now + 5.0d;

            cachedFlyCameraObjects.Clear();
            cachedPhotoSpots.Clear();
            cachedRaces.Clear();
            cachedSecretGaps.Clear();
            cachedSideHits.Clear();
            cachedExpertLines.Clear();
            cachedCollectibles.Clear();
            cachedLetters.Clear();

            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                cachedSpawnLocation = null;
                cachedMapBoundary = null;
                cachedMapTaskList = null;
                cachedPhotoSpotGroupRoot = null;
                cachedRaceGroupRoot = null;
                cachedSecretGapGroupRoot = null;
                cachedSideHitGroupRoot = null;
                cachedExpertLineGroupRoot = null;
                cachedCollectibleGroupRoot = null;
                cachedLettersRoot = null;
                cachedMissingChallengeGroupScriptCount = 0;
                return;
            }

            cachedSpawnLocation = Resources.FindObjectsOfTypeAll<MBSpawnLocation>()
                .FirstOrDefault(spawn => spawn != null && spawn.gameObject.scene == activeScene);
            cachedMapBoundary = Resources.FindObjectsOfTypeAll<MBMapBoundary>()
                .FirstOrDefault(boundary => boundary != null && boundary.gameObject.scene == activeScene);
            cachedMapTaskList = MBMapTaskList.FindInScene(activeScene);
            cachedPhotoSpotGroupRoot = FindChallengeTypeRootInScene("Photo Spots", activeScene);
            cachedRaceGroupRoot = FindChallengeTypeRootInScene("Races", activeScene);
            cachedSecretGapGroupRoot = FindChallengeTypeRootInScene("Secret Gap", activeScene);
            cachedSideHitGroupRoot = FindChallengeTypeRootInScene("Side Hit", activeScene)
                                     ?? FindChallengeTypeRootInScene("Side Hits", activeScene);
            cachedExpertLineGroupRoot = FindChallengeTypeRootInScene("Expert Line", activeScene)
                                        ?? FindChallengeTypeRootInScene("Expert Lines", activeScene);
            cachedCollectibleGroupRoot = FindChallengeTypeRootInScene("Collectible", activeScene);

            cachedFlyCameraObjects.AddRange(Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject => gameObject != null && gameObject.scene == activeScene && gameObject.GetComponent<Freecam>() != null)
                .OrderBy(gameObject => gameObject.name));
            cachedPhotoSpots.AddRange(Resources.FindObjectsOfTypeAll<MBPhotoSpot>()
                .Where(photoSpot => photoSpot != null && photoSpot.gameObject.scene == activeScene)
                .OrderBy(photoSpot => photoSpot.transform.GetSiblingIndex()));
            cachedRaces.AddRange(Resources.FindObjectsOfTypeAll<MBRace>()
                .Where(race => race != null && race.gameObject.scene == activeScene)
                .OrderBy(race => race.transform.GetSiblingIndex()));
            cachedSecretGaps.AddRange(Resources.FindObjectsOfTypeAll<MBSecretGap>()
                .Where(gap => gap != null && gap.gameObject.scene == activeScene)
                .OrderBy(gap => gap.transform.GetSiblingIndex()));
            cachedSideHits.AddRange(Resources.FindObjectsOfTypeAll<MBSideHit>()
                .Where(sideHit => sideHit != null && sideHit.gameObject.scene == activeScene)
                .OrderBy(sideHit => sideHit.transform.GetSiblingIndex()));
            cachedExpertLines.AddRange(Resources.FindObjectsOfTypeAll<MBExpertLine>()
                .Where(line => line != null && line.gameObject.scene == activeScene)
                .OrderBy(line => line.transform.GetSiblingIndex()));
            cachedCollectibles.AddRange(Resources.FindObjectsOfTypeAll<MBCollectible>()
                .Where(collectible => collectible != null && collectible.gameObject.scene == activeScene)
                .OrderBy(collectible => collectible.transform.GetSiblingIndex()));
            cachedLetters.AddRange(Resources.FindObjectsOfTypeAll<MBCollectLetter>()
                .Where(letter => letter != null && letter.gameObject.scene == activeScene)
                .OrderBy(letter => letter.transform.parent != null ? letter.transform.parent.GetSiblingIndex() : -1)
                .ThenBy(letter => letter.transform.GetSiblingIndex()));

            cachedLettersRoot = cachedLetters
                .Select(letter => letter != null ? letter.transform.parent : null)
                .FirstOrDefault(parent => parent != null);
            cachedMissingChallengeGroupScriptCount = CountMissingChallengeGroupScripts(activeScene);
        }

        public void Draw()
        {
            EnsureInitialized();
            GUILayout.Space(6);
            var newToolTab = (ToolTab)MashBoxTabDrawer.DrawTabs((int)currentToolTab, new[] { "Art Tools", "Gameplay", "Audio", "Performance", "Testing", "Exporter" }, MashBoxTabDrawer.TabVisualStyle.Secondary, new[]
            {
                false,
                GameplayHasBlockingIssues(),
                false,
                false,
                false,
                false
            });
            if (newToolTab != currentToolTab)
            {
                currentToolTab = newToolTab;
                EditorPrefs.SetInt(PREF_KEY_MAP_TOOL_TAB, (int)currentToolTab);
                MarkSceneToolCacheDirty();
                if (currentToolTab == ToolTab.Gameplay)
                    RefreshGameplayValidationIssues(force: true);
            }
            GUILayout.Space(6);
            UpdateAuthoringSceneToolState();

            switch (currentToolTab)
            {
                case ToolTab.ArtTools:
                    mapToolsScrollPosition = EditorGUILayout.BeginScrollView(mapToolsScrollPosition);
                    DrawMapAuthoringToolsSection();
                    EditorGUILayout.EndScrollView();
                    break;
                case ToolTab.Gameplay:
                    mapToolsScrollPosition = EditorGUILayout.BeginScrollView(mapToolsScrollPosition);
                    DrawGameplayTab();
                    EditorGUILayout.EndScrollView();
                    break;
                case ToolTab.Audio:
                    mapToolsScrollPosition = EditorGUILayout.BeginScrollView(mapToolsScrollPosition);
                    DrawAudioTab();
                    EditorGUILayout.EndScrollView();
                    break;
                case ToolTab.Performance:
                    performanceScannerPanel ??= new MapPerformanceScannerPanel();
                    performanceScannerPanel.DrawGUI(true);
                    break;
                case ToolTab.Testing:
                    mapToolsScrollPosition = EditorGUILayout.BeginScrollView(mapToolsScrollPosition);
                    DrawTestingTab();
                    EditorGUILayout.EndScrollView();
                    break;
                case ToolTab.MapExporter:
                    mapToolsScrollPosition = EditorGUILayout.BeginScrollView(mapToolsScrollPosition);
                    DrawMapsTab();
                    EditorGUILayout.EndScrollView();
                    break;
            }

            if (GUI.changed)
                EditorUtility.SetDirty(db);
        }
        
        private void OnGUI()
        {
            Draw();
        }
        // -------------------------------
        //           MAPS TAB
        // -------------------------------
        private void DrawMapsTab()
        {
            EditorGUILayout.HelpBox(
                "Each scene becomes a map data asset. Drag scenes here to create maps, then fill in metadata, build a single map bundle to Documents, or publish that selected map to mod.io.",
                MessageType.Info);

            list.DoLayoutList();

            var drop = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drag Scenes Here To Create Map Packs");

            Event evt = Event.current;
            if (drop.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
                else if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is SceneAsset scene)
                            CreatePackForScene(scene);
                    }
                }
            }

            EditorGUILayout.Space(10f);
            DrawSelectedPackDetails();
        }

        private void DrawAudioTab()
        {
            EditorGUILayout.HelpBox(
                "Create a scene-level ambiance audio helper GameObject.",
                MessageType.Info);

            var existingAudio = Resources
                .FindObjectsOfTypeAll<MashBoxAmbianceAudio>()
                .FirstOrDefault(audio => audio.gameObject.scene.IsValid());
            if (existingAudio != null)
            {
                EditorGUILayout.ObjectField("Scene Ambiance", existingAudio, typeof(MashBoxAmbianceAudio), true);

                if (GUILayout.Button("Select Existing Ambiance Audio", GUILayout.Height(28f)))
                {
                    Selection.activeGameObject = existingAudio.gameObject;
                    EditorGUIUtility.PingObject(existingAudio.gameObject);
                }

                EditorGUILayout.Space(8f);
            }

            if (GUILayout.Button("Add Ambiance Audio To Scene", GUILayout.Height(40f)))
                CreateOrSelectAmbianceAudio();
        }

        private void DrawGameplayTab()
        {
            EnsureSceneToolCache();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Elements", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Add core gameplay scene elements used by MashBox maps.",
                    MessageType.Info);

                DrawGameplayValidationSection();
                GUILayout.Space(10f);
                DrawSpawnLocationSection();
                GUILayout.Space(10f);
                DrawMapBoundarySection();
                GUILayout.Space(10f);
                DrawChallengesSection();
                GUILayout.Space(10f);
                DrawMapTasksSection();
            }
        }

        private void DrawTestingTab()
        {
            EnsureSceneToolCache();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Testing Tools", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Create temporary helpers for playtesting your map inside the editor.",
                    MessageType.Info);

                DrawFlyCameraSection();
            }
        }

        private void DrawFlyCameraSection()
        {
            var existingFlyCameraObjects = cachedFlyCameraObjects;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Fly Camera", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Spawn a free-fly camera rig for quickly exploring and testing your map in Play Mode. The camera starts from the current Scene view when possible.",
                    MessageType.None);

                if (existingFlyCameraObjects.Count > 0)
                {
                    foreach (var flyCameraObject in existingFlyCameraObjects)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.ObjectField(flyCameraObject, typeof(GameObject), true);

                            if (GUILayout.Button("Select", GUILayout.Width(80f)))
                            {
                                Selection.activeGameObject = flyCameraObject;
                                EditorGUIUtility.PingObject(flyCameraObject);
                            }
                        }
                    }

                    GUILayout.Space(6f);
                }

                if (GUILayout.Button("Spawn Fly Camera", GUILayout.Height(36f)))
                    CreateFlyCameraInScene();
            }
        }

        private void DrawGameplayValidationSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var activeScene = SceneManager.GetActiveScene();
                var missingGroupScripts = cachedMissingChallengeGroupScriptCount;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Gameplay Validation", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (missingGroupScripts > 0 && GUILayout.Button("Fix Missing Group Scripts", GUILayout.Width(170f)))
                    {
                        if (EnsureChallengeGroupComponents(activeScene) > 0)
                        {
                            EditorSceneManager.MarkSceneDirty(activeScene);
                            RefreshGameplayValidationIssues(force: true);
                            EnsureSceneToolCache(force: true);
                            GUIUtility.ExitGUI();
                        }
                    }

                    if (GUILayout.Button("Validate", GUILayout.Width(100f)))
                    {
                        RefreshGameplayValidationIssues(force: true);
                        EnsureSceneToolCache(force: true);
                    }
                }

                if (gameplayValidationIssues == null || gameplayValidationIssues.Count == 0)
                {
                    EditorGUILayout.HelpBox("Gameplay validation passed.", MessageType.Info);
                    return;
                }

                foreach (var issue in gameplayValidationIssues)
                {
                    var messageType = issue.Severity == MapValidationSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning;
                    EditorGUILayout.HelpBox(issue.Message, messageType);
                }
            }
        }

        private void RefreshGameplayValidationIssues(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup < nextGameplayValidationRefreshTime)
                return;

            var activeScene = SceneManager.GetActiveScene();
            gameplayValidationIssues = MapContentPackValidator.ValidateGameplayScene(activeScene);
            nextGameplayValidationRefreshTime = EditorApplication.timeSinceStartup + (currentToolTab == ToolTab.Gameplay ? 0.35d : 1.5d);
        }

        private bool GameplayHasBlockingIssues()
        {
            return gameplayValidationIssues != null &&
                   gameplayValidationIssues.Any(issue => issue.Severity == MapValidationSeverity.Error);
        }

        private void DrawSpawnLocationSection()
        {
            var existingSpawn = cachedSpawnLocation;

            EditorGUILayout.LabelField("Spawn Point", EditorStyles.boldLabel);

            if (existingSpawn != null)
            {
                EditorGUILayout.ObjectField("Scene Spawn", existingSpawn, typeof(MBSpawnLocation), true);

                if (GUILayout.Button("Select Existing Spawn Point", GUILayout.Height(28f)))
                {
                    Selection.activeGameObject = existingSpawn.gameObject;
                    EditorGUIUtility.PingObject(existingSpawn.gameObject);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No MashBox spawn point exists in the active scene yet.",
                    MessageType.None);
            }

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(existingSpawn != null))
            {
                if (GUILayout.Button("Add Spawn Point To Scene", GUILayout.Height(36f)))
                    CreateSpawnLocationInScene();
            }
        }

        private void DrawMapBoundarySection()
        {
            var activeScene = SceneManager.GetActiveScene();
            var existingBoundary = cachedMapBoundary;

            EditorGUILayout.LabelField("Map Boundary", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Create a starter boundary spline object for the active scene.",
                MessageType.None);

            if (existingBoundary != null)
            {
                EditorGUILayout.ObjectField("Scene Boundary", existingBoundary, typeof(MBMapBoundary), true);

                if (GUILayout.Button("Select Existing Boundary", GUILayout.Height(28f)))
                {
                    Selection.activeGameObject = existingBoundary.gameObject;
                    EditorGUIUtility.PingObject(existingBoundary.gameObject);
                }
            }

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(existingBoundary != null))
            {
                if (GUILayout.Button("Add Map Boundary", GUILayout.Height(36f)))
                    CreateMapBoundaryInScene();
            }
        }

        private void DrawMapAuthoringToolsSection()
        {
            EnsureAuthoringToolInstances();


            int newTab = MashBoxTabDrawer.DrawTabs(authoringToolTab, new[]
            {
                "MG Brush",
                "Spline Loft",
                "Mesh Sculpt",
                "UV Spline"
            }, MashBoxTabDrawer.TabVisualStyle.Secondary);

            if (newTab != authoringToolTab)
            {
                bool leavingUvSpline = (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline
                    && (AuthoringToolTab)newTab != AuthoringToolTab.UVSpline;
                authoringToolTab = newTab;
                UVSplineEditor.SceneEditingEnabled = (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline;
                EditorPrefs.SetInt(PREF_KEY_AUTHORING_TOOL_TAB, authoringToolTab);
                if ((AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline)
                    SelectUvSplineForCurrentSelection();
                else if (leavingUvSpline)
                    DeselectUvSplineForOtherTool();
            }

            GUILayout.Space(8f);

            switch ((AuthoringToolTab)authoringToolTab)
            {
                case AuthoringToolTab.MGBrush:
                    authoringLoftTool?.DeactivateSceneTool();
                    authoringSculptTool?.DeactivateSceneTool();
                    authoringBrushTool?.ActivateSceneTool();
                    authoringBrushTool?.Draw(embeddedInParentWindow: true);
                    break;
                case AuthoringToolTab.SplineLoft:
                    authoringBrushTool?.DeactivateSceneTool();
                    authoringSculptTool?.DeactivateSceneTool();
                    authoringLoftTool?.ActivateSceneTool();
                    authoringLoftTool?.Draw(embeddedInParentWindow: true);
                    break;
                case AuthoringToolTab.MeshSculpt:
                    authoringBrushTool?.DeactivateSceneTool();
                    authoringLoftTool?.DeactivateSceneTool();
                    authoringSculptTool?.ActivateSceneTool();
                    authoringSculptTool?.Draw(embeddedInParentWindow: true);
                    break;
                case AuthoringToolTab.UVSpline:
                    authoringBrushTool?.DeactivateSceneTool();
                    authoringLoftTool?.DeactivateSceneTool();
                    authoringSculptTool?.DeactivateSceneTool();
                    DrawUvSplineToolSection();
                    break;
            }
        }

        private void DrawUvSplineToolSection()
        {
            GameObject selected = Selection.activeGameObject;
            UVSpline uvSpline = FindUvSpline(selected, true);
            if (uvSpline != null && selected != uvSpline.gameObject)
                SelectUvSpline(uvSpline);

            EditorGUILayout.LabelField("UV Spline", EditorStyles.boldLabel);
            if (uvSpline == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with a UV Spline component to activate its Scene handles and Inspector controls.", MessageType.Info);
                return;
            }

            EditorGUILayout.ObjectField("Active UV Spline", uvSpline, typeof(UVSpline), true);
            EditorGUILayout.HelpBox("The UV spline Scene handles are active and other authoring brushes are paused. Edit the UV settings in the selected object's Inspector.", MessageType.Info);
            if (GUILayout.Button("Focus UV Spline Inspector"))
            {
                Selection.activeObject = uvSpline.gameObject;
                EditorGUIUtility.PingObject(uvSpline.gameObject);
            }
        }

        private void EnsureAuthoringToolInstances()
        {
            if (authoringBrushTool == null)
            {
                authoringBrushTool = CreateInstance<MGBrushWindow>();
                // HideAndDontSave also includes NotEditable, which makes fields drawn
                // through SerializedObject (such as the prefab palette) read-only.
                authoringBrushTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                authoringBrushTool.DeactivateSceneTool();
            }

            if (authoringLoftTool == null)
            {
                authoringLoftTool = CreateInstance<MultiSplineLoftWindow>();
                authoringLoftTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                authoringLoftTool.DeactivateSceneTool();
            }

            if (authoringSculptTool == null)
            {
                authoringSculptTool = CreateInstance<MeshSculptWindow>();
                authoringSculptTool.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                authoringSculptTool.DeactivateSceneTool();
            }
        }

        private void DestroyAuthoringToolInstances()
        {
            if (authoringBrushTool != null)
            {
                authoringBrushTool.DeactivateSceneTool();
                DestroyImmediate(authoringBrushTool);
                authoringBrushTool = null;
            }

            if (authoringLoftTool != null)
            {
                authoringLoftTool.DeactivateSceneTool();
                DestroyImmediate(authoringLoftTool);
                authoringLoftTool = null;
            }

            if (authoringSculptTool != null)
            {
                authoringSculptTool.DeactivateSceneTool();
                DestroyImmediate(authoringSculptTool);
                authoringSculptTool = null;
            }
        }

        public void DeactivateEmbeddedSceneTools()
        {
            authoringBrushTool?.DeactivateSceneTool();
            authoringLoftTool?.DeactivateSceneTool();
            authoringSculptTool?.DeactivateSceneTool();
        }

        private void UpdateAuthoringSceneToolState()
        {
            UVSplineEditor.SceneEditingEnabled = currentToolTab == ToolTab.ArtTools
                && (AuthoringToolTab)authoringToolTab == AuthoringToolTab.UVSpline;
            if (currentToolTab == ToolTab.ArtTools)
            {
                if ((AuthoringToolTab)authoringToolTab == AuthoringToolTab.MGBrush)
                {
                    authoringLoftTool?.DeactivateSceneTool();
                    authoringSculptTool?.DeactivateSceneTool();
                    return;
                }

                if ((AuthoringToolTab)authoringToolTab == AuthoringToolTab.SplineLoft)
                {
                    authoringBrushTool?.DeactivateSceneTool();
                    authoringSculptTool?.DeactivateSceneTool();
                    authoringLoftTool?.ActivateSceneTool();
                    return;
                }

                if ((AuthoringToolTab)authoringToolTab == AuthoringToolTab.MeshSculpt)
                {
                    authoringBrushTool?.DeactivateSceneTool();
                    authoringLoftTool?.DeactivateSceneTool();
                    return;
                }
            }

            DeactivateEmbeddedSceneTools();
        }

        private void DrawChallengesSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var headerRect = EditorGUILayout.GetControlRect(false, 28f);
                var headerBackground = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.30f, 0.18f, 0.95f)
                    : new Color(0.72f, 0.84f, 0.70f, 1f);
                var headerBorder = EditorGUIUtility.isProSkin
                    ? new Color(0.45f, 0.66f, 0.34f, 0.95f)
                    : new Color(0.32f, 0.52f, 0.28f, 1f);
                EditorGUI.DrawRect(headerRect, headerBackground);
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, 1f), headerBorder);
                EditorGUI.LabelField(
                    new Rect(headerRect.x + 10f, headerRect.y, headerRect.width - 20f, headerRect.height),
                    "Challenges",
                    new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 13,
                        normal =
                        {
                            textColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 1f, 0.88f, 1f) : new Color(0.12f, 0.2f, 0.1f, 1f)
                        }
                    });

                GUILayout.Space(8f);
                DrawChallengeTypeSection("PhotoSpots", "Photo Spots", cachedPhotoSpots.Count, DrawPhotoSpotsSection);
                DrawChallengeTypeSection("Races", "Races", cachedRaces.Count, DrawRacesSection);
                DrawChallengeTypeSection("SecretGaps", "Secret Gaps", cachedSecretGaps.Count, DrawSecretGapsSection);
                DrawChallengeTypeSection("SideHits", "Side Hits", cachedSideHits.Count, DrawSideHitsSection);
                DrawChallengeTypeSection("ExpertLines", "Expert Lines", cachedExpertLines.Count, DrawExpertLinesSection);
                DrawChallengeTypeSection("Collectibles", "Collectibles", cachedCollectibles.Count, DrawCollectiblesSection);
                DrawChallengeTypeSection("BikeLetters", "B.I.K.E.S Letters", cachedLetters.Count, DrawBikeLettersSection);
            }
        }

        private void DrawMapTasksSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Map Tasks", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Author the player-facing task list for this map. These tasks feed the in-game MTB task HUD and can be exported with the map challenge manifest.",
                    MessageType.None);

                MBMapTaskList taskList = cachedMapTaskList;
                if (taskList == null)
                {
                    EditorGUILayout.HelpBox("No map task list exists in the active scene yet.", MessageType.None);
                    if (GUILayout.Button("Create Map Task List", GUILayout.Height(36f)))
                        CreateOrSelectMapTaskList();
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField("Task List", taskList, typeof(MBMapTaskList), true);
                    if (GUILayout.Button("Select", GUILayout.Width(80f)))
                    {
                        Selection.activeGameObject = taskList.gameObject;
                        EditorGUIUtility.PingObject(taskList.gameObject);
                    }
                }

                SerializedObject serializedTaskList = new SerializedObject(taskList);
                SerializedProperty tasksProperty = serializedTaskList.FindProperty("tasks");
                serializedTaskList.Update();

                DrawMapTaskPresetButtons(tasksProperty);
                GUILayout.Space(6f);

                if (tasksProperty.arraySize == 0)
                    EditorGUILayout.HelpBox("Add a task preset to start building this map's challenge list.", MessageType.None);

                for (int i = 0; i < tasksProperty.arraySize; i++)
                    DrawMapTaskProperty(tasksProperty, i);

                if (serializedTaskList.ApplyModifiedProperties())
                {
                    taskList.Sanitize();
                    EditorUtility.SetDirty(taskList);
                    EditorSceneManager.MarkSceneDirty(taskList.gameObject.scene);
                    MarkSceneToolCacheDirty();
                }
            }
        }

        private static void DrawMapTaskPresetButtons(SerializedProperty tasksProperty)
        {
            EditorGUILayout.LabelField("Add Task", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Activity Match"))
                    AddMapTask(tasksProperty, MBMapTaskKind.Standard);
                if (GUILayout.Button("Race Time"))
                    AddMapTask(tasksProperty, MBMapTaskKind.RaceTime);
                if (GUILayout.Button("Speed"))
                    AddMapTask(tasksProperty, MBMapTaskKind.GroundSpeedOnRace);
                if (GUILayout.Button("Side Hits"))
                    AddMapTask(tasksProperty, MBMapTaskKind.ParkHit);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Manual"))
                    AddMapTask(tasksProperty, MBMapTaskKind.ManualDistance);
                if (GUILayout.Button("Stoppie"))
                    AddMapTask(tasksProperty, MBMapTaskKind.StoppieDistance);
                if (GUILayout.Button("Expert Lines"))
                    AddMapTask(tasksProperty, MBMapTaskKind.ExpertLineSession);
                if (GUILayout.Button("Leaderboard"))
                    AddMapTask(tasksProperty, MBMapTaskKind.BeatLeaderboardPlayer);
            }
        }

        private static void DrawMapTaskProperty(SerializedProperty tasksProperty, int index)
        {
            SerializedProperty taskProperty = tasksProperty.GetArrayElementAtIndex(index);
            SerializedProperty enabledProperty = taskProperty.FindPropertyRelative("enabled");
            SerializedProperty typeProperty = taskProperty.FindPropertyRelative("taskType");
            SerializedProperty displayNameProperty = taskProperty.FindPropertyRelative("displayName");
            string taskName = string.IsNullOrWhiteSpace(displayNameProperty.stringValue)
                ? $"Task {index + 1:00}"
                : displayNameProperty.stringValue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    EditorGUILayout.LabelField($"{index + 1:00}. {taskName}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(index == 0))
                    {
                        if (GUILayout.Button("Up", GUILayout.Width(46f)))
                            tasksProperty.MoveArrayElement(index, index - 1);
                    }

                    using (new EditorGUI.DisabledScope(index >= tasksProperty.arraySize - 1))
                    {
                        if (GUILayout.Button("Down", GUILayout.Width(56f)))
                            tasksProperty.MoveArrayElement(index, index + 1);
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(60f)))
                    {
                        tasksProperty.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                EditorGUILayout.PropertyField(displayNameProperty, new GUIContent("Name"));
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(typeProperty, new GUIContent("Type"));
                if (EditorGUI.EndChangeCheck())
                    ApplyMapTaskTypeDefaults(taskProperty, (MBMapTaskKind)typeProperty.enumValueIndex, keepDisplayName: true);

                DrawMapTaskFields(taskProperty, (MBMapTaskKind)typeProperty.enumValueIndex);
                EditorGUILayout.HelpBox(BuildMapTaskPreview(taskProperty), MessageType.None);
            }
        }

        private static void DrawMapTaskFields(SerializedProperty taskProperty, MBMapTaskKind taskType)
        {
            switch (taskType)
            {
                case MBMapTaskKind.RaceTime:
                    DrawTaskStringField(taskProperty, "verb", "Race Name Contains");
                    DrawTaskStringField(taskProperty, "preposition", "Required Activity Word");
                    DrawTaskFloatField(taskProperty, "targetValue", "Seconds Or Faster");
                    DrawTaskIntField(taskProperty, "targetCount", "Completions");
                    break;
                case MBMapTaskKind.GroundSpeedOnRace:
                    DrawTaskStringField(taskProperty, "adjective", "Race Name Contains");
                    DrawTaskFloatField(taskProperty, "targetValue", "KPH Or Faster");
                    DrawTaskIntField(taskProperty, "targetCount", "Times");
                    break;
                case MBMapTaskKind.BeatLeaderboardPlayer:
                    DrawTaskStringField(taskProperty, "verb", "Player Name Contains");
                    DrawTaskStringField(taskProperty, "adjective", "Race Name Contains");
                    DrawTaskIntField(taskProperty, "targetCount", "Times");
                    break;
                case MBMapTaskKind.ParkHit:
                    DrawTaskIntField(taskProperty, "targetCount", "Side Hits");
                    break;
                case MBMapTaskKind.ManualDistance:
                    DrawTaskFloatField(taskProperty, "targetValue", "Meters");
                    break;
                case MBMapTaskKind.StoppieDistance:
                    DrawTaskFloatField(taskProperty, "targetValue", "Meters");
                    break;
                case MBMapTaskKind.ExpertLineSession:
                    DrawTaskIntField(taskProperty, "targetCount", "Expert Lines");
                    break;
                default:
                    DrawTaskStringField(taskProperty, "verb", "Activity Contains");
                    DrawTaskStringField(taskProperty, "preposition", "Also Contains");
                    DrawTaskStringField(taskProperty, "adjective", "Also Contains");
                    DrawTaskIntField(taskProperty, "targetCount", "Times");
                    break;
            }
        }

        private static void DrawTaskStringField(SerializedProperty taskProperty, string propertyName, string label)
        {
            SerializedProperty property = taskProperty.FindPropertyRelative(propertyName);
            property.stringValue = EditorGUILayout.TextField(label, property.stringValue);
        }

        private static void DrawTaskIntField(SerializedProperty taskProperty, string propertyName, string label)
        {
            SerializedProperty property = taskProperty.FindPropertyRelative(propertyName);
            property.intValue = Mathf.Max(1, EditorGUILayout.IntField(label, Mathf.Max(1, property.intValue)));
        }

        private static void DrawTaskFloatField(SerializedProperty taskProperty, string propertyName, string label)
        {
            SerializedProperty property = taskProperty.FindPropertyRelative(propertyName);
            property.floatValue = Mathf.Max(0.0f, EditorGUILayout.FloatField(label, Mathf.Max(0.0f, property.floatValue)));
        }

        private static void AddMapTask(SerializedProperty tasksProperty, MBMapTaskKind taskType)
        {
            int index = tasksProperty.arraySize;
            tasksProperty.InsertArrayElementAtIndex(index);
            ApplyMapTaskTypeDefaults(tasksProperty.GetArrayElementAtIndex(index), taskType, keepDisplayName: false);
            GUI.FocusControl(null);
        }

        private static void ApplyMapTaskTypeDefaults(SerializedProperty taskProperty, MBMapTaskKind taskType, bool keepDisplayName)
        {
            taskProperty.FindPropertyRelative("enabled").boolValue = true;
            taskProperty.FindPropertyRelative("taskType").enumValueIndex = (int)taskType;
            taskProperty.FindPropertyRelative("verb").stringValue = string.Empty;
            taskProperty.FindPropertyRelative("preposition").stringValue = string.Empty;
            taskProperty.FindPropertyRelative("adjective").stringValue = string.Empty;
            taskProperty.FindPropertyRelative("targetValue").floatValue = 0.0f;
            taskProperty.FindPropertyRelative("targetCount").intValue = 1;

            SerializedProperty displayNameProperty = taskProperty.FindPropertyRelative("displayName");
            string displayName = displayNameProperty.stringValue;

            switch (taskType)
            {
                case MBMapTaskKind.RaceTime:
                    displayName = "Finish Race In 60 Seconds";
                    taskProperty.FindPropertyRelative("verb").stringValue = "Race Name";
                    taskProperty.FindPropertyRelative("targetValue").floatValue = 60.0f;
                    break;
                case MBMapTaskKind.GroundSpeedOnRace:
                    displayName = "Reach 50 KPH On Race";
                    taskProperty.FindPropertyRelative("adjective").stringValue = "Race Name";
                    taskProperty.FindPropertyRelative("targetValue").floatValue = 50.0f;
                    break;
                case MBMapTaskKind.BeatLeaderboardPlayer:
                    displayName = "Beat Creator's Time";
                    taskProperty.FindPropertyRelative("verb").stringValue = "Creator";
                    break;
                case MBMapTaskKind.ParkHit:
                    displayName = "Session 5 Side Hits";
                    taskProperty.FindPropertyRelative("targetCount").intValue = 5;
                    break;
                case MBMapTaskKind.ManualDistance:
                    displayName = "Manual 20 m";
                    taskProperty.FindPropertyRelative("targetValue").floatValue = 20.0f;
                    break;
                case MBMapTaskKind.StoppieDistance:
                    displayName = "Stoppie 15 m";
                    taskProperty.FindPropertyRelative("targetValue").floatValue = 15.0f;
                    break;
                case MBMapTaskKind.ExpertLineSession:
                    displayName = "Session Expert Lines";
                    taskProperty.FindPropertyRelative("targetCount").intValue = 2;
                    break;
                default:
                    displayName = "Land A Trick";
                    taskProperty.FindPropertyRelative("verb").stringValue = "Landed";
                    break;
            }

            if (!keepDisplayName || string.IsNullOrWhiteSpace(displayNameProperty.stringValue))
                displayNameProperty.stringValue = displayName;
        }

        private static string BuildMapTaskPreview(SerializedProperty taskProperty)
        {
            MBMapTaskKind taskType = (MBMapTaskKind)taskProperty.FindPropertyRelative("taskType").enumValueIndex;
            string verb = taskProperty.FindPropertyRelative("verb").stringValue;
            string preposition = taskProperty.FindPropertyRelative("preposition").stringValue;
            string adjective = taskProperty.FindPropertyRelative("adjective").stringValue;
            int targetCount = Mathf.Max(1, taskProperty.FindPropertyRelative("targetCount").intValue);
            float targetValue = Mathf.Max(0.0f, taskProperty.FindPropertyRelative("targetValue").floatValue);

            return taskType switch
            {
                MBMapTaskKind.RaceTime => $"Completes when race activity contains '{verb}' and the reported time is <= {targetValue:0.#}s.",
                MBMapTaskKind.GroundSpeedOnRace => $"Completes when the rider reaches {targetValue:0.#} KPH on a race containing '{adjective}'.",
                MBMapTaskKind.BeatLeaderboardPlayer => $"Completes when the rider beats a leaderboard player containing '{verb}'{(string.IsNullOrWhiteSpace(adjective) ? string.Empty : $" on '{adjective}'")}.",
                MBMapTaskKind.ParkHit => $"Completes after {targetCount} side hit or park hit completion(s).",
                MBMapTaskKind.ManualDistance => $"Completes after {targetValue:0.#} meters of manual distance.",
                MBMapTaskKind.StoppieDistance => $"Completes after {targetValue:0.#} meters of stoppie distance.",
                MBMapTaskKind.ExpertLineSession => $"Completes after {targetCount} expert line completion(s).",
                _ => $"Advances when an activity contains: '{verb}' '{preposition}' '{adjective}'. Target: {targetCount} time(s)."
            };
        }
        private void DrawChallengeTypeSection(string key, string title, int count, Action drawContent)
        {
            if (!challengeSectionFoldouts.TryGetValue(key, out bool expanded))
                expanded = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool nextExpanded = EditorGUILayout.Foldout(expanded, $"{title} ({count})", true, EditorStyles.foldoutHeader);
                if (nextExpanded != expanded)
                    challengeSectionFoldouts[key] = nextExpanded;
                else if (!challengeSectionFoldouts.ContainsKey(key))
                    challengeSectionFoldouts.Add(key, expanded);

                if (!nextExpanded)
                    return;

                GUILayout.Space(6f);
                drawContent?.Invoke();
            }

            GUILayout.Space(6f);
        }
        private void DrawSecretGapsSection()
        {
            EditorGUILayout.LabelField("Secret Gaps", EditorStyles.boldLabel);
            if (DrawAddButton("Add Secret Gap", "Create a new secret gap.", 32f))
                CreateSecretGapChallenge();

            EditorGUILayout.HelpBox(
                "Scale, position, and rotate the Gate proxy transforms to set the gap. Duplicate gates under a gap if you want a multi-gate requirement for a more precise gap or a manual line through the trick.",
                MessageType.None);
            var activeScene = SceneManager.GetActiveScene();
            var secretGapGroupRoot = cachedSecretGapGroupRoot;
            var secretGapGroup = secretGapGroupRoot != null ? secretGapGroupRoot.GetComponent<MBSecretGapGroup>() : null;

            if (secretGapGroupRoot != null && secretGapGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Secret Gap group is missing its root challenge script. Add it to listen for gap progress and all-gaps-complete events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Secret Gap Group Script", GUILayout.Height(26f)))
                {
                    EnsureSecretGapGroupComponent(secretGapGroupRoot);
                                EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var secretGaps = cachedSecretGaps;

            if (secretGaps.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No secret gaps are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (secretGapGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = secretGapGroupRoot;
                        EditorGUIUtility.PingObject(secretGapGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            foreach (var secretGap in secretGaps)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("SecretGap", secretGap, secretGap.GapName))
                        continue;

                    GUI.SetNextControlName($"SecretGapName_{GetObjectStableId(secretGap)}");
                    EditorGUI.BeginChangeCheck();
                    var updatedGapName = EditorGUILayout.TextField("Gap Name", secretGap.GapName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(secretGap, "Rename Secret Gap");
                        secretGap.GapName = updatedGapName;
                        EditorUtility.SetDirty(secretGap);
            EditorSceneManager.MarkSceneDirty(secretGap.gameObject.scene);
                    }

                    EditorGUILayout.ObjectField("Gap Root", secretGap, typeof(MBSecretGap), true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = secretGap.gameObject;
                            EditorGUIUtility.PingObject(secretGap.gameObject);
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemoveSecretGap(secretGap);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawSideHitsSection()
        {
            EditorGUILayout.LabelField("Side Hits", EditorStyles.boldLabel);
            if (DrawAddButton("Add Side Hit", "Create a new side hit with one editable center trigger.", 32f))
                CreateSideHitChallenge();

            EditorGUILayout.HelpBox(
                "Scale, position, and rotate each Side Hit proxy to set the center trigger. The game spawns visual posts on the left and right sides at runtime.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var sideHitGroupRoot = cachedSideHitGroupRoot;
            var sideHitGroup = sideHitGroupRoot != null ? sideHitGroupRoot.GetComponent<MBSideHitGroup>() : null;

            if (sideHitGroupRoot != null && sideHitGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Side Hit group is missing its root challenge script. Add it to listen for side-hit progress and all-side-hits-complete events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Side Hit Group Script", GUILayout.Height(26f)))
                {
                    EnsureSideHitGroupComponent(sideHitGroupRoot);
                                EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var sideHits = cachedSideHits;

            if (sideHits.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No side hits are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (sideHitGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = sideHitGroupRoot;
                        EditorGUIUtility.PingObject(sideHitGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            foreach (var sideHit in sideHits)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("SideHit", sideHit, sideHit.SideHitName, $"{sideHit.FlagSetCount} set(s)"))
                        continue;

                    GUI.SetNextControlName($"SideHitName_{GetObjectStableId(sideHit)}");
                    EditorGUI.BeginChangeCheck();
                    var updatedSideHitName = EditorGUILayout.TextField("Side Hit Name", sideHit.SideHitName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(sideHit, "Rename Side Hit");
                        sideHit.SideHitName = updatedSideHitName;
                        EditorUtility.SetDirty(sideHit);
            EditorSceneManager.MarkSceneDirty(sideHit.gameObject.scene);
                    }

                    EditorGUILayout.LabelField("Reset Delay", $"{sideHit.ResetDelaySeconds:0.#} seconds");
                    EditorGUILayout.ObjectField("Side Hit Proxy", sideHit, typeof(MBSideHit), true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = sideHit.gameObject;
                            EditorGUIUtility.PingObject(sideHit.gameObject);
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemoveSideHit(sideHit);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }
        private void DrawExpertLinesSection()
        {
            EditorGUILayout.LabelField("Expert Lines", EditorStyles.boldLabel);
            if (DrawAddButton("Add Expert Line", "Create a timed expert line with required gates.", 32f))
                CreateExpertLineChallenge();

            EditorGUILayout.HelpBox(
                "Scale, position, and rotate Gate proxy transforms to set the line. Expert Lines must hit Gate 01 Start first, then every other gate before the timer expires.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var expertLineGroupRoot = cachedExpertLineGroupRoot;
            var expertLineGroup = expertLineGroupRoot != null ? expertLineGroupRoot.GetComponent<MBExpertLineGroup>() : null;

            if (expertLineGroupRoot != null && expertLineGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Expert Line group is missing its root challenge script. Add it to listen for expert-line progress and all-expert-lines-complete events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Expert Line Group Script", GUILayout.Height(26f)))
                {
                    EnsureExpertLineGroupComponent(expertLineGroupRoot);
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var expertLines = cachedExpertLines;

            if (expertLines.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No expert lines are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (expertLineGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = expertLineGroupRoot;
                        EditorGUIUtility.PingObject(expertLineGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            foreach (var expertLine in expertLines)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("ExpertLine", expertLine, expertLine.LineName, $"{expertLine.TimeLimitSeconds:0.#}s"))
                        continue;

                    GUI.SetNextControlName($"ExpertLineName_{GetObjectStableId(expertLine)}");
                    EditorGUI.BeginChangeCheck();
                    var updatedLineName = EditorGUILayout.TextField("Line Name", expertLine.LineName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(expertLine, "Rename Expert Line");
                        expertLine.LineName = updatedLineName;
                        EditorUtility.SetDirty(expertLine);
                        EditorSceneManager.MarkSceneDirty(expertLine.gameObject.scene);
                    }

                    EditorGUI.BeginChangeCheck();
                    float updatedTimeLimit = EditorGUILayout.FloatField("Time Limit", expertLine.TimeLimitSeconds);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(expertLine, "Change Expert Line Time Limit");
                        expertLine.TimeLimitSeconds = updatedTimeLimit;
                        EditorUtility.SetDirty(expertLine);
                        EditorSceneManager.MarkSceneDirty(expertLine.gameObject.scene);
                    }

                    EditorGUILayout.ObjectField("Expert Line Root", expertLine, typeof(MBExpertLine), true);
                    Transform signProxy = expertLine.SignProxyTransform;
                    EditorGUILayout.ObjectField("Sign Proxy", signProxy, typeof(Transform), true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = expertLine.gameObject;
                            EditorGUIUtility.PingObject(expertLine.gameObject);
                        }

                        if (signProxy != null)
                        {
                            if (GUILayout.Button("Select Sign", GUILayout.Height(26f)))
                            {
                                Selection.activeGameObject = signProxy.gameObject;
                                EditorGUIUtility.PingObject(signProxy.gameObject);
                            }
                        }
                        else if (GUILayout.Button("Add Sign Proxy", GUILayout.Height(26f)))
                        {
                            EnsureExpertLineSignProxy(expertLine);
                            MarkSceneToolCacheDirty();
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemoveExpertLine(expertLine);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }
        private void DrawRacesSection()
        {
            EditorGUILayout.LabelField("Races", EditorStyles.boldLabel);
            if (DrawAddButton("Add Race", "Create a new race with editable gate markers.", 32f))
                CreateRaceChallenge();

            EditorGUILayout.HelpBox(
                "Races work like authored gate paths. Gates are numbered from hierarchy order, draw with their own color, and can be added or renumbered from here.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var raceGroupRoot = cachedRaceGroupRoot;
            var raceGroup = raceGroupRoot != null ? raceGroupRoot.GetComponent<MBRaceGroup>() : null;

            if (raceGroupRoot != null && raceGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Races group is missing its root challenge script. Add it to listen for race progress and all-races-complete events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Race Group Script", GUILayout.Height(26f)))
                {
                    EnsureRaceGroupComponent(raceGroupRoot);
                                EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var races = cachedRaces;

            if (races.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No races are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (raceGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = raceGroupRoot;
                        EditorGUIUtility.PingObject(raceGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            foreach (var race in races)
            {
                var raceGates = GetOrderedRaceGates(race.transform);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("Race", race, race.RaceName, $"{raceGates.Count:00} gates"))
                        continue;

                    GUI.SetNextControlName($"RaceName_{GetObjectStableId(race)}");
                    EditorGUI.BeginChangeCheck();
                    var updatedRaceName = EditorGUILayout.TextField("Race Name", race.RaceName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(race, "Rename Race");
                        race.RaceName = updatedRaceName;
                        EditorUtility.SetDirty(race);
            EditorSceneManager.MarkSceneDirty(race.gameObject.scene);
                    }

                    EditorGUILayout.ObjectField("Race Root", race, typeof(MBRace), true);
                    EditorGUILayout.LabelField("Gate Count", raceGates.Count.ToString("00"));
                    EditorGUILayout.LabelField("Total Distance", $"{race.GetTotalGatePathDistance():0.0} m");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = race.gameObject;
                            EditorGUIUtility.PingObject(race.gameObject);
                        }

                        if (GUILayout.Button("Add Gate", GUILayout.Height(26f)))
                        {
                            CreateRaceGate(race);
                            GUIUtility.ExitGUI();
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(raceGates.Count == 0))
                        {
                            if (GUILayout.Button("Renumber Gates", GUILayout.Height(26f)))
                            {
                                NormalizeRaceGates(race);
                                GUIUtility.ExitGUI();
                            }
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemoveRace(race);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawPhotoSpotsSection()
        {
            EditorGUILayout.LabelField("Photo Spots", EditorStyles.boldLabel);
            if (DrawAddButton("Add Photo Spot", "Create a new photo spot camera marker.", 32f))
                CreatePhotoSpot();

            EditorGUILayout.HelpBox(
                "Photo Spots spawn under Challenges/Photo Spots. Position and rotate them where you want the player photo camera to live.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var photoSpotGroupRoot = cachedPhotoSpotGroupRoot;
            var photoSpotGroup = photoSpotGroupRoot != null ? photoSpotGroupRoot.GetComponent<MBPhotoSpotGroup>() : null;

            if (photoSpotGroupRoot != null && photoSpotGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Photo Spots group is missing its root challenge script. Add it to listen for spot progress and all-spots-complete events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Photo Spot Group Script", GUILayout.Height(26f)))
                {
                    EnsurePhotoSpotGroupComponent(photoSpotGroupRoot);
                                EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var photoSpots = cachedPhotoSpots;

            if (photoSpots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No photo spots are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (photoSpotGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = photoSpotGroupRoot;
                        EditorGUIUtility.PingObject(photoSpotGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            for (var index = 0; index < photoSpots.Count; index++)
            {
                var photoSpot = photoSpots[index];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("PhotoSpot", photoSpot, $"Photo Spot {index + 1:00}", photoSpot.gameObject.name))
                        continue;

                    EditorGUILayout.ObjectField($"Photo Spot {index + 1:00}", photoSpot, typeof(MBPhotoSpot), true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = photoSpot.gameObject;
                            EditorGUIUtility.PingObject(photoSpot.gameObject);
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemovePhotoSpot(photoSpot);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawCollectiblesSection()
        {
            EditorGUILayout.LabelField("Collectibles", EditorStyles.boldLabel);
            if (DrawAddButton("Add Collectible", "Create a new collectible.", 32f))
                CreateCollectibleChallenge();

            EditorGUILayout.HelpBox(
                "Collectibles spawn under Challenges/Collectible. Position them in the scene and use the sphere proxy as a placement guide.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var collectibleGroupRoot = cachedCollectibleGroupRoot;
            var collectibleGroup = collectibleGroupRoot != null ? collectibleGroupRoot.GetComponent<MBCollectibleGroup>() : null;

            if (collectibleGroupRoot != null && collectibleGroup == null)
            {
                EditorGUILayout.HelpBox(
                    "The Collectible group is missing its root challenge script. Add it to listen for collectible progress and all-collected events.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Collectible Group Script", GUILayout.Height(26f)))
                {
                    EnsureCollectibleGroupComponent(collectibleGroupRoot);
                                EditorSceneManager.MarkSceneDirty(activeScene);
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            var collectibles = cachedCollectibles;

            if (collectibles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No collectibles are in the active scene yet.",
                    MessageType.None);
                return;
            }

            if (collectibleGroupRoot != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = collectibleGroupRoot;
                        EditorGUIUtility.PingObject(collectibleGroupRoot);
                    }
                }

                GUILayout.Space(6f);
            }

            foreach (var collectible in collectibles)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("Collectible", collectible, collectible.gameObject.name))
                        continue;

                    EditorGUILayout.ObjectField(collectible.gameObject.name, collectible, typeof(MBCollectible), true);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select", GUILayout.Height(26f)))
                        {
                            Selection.activeGameObject = collectible.gameObject;
                            EditorGUIUtility.PingObject(collectible.gameObject);
                        }

                        if (GUILayout.Button("Remove", GUILayout.Height(26f)))
                        {
                            RemoveCollectible(collectible);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        private void DrawBikeLettersSection()
        {
            EditorGUILayout.LabelField("B.I.K.E.S Letters", EditorStyles.boldLabel);
            if (DrawAddButton("Add Collect B.I.K.E.S Letters", "Create a new B.I.K.E.S. letters challenge.", 32f))
                CreateBikeLettersChallenge();

            EditorGUILayout.HelpBox(
                "Collect Letters creates B, I, K, E, and S under Challenges/Collect Letters. Position each letter proxy where you want players to collect it.",
                MessageType.None);

            var activeScene = SceneManager.GetActiveScene();
            var letters = cachedLetters;

            if (letters.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No B.I.K.E.S letters are in the active scene yet.",
                    MessageType.None);
                return;
            }

            var lettersRoot = cachedLettersRoot;
            var lettersChallenge = lettersRoot != null ? lettersRoot.GetComponent<MBCollectLettersChallenge>() : null;

            if (lettersRoot != null && lettersChallenge == null)
            {
                EditorGUILayout.HelpBox(
                    "This B.I.K.E.S challenge group is missing its challenge script. Add it to unlock group events like letter collected and completed.",
                    MessageType.Warning);

                if (GUILayout.Button("Add Challenge Script", GUILayout.Height(26f)))
                {
                    EnsureBikeLettersChallengeComponent(lettersRoot.gameObject);
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(6f);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select Challenge Group", GUILayout.Height(26f)))
                {
                    var selectionTarget = lettersRoot != null ? lettersRoot.gameObject : letters[0].gameObject;
                    Selection.activeGameObject = selectionTarget;
                    EditorGUIUtility.PingObject(selectionTarget);
                }

                if (GUILayout.Button("Remove Challenge Group", GUILayout.Height(26f)))
                {
                    RemoveBikeLettersChallenge(lettersRoot != null ? lettersRoot.gameObject : null);
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(6f);

            foreach (var letter in letters)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!DrawChallengeItemFoldout("CollectLetter", letter, letter.Letter.ToString(), letter.gameObject.name))
                        continue;

                    EditorGUILayout.ObjectField(letter.Letter.ToString(), letter, typeof(MBCollectLetter), true);

                    if (GUILayout.Button("Select", GUILayout.Height(26f)))
                    {
                        Selection.activeGameObject = letter.gameObject;
                        EditorGUIUtility.PingObject(letter.gameObject);
                    }
                }
            }
        }

        private bool DrawChallengeItemFoldout(string scope, UnityEngine.Object obj, string title, string detail = null)
        {
            string id = GetObjectStableId(obj);
            string key = string.IsNullOrEmpty(id) ? $"{scope}:{title}" : $"{scope}:{id}";
            if (!challengeItemFoldouts.TryGetValue(key, out bool expanded))
                expanded = false;

            string cleanTitle = string.IsNullOrWhiteSpace(title) ? "Unnamed" : title.Trim();
            string label = string.IsNullOrWhiteSpace(detail) ? cleanTitle : $"{cleanTitle}  -  {detail}";
            bool nextExpanded = EditorGUILayout.Foldout(expanded, label, true);
            if (nextExpanded != expanded)
                challengeItemFoldouts[key] = nextExpanded;
            else if (!challengeItemFoldouts.ContainsKey(key))
                challengeItemFoldouts.Add(key, expanded);

            return nextExpanded;
        }
        private static bool DrawAddButton(string label, string tooltip, float height)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            var buttonPressed = GUI.Button(rect, new GUIContent(string.Empty, tooltip));

            var iconRect = new Rect(rect.x + 12f, rect.y + (rect.height - 14f) * 0.5f, 14f, 14f);
            var labelRect = new Rect(rect.x + 30f, rect.y, rect.width - 42f, rect.height);
            var previousColor = GUI.color;

            GUI.color = new Color(0.34f, 0.84f, 0.38f, 1f);
            GUI.Label(iconRect, "+", new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            });
            GUI.color = previousColor;

            GUI.Label(labelRect, label, new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft
            });

            return buttonPressed;
        }

        private static void CreateOrSelectAmbianceAudio()
        {
            var existingAudio = Resources
                .FindObjectsOfTypeAll<MashBoxAmbianceAudio>()
                .FirstOrDefault(audio => audio.gameObject.scene.IsValid());
            if (existingAudio != null)
            {
                Selection.activeGameObject = existingAudio.gameObject;
                EditorGUIUtility.PingObject(existingAudio.gameObject);
                Debug.Log("[MashBox] Selected existing Ambiance Audio object in the scene.");
                return;
            }

            var audioObject = new GameObject("Ambiance Audio");
            Undo.RegisterCreatedObjectUndo(audioObject, "Create Ambiance Audio");
            audioObject.AddComponent<MashBoxAmbianceAudio>();

            Selection.activeGameObject = audioObject;
            EditorGUIUtility.PingObject(audioObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateSpawnLocationInScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnLocationPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MashBoxMapTools] Could not load spawn point prefab at '{SpawnLocationPrefabPath}'.");
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene()) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[MashBoxMapTools] Failed to instantiate the spawn point prefab.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create MashBox Spawn Point");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateMapBoundaryInScene()
        {
            var boundary = new GameObject("Map Boundary");
            Undo.RegisterCreatedObjectUndo(boundary, "Create Map Boundary");
            Undo.AddComponent<MBMapBoundary>(boundary);
            boundary.AddComponent<MBSplineComponent>();
            boundary.AddComponent<MBSplineMeshGenerator>();

            Selection.activeGameObject = boundary;
            EditorGUIUtility.PingObject(boundary);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateFlyCameraInScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FreeCamPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MashBoxMapTools] Could not load free cam prefab at '{FreeCamPrefabPath}'.");
                return;
            }

            var flyCameraObject = PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene()) as GameObject;
            if (flyCameraObject == null)
            {
                Debug.LogError("[MashBoxMapTools] Failed to instantiate the free cam prefab.");
                return;
            }

            RemoveOtherSceneCameras(SceneManager.GetActiveScene(), flyCameraObject);

            Undo.RegisterCreatedObjectUndo(flyCameraObject, "Create Fly Camera");
            flyCameraObject.tag = "EditorOnly";

            var transform = flyCameraObject.transform;
            var sceneView = SceneView.lastActiveSceneView;

            if (sceneView != null)
            {
                transform.SetPositionAndRotation(sceneView.camera.transform.position, sceneView.camera.transform.rotation);
            }
            else
            {
                var spawnLocation = Resources
                    .FindObjectsOfTypeAll<MBSpawnLocation>()
                    .FirstOrDefault(spawn => spawn.gameObject.scene == SceneManager.GetActiveScene());

                if (spawnLocation != null)
                {
                    transform.SetPositionAndRotation(
                        spawnLocation.transform.position + new Vector3(0f, 1.75f, -4f),
                        spawnLocation.transform.rotation);
                }
            }

            Selection.activeGameObject = flyCameraObject;
            EditorGUIUtility.PingObject(flyCameraObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveOtherSceneCameras(Scene scene, GameObject keepRoot)
        {
            var cameraRootsToRemove = Resources
                .FindObjectsOfTypeAll<Camera>()
                .Where(camera => camera != null && camera.gameObject.scene == scene)
                .Select(camera => camera.transform.root.gameObject)
                .Where(root => root != null && root != keepRoot)
                .Distinct()
                .ToList();

            foreach (var root in cameraRootsToRemove)
                Undo.DestroyObjectImmediate(root);
        }

        private void CreateOrSelectMapTaskList()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                EditorUtility.DisplayDialog("Map Tasks", "Open a scene before creating map tasks.", "OK");
                return;
            }

            MBMapTaskList existing = MBMapTaskList.FindInScene(activeScene);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return;
            }

            GameObject taskRoot = FindOrCreateChallengeTypeRoot(MBMapTaskList.RootName);
            MBMapTaskList taskList = Undo.AddComponent<MBMapTaskList>(taskRoot);
            taskList.AddStarterTasks();
            EditorUtility.SetDirty(taskList);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = taskRoot;
            EditorGUIUtility.PingObject(taskRoot);
            MarkSceneToolCacheDirty();
            EnsureSceneToolCache(force: true);
        }
        private static void CreateSecretGapChallenge()
        {
            var secretGapRoot = FindOrCreateChallengeTypeRoot("Secret Gap");
            EnsureSecretGapGroupComponent(secretGapRoot);
            var secretGap = CreateChallengeMarker("Secret Gap", "New Secret Gap");
            var firstGate = CreateChildObject(secretGap.transform, "Gate");
            var secondGate = CreateChildObject(secretGap.transform, "Gate");
            var secretGapComponent = Undo.AddComponent<MBSecretGap>(secretGap);

            firstGate.transform.localPosition = Vector3.zero;
            secondGate.transform.localPosition = new Vector3(0f, 0f, 4f);

            Undo.AddComponent<MBGateGizmo>(firstGate);
            Undo.AddComponent<MBGateGizmo>(secondGate);
            secretGapComponent.GapName = secretGap.name;

            Selection.activeGameObject = secretGap;
            EditorGUIUtility.PingObject(secretGap);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateRaceChallenge()
        {
            var raceRoot = FindOrCreateChallengeTypeRoot("Races");
            EnsureRaceGroupComponent(raceRoot);
            var race = CreateChallengeMarker("Races", "New Race");
            var raceComponent = Undo.AddComponent<MBRace>(race);
            raceComponent.RaceName = race.name;
            CreateRaceGate(raceComponent, Vector3.zero, false);
            CreateRaceGate(raceComponent, new Vector3(0f, 0f, 8f), true);

            Selection.activeGameObject = race;
            EditorGUIUtility.PingObject(race);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateSideHitChallenge()
        {
            var sideHitRoot = FindOrCreateChallengeTypeRoot("Side Hit");
            EnsureSideHitGroupComponent(sideHitRoot);
            var sideHit = CreateChallengeMarker("Side Hit", GetNextSideHitName());
            var sideHitComponent = Undo.AddComponent<MBSideHit>(sideHit);
            sideHitComponent.SideHitName = sideHit.name;

            Selection.activeGameObject = sideHit;
            EditorGUIUtility.PingObject(sideHit);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateExpertLineChallenge()
        {
            var expertLineRoot = FindOrCreateChallengeTypeRoot("Expert Line");
            EnsureExpertLineGroupComponent(expertLineRoot);
            var expertLine = CreateChallengeMarker("Expert Line", GetNextExpertLineName());
            var firstGate = CreateChildObject(expertLine.transform, "Gate 01 Start");
            var secondGate = CreateChildObject(expertLine.transform, "Gate 02");
            var expertLineComponent = Undo.AddComponent<MBExpertLine>(expertLine);

            firstGate.transform.localPosition = Vector3.zero;
            secondGate.transform.localPosition = new Vector3(0f, 0f, 5f);

            ConfigureExpertLineGateGizmo(Undo.AddComponent<MBGateGizmo>(firstGate));
            ConfigureExpertLineGateGizmo(Undo.AddComponent<MBGateGizmo>(secondGate));
            EnsureExpertLineSignProxy(expertLineComponent);
            expertLineComponent.LineName = expertLine.name;
            expertLineComponent.TimeLimitSeconds = 5.0f;

            Selection.activeGameObject = expertLine;
            EditorGUIUtility.PingObject(expertLine);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        private static void CreateCollectibleChallenge()
        {
            var collectibleRoot = FindOrCreateChallengeTypeRoot("Collectible");
            EnsureCollectibleGroupComponent(collectibleRoot);
            var collectible = CreateChallengeMarker("Collectible", GetNextCollectibleName());
            Undo.AddComponent<MBCollectible>(collectible);

            Selection.activeGameObject = collectible;
            EditorGUIUtility.PingObject(collectible);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreatePhotoSpot()
        {
            var photoSpotRoot = FindOrCreateChallengeTypeRoot("Photo Spots");
            EnsurePhotoSpotGroupComponent(photoSpotRoot);
            var photoSpot = CreateChildObject(photoSpotRoot.transform, "Camera");
            PlaceNewChallengeObject(photoSpot.transform, matchSceneViewRotation: true);
            MBSceneIconUtility.ApplyChallengeSceneIcon(photoSpot);
            Undo.AddComponent<MBPhotoSpot>(photoSpot);

            Selection.activeGameObject = photoSpot;
            EditorGUIUtility.PingObject(photoSpot);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void CreateBikeLettersChallenge()
        {
            var lettersRoot = FindOrCreateChallengeTypeRoot("Collect Letters");
            EnsureBikeLettersChallengeComponent(lettersRoot);
            var startPosition = GetScenePlacementPosition();
            CreateBikeLetterIfMissing(lettersRoot.transform, MBCollectLetter.LetterType.B, startPosition + new Vector3(0f, 0f, 0f));
            CreateBikeLetterIfMissing(lettersRoot.transform, MBCollectLetter.LetterType.I, startPosition + new Vector3(2f, 0f, 0f));
            CreateBikeLetterIfMissing(lettersRoot.transform, MBCollectLetter.LetterType.K, startPosition + new Vector3(4f, 0f, 0f));
            CreateBikeLetterIfMissing(lettersRoot.transform, MBCollectLetter.LetterType.E, startPosition + new Vector3(6f, 0f, 0f));
            CreateBikeLetterIfMissing(lettersRoot.transform, MBCollectLetter.LetterType.S, startPosition + new Vector3(8f, 0f, 0f));

            Selection.activeGameObject = lettersRoot;
            EditorGUIUtility.PingObject(lettersRoot);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveSecretGap(MBSecretGap secretGap)
        {
            if (secretGap == null)
                return;

            var gapObject = secretGap.gameObject;
            Undo.DestroyObjectImmediate(gapObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveSideHit(MBSideHit sideHit)
        {
            if (sideHit == null)
                return;

            var sideHitObject = sideHit.gameObject;
            Undo.DestroyObjectImmediate(sideHitObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveExpertLine(MBExpertLine expertLine)
        {
            if (expertLine == null)
                return;

            Undo.DestroyObjectImmediate(expertLine.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void ConfigureExpertLineGateGizmo(MBGateGizmo gizmo)
        {
            if (gizmo == null)
                return;

            gizmo.FillColor = new Color(0.0f, 0.0f, 0.0f, 0.18f);
            gizmo.WireColor = new Color(0.0f, 0.0f, 0.0f, 0.96f);
        }
        private static Transform EnsureExpertLineSignProxy(MBExpertLine expertLine)
        {
            if (expertLine == null)
                return null;

            Transform signProxy = expertLine.SignProxyTransform;
            if (signProxy == null)
            {
                var signObject = new GameObject(MBExpertLine.SignProxyName);
                Undo.RegisterCreatedObjectUndo(signObject, "Add Expert Line Sign Proxy");
                signProxy = signObject.transform;
                signProxy.SetParent(expertLine.transform, false);
                signProxy.localPosition = new Vector3(-1.25f, 0f, 0f);
                signProxy.localRotation = Quaternion.identity;
                signProxy.localScale = Vector3.one;
            }

            if (signProxy.GetComponent<MBExpertLineSignGizmo>() == null)
                Undo.AddComponent<MBExpertLineSignGizmo>(signProxy.gameObject);

            Selection.activeGameObject = signProxy.gameObject;
            EditorGUIUtility.PingObject(signProxy.gameObject);
            EditorSceneManager.MarkSceneDirty(expertLine.gameObject.scene);
            return signProxy;
        }

        private static void RemoveCollectible(MBCollectible collectible)
        {
            if (collectible == null)
                return;

            var collectibleRoot = collectible.transform.parent;
            Undo.DestroyObjectImmediate(collectible.gameObject);
            RenumberCollectibles(collectibleRoot);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveRace(MBRace race)
        {
            if (race == null)
                return;

            Undo.DestroyObjectImmediate(race.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemovePhotoSpot(MBPhotoSpot photoSpot)
        {
            if (photoSpot == null)
                return;

            Undo.DestroyObjectImmediate(photoSpot.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void RemoveBikeLettersChallenge(GameObject lettersRoot)
        {
            if (lettersRoot == null)
                return;

            Undo.DestroyObjectImmediate(lettersRoot);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static GameObject CreateChallengeMarker(string challengeTypeName, string baseChallengeName)
        {
            var challengeTypeRoot = FindOrCreateChallengeTypeRoot(challengeTypeName);
            var challengeName = GetUniqueChildName(challengeTypeRoot.transform, baseChallengeName);
            var marker = new GameObject(challengeName);
            Undo.RegisterCreatedObjectUndo(marker, $"Create {challengeTypeName}");
            Undo.SetTransformParent(marker.transform, challengeTypeRoot.transform, $"Parent {challengeName}");
            PlaceNewChallengeObject(marker.transform);
            MBSceneIconUtility.ApplyChallengeSceneIcon(marker);

            Selection.activeGameObject = marker;
            EditorGUIUtility.PingObject(marker);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return marker;
        }

        private static GameObject FindOrCreateChallengeTypeRoot(string challengeTypeName)
        {
            var challengesRoot = FindOrCreateSceneRoot(ChallengesRootName);
            var existingTypeRoot = challengesRoot.transform
                .Cast<Transform>()
                .FirstOrDefault(child => string.Equals(child.name, challengeTypeName, StringComparison.Ordinal));

            if (existingTypeRoot != null)
                return existingTypeRoot.gameObject;

            var createdTypeRoot = new GameObject(challengeTypeName);
            Undo.RegisterCreatedObjectUndo(createdTypeRoot, $"Create {challengeTypeName}");
            Undo.SetTransformParent(createdTypeRoot.transform, challengesRoot.transform, $"Parent {challengeTypeName}");
            return createdTypeRoot;
        }

        private static GameObject CreateChildObject(Transform parent, string childName)
        {
            var child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
            Undo.SetTransformParent(child.transform, parent, $"Parent {childName}");
            return child;
        }

        private static void PlaceNewChallengeObject(Transform transformToPlace, float forwardDistance = 10f, bool matchSceneViewRotation = false)
        {
            if (transformToPlace == null)
                return;

            Undo.RecordObject(transformToPlace, "Place Challenge Element");
            transformToPlace.position = GetScenePlacementPosition(forwardDistance);

            if (!matchSceneViewRotation)
                return;

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView?.camera != null)
                transformToPlace.rotation = sceneView.camera.transform.rotation;
        }

        private static Vector3 GetScenePlacementPosition(float forwardDistance = 10f)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView?.camera == null)
                return Vector3.zero;

            var sceneCameraTransform = sceneView.camera.transform;
            return sceneCameraTransform.position + (sceneCameraTransform.forward * forwardDistance);
        }

        private static GameObject CreateRaceGate(MBRace race, Vector3? explicitLocalPosition = null, bool selectCreatedGate = true)
        {
            if (race == null)
                return null;

            var orderedGates = GetOrderedRaceGates(race.transform);
            var gate = CreateChildObject(race.transform, GetRaceGateName(orderedGates.Count + 1));
            var gateTransform = gate.transform;

            gateTransform.localPosition = explicitLocalPosition ?? GetNextRaceGateLocalPosition(orderedGates);
            gateTransform.localRotation = Quaternion.identity;
            gateTransform.localScale = Vector3.one;

            EnsureRaceGateComponent(gate);
            NormalizeRaceGates(race);

            if (selectCreatedGate)
            {
                Selection.activeGameObject = gate;
                EditorGUIUtility.PingObject(gate);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return gate;
        }

        private static void NormalizeRaceGates(MBRace race)
        {
            if (race == null)
                return;

            var gates = GetOrderedRaceGates(race.transform);
            if (gates.Count == 0)
                return;

            Undo.RecordObjects(gates.Select(gate => gate.gameObject).ToArray(), "Renumber Race Gates");

            for (var index = 0; index < gates.Count; index++)
            {
                var gate = gates[index];
                gate.name = GetRaceGateName(index + 1);

                EnsureRaceGateComponent(gate.gameObject);
                EditorUtility.SetDirty(gate.gameObject);
            }

            EditorUtility.SetDirty(race);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static List<Transform> GetOrderedRaceGates(Transform raceRoot)
        {
            if (raceRoot == null)
                return new List<Transform>();

            return raceRoot
                .Cast<Transform>()
                .Where(child => child.name.StartsWith("Gate", StringComparison.Ordinal) || child.GetComponent<MBRaceGate>() != null || child.GetComponent<MBGateGizmo>() != null)
                .OrderBy(child => child.GetSiblingIndex())
                .ToList();
        }

        private static Vector3 GetNextRaceGateLocalPosition(IReadOnlyList<Transform> orderedGates)
        {
            if (orderedGates == null || orderedGates.Count == 0)
                return Vector3.zero;

            if (orderedGates.Count == 1)
                return orderedGates[0].localPosition + new Vector3(0f, 0f, 8f);

            var lastGate = orderedGates[orderedGates.Count - 1];
            var previousGate = orderedGates[orderedGates.Count - 2];
            var offset = lastGate.localPosition - previousGate.localPosition;
            if (offset.sqrMagnitude < 0.0001f)
                offset = new Vector3(0f, 0f, 8f);

            return lastGate.localPosition + offset;
        }

        private static string GetRaceGateName(int gateNumber)
        {
            return $"Gate {gateNumber:00}";
        }

        private static void EnsureRaceGateComponent(GameObject gateObject)
        {
            if (gateObject == null)
                return;

            if (gateObject.GetComponent<MBRaceGate>() == null)
                Undo.AddComponent<MBRaceGate>(gateObject);

            var legacyGizmo = gateObject.GetComponent<MBGateGizmo>();
            if (legacyGizmo != null)
                Undo.DestroyObjectImmediate(legacyGizmo);

            EditorUtility.SetDirty(gateObject);
        }

        private static string GetUniqueChildName(Transform parent, string baseName)
        {
            if (parent.Find(baseName) == null)
                return baseName;

            for (var index = 2; index < 1000; index++)
            {
                var candidate = $"{baseName} {index}";
                if (parent.Find(candidate) == null)
                    return candidate;
            }

            return $"{baseName} {Guid.NewGuid():N}";
        }

        private static string GetNextSideHitName()
        {
            var sideHitRoot = FindOrCreateChallengeTypeRoot("Side Hit");
            var usedIndices = sideHitRoot.transform
                .Cast<Transform>()
                .Select(child => TryParseSideHitIndex(child.name))
                .Where(index => index > 0)
                .ToHashSet();

            var nextIndex = 1;
            while (usedIndices.Contains(nextIndex))
                nextIndex++;

            return $"Side Hit {nextIndex:00}";
        }

        private static int TryParseSideHitIndex(string name)
        {
            const string prefix = "Side Hit ";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;

            var suffix = name.Substring(prefix.Length);
            return int.TryParse(suffix, out var index) ? index : -1;
        }
        private static string GetNextExpertLineName()
        {
            var expertLineRoot = FindOrCreateChallengeTypeRoot("Expert Line");
            var usedIndices = expertLineRoot.transform
                .Cast<Transform>()
                .Select(child => TryParseExpertLineIndex(child.name))
                .Where(index => index > 0)
                .ToHashSet();

            var nextIndex = 1;
            while (usedIndices.Contains(nextIndex))
                nextIndex++;

            return $"Expert Line {nextIndex:00}";
        }

        private static int TryParseExpertLineIndex(string name)
        {
            const string prefix = "Expert Line ";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;

            var suffix = name.Substring(prefix.Length);
            return int.TryParse(suffix, out var index) ? index : -1;
        }
        private static string GetNextCollectibleName()
        {
            var collectiblesRoot = FindOrCreateChallengeTypeRoot("Collectible");
            var usedIndices = collectiblesRoot.transform
                .Cast<Transform>()
                .Select(child => TryParseCollectibleIndex(child.name))
                .Where(index => index > 0)
                .ToHashSet();

            var nextIndex = 1;
            while (usedIndices.Contains(nextIndex))
                nextIndex++;

            return $"Collectible_{nextIndex:00}";
        }

        private static int TryParseCollectibleIndex(string name)
        {
            const string prefix = "Collectible_";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;

            var suffix = name.Substring(prefix.Length);
            return int.TryParse(suffix, out var index) ? index : -1;
        }

        private static void RenumberCollectibles(Transform collectiblesRoot)
        {
            if (collectiblesRoot == null)
                return;

            var collectibles = collectiblesRoot
                .Cast<Transform>()
                .Where(child => child.GetComponent<MBCollectible>() != null)
                .OrderBy(child => child.GetSiblingIndex())
                .ToList();

            for (var index = 0; index < collectibles.Count; index++)
            {
                var collectible = collectibles[index];
                var expectedName = $"Collectible_{index + 1:00}";
                if (collectible.name == expectedName)
                    continue;

                Undo.RecordObject(collectible.gameObject, "Renumber Collectibles");
                collectible.name = expectedName;
                EditorUtility.SetDirty(collectible.gameObject);
            }
        }

        private static void CreateBikeLetterIfMissing(Transform parent, MBCollectLetter.LetterType letterType, Vector3 localPosition)
        {
            var expectedName = letterType.ToString();
            var existing = parent.Find(expectedName);
            if (existing != null)
            {
                var existingLetter = existing.GetComponent<MBCollectLetter>();
                if (existingLetter == null)
                    existingLetter = Undo.AddComponent<MBCollectLetter>(existing.gameObject);

                existingLetter.Letter = letterType;
                return;
            }

            var letterObject = CreateChildObject(parent, expectedName);
            letterObject.transform.localPosition = localPosition;
            MBSceneIconUtility.ApplyChallengeSceneIcon(letterObject);
            var letterComponent = Undo.AddComponent<MBCollectLetter>(letterObject);
            letterComponent.Letter = letterType;
        }

        private static void EnsureBikeLettersChallengeComponent(GameObject lettersRoot)
        {
            if (lettersRoot == null)
                return;

            if (lettersRoot.GetComponent<MBCollectLettersChallenge>() == null)
                Undo.AddComponent<MBCollectLettersChallenge>(lettersRoot);
        }

        private static void EnsureCollectibleGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBCollectibleGroup>() == null)
                Undo.AddComponent<MBCollectibleGroup>(groupRoot);
        }

        private static void EnsurePhotoSpotGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBPhotoSpotGroup>() == null)
                Undo.AddComponent<MBPhotoSpotGroup>(groupRoot);
        }

        private static void EnsureSideHitGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBSideHitGroup>() == null)
                Undo.AddComponent<MBSideHitGroup>(groupRoot);
        }
        private static void EnsureExpertLineGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBExpertLineGroup>() == null)
                Undo.AddComponent<MBExpertLineGroup>(groupRoot);
        }

        private static void EnsureSecretGapGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBSecretGapGroup>() == null)
                Undo.AddComponent<MBSecretGapGroup>(groupRoot);
        }

        private static void EnsureRaceGroupComponent(GameObject groupRoot)
        {
            if (groupRoot == null)
                return;

            if (groupRoot.GetComponent<MBRaceGroup>() == null)
                Undo.AddComponent<MBRaceGroup>(groupRoot);
        }

        private static GameObject FindChallengeTypeRootInScene(string challengeTypeName, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => string.Equals(transform.name, challengeTypeName, StringComparison.Ordinal))
                ?.gameObject;
        }

        private static int CountMissingChallengeGroupScripts(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return 0;

            var missingCount = 0;

            var collectibleRoot = FindChallengeTypeRootInScene("Collectible", scene);
            if (collectibleRoot != null && collectibleRoot.GetComponent<MBCollectibleGroup>() == null && collectibleRoot.GetComponentsInChildren<MBCollectible>(true).Length > 0)
                missingCount++;

            var photoSpotRoot = FindChallengeTypeRootInScene("Photo Spots", scene);
            if (photoSpotRoot != null && photoSpotRoot.GetComponent<MBPhotoSpotGroup>() == null && photoSpotRoot.GetComponentsInChildren<MBPhotoSpot>(true).Length > 0)
                missingCount++;

            var racesRoot = FindChallengeTypeRootInScene("Races", scene);
            if (racesRoot != null && racesRoot.GetComponent<MBRaceGroup>() == null && racesRoot.GetComponentsInChildren<MBRace>(true).Length > 0)
                missingCount++;

            var secretGapRoot = FindChallengeTypeRootInScene("Secret Gap", scene);
            if (secretGapRoot != null && secretGapRoot.GetComponent<MBSecretGapGroup>() == null && secretGapRoot.GetComponentsInChildren<MBSecretGap>(true).Length > 0)
                missingCount++;

            var sideHitRoot = FindChallengeTypeRootInScene("Side Hit", scene) ?? FindChallengeTypeRootInScene("Side Hits", scene);
            if (sideHitRoot != null && sideHitRoot.GetComponent<MBSideHitGroup>() == null && sideHitRoot.GetComponentsInChildren<MBSideHit>(true).Length > 0)
                missingCount++;

            var expertLineRoot = FindChallengeTypeRootInScene("Expert Line", scene) ?? FindChallengeTypeRootInScene("Expert Lines", scene);
            if (expertLineRoot != null && expertLineRoot.GetComponent<MBExpertLineGroup>() == null && expertLineRoot.GetComponentsInChildren<MBExpertLine>(true).Length > 0)
                missingCount++;

            var collectLettersRoot = FindChallengeTypeRootInScene("Collect Letters", scene);
            if (collectLettersRoot != null && collectLettersRoot.GetComponent<MBCollectLettersChallenge>() == null && collectLettersRoot.GetComponentsInChildren<MBCollectLetter>(true).Length > 0)
                missingCount++;

            return missingCount;
        }

        private static int EnsureChallengeGroupComponents(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return 0;

            var addedCount = 0;

            var collectibleRoot = FindChallengeTypeRootInScene("Collectible", scene);
            if (collectibleRoot != null && collectibleRoot.GetComponent<MBCollectibleGroup>() == null && collectibleRoot.GetComponentsInChildren<MBCollectible>(true).Length > 0)
            {
                EnsureCollectibleGroupComponent(collectibleRoot);
                addedCount++;
            }

            var photoSpotRoot = FindChallengeTypeRootInScene("Photo Spots", scene);
            if (photoSpotRoot != null && photoSpotRoot.GetComponent<MBPhotoSpotGroup>() == null && photoSpotRoot.GetComponentsInChildren<MBPhotoSpot>(true).Length > 0)
            {
                EnsurePhotoSpotGroupComponent(photoSpotRoot);
                addedCount++;
            }

            var racesRoot = FindChallengeTypeRootInScene("Races", scene);
            if (racesRoot != null && racesRoot.GetComponent<MBRaceGroup>() == null && racesRoot.GetComponentsInChildren<MBRace>(true).Length > 0)
            {
                EnsureRaceGroupComponent(racesRoot);
                addedCount++;
            }

            var secretGapRoot = FindChallengeTypeRootInScene("Secret Gap", scene);
            if (secretGapRoot != null && secretGapRoot.GetComponent<MBSecretGapGroup>() == null && secretGapRoot.GetComponentsInChildren<MBSecretGap>(true).Length > 0)
            {
                EnsureSecretGapGroupComponent(secretGapRoot);
                addedCount++;
            }

            var sideHitRoot = FindChallengeTypeRootInScene("Side Hit", scene) ?? FindChallengeTypeRootInScene("Side Hits", scene);
            if (sideHitRoot != null && sideHitRoot.GetComponent<MBSideHitGroup>() == null && sideHitRoot.GetComponentsInChildren<MBSideHit>(true).Length > 0)
            {
                EnsureSideHitGroupComponent(sideHitRoot);
                addedCount++;
            }

            var expertLineRoot = FindChallengeTypeRootInScene("Expert Line", scene) ?? FindChallengeTypeRootInScene("Expert Lines", scene);
            if (expertLineRoot != null && expertLineRoot.GetComponent<MBExpertLineGroup>() == null && expertLineRoot.GetComponentsInChildren<MBExpertLine>(true).Length > 0)
            {
                EnsureExpertLineGroupComponent(expertLineRoot);
                addedCount++;
            }

            var collectLettersRoot = FindChallengeTypeRootInScene("Collect Letters", scene);
            if (collectLettersRoot != null && collectLettersRoot.GetComponent<MBCollectLettersChallenge>() == null && collectLettersRoot.GetComponentsInChildren<MBCollectLetter>(true).Length > 0)
            {
                EnsureBikeLettersChallengeComponent(collectLettersRoot);
                addedCount++;
            }

            return addedCount;
        }

        private static GameObject FindOrCreateSceneRoot(string rootName)
        {
            var existing = SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .FirstOrDefault(go => string.Equals(go.name, rootName, StringComparison.Ordinal));

            if (existing != null)
                return existing;

            var created = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {rootName}");
            return created;
        }

        private void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var entry = db.Packs[index];
            float line = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 2;
            float secondY = y + line + 2f;
            var isSelected = index == list.index;

            if (Event.current.type == EventType.Repaint && isSelected)
            {
                var highlight = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.42f, 0.22f, 0.55f)
                    : new Color(0.50f, 0.78f, 0.50f, 0.85f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 2f), highlight);
            }

            if (entry == null)
            {
                EditorGUI.LabelField(new Rect(rect.x, y, rect.width, line), "Missing Map Pack");
                return;
            }

#if MashBoxDev
            entry.IncludeInBuild = EditorGUI.ToggleLeft(
                new Rect(rect.x, y, 60f, line),
                "Build", entry.IncludeInBuild);
#else
            EditorGUI.LabelField(new Rect(rect.x, y, 60f, line), "Map");
#endif

            var actionWidth = 72f;
            var contentX = rect.x + 64f;
            var contentWidth = rect.width - 68f - actionWidth;

            entry.Scene = (SceneAsset)EditorGUI.ObjectField(
                new Rect(contentX, y, contentWidth, line),
                entry.Scene, typeof(SceneAsset), false);

            EditorGUI.LabelField(
                new Rect(contentX, secondY, contentWidth, line),
                $"Map Name: {GetDisplayMapName(entry)}",
                EditorStyles.miniLabel);

            var actionRect = new Rect(rect.xMax - actionWidth + 4f, y, actionWidth - 8f, line);
            if (isSelected)
            {
                EditorGUI.LabelField(actionRect, "Selected", EditorStyles.miniBoldLabel);
            }
            else if (GUI.Button(actionRect, "Select"))
            {
                list.index = index;
                GUI.FocusControl(null);
                Repaint();
            }

            EditorUtility.SetDirty(entry);
        }

        private static string GetDisplayMapName(MapContentPackDefinition entry)
        {
            if (entry == null)
                return "Unnamed";

            return string.IsNullOrWhiteSpace(entry.MapName) ? entry.name : entry.MapName;
        }

        // -------------------------------
        //      SELECTIVE BUILD
        // -------------------------------
        private void BuildSelected()
        {
            // Clear all bundle names
            foreach (var entry in db.entries)
            {
                if (entry.scene == null) continue;

                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(entry.scene));
                importer.assetBundleName = "";
            }

            // Assign only included bundles
            foreach (var entry in db.entries)
            {
                if (!entry.includeInBuild || entry.scene == null)
                    continue;

                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(entry.scene));
                importer.assetBundleName = entry.bundleName;
            }

            AssetDatabase.SaveAssets();

            // ?? Run your ORIGINAL stable pipeline
            AssetBundleExporter.RunBuildFromExternalTool(outputPath);

            EditorUtility.DisplayDialog("Build Complete", "Selected bundles built successfully!", "OK");
        }

        // -------------------------------
        //   OLD EXPORTER CODE MERGED
        // -------------------------------
        private void BuildBundles()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

            string targetName = target.ToString();
            string platformOutput = Path.Combine(outputPath, targetName);

            Directory.CreateDirectory(platformOutput);

            BuildAssetBundleOptions unityOptions = BuildAssetBundleOptions.None;

            switch (compressionMode)
            {
                case CompressionMode.ChunkBasedCompression:
                    unityOptions = BuildAssetBundleOptions.ChunkBasedCompression;
                    break;
                case CompressionMode.None:
                    unityOptions = BuildAssetBundleOptions.UncompressedAssetBundle;
                    break;
            }

            Debug.Log($"[MGMapTools] Building bundles to {platformOutput} using {unityOptions}");

            var manifest = BuildPipeline.BuildAssetBundles(platformOutput, unityOptions, target);

            if (manifest == null)
            {
                Debug.LogError("[MGMapTools] BuildPipeline returned NULL — build failed.");
                return;
            }

            foreach (string bundleName in manifest.GetAllAssetBundles())
            {
                string oldPath = Path.Combine(platformOutput, bundleName);
                string newPath = Path.Combine(platformOutput, bundleName + ".bundle");

                if (File.Exists(oldPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                }
            }

            GenerateManifestOnly();

            AssetDatabase.Refresh();
        }

        // -------------------------------
        //  OLD MANIFEST GENERATION
        // -------------------------------
        private void GenerateManifestOnly()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string platform = target.ToString();
            string rootPath = Path.Combine(outputPath, platform);

            string versionLogPath = Path.Combine(rootPath, "version_log.json");

            VersionLog versionLog = LoadVersionLog(versionLogPath);

            var manifest = new MapManifestWrapper { maps = new Dictionary<string, MapInfo>() };

            string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();

            foreach (var bundleName in bundleNames)
            {
                string bundleFile = bundleName + ".bundle";
                string bundlePath = Path.Combine(rootPath, bundleFile);

                if (File.Exists(bundlePath))
                {
                    if (!versionLog.versions.ContainsKey(bundleName))
                        versionLog.versions[bundleName] = 1;

                    long size = new FileInfo(bundlePath).Length;

                    manifest.maps[bundleName] = new MapInfo
                    {
                        version = versionLog.versions[bundleName],
                        filename = bundleFile,
                        size = size
                    };
                }

                string folderName = bundleName + MapFolderSuffix;
                string folderPath = Path.Combine(rootPath, folderName);
                Directory.CreateDirectory(folderPath);

                SafeMove(bundlePath, Path.Combine(folderPath, bundleFile));
                SafeMove(Path.Combine(rootPath, bundleName + ".manifest"),
                         Path.Combine(folderPath, bundleName + ".manifest"));

                CopyScreenshot(bundleName, folderPath);
                WriteChallengeData(bundleName, folderPath);
            }

            File.WriteAllText(Path.Combine(rootPath, "manifest.json"), JsonUtility.ToJson(manifest, true));
            File.WriteAllText(versionLogPath, JsonUtility.ToJson(versionLog, true));

            Debug.Log("[MGMapTools] Manifest generated.");
        }

        private VersionLog LoadVersionLog(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    var data = JsonUtility.FromJson<VersionLog>(File.ReadAllText(path));
                    if (data.versions == null)
                        data.versions = new Dictionary<string, int>();
                    return data;
                }
                catch { }
            }

            return new VersionLog { versions = new Dictionary<string, int>() };
        }

        private void CopyScreenshot(string mapName, string dest)
        {
            string[] sceneGuids = AssetDatabase.FindAssets($"{mapName} t:Scene");
            if (sceneGuids.Length == 0) return;

            string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
            string sceneDir = Path.GetDirectoryName(scenePath);
            string screenshotPath = Path.Combine(sceneDir, mapName + ".png");

            if (File.Exists(screenshotPath))
                File.Copy(screenshotPath, Path.Combine(dest, mapName + ".png"), true);
        }

        private static List<MapTaskData> ExtractMapTaskData()
        {
            var result = new List<MapTaskData>();
            MBMapTaskList taskList = MBMapTaskList.FindInScene(SceneManager.GetActiveScene());
            if (taskList == null || taskList.Tasks == null)
                return result;

            for (int i = 0; i < taskList.Tasks.Count; i++)
            {
                MBMapTaskDefinition task = taskList.Tasks[i];
                if (task == null || !task.enabled)
                    continue;

                result.Add(new MapTaskData
                {
                    taskType = task.taskType.ToString(),
                    displayName = task.DisplayNameOrFallback,
                    verb = task.verb,
                    preposition = task.preposition,
                    adjective = task.adjective,
                    targetValue = task.targetValue,
                    targetCount = Mathf.Max(1, task.targetCount)
                });
            }

            return result;
        }
        private void WriteChallengeData(string mapName, string dest)
        {
            string[] guids = AssetDatabase.FindAssets($"{mapName} t:Scene");
            if (guids.Length == 0) return;

            string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject challengesRoot = GameObject.Find("Challenges");
            if (challengesRoot == null) return;

            var data = new ChallengeMapData
            {
                mapName = mapName,
                categories = new List<ChallengeCategory>(),
                    tasks = ExtractMapTaskData()
                };

            foreach (Transform cat in challengesRoot.transform)
            {
                var items = new List<string>();
                foreach (Transform c in cat) items.Add(c.name);

                data.categories.Add(new ChallengeCategory
                {
                    categoryName = cat.name,
                    items = items
                });
            }

            File.WriteAllText(Path.Combine(dest, $"{mapName}_Challenges.json"),
                              JsonUtility.ToJson(data, true));
        }

        private void SafeMove(string source, string dest)
        {
            if (!File.Exists(source)) return;

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(source, dest);
        }

        private void DrawSelectedPackDetails()
        {
            var selected = GetSelectedPack();
            if (selected == null)
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("Selected Map Pack", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No map is currently selected. Choose a map in the list above before building or publishing.", MessageType.Info);

                var emptyStateCurrentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Custom Folder");
                var emptyStateBuildLabel = GetBuildLabelForGame(emptyStateCurrentGame);
                var emptyStateGameFolder = ResolveDocumentsMapsFolderForGame(emptyStateCurrentGame);
                var emptyStateMashBoxFolder = ResolveMashBoxMapsFolder();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Game Target", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Current Game:", emptyStateCurrentGame, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Game Maps Folder:", string.IsNullOrWhiteSpace(emptyStateGameFolder) ? "Unavailable" : emptyStateGameFolder, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("MashBox Maps Folder:", emptyStateMashBoxFolder, EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button($"Build To {emptyStateBuildLabel}", GUILayout.Height(26f));
                        GUILayout.Button("Build To MashBox", GUILayout.Height(26f));
                    }
                }

                return;
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Selected Map Pack", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Map Name");
            EditorGUILayout.SelectableLabel(
                string.IsNullOrWhiteSpace(selected.MapName) ? selected.name : selected.MapName,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Rename Map Pack", GUILayout.Width(140f)))
            {
                RenameDialog.Show(
                    "Rename Map Pack",
                    "Enter the map pack name. This updates the map data name without changing the scene name.",
                    string.IsNullOrWhiteSpace(selected.MapName) ? selected.name : selected.MapName,
                    newName => CommitMapPackRename(selected, newName));
            }
            EditorGUILayout.EndHorizontal();

            selected.Scene = (SceneAsset)EditorGUILayout.ObjectField("Scene", selected.Scene, typeof(SceneAsset), false);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("In-Game Thumbnail");
            selected.Screenshot = (Texture2D)EditorGUILayout.ObjectField(selected.Screenshot, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();

#if MashBoxInsider
            selected.IsVanillaContent = EditorGUILayout.Toggle("Vanilla SDK Content", selected.IsVanillaContent);
#endif

#if MashBoxDev
            selected.IncludeInBuild = EditorGUILayout.Toggle("Include In Build", selected.IncludeInBuild);
            selected.BuildToCustomFolder = EditorGUILayout.Toggle("Build To Custom Folder", selected.BuildToCustomFolder);
#endif

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Mod.io Mod IDs", EditorStyles.boldLabel);
            DrawMapModIdMappings(selected);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selected);
                AssetDatabase.SaveAssets();
                InvalidateValidationResults(selected);
            }

            EditorGUILayout.Space(8f);
            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Custom Folder");
            var currentGameBuildLabel = GetBuildLabelForGame(currentGame);
            var hasCorrectPublishUnityVersion = GameTargetUnityVersionValidator.IsValidForPublishing(currentGame, out var unityVersionMessage);
            var canPublish = ModIoAuth.IsAuthorizedForCurrentGame() &&
                             !string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase) &&
                             hasCorrectPublishUnityVersion;
            var currentGameFolder = ResolveDocumentsMapsFolderForGame(currentGame);
            var mashBoxFolder = ResolveMashBoxMapsFolder();

            DrawValidationSection(selected);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Game Target", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Current Game:", currentGame, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Game Maps Folder:", string.IsNullOrWhiteSpace(currentGameFolder) ? "Unavailable" : currentGameFolder, EditorStyles.miniLabel);
                EditorGUILayout.LabelField("MashBox Maps Folder:", mashBoxFolder, EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(currentGameFolder)))
                    {
                        if (GUILayout.Button("Open Game Folder", GUILayout.Width(130f)))
                            OpenBuildOutputFolder(currentGameFolder);
                    }

                    if (GUILayout.Button("Open MashBox Folder", GUILayout.Width(140f)))
                        OpenBuildOutputFolder(mashBoxFolder);
                }
            }

            EditorGUILayout.HelpBox(
                "Publish packages only the currently selected map pack. Each publish uploads one map only, never multiple maps in the same mod.io package.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(currentGameFolder)))
                {
                    if (GUILayout.Button($"Build To {currentGameBuildLabel}", GUILayout.Height(26f)))
                        BuildSelectedPackToCurrentGame(selected);
                }

                using (new EditorGUI.DisabledScope(false))
                {
                    if (GUILayout.Button("Build To MashBox", GUILayout.Height(26f)))
                        BuildSelectedPackToMashBox(selected);
                }

                using (new EditorGUI.DisabledScope(!canPublish || isMapPublishInProgress))
                {
                    if (GUILayout.Button(
                            isMapPublishInProgress ? "Publishing Map..." : $"Publish Map To {currentGame} Mod.io",
                            GUILayout.Height(26f)))
                        ShowPublishPlatformSelector(selected, currentGame);
                }
            }

            if (isMapPublishInProgress)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Upload In Progress", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(activeMapPublishStatus, EditorStyles.wordWrappedMiniLabel);

                    if (!string.IsNullOrWhiteSpace(activeMapPublishRegion))
                        EditorGUILayout.LabelField($"Region: {activeMapPublishRegion}", EditorStyles.miniLabel);

                    var progressRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(progressRect, Mathf.Clamp01(activeMapPublishProgress), $"{Mathf.RoundToInt(activeMapPublishProgress * 100f)}%");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Cancel Upload", GUILayout.Width(140f)))
                            CancelActiveMapPublish();
                    }
                }
            }

#if MashBoxDev || YAKY
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Debug Export .unitypackage", GUILayout.Width(190f)))
                    ExportDebugUnityPackage(selected);

                using (new EditorGUI.DisabledScope(isMapPublishInProgress))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Debug Simulate Mod.io Publish",
                                "Runs the same local validation, scene preparation, lighting, packaging, and cleanup path as publishing, but stops before any upload."),
                            GUILayout.Width(230f)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Simulate Mod.io Publish?",
                                "This runs the same local pipeline as Publish Map To Mod.io, including full validation, temporary scene preparation, lighting-data rebinding, package creation, and cleanup.\n\nNothing will be uploaded to mod.io.",
                                "Run Simulation",
                                "Cancel"))
                        {
                            PublishMapToModioAsync(selected, currentGame, PublishPlatformOptions, simulateOnly: true);
                        }
                    }
                }
            }
#endif

            if (!ModIoAuth.IsAuthorizedForCurrentGame())
                EditorGUILayout.HelpBox("Log in to mod.io for the active game to enable map publishing.", MessageType.Info);
            else if (string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase))
                EditorGUILayout.HelpBox("Choose a game target instead of Custom Folder to publish this map to mod.io.", MessageType.Info);
            else if (!hasCorrectPublishUnityVersion)
                EditorGUILayout.HelpBox(unityVersionMessage, MessageType.Error);

            if (string.IsNullOrWhiteSpace(currentGameFolder))
                EditorGUILayout.HelpBox("Build To MashBox is always available. Set a game title when you also want to build to that game's Documents/Maps folder.", MessageType.Info);
        }

        private void DrawMapModIdMappings(MapContentPackDefinition selected)
        {
            foreach (var game in GameRegistry.Games)
            {
                var current = selected.GetModIdForGame(game.DisplayName) ?? string.Empty;
                bool isPublishTarget = selected.GameModMappings != null &&
                                       selected.GameModMappings.Any(mapping =>
                                           string.Equals(mapping.GameName, game.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                                           mapping.IsPublishTarget);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(game.DisplayName);
                    var updated = EditorGUILayout.TextField(current);
                    if (updated != current)
                        ApplyMapModId(selected, game.DisplayName, updated);

                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Toggle(
                            isPublishTarget,
                            new GUIContent("Publish Target", "Set automatically to the active Setup game when publishing."),
                            GUILayout.Width(105f));
                    }

                    ModIoModCreator.DrawCreateButton(
                        game.DisplayName,
                        !string.IsNullOrWhiteSpace(selected.MapName) ? selected.MapName : selected.name,
                        string.Empty,
                        createdId => ApplyCreatedMapModId(selected, game.DisplayName, createdId),
                        this);
                }
            }
        }

        private void ApplyCreatedMapModId(MapContentPackDefinition selected, string gameName, string modId)
        {
            ApplyMapModId(selected, gameName, modId);
            InvalidateValidationResults(selected);
        }


        private static void ApplyMapModId(MapContentPackDefinition selected, string gameName, string modId)
        {
            if (selected == null)
                return;

            Undo.RecordObject(selected, "Edit Mod ID");
            modId = (modId ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(modId))
            {
                selected.SetModIdForGame(gameName, modId);
            }
            else
            {
                selected.GameModMappings.RemoveAll(g =>
                    string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));
            }

            EditorUtility.SetDirty(selected);
            AssetDatabase.SaveAssets();
        }

        private MapContentPackDefinition GetSelectedPack()
        {
            if (db == null || db.Packs.Count == 0 || list.index < 0 || list.index >= db.Packs.Count)
                return null;

            return db.Packs[list.index];
        }

        private void BuildSelectedPackToCurrentGame(MapContentPackDefinition pack)
        {
            var currentGame = EditorPrefs.GetString("ModIo.CurrentGame", "Custom Folder");
            var buildRoot = ResolveDocumentsMapsFolderForGame(currentGame);

            if (string.IsNullOrEmpty(buildRoot))
                return;

            BuildPacksToDestination(GetPacksForBuild(pack), buildRoot, GetBuildLabelForGame(currentGame));
        }

        private void BuildSelectedPackToMashBox(MapContentPackDefinition pack)
        {
            BuildPacksToDestination(GetPacksForBuild(pack), ResolveMashBoxMapsFolder(), "Mash Box");
        }

        private List<MapContentPackDefinition> GetPacksForBuild(MapContentPackDefinition selectedPack)
        {
            var result = new List<MapContentPackDefinition>();

#if MashBoxDev
            if (db != null)
            {
                result = db.Packs
                    .Where(pack => pack != null && pack.IncludeInBuild && pack.Scene != null)
                    .ToList();
            }
#endif

            if (result.Count == 0 && selectedPack != null && selectedPack.Scene != null)
                result.Add(selectedPack);

            return result;
        }

        private void RunValidationForSelectedPack(MapContentPackDefinition pack, bool forceOpenScene)
        {
            if (pack == null)
            {
                InvalidateValidationResults();
                return;
            }

            lastValidationIssues = MapContentPackValidator.Validate(pack, forceOpenScene);
            validatedPackInstanceId = GetObjectStableId(pack);
        }

        private void InvalidateValidationResults(MapContentPackDefinition pack = null)
        {
            if (pack != null && validatedPackInstanceId != GetObjectStableId(pack))
                return;

            validatedPackInstanceId = null;
            lastValidationIssues = null;
        }

        private void DrawValidationSection(MapContentPackDefinition selectedPack)
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(selectedPack == null || selectedPack.Scene == null))
                {
                    if (GUILayout.Button("Validate", GUILayout.Width(100f)))
                        RunValidationForSelectedPack(selectedPack, forceOpenScene: true);
                }
            }

            if (selectedPack == null || validatedPackInstanceId != GetObjectStableId(selectedPack))
                return;

            var issues = lastValidationIssues ?? new List<MapValidationIssue>();

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Map validation passed.", MessageType.Info);
                return;
            }

            foreach (var issue in issues)
            {
                var messageType = issue.Severity == MapValidationSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning;
                EditorGUILayout.HelpBox(issue.Message, messageType);
            }
        }

        private void BuildPacksToDestination(List<MapContentPackDefinition> packs, string buildRoot, string targetLabel)
        {
            if (packs == null || packs.Count == 0)
            {
                EditorUtility.DisplayDialog("Missing Scene", "Select a map pack with a scene before building.", "OK");
                return;
            }

            var selectedPack = GetSelectedPack();
            var validationErrors = new List<string>();
            foreach (var pack in packs)
            {
                var issues = MapContentPackValidator.Validate(pack, forceOpenScene: true);
                if (selectedPack != null && pack == selectedPack)
                {
                    lastValidationIssues = issues;
                    validatedPackInstanceId = GetObjectStableId(pack);
                }

                validationErrors.AddRange(
                    issues.Where(issue => issue.Severity == MapValidationSeverity.Error)
                        .Select(issue => $"{pack.PackName}: {issue.Message}"));
            }

            if (validationErrors.Count > 0)
            {
                Debug.LogWarning(
                    "[MashBoxMapTools] Building map bundle with validation errors. " +
                    "Local builds are allowed, but publishing to mod.io will remain blocked until these are fixed.\n\n" +
                    string.Join("\n\n", validationErrors.Take(6)) +
                    (validationErrors.Count > 6 ? "\n\nMore issues were found. See the validation panel for the full list." : ""));
            }

            if (string.IsNullOrWhiteSpace(buildRoot))
            {
                EditorUtility.DisplayDialog("Missing Build Folder", "Could not resolve the Maps folder for this target.", "OK");
                return;
            }

            try
            {
                for (var i = 0; i < packs.Count; i++)
                {
                    var pack = packs[i];
                    pack.modioUserToken = ModIoAuth.CurrentToken;
                    pack.PublisherEmail = ModIoAuth.CurrentEmail;
                    EditorUtility.SetDirty(pack);

                    var progress = packs.Count == 1 ? 0.25f : (float)i / packs.Count;
                    EditorUtility.DisplayProgressBar("Build Map Bundle", $"Building {pack.PackName} to {targetLabel}", progress);
                    BuildSingleMapBundle(pack, buildRoot);
                }

                AssetDatabase.SaveAssets();
                EditorUtility.ClearProgressBar();
                var message = packs.Count == 1
                    ? $"Built '{packs[0].PackName}' to {targetLabel}."
                    : $"Built {packs.Count} maps to {targetLabel}.";
                EditorUtility.DisplayDialog("Build Complete", message, "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[MashBoxMapTools] Build to target failed: {ex}");
                EditorUtility.DisplayDialog("Build Failed", ex.Message, "OK");
            }
        }

        private void OpenBuildOutputFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", path.Replace("/", "\\"));
#else
            EditorUtility.RevealInFinder(path);
#endif
        }

        private string ResolveDocumentsMapsFolderForGame(string currentGame)
        {
            if (string.IsNullOrWhiteSpace(currentGame) ||
                string.Equals(currentGame, "Custom Folder", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, GetBuildLabelForGame(currentGame), "Maps").Replace("\\", "/");
        }

        private string ResolveMashBoxMapsFolder()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Mash Box", "Maps").Replace("\\", "/");
        }

        private string GetBuildLabelForGame(string currentGame)
        {
            if (string.Equals(currentGame, "BMXS", StringComparison.OrdinalIgnoreCase))
                return "BMX Streets";

            return string.IsNullOrWhiteSpace(currentGame) ? "Current Game" : currentGame;
        }

        private void BuildSingleMapBundle(MapContentPackDefinition pack, string destinationRoot)
        {
            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                throw new Exception("Could not resolve the selected scene asset.");

            var bundleName = SanitizeBundleName(pack.PackName);
            var tempRoot = Path.Combine(Path.GetTempPath(), "MashBoxSDK", "MapBuild", bundleName, EditorUserBuildSettings.activeBuildTarget.ToString());

            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(destinationRoot);

            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = new[] { scenePath }
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                tempRoot,
                new[] { build },
                GetUnityBundleBuildOptions(),
                EditorUserBuildSettings.activeBuildTarget);

            if (manifest == null)
                throw new Exception("Unity failed to build the map bundle.");

            var mapFolder = Path.Combine(destinationRoot, bundleName + MapFolderSuffix);
            Directory.CreateDirectory(mapFolder);

            var sourceBundlePath = Path.Combine(tempRoot, bundleName);
            var destBundlePath = Path.Combine(mapFolder, bundleName + ".bundle");
            SafeMove(sourceBundlePath, destBundlePath);

            var sourceManifestPath = Path.Combine(tempRoot, bundleName + ".manifest");
            var destManifestPath = Path.Combine(mapFolder, bundleName + ".manifest");
            SafeMove(sourceManifestPath, destManifestPath);

            CopyScreenshot(pack, mapFolder, bundleName);
            WriteChallengeData(pack, mapFolder, bundleName);
            WriteMapsManifestFiles(destinationRoot, mapFolder, bundleName);

            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        private BuildAssetBundleOptions GetUnityBundleBuildOptions()
        {
            switch (compressionMode)
            {
                case CompressionMode.ChunkBasedCompression:
                    return BuildAssetBundleOptions.ChunkBasedCompression;
                case CompressionMode.None:
                    return BuildAssetBundleOptions.UncompressedAssetBundle;
                default:
                    return BuildAssetBundleOptions.None;
            }
        }

        private void WriteMapsManifestFiles(string destinationRoot, string selectedMapFolder, string bundleName)
        {
            if (string.IsNullOrWhiteSpace(selectedMapFolder))
                return;

            Directory.CreateDirectory(selectedMapFolder);

            var versionLogPath = Path.Combine(selectedMapFolder, "version_log.json");
            var versions = LoadVersionDictionary(versionLogPath);
            if (!versions.ContainsKey(bundleName))
                versions[bundleName] = 1;

            var bundlePath = Path.Combine(selectedMapFolder, bundleName + ".bundle");
            var manifestEntries = new SortedDictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [bundleName] = new MapInfo
                {
                    version = versions[bundleName],
                    filename = bundleName + ".bundle",
                    size = File.Exists(bundlePath) ? new FileInfo(bundlePath).Length : 0
                }
            };

            WriteManifestJson(Path.Combine(selectedMapFolder, "manifest.json"), manifestEntries);
            WriteVersionLogJson(versionLogPath, versions);

            var rootManifestPath = Path.Combine(destinationRoot, "manifest.json");
            if (File.Exists(rootManifestPath))
                File.Delete(rootManifestPath);

            var rootVersionLogPath = Path.Combine(destinationRoot, "version_log.json");
            if (File.Exists(rootVersionLogPath))
                File.Delete(rootVersionLogPath);
        }

        private Dictionary<string, int> LoadVersionDictionary(string path)
        {
            var versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return versions;

            var json = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(json, "\"(?<key>[^\"]+)\"\\s*:\\s*(?<value>\\d+)"))
            {
                var key = match.Groups["key"].Value;
                if (string.Equals(key, "versions", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (int.TryParse(match.Groups["value"].Value, out var value))
                    versions[key] = value;
            }

            return versions;
        }

        private void WriteManifestJson(string path, SortedDictionary<string, MapInfo> manifestEntries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"maps\": {");

            var first = true;
            foreach (var pair in manifestEntries)
            {
                if (!first)
                    sb.AppendLine(",");

                sb.Append($"    \"{EscapeJson(pair.Key)}\": {{ \"version\": {pair.Value.version}, \"filename\": \"{EscapeJson(pair.Value.filename)}\", \"size\": {pair.Value.size} }}");
                first = false;
            }

            if (!first)
                sb.AppendLine();

            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        private void WriteVersionLogJson(string path, Dictionary<string, int> versions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"versions\": {");

            var first = true;
            foreach (var pair in versions.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!first)
                    sb.AppendLine(",");

                sb.Append($"    \"{EscapeJson(pair.Key)}\": {pair.Value}");
                first = false;
            }

            if (!first)
                sb.AppendLine();

            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        private void CopyScreenshot(MapContentPackDefinition pack, string destinationFolder, string bundleName)
        {
            var screenshotPath = AssetDatabase.GetAssetPath(pack.Screenshot);
            if (!string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath))
            {
                File.Copy(screenshotPath, Path.Combine(destinationFolder, bundleName + Path.GetExtension(screenshotPath)), true);
                return;
            }

            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            var sceneDirectory = Path.GetDirectoryName(scenePath);
            if (string.IsNullOrWhiteSpace(sceneDirectory))
                return;

            var fallbackPath = Path.Combine(sceneDirectory, pack.Scene.name + ".png");
            if (File.Exists(fallbackPath))
                File.Copy(fallbackPath, Path.Combine(destinationFolder, bundleName + ".png"), true);
        }

        private void WriteChallengeData(MapContentPackDefinition pack, string destinationFolder, string bundleName)
        {
            var scenePath = AssetDatabase.GetAssetPath(pack.Scene);
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            var originalScene = SceneManager.GetActiveScene().path;
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (!scene.IsValid())
                    return;

                var challengesRoot = GameObject.Find("Challenges");
                if (challengesRoot == null)
                    return;

                var data = new ChallengeMapData
                {
                    mapName = string.IsNullOrWhiteSpace(pack.MapName) ? pack.PackName : pack.MapName,
                    categories = new List<ChallengeCategory>(),
                    tasks = ExtractMapTaskData()
                };

                foreach (Transform category in challengesRoot.transform)
                {
                    var items = new List<string>();
                    foreach (Transform child in category)
                        items.Add(child.name);

                    data.categories.Add(new ChallengeCategory
                    {
                        categoryName = category.name,
                        items = items
                    });
                }

                File.WriteAllText(Path.Combine(destinationFolder, $"{bundleName}_Challenges.json"), JsonUtility.ToJson(data, true));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalScene))
                    EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }
        }

        private static string SanitizeBundleName(string raw)
        {
            var safe = SanitizeAssetName(raw).ToLowerInvariant().Replace(" ", "_");
            return string.IsNullOrWhiteSpace(safe) ? "map" : safe;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private void ShowPublishPlatformSelector(MapContentPackDefinition pack, string currentGame)
        {
            if (!EnsureCorrectUnityVersionForPublishing(currentGame))
                return;

            PublishPlatformSelectionPopup.Show(
                this,
                currentGame,
                PublishPlatformOptions,
                selectedPlatforms =>
                {
                    if (selectedPlatforms == null || selectedPlatforms.Count == 0)
                    {
                        EditorUtility.DisplayDialog("No Platforms Selected", "Choose at least one platform before publishing.", "OK");
                        return;
                    }

                    PublishMapToModioAsync(pack, currentGame, selectedPlatforms);
                });
        }

        private async void PublishMapToModioAsync(
            MapContentPackDefinition pack,
            string currentGame,
            IReadOnlyList<PublishPlatformOption> selectedPlatforms,
            bool simulateOnly = false)
        {
            var lightingBefore = simulateOnly ? CaptureMapLightingDebugSnapshot(pack) : null;

            if (!EnsureCorrectUnityVersionForPublishing(currentGame))
                return;

            if (pack == null || pack.Scene == null)
            {
                EditorUtility.DisplayDialog("Missing Scene", "Select a map pack with a scene before publishing.", "OK");
                return;
            }

            if (selectedPlatforms == null || selectedPlatforms.Count == 0)
            {
                EditorUtility.DisplayDialog("No Platforms Selected", "Choose at least one platform before publishing.", "OK");
                return;
            }

            if (isMapPublishInProgress)
            {
                EditorUtility.DisplayDialog("Upload In Progress", "A map upload is already running. Cancel it from the exporter window before starting another one.", "OK");
                return;
            }

            pack.SetPublishTargetGame(currentGame);
            EditorUtility.SetDirty(pack);
            AssetDatabase.SaveAssets();

            if (!await EnsureLatestSdkForPublishingAsync())
                return;

            RunValidationForSelectedPack(pack, forceOpenScene: true);
            var simulationValidationBypassConfirmed = false;

            var blockingIssues = (lastValidationIssues ?? new List<MapValidationIssue>())
                .Where(issue => issue.Severity == MapValidationSeverity.Error)
                .Select(issue => issue.Message)
                .ToList();

            if (blockingIssues.Count > 0)
            {
                if (!simulateOnly)
                {
                    EditorUtility.DisplayDialog("Validation Failed", string.Join("\n\n", blockingIssues), "OK");
                    return;
                }

                if (!ConfirmDebugSimulationValidationBypass(blockingIssues))
                    return;

                simulationValidationBypassConfirmed = true;
            }

            blockingIssues = MapContentPackValidator.Validate(pack, forceOpenScene: true)
                .Where(issue => issue.Severity == MapValidationSeverity.Error)
                .Select(issue => issue.Message)
                .ToList();

            if (blockingIssues.Count > 0)
            {
                if (!simulateOnly)
                {
                    EditorUtility.DisplayDialog("Validation Failed", string.Join("\n\n", blockingIssues), "OK");
                    return;
                }

                if (!simulationValidationBypassConfirmed && !ConfirmDebugSimulationValidationBypass(blockingIssues))
                    return;

                Debug.LogWarning(
                    "[MashBoxMapTools] Debug Mod.io publish simulation is bypassing validation errors:\n" +
                    string.Join("\n\n", blockingIssues),
                    pack);
            }

            var modId = pack.GetModIdForGame(currentGame);
            if (string.IsNullOrWhiteSpace(modId))
            {
                EditorUtility.DisplayDialog(
                    "Missing Mod ID",
                    $"This map pack does not have a Mod ID configured for '{currentGame}'.",
                    "OK");
                return;
            }

            try
            {
                activeMapPublishCts = new CancellationTokenSource();
                var cts = activeMapPublishCts;
                pack.modioUserToken = ModIoAuth.CurrentToken;
                pack.PublisherEmail = ModIoAuth.CurrentEmail;
                pack.SetPublishTargetGame(currentGame);
                EditorUtility.SetDirty(pack);
                AssetDatabase.SaveAssets();

                var uploadRegion = DetermineUploadRegion();
                var uploadRegionLabel = GetUploadRegionDisplayName(uploadRegion);
                SetActiveMapPublishStatus("Exporting unitypackage...", uploadRegionLabel, 0.25f);
                DisplayProgress("Publish Map To Mod.io", "Exporting unitypackage...", 0.25f);
                var packagePath = BuildUnityPackageForMapPack(pack);
                GameTargetUnityVersionValidator.ThrowIfInvalidForPublishing(currentGame);
                if (!pack.IsVanillaContent)
                    EnsurePackageSizeWithinLimit(packagePath, MaxMapPublishPackageBytes, "map");
                var packageBytes = new FileInfo(packagePath).Length;

                if (simulateOnly)
                {
                    // Allow Bakery and other editor scene callbacks, plus our delayed restoration,
                    // to run before comparing the final source-scene state.
                    await WaitForEditorDelayCallAsync();
                    var lightingAfter = CaptureMapLightingDebugSnapshot(pack);
                    var lightingChanged = !MapLightingDebugSnapshot.AreEquivalent(lightingBefore, lightingAfter);
                    var diagnostic =
                        $"Before:\n{lightingBefore?.Describe() ?? "<unavailable>"}\n\n" +
                        $"After:\n{lightingAfter?.Describe() ?? "<unavailable>"}";

                    EditorUtility.ClearProgressBar();
                    if (lightingChanged)
                    {
                        Debug.LogError(
                            $"[MashBoxMapTools] Debug publish simulation changed the source map lighting state.\n{diagnostic}",
                            pack);
                        EditorUtility.DisplayDialog(
                            "Simulation Detected A Lighting Change",
                            "The local Mod.io publish pipeline changed the source map's lighting state. No upload was attempted.\n\n" +
                            diagnostic +
                            "\n\nThe generated package remains at:\n" + packagePath,
                            "OK");
                    }
                    else
                    {
                        Debug.Log(
                            $"[MashBoxMapTools] Debug publish simulation completed without changing the captured source lighting state. Package: {packagePath}\n{diagnostic}",
                            pack);
                        EditorUtility.DisplayDialog(
                            "Mod.io Publish Simulation Complete",
                            "The full local publish pipeline completed and no upload was attempted. The captured source lighting state did not change.\n\n" +
                            diagnostic +
                            "\n\nGenerated package:\n" + packagePath,
                            "OK");
                    }

                    return;
                }

                await UploadMapPackageToContainersAsync(packagePath, packageBytes, uploadRegionLabel, selectedPlatforms, cts.Token);

                SetActiveMapPublishStatus("Finalizing...", uploadRegionLabel, 0.98f);
                DisplayProgress("Publish Map To Mod.io", "Finalizing...", 0.98f);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "Processing Submission",
                    $"Your map '{pack.name}' is processing for {currentGame}.\n\n" +
                    "You will be emailed when it is ready on mod.io.\n\n" +
                    "This may take a few minutes or a few hours.",
                    "OK");
            }
            catch (OperationCanceledException)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Publish Cancelled", "The map upload was cancelled.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[MashBoxMapTools] Publish failed: {ex}");
                EditorUtility.DisplayDialog("Publish Failed", ex.Message, "OK");
            }
            finally
            {
                activeMapPublishCts?.Dispose();
                activeMapPublishCts = null;
                ClearActiveMapPublishStatus();
            }
        }

        private static bool ConfirmDebugSimulationValidationBypass(IReadOnlyList<string> blockingIssues)
        {
            var issues = blockingIssues ?? Array.Empty<string>();
            var shownIssues = string.Join("\n\n", issues.Take(6));
            var hiddenCount = Math.Max(0, issues.Count - 6);
            if (hiddenCount > 0)
                shownIssues += $"\n\n...and {hiddenCount} more validation error{(hiddenCount == 1 ? string.Empty : "s")}.";

            var shouldContinue = EditorUtility.DisplayDialog(
                "Bypass Validation For Debug Simulation?",
                "The map has publishing validation errors. You may bypass them for this local debug simulation only. " +
                "Nothing will be uploaded to mod.io, and normal publishing will remain blocked.\n\n" +
                shownIssues,
                "Continue Simulation",
                "Cancel");

            if (shouldContinue)
            {
                Debug.LogWarning(
                    "[MashBoxMapTools] User chose to bypass validation errors for a local debug Mod.io publish simulation:\n" +
                    string.Join("\n\n", issues));
            }

            return shouldContinue;
        }

        private sealed class MapLightingDebugSnapshot
        {
            public string ScenePath;
            public string AssociatedLightingDataPath;
            public string[] LightingDataDependencies = Array.Empty<string>();
            public int ActiveLightmapCount = -1;
            public string[] ActiveLightmapTextures = Array.Empty<string>();
            public string[] RendererLightmapAssignments = Array.Empty<string>();

            public string Describe()
            {
                var dependencies = LightingDataDependencies.Length == 0
                    ? "<none>"
                    : string.Join(", ", LightingDataDependencies);
                var lightmapCount = ActiveLightmapCount < 0 ? "<scene not active>" : ActiveLightmapCount.ToString();
                var lightmapTextures = ActiveLightmapCount < 0
                    ? "<scene not active>"
                    : ActiveLightmapTextures.Length == 0
                        ? "<none>"
                        : string.Join("\n", ActiveLightmapTextures.Select(entry => "  " + entry));
                var rendererAssignments = RendererLightmapAssignments.Length == 0
                    ? "<none>"
                    : string.Join("\n", RendererLightmapAssignments.Take(20).Select(entry => "  " + entry)) +
                      (RendererLightmapAssignments.Length > 20
                          ? $"\n  ...and {RendererLightmapAssignments.Length - 20} more."
                          : string.Empty);
                return
                    $"Scene: {ScenePath}\n" +
                    $"Associated Lighting Data: {AssociatedLightingDataPath}\n" +
                    $"Lighting Data Dependencies: {dependencies}\n" +
                    $"Active Lightmap Count: {lightmapCount}\n" +
                    $"Active Lightmap Textures:\n{lightmapTextures}\n" +
                    $"Renderer Lightmap Assignments ({RendererLightmapAssignments.Length}):\n{rendererAssignments}";
            }

            public static bool AreEquivalent(MapLightingDebugSnapshot left, MapLightingDebugSnapshot right)
            {
                if (left == null || right == null)
                    return left == right;

                var associatedLightingComparable =
                    !string.Equals(left.AssociatedLightingDataPath, "<scene not loaded>", StringComparison.Ordinal) &&
                    !string.Equals(right.AssociatedLightingDataPath, "<scene not loaded>", StringComparison.Ordinal);
                var activeLightmapsComparable = left.ActiveLightmapCount >= 0 && right.ActiveLightmapCount >= 0;

                return string.Equals(left.ScenePath, right.ScenePath, StringComparison.OrdinalIgnoreCase) &&
                       (!associatedLightingComparable || string.Equals(left.AssociatedLightingDataPath, right.AssociatedLightingDataPath, StringComparison.OrdinalIgnoreCase)) &&
                       (!activeLightmapsComparable || left.ActiveLightmapCount == right.ActiveLightmapCount) &&
                       (!activeLightmapsComparable || left.ActiveLightmapTextures.SequenceEqual(right.ActiveLightmapTextures, StringComparer.OrdinalIgnoreCase)) &&
                       left.RendererLightmapAssignments.SequenceEqual(right.RendererLightmapAssignments, StringComparer.Ordinal) &&
                       left.LightingDataDependencies.SequenceEqual(right.LightingDataDependencies, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Task WaitForEditorDelayCallAsync()
        {
            var completion = new TaskCompletionSource<bool>();
            EditorApplication.delayCall += () => completion.TrySetResult(true);
            return completion.Task;
        }

        private static MapLightingDebugSnapshot CaptureMapLightingDebugSnapshot(MapContentPackDefinition pack)
        {
            var scenePath = NormalizeAssetPath(pack == null ? null : AssetDatabase.GetAssetPath(pack.Scene));
            var snapshot = new MapLightingDebugSnapshot
            {
                ScenePath = string.IsNullOrWhiteSpace(scenePath) ? "<unresolved>" : scenePath,
                AssociatedLightingDataPath = "<scene not loaded>"
            };

            if (string.IsNullOrWhiteSpace(scenePath))
                return snapshot;

            snapshot.LightingDataDependencies = AssetDatabase.GetDependencies(scenePath, true)
                .Select(NormalizeAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && AssetDatabase.LoadAssetAtPath<LightingDataAsset>(path) != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                return snapshot;

            snapshot.RendererLightmapAssignments = CaptureRendererLightmapDebugSignatures(scene);

#if UNITY_6000_0_OR_NEWER
            var lightingData = Lightmapping.GetLightingDataAssetForScene(scene);
#else
            var lightingData = SceneManager.GetActiveScene() == scene ? Lightmapping.lightingDataAsset : null;
#endif
            snapshot.AssociatedLightingDataPath = lightingData == null
                ? "<none>"
                : NormalizeAssetPath(AssetDatabase.GetAssetPath(lightingData));

            if (SceneManager.GetActiveScene() == scene)
            {
                var activeLightmaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                snapshot.ActiveLightmapCount = activeLightmaps.Length;
                snapshot.ActiveLightmapTextures = activeLightmaps
                    .Select((lightmap, index) =>
                        $"[{index}] color={GetDebugTextureAssetPath(lightmap?.lightmapColor)}, " +
                        $"directional={GetDebugTextureAssetPath(lightmap?.lightmapDir)}, " +
                        $"shadowMask={GetDebugTextureAssetPath(lightmap?.shadowMask)}")
                    .ToArray();
            }

            return snapshot;
        }

        private static string[] CaptureRendererLightmapDebugSignatures(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return Array.Empty<string>();

            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null &&
                                   (IsAssignedLightmapIndex(renderer.lightmapIndex) ||
                                    IsAssignedLightmapIndex(renderer.realtimeLightmapIndex)))
                .Select(renderer =>
                    $"{GetHierarchyPath(renderer.transform)} ({renderer.GetType().Name}): " +
                    $"bakedIndex={renderer.lightmapIndex}, bakedST={FormatDebugVector4(renderer.lightmapScaleOffset)}, " +
                    $"realtimeIndex={renderer.realtimeLightmapIndex}, realtimeST={FormatDebugVector4(renderer.realtimeLightmapScaleOffset)}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string FormatDebugVector4(Vector4 value)
        {
            return $"({value.x:R},{value.y:R},{value.z:R},{value.w:R})";
        }

        private static bool IsAssignedLightmapIndex(int index)
        {
            return index >= 0 && index < 0xFFFE;
        }

        private static string GetDebugTextureAssetPath(Texture texture)
        {
            if (texture == null)
                return "<none>";

            var path = NormalizeAssetPath(AssetDatabase.GetAssetPath(texture));
            return string.IsNullOrWhiteSpace(path) ? $"<non-asset:{texture.name}>" : path;
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

        private async Task UploadMapPackageToContainersAsync(
            string packagePath,
            long packageBytes,
            string uploadRegionLabel,
            IReadOnlyList<PublishPlatformOption> selectedPlatforms,
            CancellationToken cancellationToken)
        {
            var containers = selectedPlatforms?.ToArray() ?? Array.Empty<PublishPlatformOption>();
            if (containers.Length == 0)
                throw new InvalidOperationException("No publish platforms were selected.");

            if (packageBytes >= SequentialContainerUploadThresholdBytes)
            {
                for (var i = 0; i < containers.Length; i++)
                {
                    var container = containers[i];
                    await UploadToContainer(
                        packagePath,
                        container.Container,
                        CreateSequentialContainerProgressReporter(i, containers.Length, container.DisplayName, packageBytes, uploadRegionLabel),
                        cancellationToken);
                }

                return;
            }

            var progresses = new float[containers.Length];
            void ReportCombinedProgress()
            {
                var combined = progresses.Average();
                var uploadedBytes = (long)Math.Round(packageBytes * combined);
                var status = $"Uploading all platforms... {(int)(combined * 100f)}% ({FormatBytes(uploadedBytes)} / {FormatBytes(packageBytes)})";
                var displayProgress = 0.45f + combined * 0.50f;
                SetActiveMapPublishStatus(status, uploadRegionLabel, displayProgress);
                DisplayProgress("Publish Map To Mod.io", $"{status}\nRegion: {uploadRegionLabel}", displayProgress);
            }

            var tasks = containers
                .Select((container, index) => UploadToContainer(
                    packagePath,
                    container.Container,
                    new Progress<float>(p =>
                    {
                        progresses[index] = p;
                        ReportCombinedProgress();
                    }),
                    cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
        }

        private Progress<float> CreateSequentialContainerProgressReporter(
            int containerIndex,
            int containerCount,
            string containerLabel,
            long packageBytes,
            string uploadRegionLabel)
        {
            return new Progress<float>(p =>
            {
                var completedContainers = containerIndex;
                var overallProgress = (completedContainers + Mathf.Clamp01(p)) / containerCount;
                var uploadedBytes = (long)Math.Round(packageBytes * Mathf.Clamp01(p));
                var status = $"Uploading {containerLabel} ({containerIndex + 1}/{containerCount})... {(int)(p * 100f)}% ({FormatBytes(uploadedBytes)} / {FormatBytes(packageBytes)})";
                var displayProgress = 0.45f + overallProgress * 0.50f;
                SetActiveMapPublishStatus(status, uploadRegionLabel, displayProgress);
                DisplayProgress("Publish Map To Mod.io", $"{status}\nRegion: {uploadRegionLabel}", displayProgress);
            });
        }

        private void ExportDebugUnityPackage(MapContentPackDefinition pack)
        {
            if (pack == null)
            {
                EditorUtility.DisplayDialog("No Map Selected", "Select a map pack before exporting a debug package.", "OK");
                return;
            }

            try
            {
                var tempPackagePath = BuildUnityPackageForMapPack(pack);
                var defaultName = Path.GetFileName(tempPackagePath);
                var savePath = EditorUtility.SaveFilePanel(
                    "Save Debug Map Package",
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
                Debug.LogError($"[MashBoxMapTools] Debug package export failed: {ex}");
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }

        private static string BuildUnityPackageForMapPack(MapContentPackDefinition pack)
        {
            if (pack == null)
                throw new ArgumentNullException(nameof(pack));

            // Protect the loaded source scene for the entire export, including deletion of the
            // temporary copied scene. Bakery can process that deletion during AssetDatabase.Refresh
            // and clear serialized renderer lightmap assignments after the copied scene was closed.
            var sourceLightingState = SourceSceneLightingState.Capture(pack);
            pack.StampMashBoxSdkVersion();
            AssetDatabase.SaveAssets();

            MapPackageExportContext exportContext = null;
            try
            {
                exportContext = CreateMapPackageExportContext(pack);
                var export = CollectExportPaths(exportContext);
                if (export.Count == 0)
                    throw new Exception("No assets were found to export for this map pack.");

                var outPath = TempPackagePathFor(pack.name);
                EditorUtility.DisplayProgressBar("Exporting Package",
                    $"Creating {Path.GetFileName(outPath)} ({export.Count} items)...", 0.5f);
                AssetDatabase.ExportPackage(export.ToArray(), outPath, ExportPackageOptions.Default);
                AssetDatabase.Refresh();

                if (!File.Exists(outPath))
                    throw new FileNotFoundException("Export failed.", outPath);

                EditorUtility.ClearProgressBar();
                return outPath;
            }
            finally
            {
                exportContext?.Cleanup();
                sourceLightingState?.Restore(refreshBakery: true);
                sourceLightingState?.QueueDeferredRestores(4);
                EditorUtility.ClearProgressBar();
            }
        }

        private sealed class MapPackageExportContext
        {
            public MapContentPackDefinition Pack { get; }
            public string PackAssetPath { get; }
            public string SceneAssetPath { get; }
            private readonly string tempRoot;

            public MapPackageExportContext(MapContentPackDefinition pack, string tempRoot, string packAssetPath, string sceneAssetPath)
            {
                Pack = pack;
                this.tempRoot = tempRoot;
                PackAssetPath = NormalizeAssetPath(packAssetPath);
                SceneAssetPath = NormalizeAssetPath(sceneAssetPath);
            }

            public void Cleanup()
            {
                if (string.IsNullOrWhiteSpace(tempRoot))
                    return;

                if (AssetDatabase.IsValidFolder(tempRoot))
                    AssetDatabase.DeleteAsset(tempRoot);

                AssetDatabase.Refresh();
            }
        }

        private static MapPackageExportContext CreateMapPackageExportContext(MapContentPackDefinition sourcePack)
        {
            var sourceScenePath = AssetDatabase.GetAssetPath(sourcePack.Scene)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(sourceScenePath))
                throw new Exception("Map scene asset path could not be resolved.");

            var tempRoot = CreateMapExportTempRoot(sourcePack.PackName);
            try
            {
                var tempScenePath = $"{tempRoot}/{Path.GetFileName(sourceScenePath)}";
                if (!AssetDatabase.CopyAsset(sourceScenePath, tempScenePath))
                    throw new Exception($"Could not create temporary export scene at '{tempScenePath}'.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                FreezeSceneMeshesIntoAssets(tempScenePath, tempRoot);

                var tempScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(tempScenePath);
                if (tempScene == null)
                    throw new Exception("Temporary export scene could not be loaded.");

                var tempPack = ScriptableObject.CreateInstance<MapContentPackDefinition>();
                tempPack.name = sourcePack.name;
                tempPack.MapName = sourcePack.MapName;
                tempPack.Scene = tempScene;
                tempPack.Screenshot = sourcePack.Screenshot;
                tempPack.IncludeInBuild = sourcePack.IncludeInBuild;
                tempPack.BuildToCustomFolder = sourcePack.BuildToCustomFolder;
                tempPack.modioUserToken = sourcePack.modioUserToken;
                tempPack.PublisherEmail = sourcePack.PublisherEmail;
                tempPack.MashBoxSdkVersion = string.IsNullOrWhiteSpace(sourcePack.MashBoxSdkVersion)
                    ? MashBoxSDK.ContentTools.ContentPackDefinition.ResolveMashBoxSdkVersion()
                    : sourcePack.MashBoxSdkVersion;
                tempPack.GameModMappings = sourcePack.GameModMappings?
                    .Select(mapping => new MashBoxSDK.ContentTools.ContentPackDefinition.GameModMapping
                    {
                        GameName = mapping.GameName,
                        ModId = mapping.ModId,
                        IsPublishTarget = mapping.IsPublishTarget
                    })
                    .ToList() ?? new List<MashBoxSDK.ContentTools.ContentPackDefinition.GameModMapping>();

                var tempPackPath = $"{tempRoot}/{SanitizeAssetName(sourcePack.name)}.asset";
                AssetDatabase.CreateAsset(tempPack, tempPackPath);
                EditorUtility.SetDirty(tempPack);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var resolvedTempPackPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(tempPack));
                Debug.Log($"[MashBoxMapTools] Created temporary map export pack with frozen mesh assets at '{tempRoot}'. Pack: '{resolvedTempPackPath}', Scene: '{tempScenePath}'.");
                return new MapPackageExportContext(tempPack, tempRoot, resolvedTempPackPath, tempScenePath);
            }
            catch
            {
                if (AssetDatabase.IsValidFolder(tempRoot))
                    AssetDatabase.DeleteAsset(tempRoot);

                AssetDatabase.Refresh();
                throw;
            }
        }

        private static string CreateMapExportTempRoot(string packName)
        {
            const string parent = "Assets/__MashBoxMapExportTemp";
            EnsureAssetFolder("Assets", "__MashBoxMapExportTemp");

            var safeName = SanitizeAssetName(string.IsNullOrWhiteSpace(packName) ? "Map" : packName);
            var safePrefix = safeName.Length > 40 ? safeName.Substring(0, 40) : safeName;
            var folderName = $"{safePrefix}_{Guid.NewGuid():N}";

            var guid = AssetDatabase.CreateFolder(parent, folderName);
            if (string.IsNullOrWhiteSpace(guid))
                throw new Exception("Could not create temporary map export folder.");

            return AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
        }

        private static void FreezeSceneMeshesIntoAssets(string scenePath, string tempRoot)
        {
            var previousActiveScene = SceneManager.GetActiveScene();
            var previousLightmaps = CloneLightmapData(LightmapSettings.lightmaps);
            var previousLightmapsMode = LightmapSettings.lightmapsMode;
            var previousLightProbes = LightmapSettings.lightProbes;
            var previousRendererLightmaps = CaptureRendererLightmapState(previousActiveScene);
#if !UNITY_6000_0_OR_NEWER
            var previousLightingData = previousActiveScene.IsValid() && previousActiveScene.isLoaded
                ? Lightmapping.lightingDataAsset
                : null;
#endif
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var meshFolder = $"{tempRoot}/Meshes";
            EnsureAssetFolder(tempRoot, "Meshes");

            var frozenMeshes = new Dictionary<Mesh, Mesh>();
            var frozenReferenceCount = 0;

            void RestorePreviousSceneLightingState()
            {
                if (!previousActiveScene.IsValid() || !previousActiveScene.isLoaded)
                    return;

                // Do not replace global lighting if the user deliberately changed scenes while
                // an upload was in progress. Renderer assignments are still safe to restore.
                if (SceneManager.GetActiveScene() != previousActiveScene)
                {
                    RestoreRendererLightmapState(previousRendererLightmaps);
                    return;
                }

#if !UNITY_6000_0_OR_NEWER
                Lightmapping.lightingDataAsset = previousLightingData;
#endif
                LightmapSettings.lightmapsMode = previousLightmapsMode;
                LightmapSettings.lightmaps = CloneLightmapData(previousLightmaps);
                LightmapSettings.lightProbes = previousLightProbes;
                RestoreRendererLightmapState(previousRendererLightmaps);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }

            try
            {
                if (!SceneManager.SetActiveScene(scene))
                    throw new Exception($"Could not activate temporary export scene '{scenePath}' while preparing baked lighting.");

                CopyAndRebindLightingDataForTemporaryScene(scene, scenePath, tempRoot);

                ThrowIfSceneHasMissingPrefabInstances(scene,
                    "Cannot export this map because the copied export scene contains missing prefab reference(s). Restore or replace the missing prefab instances in the source scene before publishing.");

                UnpackPrefabInstancesInTemporaryScene(scene);

                ThrowIfSceneHasMissingPrefabInstances(scene,
                    "Cannot export this map because missing prefab reference(s) remain after preparing the temporary export scene.");

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (meshFilter == null || meshFilter.sharedMesh == null)
                            continue;

                        var meshRenderer = meshFilter.GetComponent<MeshRenderer>();
                        var originalMaterials = CaptureRendererMaterials(meshRenderer);
                        var frozenMesh = GetOrCreateFrozenMesh(meshFilter.sharedMesh, meshFolder, frozenMeshes);

                        meshFilter.sharedMesh = frozenMesh;
                        RestoreRendererMaterials(meshRenderer, originalMaterials);
                        EditorUtility.SetDirty(meshFilter);
                        frozenReferenceCount++;
                    }

                    foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
                            continue;

                        var originalMaterials = CaptureRendererMaterials(skinnedMeshRenderer);
                        var frozenMesh = GetOrCreateFrozenMesh(skinnedMeshRenderer.sharedMesh, meshFolder, frozenMeshes);

                        skinnedMeshRenderer.sharedMesh = frozenMesh;
                        RestoreRendererMaterials(skinnedMeshRenderer, originalMaterials);
                        EditorUtility.SetDirty(skinnedMeshRenderer);
                        frozenReferenceCount++;
                    }

                    foreach (var meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (meshCollider == null || meshCollider.sharedMesh == null)
                            continue;

                        meshCollider.sharedMesh = GetOrCreateFrozenMesh(meshCollider.sharedMesh, meshFolder, frozenMeshes);
                        EditorUtility.SetDirty(meshCollider);
                        frozenReferenceCount++;
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MashBoxMapTools] Frozen {frozenMeshes.Count} unique mesh asset(s) across {frozenReferenceCount} scene mesh reference(s) for map export.");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);

                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    // Closing an additive temporary scene can clear the global LightmapSettings array.
                    // Bakery-backed scenes commonly have populated LightmapSettings without a Unity
                    // LightingDataAsset, so restoring only Lightmapping.lightingDataAsset is insufficient.
                    SceneManager.SetActiveScene(previousActiveScene);
                    RestorePreviousSceneLightingState();

                    // Bakery can update renderer/lightmap state from a delayed scene-close callback.
                    // Queue our restoration after callbacks registered during the temporary close.
                    EditorApplication.delayCall += RestorePreviousSceneLightingState;
                }
            }
        }

        private sealed class RendererLightmapState
        {
            public Renderer Renderer;
            public int LightmapIndex;
            public Vector4 LightmapScaleOffset;
            public int RealtimeLightmapIndex;
            public Vector4 RealtimeLightmapScaleOffset;
        }

        private sealed class SourceSceneLightingState
        {
            private readonly Scene scene;
            private readonly LightmapData[] lightmaps;
            private readonly LightmapsMode lightmapsMode;
            private readonly LightProbes lightProbes;
            private readonly RendererLightmapState[] rendererLightmaps;
            private readonly bool usesBakery;
#if !UNITY_6000_0_OR_NEWER
            private readonly LightingDataAsset lightingData;
#endif

            private SourceSceneLightingState(Scene sourceScene)
            {
                scene = sourceScene;
                lightmaps = CloneLightmapData(LightmapSettings.lightmaps);
                lightmapsMode = LightmapSettings.lightmapsMode;
                lightProbes = LightmapSettings.lightProbes;
                rendererLightmaps = CaptureRendererLightmapState(sourceScene);
                usesBakery = SceneContainsBakeryLightmapStorage(sourceScene);
#if !UNITY_6000_0_OR_NEWER
                lightingData = Lightmapping.lightingDataAsset;
#endif
            }

            public static SourceSceneLightingState Capture(MapContentPackDefinition pack)
            {
                var scenePath = NormalizeAssetPath(pack == null ? null : AssetDatabase.GetAssetPath(pack.Scene));
                if (string.IsNullOrWhiteSpace(scenePath))
                    return null;

                var sourceScene = SceneManager.GetSceneByPath(scenePath);
                if (!sourceScene.IsValid() || !sourceScene.isLoaded || SceneManager.GetActiveScene() != sourceScene)
                    return null;

                return new SourceSceneLightingState(sourceScene);
            }

            public void Restore(bool refreshBakery)
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    return;

                // Never replace global lighting after the user deliberately switches scenes.
                if (SceneManager.GetActiveScene() != scene)
                    return;

                if (refreshBakery && usesBakery)
                    TryRefreshBakeryLightmaps();

#if !UNITY_6000_0_OR_NEWER
                Lightmapping.lightingDataAsset = lightingData;
#endif
                LightmapSettings.lightmapsMode = lightmapsMode;
                LightmapSettings.lightmaps = CloneLightmapData(lightmaps);
                LightmapSettings.lightProbes = lightProbes;
                RestoreRendererLightmapState(rendererLightmaps);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }

            public void QueueDeferredRestores(int passCount)
            {
                var remaining = Math.Max(0, passCount);
                EditorApplication.CallbackFunction restoreNext = null;
                restoreNext = () =>
                {
                    if (remaining-- <= 0)
                        return;

                    Restore(refreshBakery: false);
                    if (remaining > 0)
                        EditorApplication.delayCall += restoreNext;
                };

                if (remaining > 0)
                    EditorApplication.delayCall += restoreNext;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool SceneContainsBakeryLightmapStorage(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            var storageType = FindLoadedType("ftLightmapsStorage");
            if (storageType == null || !typeof(Component).IsAssignableFrom(storageType))
                return false;

            return scene.GetRootGameObjects()
                .Any(root => root != null && root.GetComponentInChildren(storageType, true) != null);
        }

        private static bool TryRefreshBakeryLightmaps()
        {
            var lightmapsType = FindLoadedType("ftLightmaps");
            var refreshFull = lightmapsType?.GetMethod(
                "RefreshFull",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (refreshFull == null)
            {
                Debug.LogWarning(
                    "[MashBoxMapTools] The source scene contains Bakery lightmap storage, but ftLightmaps.RefreshFull() could not be found. " +
                    "Falling back to captured Unity lightmap and renderer state.");
                return false;
            }

            try
            {
                refreshFull.Invoke(null, null);
                Debug.Log("[MashBoxMapTools] Refreshed Bakery lightmaps after closing the temporary export scene.");
                return true;
            }
            catch (Exception ex)
            {
                var cause = ex.GetBaseException();
                Debug.LogWarning(
                    $"[MashBoxMapTools] Bakery ftLightmaps.RefreshFull() failed after closing the temporary export scene: {cause.Message}");
                return false;
            }
        }

        private static RendererLightmapState[] CaptureRendererLightmapState(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return Array.Empty<RendererLightmapState>();

            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null)
                .Select(renderer => new RendererLightmapState
                {
                    Renderer = renderer,
                    LightmapIndex = renderer.lightmapIndex,
                    LightmapScaleOffset = renderer.lightmapScaleOffset,
                    RealtimeLightmapIndex = renderer.realtimeLightmapIndex,
                    RealtimeLightmapScaleOffset = renderer.realtimeLightmapScaleOffset
                })
                .ToArray();
        }

        private static void RestoreRendererLightmapState(IEnumerable<RendererLightmapState> states)
        {
            foreach (var state in states ?? Enumerable.Empty<RendererLightmapState>())
            {
                var renderer = state?.Renderer;
                if (renderer == null)
                    continue;

                renderer.lightmapIndex = state.LightmapIndex;
                renderer.lightmapScaleOffset = state.LightmapScaleOffset;
                renderer.realtimeLightmapIndex = state.RealtimeLightmapIndex;
                renderer.realtimeLightmapScaleOffset = state.RealtimeLightmapScaleOffset;

                // Runtime setters alone can be overwritten by editor scene/import callbacks. Write
                // Unity's serialized renderer fields as well so the Inspector and scene state retain
                // the exact Bakery assignment captured before export.
                var serializedRenderer = new SerializedObject(renderer);
                SetSerializedInt(serializedRenderer, "m_LightmapIndex", state.LightmapIndex);
                SetSerializedVector4(serializedRenderer, "m_LightmapTilingOffset", state.LightmapScaleOffset);
                SetSerializedInt(serializedRenderer, "m_DynamicLightmapIndex", state.RealtimeLightmapIndex);
                SetSerializedVector4(serializedRenderer, "m_DynamicLightmapTilingOffset", state.RealtimeLightmapScaleOffset);
                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void SetSerializedInt(SerializedObject target, string propertyName, int value)
        {
            var property = target?.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Integer)
                property.intValue = value;
        }

        private static void SetSerializedVector4(SerializedObject target, string propertyName, Vector4 value)
        {
            var property = target?.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Vector4)
                property.vector4Value = value;
        }

        private static LightmapData[] CloneLightmapData(IReadOnlyList<LightmapData> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<LightmapData>();

            var clone = new LightmapData[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null)
                    continue;

                clone[i] = new LightmapData
                {
                    lightmapColor = entry.lightmapColor,
                    lightmapDir = entry.lightmapDir,
                    shadowMask = entry.shadowMask
                };
            }

            return clone;
        }

        private static void CopyAndRebindLightingDataForTemporaryScene(Scene scene, string scenePath, string tempRoot)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var sourceLightingData = Lightmapping.lightingDataAsset;
            if (sourceLightingData == null)
                return;

            var sourceLightingDataPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceLightingData));
            if (string.IsNullOrWhiteSpace(sourceLightingDataPath))
                throw new Exception("The map uses baked lighting, but its Lighting Data Asset path could not be resolved.");

            var extension = Path.GetExtension(sourceLightingDataPath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".asset";

            var lightingDataPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{tempRoot}/{SanitizeAssetName(Path.GetFileNameWithoutExtension(sourceLightingDataPath))}_LightingData{extension}");

            if (!AssetDatabase.CopyAsset(sourceLightingDataPath, lightingDataPath))
                throw new Exception($"Could not copy the map Lighting Data Asset from '{sourceLightingDataPath}' for export.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var copiedLightingData = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(lightingDataPath);
            var temporarySceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (copiedLightingData == null || temporarySceneAsset == null)
                throw new Exception("Could not load the copied Lighting Data Asset or temporary scene while preparing the map export.");

            var serializedLightingData = new SerializedObject(copiedLightingData);
            var targetSceneProperty = serializedLightingData.FindProperty("m_Scene");
            if (targetSceneProperty == null || targetSceneProperty.propertyType != SerializedPropertyType.ObjectReference)
            {
                throw new Exception(
                    "The map uses baked lighting, but this Unity version does not expose the Lighting Data Asset scene link expected by the exporter. " +
                    "Publishing was stopped to avoid uploading incompatible lighting data.");
            }

            targetSceneProperty.objectReferenceValue = temporarySceneAsset;
            serializedLightingData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(copiedLightingData);

#if UNITY_6000_0_OR_NEWER
            Lightmapping.SetLightingDataAssetForScene(scene, copiedLightingData);
#else
            Lightmapping.lightingDataAsset = copiedLightingData;
#endif
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

#if UNITY_6000_0_OR_NEWER
            if (Lightmapping.GetLightingDataAssetForScene(scene) != copiedLightingData)
#else
            if (Lightmapping.lightingDataAsset != copiedLightingData)
#endif
                throw new Exception("Unity did not retain the copied Lighting Data Asset on the temporary export scene.");

            Debug.Log(
                $"[MashBoxMapTools] Copied and rebound baked lighting data '{lightingDataPath}' to temporary export scene '{scenePath}'.");
        }

        private static void ThrowIfSceneHasMissingPrefabInstances(Scene scene, string message)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var missingPrefabPaths = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform != null && HasMissingPrefabAsset(transform.gameObject))
                .Select(transform => GetHierarchyPath(transform))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (missingPrefabPaths.Count == 0)
                return;

            var shownPaths = string.Join("\n", missingPrefabPaths.Take(12).Select(path => $"- {path}"));
            var hiddenCount = missingPrefabPaths.Count - 12;
            var hiddenMessage = hiddenCount > 0 ? $"\n...and {hiddenCount} more." : string.Empty;
            throw new Exception($"{message}\n\nMissing prefabs:\n{shownPaths}{hiddenMessage}");
        }

        private static bool HasMissingPrefabAsset(GameObject gameObject)
        {
            if (gameObject == null)
                return false;

            try
            {
                return PrefabUtility.IsPartOfAnyPrefab(gameObject) && PrefabUtility.IsPrefabAssetMissing(gameObject);
            }
            catch
            {
                return false;
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<missing object>";

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static void UnpackPrefabInstancesInTemporaryScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var prefabRoots = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject))
                .Where(root => root != null && root.scene == scene)
                .Distinct()
                .ToList();

            foreach (var prefabRoot in prefabRoots)
            {
                if (prefabRoot == null || !PrefabUtility.IsAnyPrefabInstanceRoot(prefabRoot))
                    continue;

                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                EditorUtility.SetDirty(prefabRoot);
            }
        }

        private static Material[] CaptureRendererMaterials(Renderer renderer)
        {
            return renderer == null ? null : renderer.sharedMaterials?.ToArray();
        }

        private static void RestoreRendererMaterials(Renderer renderer, Material[] materials)
        {
            if (renderer == null || materials == null)
                return;

            renderer.sharedMaterials = materials.ToArray();
            EditorUtility.SetDirty(renderer);
        }

        private static Mesh GetOrCreateFrozenMesh(Mesh sourceMesh, string meshFolder, Dictionary<Mesh, Mesh> frozenMeshes)
        {
            if (frozenMeshes.TryGetValue(sourceMesh, out var frozenMesh))
                return frozenMesh;

            frozenMesh = UnityEngine.Object.Instantiate(sourceMesh);
            frozenMesh.name = string.IsNullOrWhiteSpace(sourceMesh.name) ? "FrozenMesh" : sourceMesh.name;

            var assetName = SanitizeAssetName(frozenMesh.name);
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{meshFolder}/{assetName}.asset");
            AssetDatabase.CreateAsset(frozenMesh, assetPath);
            frozenMeshes[sourceMesh] = frozenMesh;
            return frozenMesh;
        }

        private static void EnsureAssetFolder(string parent, string folderName)
        {
            var path = $"{parent.TrimEnd('/')}/{folderName}";
            if (AssetDatabase.IsValidFolder(path))
                return;

            var guid = AssetDatabase.CreateFolder(parent, folderName);
            if (string.IsNullOrWhiteSpace(guid))
                throw new Exception($"Could not create asset folder '{path}'.");
        }
        private static List<string> CollectExportPaths(MapPackageExportContext context)
        {
            if (context == null || context.Pack == null)
                throw new ArgumentNullException(nameof(context));

            var pack = context.Pack;
            var roots = new List<string>();
            AddExportPath(roots, context.PackAssetPath);
            AddExportPath(roots, context.SceneAssetPath);
            AddExportPath(roots, AssetDatabase.GetAssetPath(pack.Screenshot));

            var all = roots
                .Concat(AssetDatabase.GetDependencies(roots.ToArray(), true))
                .Select(NormalizeAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => IsExportableMapPackagePath(path, context))
                .ToList();

            if (!all.Contains(context.PackAssetPath, StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Temporary map pack was not included in the exported package: {context.PackAssetPath}");

            if (!all.Contains(context.SceneAssetPath, StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Temporary map scene was not included in the exported package: {context.SceneAssetPath}");

            Debug.Log($"[MashBoxMapTools] Unity package export is scoped to map pack '{context.PackAssetPath}' and scene '{context.SceneAssetPath}' ({all.Count} assets). Other map packs/scenes are excluded.");
            return all;
        }

        private static bool IsExportableMapPackagePath(string path, MapPackageExportContext context)
        {
            if (!IsExportableUnityPackagePath(path))
                return false;

            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return string.Equals(path, context.SceneAssetPath, StringComparison.OrdinalIgnoreCase);

            var mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (mainAssetType == typeof(MapContentPackDefinition))
                return string.Equals(path, context.PackAssetPath, StringComparison.OrdinalIgnoreCase);

            return true;
        }

        private static bool IsExportableUnityPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/Editor/");
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }


        private static void AddExportPath(List<string> paths, string path)
        {
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        private static string TempPackagePathFor(string packName)
        {
            var tempDir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Temp");
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            return Path.Combine(tempDir, $"RemoteCook_{SanitizeAssetName(packName)}.unitypackage");
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

        [Serializable]
        private class UploadRequest
        {
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

        [Serializable]
        private class UploadResponse
        {
            public string jobId;
            public string uploadUrl;
        }

        private async Task UploadToContainer(string packagePath, string container, IProgress<float> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(packagePath);
            var (_, uploadUrl) = await RequestUploadUrlAsync(fileName, container);

            try
            {
                await UploadFileToSasAsync(packagePath, uploadUrl, progress, cancellationToken);
            }
            catch (Exception ex) when (LooksLikeSasClockWindowIssue(ex))
            {
                Debug.LogWarning($"[MashBoxMapTools] Upload URL had an invalid SAS time window. Requesting a fresh upload URL and retrying once. Details: {ex.Message}");
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                var (_, refreshedUploadUrl) = await RequestUploadUrlAsync(fileName, container);
                await UploadFileToSasAsync(packagePath, refreshedUploadUrl, progress, cancellationToken);
            }
        }

        private static async Task<(string jobId, string uploadUrl)> RequestUploadUrlAsync(string fileName, string container)
        {
            var region = DetermineUploadRegion();
            var json = JsonUtility.ToJson(new UploadRequest
            {
                fileName = fileName,
                container = container,
                region = region
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, UploaderEndpoint)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

            using var res = await SharedHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
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

        private static string GetUploadRegionDisplayName(string region)
        {
            return string.Equals(region, "eu", StringComparison.OrdinalIgnoreCase)
                ? "UK South"
                : "Central US";
        }

        private void SetActiveMapPublishStatus(string status, string region, float progress)
        {
            isMapPublishInProgress = true;
            activeMapPublishStatus = status ?? string.Empty;
            activeMapPublishRegion = region ?? string.Empty;
            activeMapPublishProgress = Mathf.Clamp01(progress);
            Repaint();
        }

        private void ClearActiveMapPublishStatus()
        {
            isMapPublishInProgress = false;
            activeMapPublishStatus = string.Empty;
            activeMapPublishRegion = string.Empty;
            activeMapPublishProgress = 0f;
            Repaint();
        }

        private void CancelActiveMapPublish()
        {
            if (activeMapPublishCts == null || activeMapPublishCts.IsCancellationRequested)
                return;

            activeMapPublishCts.Cancel();
            SetActiveMapPublishStatus("Cancelling upload...", activeMapPublishRegion, activeMapPublishProgress);
        }

        internal readonly struct PublishPlatformOption
        {
            public PublishPlatformOption(string container, string displayName)
            {
                Container = container;
                DisplayName = displayName;
            }

            public string Container { get; }
            public string DisplayName { get; }
        }

        private sealed class ProgressStreamContent : HttpContent
        {
            private readonly Stream source;
            private readonly int bufferSize;
            private readonly IProgress<float> progress;
            private readonly long contentLength;
            private readonly int minMsBetweenReports;

            public ProgressStreamContent(Stream source, int bufferSize, IProgress<float> progress, int minMsBetweenReports = 150)
            {
                this.source = source ?? throw new ArgumentNullException(nameof(source));
                this.bufferSize = Mathf.Max(8 * 1024, bufferSize);
                this.progress = progress;
                this.minMsBetweenReports = minMsBetweenReports;
                contentLength = source.CanSeek ? source.Length : -1;
                if (contentLength >= 0)
                    Headers.ContentLength = contentLength;
                Headers.Add("x-ms-blob-type", "BlockBlob");
            }

            protected override async Task SerializeToStreamAsync(Stream target, TransportContext context)
            {
                var buffer = new byte[bufferSize];
                long uploaded = 0;
                var lastReport = Environment.TickCount;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    uploaded += read;

                    if (contentLength > 0 && progress != null)
                    {
                        var now = Environment.TickCount;
                        if (now - lastReport >= minMsBetweenReports)
                        {
                            progress.Report((float)uploaded / contentLength);
                            lastReport = now;
                        }
                    }
                }

                if (contentLength > 0 && progress != null)
                    progress.Report(1f);
            }

            protected override bool TryComputeLength(out long length)
            {
                if (contentLength >= 0)
                {
                    length = contentLength;
                    return true;
                }

                length = -1;
                return false;
            }
        }

        private static async Task UploadFileToSasAsync(string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            var servicePoint = System.Net.ServicePointManager.FindServicePoint(new Uri(uploadUrl));
            servicePoint.ConnectionLimit = Math.Max(servicePoint.ConnectionLimit, 64);
            servicePoint.Expect100Continue = false;

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
                Debug.LogWarning($"[MashBoxMapTools] Block upload was rejected by the SAS URL. Falling back to a direct blob upload. Details: {ex.Message}");
                await UploadFileSinglePutWithRetryAsync(http, filePath, uploadUrl, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task UploadFileSinglePutAsync(HttpClient http, string filePath, string uploadUrl, IProgress<float> progress, CancellationToken cancellationToken)
        {
            using var fileStream = File.OpenRead(filePath);
            using var content = new ProgressStreamContent(fileStream, 4 * 1024 * 1024, progress, 300);
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
            var blockIds = new List<string>();

            using var fileStream = File.OpenRead(filePath);
            var totalLength = fileStream.Length;
            var maxParallelUploads = GetRecommendedParallelBlockUploads(totalLength);
            var inFlightUploads = new List<Task>(maxParallelUploads);
            var buffer = new byte[blockSize];
            long uploaded = 0;
            var blockIndex = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockIndex.ToString("D6")));
                blockIds.Add(blockId);
                blockIndex++;

                var blockData = new byte[read];
                Buffer.BlockCopy(buffer, 0, blockData, 0, read);

                inFlightUploads.Add(UploadBlockWithRetryAsync(http, uploadUrl, blockData, blockId, cancellationToken,
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

        private static int GetRecommendedParallelBlockUploads(long totalLength)
        {
            if (totalLength >= 3L * 1024L * 1024L * 1024L)
                return 3;

            if (totalLength >= 1024L * 1024L * 1024L)
                return 4;

            return 6;
        }

        private static async Task UploadBlockWithRetryAsync(
            HttpClient http,
            string uploadUrl,
            byte[] data,
            string blockId,
            CancellationToken cancellationToken,
            Action<int> onUploaded)
        {
            Exception lastError = null;
            var delaysMs = new[] { 0, 1000, 2500, 5000 };

            for (var attempt = 0; attempt < delaysMs.Length; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (delaysMs[attempt] > 0)
                    await Task.Delay(delaysMs[attempt], cancellationToken).ConfigureAwait(false);

                try
                {
                    await UploadBlockAsync(http, uploadUrl, data, blockId, cancellationToken, onUploaded).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (LooksLikeBlockUploadSasIssue(ex))
                        throw;

                    if (!LooksLikeTransientUploadFailure(ex))
                        throw;
                }
            }

            throw BuildFriendlyUploadException(lastError);
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

        private static void DisplayProgress(string title, string info, float progress)
        {
            EditorUtility.DisplayProgressBar(title, info, progress);
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
                    "The upload URL from the backend was rejected for this large map upload.\n\n" +
                    "The SDK tried the chunked upload path and then a direct blob upload fallback, but the SAS authorization still failed.\n\n" +
                    "This usually means the backend needs a longer-lived SAS token or different blob permissions for large uploads.\n\n" +
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

        private void CreatePackForScene(SceneAsset scene)
        {
            if (scene == null)
                return;

            if (db.Packs.Any(pack => pack != null && pack.Scene == scene))
                return;

            var pack = CreateInstance<MapContentPackDefinition>();
            pack.name = SanitizeAssetName(scene.name);
            pack.MapName = scene.name;
            pack.Scene = scene;
            pack.IncludeInBuild = true;

            var scenePath = AssetDatabase.GetAssetPath(scene);
            var sceneDirectory = Path.GetDirectoryName(scenePath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(sceneDirectory))
            {
                var screenshotPath = $"{sceneDirectory}/{scene.name}.png";
                pack.Screenshot = AssetDatabase.LoadAssetAtPath<Texture2D>(screenshotPath);
            }

            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{MapContentDatabase.AssetFolder}/{pack.name}.asset");
            AssetDatabase.CreateAsset(pack, assetPath);
            db.Packs.Add(pack);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            list.index = db.Packs.Count - 1;
        }

        private void CreateEmptyPackAsset()
        {
            var pack = CreateInstance<MapContentPackDefinition>();
            pack.name = "NewMapPack";
            pack.MapName = "NewMapPack";

            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{MapContentDatabase.AssetFolder}/{pack.name}.asset");
            AssetDatabase.CreateAsset(pack, assetPath);
            db.Packs.Add(pack);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            list.index = db.Packs.Count - 1;
        }

        private void RemovePackAt(int index)
        {
            if (index < 0 || index >= db.Packs.Count)
                return;

            var pack = db.Packs[index];
            var assetPath = pack != null ? AssetDatabase.GetAssetPath(pack) : string.Empty;

            if (!string.IsNullOrWhiteSpace(assetPath) && !AssetDatabase.DeleteAsset(assetPath))
            {
                Debug.LogWarning($"[MashBoxMapTools] Could not delete map pack asset at '{assetPath}'.");
                return;
            }

            db.Packs.RemoveAt(index);
            if (list != null)
                list.index = Mathf.Clamp(index - 1, -1, db.Packs.Count - 1);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void CommitMapPackRename(MapContentPackDefinition pack, string newName)
        {
            if (pack == null)
                return;

            var trimmed = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            pack.MapName = trimmed;
            RenameMapPackAssetToMatch(pack);
            EditorUtility.SetDirty(pack);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Repaint();
        }

        private void RenameMapPackAssetToMatch(MapContentPackDefinition pack)
        {
            if (pack == null)
                return;

            var desiredName = SanitizeAssetName(string.IsNullOrWhiteSpace(pack.MapName) ? pack.name : pack.MapName);
            if (string.Equals(pack.name, desiredName, StringComparison.Ordinal))
                return;

            var assetPath = AssetDatabase.GetAssetPath(pack);
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            var error = AssetDatabase.RenameAsset(assetPath, desiredName);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"[MashBoxMapTools] Could not rename map pack asset: {error}");
                return;
            }

            pack.name = desiredName;
            EditorUtility.SetDirty(pack);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static string SanitizeAssetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "NewMapPack";

            var invalid = Path.GetInvalidFileNameChars();
            return new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // DATA STRUCTURES
        [System.Serializable] public class MapInfo { public int version; public string filename; public long size; }
        [System.Serializable] public class MapManifestWrapper { [System.NonSerialized] public Dictionary<string, MapInfo> maps; }
        [System.Serializable] public class VersionLog { [System.NonSerialized] public Dictionary<string, int> versions; }
        [System.Serializable] public class ChallengeMapData { public string mapName; public List<ChallengeCategory> categories; public List<MapTaskData> tasks; }
        [System.Serializable] public class ChallengeCategory { public string categoryName; public List<string> items; }
        [System.Serializable] public class MapTaskData { public string taskType; public string displayName; public string verb; public string preposition; public string adjective; public float targetValue; public int targetCount; }
    }

    internal sealed class PublishPlatformSelectionPopup : EditorWindow
    {
        private readonly List<MashBoxMapToolsWindow.PublishPlatformOption> options = new List<MashBoxMapToolsWindow.PublishPlatformOption>();
        private readonly List<bool> selected = new List<bool>();
        private Action<IReadOnlyList<MashBoxMapToolsWindow.PublishPlatformOption>> onConfirm;
        private string gameName = string.Empty;

        public static void Show(
            EditorWindow parent,
            string currentGame,
            IEnumerable<MashBoxMapToolsWindow.PublishPlatformOption> platformOptions,
            Action<IReadOnlyList<MashBoxMapToolsWindow.PublishPlatformOption>> onConfirm)
        {
            var window = CreateInstance<PublishPlatformSelectionPopup>();
            window.titleContent = new GUIContent("Publish Platforms");
            window.gameName = currentGame ?? string.Empty;
            window.onConfirm = onConfirm;

            foreach (var option in platformOptions)
            {
                window.options.Add(option);
                window.selected.Add(true);
            }

            window.minSize = new Vector2(280f, 180f);
            if (parent != null)
                window.ShowUtility();
            else
                window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Choose Publish Platforms", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"Select which {gameName} platforms this map should upload to.", MessageType.Info);

            for (var i = 0; i < options.Count; i++)
                selected[i] = EditorGUILayout.ToggleLeft(options[i].DisplayName, selected[i]);

            GUILayout.FlexibleSpace();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                    return;
                }

                GUI.enabled = selected.Any(value => value);
                if (GUILayout.Button("Start Publish"))
                {
                    var chosen = options
                        .Where((option, index) => selected[index])
                        .ToArray();
                    onConfirm?.Invoke(chosen);
                    Close();
                }

                GUI.enabled = true;
            }
        }
    }
}


#endif
