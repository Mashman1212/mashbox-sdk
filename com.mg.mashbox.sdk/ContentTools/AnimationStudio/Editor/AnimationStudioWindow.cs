using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow : EditorWindow
    {
        private const string SkeletonPath = "Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx";
        private const string BodyPath = "Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/SM_Body_01.fbx";
        private static readonly string[] LimbNames = { "Left hand", "Right hand", "Left foot", "Right foot" };
        [SerializeField] private AnimationTake take;
        [SerializeField] private GameObject character;
        [SerializeField] private GameObject body;
        [SerializeField] private GameObject[] clothing = Array.Empty<GameObject>();
        [SerializeField] private OutfitOptions[] outfitOptions = Array.Empty<OutfitOptions>();
        [SerializeField] private AnimationClip importClip;
        [SerializeField] private int frame;
        [SerializeField] private int control;
        [SerializeField] private bool ikMode = true;
        [SerializeField] private bool poleMode;
        [SerializeField] private bool rotate;
        [SerializeField] private bool humanoidExport = true;
        [SerializeField] private bool showSkeleton = true;
        [SerializeField] private bool showGroundGrid = true;
        [SerializeField] private bool autoKey;
        private bool unkeyedPose;
        [SerializeField] private bool neutralShading = true;
        [SerializeField] private Vector2 scroll;
        private AuthoringRig rig;
        private MashBoxStudioViewport viewport;
        private bool hadViewport;
        private UnityEngine.SceneManagement.Scene emptyWorkspace;
        private PoseKey pose;
        private PoseKey clipboard;

        private double previousTime;
        private float playFrame;
        private string message;
        private string boneSearch = "";
        private string cachedBoneSearch;
        private int[] boneIndices;
        private string[] boneLabels;
        private AnimationTake pendingSave;
        private double saveAfter;
        private bool sceneDraft;
        private string draftLabel;
        [SerializeField] private float controlSize = 1.5f;
        [SerializeField] private bool showPoles = true;
        [SerializeField] private bool showFKControls = true;

#if MashBoxDev
        [MenuItem("MashBox/MashBox Studio", false, 119)]
#endif
        public static void Open() => OpenInStudio(Selection.activeObject as AnimationTake);

#if MashBoxDev
        [MenuItem("Assets/Open Clip in MashBox Studio",false,2000)]
#endif
        private static void OpenSelectedClip()
        {
            if (Selection.activeObject is AnimationClip clip) { OpenClipForEditing(clip); return; }
            var clips = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(Selection.activeObject))
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
            if (clips.Length == 1) { OpenClipForEditing(clips[0]); return; }
            var menu = new GenericMenu();
            foreach (var item in clips)
            { var selectedClip = item; menu.AddItem(new GUIContent(item.name),false,() => OpenClipForEditing(selectedClip)); }
            menu.ShowAsContext();
        }
#if MashBoxDev
        [MenuItem("Assets/Open Clip in MashBox Studio",true)]
#endif
        private static bool CanOpenSelectedClip()
        {
            if (Selection.activeObject is AnimationClip) return true;
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter &&
                AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__",StringComparison.Ordinal));
        }
        private static void OpenClipAsTake(AnimationClip clip)
        {
            string path = EditorUtility.SaveFilePanelInProject("Open clip as take",clip.name+" Take","asset","Save an editable take; the source clip remains unchanged.");
            if (string.IsNullOrEmpty(path)) return;
            OpenClip(clip, path);
        }
        private static void OpenClip(AnimationClip clip, string path)
        {
            OpenInStudio(null);
            var window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            window.Guard(() =>
            {
                if (!window.ConfirmReplaceClip()) return;
                var previousTake = window.take;
                var previousCharacter = window.character;
                var previousBody = window.body;
                var previousClothing = window.clothing;
                var previousOutfit = window.outfitOptions;
                var previousClip = window.importClip;
                bool previousMode = window.modelingMode;
                bool previousSkinningMode = window.skinningMode;
                int previousFrame = window.frame;
                try
                {
                window.modelingMode = false;
                window.skinningEditor?.EndStroke(); window.skinningMode = false;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(clip));
                if (window.rig == null || (!clip.isHumanMotion && model))
                {
                    window.take = null;
                    if (!clip.isHumanMotion && model)
                    { window.body = null; window.clothing = Array.Empty<GameObject>(); window.outfitOptions = Array.Empty<OutfitOptions>(); }
                    window.character = !clip.isHumanMotion && model ? model : window.character ? window.character : AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath);
                    if (!window.character) window.character = model;
                    if (!window.character) throw new InvalidOperationException("Choose a character in Studio, then open this clip again.");
                    window.BuildRig();
                }
                window.importClip = clip;
                window.ImportClipToPath(path);
                window.humanoidExport = clip.isHumanMotion;
                window.OpenViewport();
                window.inspectorTab = 1;
                window.viewport?.Focus();
                }
                catch
                {
                    window.take = previousTake; window.character = previousCharacter; window.body = previousBody;
                    window.clothing = previousClothing; window.outfitOptions = previousOutfit;
                    window.importClip = previousClip; window.frame = previousFrame; window.modelingMode = previousMode;
                    window.skinningMode = previousSkinningMode;
                    if (window.character) window.BuildRig(); else window.ReleaseRig();
                    throw;
                }
            });
        }

        public static void OpenInStudio(AnimationTake asset)
        {
#if MashBoxDev
            var window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().FirstOrDefault();
            if (!window) window = CreateInstance<AnimationStudioWindow>();
            if (!asset && !window.take)
                asset = AssetDatabase.LoadAssetAtPath<AnimationTake>(EditorPrefs.GetString("MashBox.LastTake." + Application.dataPath,""));
            if (asset) { window.skinningEditor?.EndStroke(); window.skinningMode = false; window.modelingMode = false; window.LoadTake(asset); }
            else if (window.take && window.rig == null) window.Guard(window.BuildRig);
            else window.OpenViewport();
            if (!window.viewport) window.OpenViewport();
            window.viewport?.Focus();
#endif
        }

#if MashBoxDev
        [UnityEditor.Callbacks.OnOpenAsset]
#endif
        private static bool OpenTakeAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as AnimationTake;
            if (!asset) return false;
            OpenInStudio(asset);
            return true;
        }

        private void LoadTake(AnimationTake asset)
        {
            if (asset != take && !ConfirmReplaceClip()) return;
            FlushEdits();
            if (asset != clipSession) ReleaseClipSession();
            take = asset; character = asset.character; body = asset.body; frame = 0;
            EditorPrefs.SetString("MashBox.LastTake." + Application.dataPath,AssetDatabase.GetAssetPath(asset));
            clothing = asset.clothing ?? Array.Empty<GameObject>(); outfitOptions = asset.outfitOptions ?? Array.Empty<OutfitOptions>();
            Guard(BuildRig);
        }

        private void OnEnable()
        {
#if MashBoxDev
            RestoreMotionJob();
            minSize = new Vector2(470, 640);
            SceneView.duringSceneGui += DuringSceneGUI;
            SceneView.beforeSceneGui += BeforeSceneGUI;
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += UndoRedo;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            MashBoxStudioViewport.RestoreStudioViews();
#else
            EditorApplication.delayCall += () => { if (this) Close(); };
#endif
        }
        private void OnDisable()
        {
            ReleaseModelEditor();
            ReleaseImagePosePreview();
            SceneView.duringSceneGui -= DuringSceneGUI;
            SceneView.beforeSceneGui -= BeforeSceneGUI;
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= UndoRedo;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorApplication.delayCall -= Restore;
            ReleaseRig();
        }
        internal static void RestoreExistingViewport(MashBoxStudioViewport view)
        {
            if (!view || EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode) return;
            var window = view.Studio;
            if (!window) window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().FirstOrDefault();
            if (!window) window = CreateInstance<AnimationStudioWindow>();
            window.viewport = view;
            window.Restore();
        }
        private void Restore()
        {
            if (!this || EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode) return;
            // A hidden controller can survive closing the viewport. Never recreate it on reload or Play.
            if (!viewport) viewport = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().FirstOrDefault(view => view.Studio == this);
            if (!viewport) return;
            if (rig != null)
            {
                if (!viewport.HasStudioBindings) OpenViewport();
                return;
            }
            if (take && take.character) { character = take.character; body = take.body; clothing = take.clothing ?? Array.Empty<GameObject>(); outfitOptions = take.outfitOptions ?? Array.Empty<OutfitOptions>(); }
            if (character)
            {
                // Rebuild only the disposable preview. Keep the document, time and user's view.
                var pivot = viewport.pivot; var rotation = viewport.rotation;
                float size = viewport.size; bool orthographic = viewport.orthographic;
                Guard(BuildRig);
                viewport.pivot = pivot; viewport.rotation = rotation;
                viewport.size = size; viewport.orthographic = orthographic;
            }
            else OpenViewport();
        }
        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            { ReleaseRig(); ReleaseModelEditor(); }
            else if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
                MashBoxStudioViewport.RestoreStudioViews();
        }
        private void ReleaseRig()
        {
            EndCorrectionGizmo(false);
            FlushEdits();
            rigSelection.Clear(); rigSelectionExplicit=false; selectingRig=false;
            playing = false;
            ReleaseActors();
            rig?.Dispose(); rig = null; pose = null;
            if (emptyWorkspace.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(emptyWorkspace);
            emptyWorkspace = default;
            unkeyedPose = false;
        }
        private void UndoRedo()
        {
            if (skinningMode) return; // Skinning owns mesh/document Undo and preview rebuilding.
            if (RestoreUnkeyedPoseUndo()) return;
            sceneDraft = false;
            foreach (var actor in actorPreviews) actor.Draft = false;
            ReleaseActors();
            if (rig != null && take && (character != take.character || body != take.body || !clothing.SequenceEqual(take.clothing ?? Array.Empty<GameObject>()) || !outfitOptions.SequenceEqual(take.outfitOptions ?? Array.Empty<OutfitOptions>())))
            {
                character = take.character; body = take.body; clothing = take.clothing ?? Array.Empty<GameObject>(); outfitOptions = take.outfitOptions ?? Array.Empty<OutfitOptions>();
                Guard(RebuildAppearance);
            }
            if (take) Save();
            if (rig != null && take) Guard(() => Seek(Mathf.Clamp(frame, 0, take.lastFrame)));
            Repaint();
        }
        private void Guard(Action action)
        {
            try { action(); message = null; }
            catch (Exception ex) { message = ex.GetBaseException().Message; Debug.LogException(ex); playing = false; }
            Repaint();
        }

        private void OnGUI() { using (StudioTheme.Scope()) DrawThemedGUI(); }
        private void DrawThemedGUI()
        {
            DrawHeader();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawWorkspace();
            if (rig == null)
                EditorGUILayout.HelpBox("Choose the Animator root, or load the MashBox SM base. The viewport is an isolated preview scene; gameplay and source objects stay independent.", MessageType.Info);
            else
            {
                EditorGUILayout.HelpBox(rig.SolverStatus, rig.HasIK ? MessageType.Info : MessageType.Warning);
                if (take && pose != null) { DrawTimeline(); DrawControls(); DrawClipTools(); DrawGeneration(); }
                else EditorGUILayout.HelpBox("Create or open a take to begin posing. Press S to key a pose, or enable Auto Key. Saved keys survive Play Mode and editor reloads.", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Error);
            EditorGUILayout.EndScrollView();
            HandleKeys(Event.current);
        }

        private void DrawWorkspace(bool appearance = true)
        {
            DrawDefaultCharacter();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(appearance ? "CHARACTER WORKSPACE" : EditingClip ? "ANIMATION" : "TAKE", StudioTheme.Styles.boldLabel);
            if(appearance || !take)
            {
            using (new EditorGUI.DisabledScope(take))
            {
                character = (GameObject)EditorGUILayout.ObjectField("Skeleton / character root", character, typeof(GameObject), true);
            }
            if(appearance)
            {
                body = (GameObject)EditorGUILayout.ObjectField("Preview body / bust", body, typeof(GameObject), false);
                DrawClothing();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(take))
                {
                    if (GUILayout.Button("Use MashBox SM base"))
                    {
                        character = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPath);
                        body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
                        Guard(BuildRig);
                    }
                    if (GUILayout.Button("Use selection"))
                    {
                        character = Selection.activeGameObject;
                        body = null; Guard(BuildRig);
                    }
                }
                using (new EditorGUI.DisabledScope(!character))
                    if (GUILayout.Button("Open viewport")) Guard(BuildOrFocus);
            }
            }
            if (EditingClip) DrawClipDocument();
            EditorGUI.BeginChangeCheck();
            var nextTake = (AnimationTake)EditorGUILayout.ObjectField(EditingClip ? "Open saved take" : "Animation take", EditingClip ? null : take, typeof(AnimationTake), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (!ConfirmReplaceClip()) return;
                FlushEdits();
                ReleaseClipSession();
                take = nextTake;
                if (take) { character = take.character; body = take.body; clothing = take.clothing ?? Array.Empty<GameObject>(); outfitOptions = take.outfitOptions ?? Array.Empty<OutfitOptions>(); frame = 0; Guard(BuildRig); }
                else { ReleaseRig(); }
            }
            using (new EditorGUI.DisabledScope(rig == null))
                if (GUILayout.Button("New take…")) Guard(NewTake);
            if (!take)
            {
                var example = AssetDatabase.LoadAssetAtPath<AnimationTake>("Assets/MashBox/MashBox Studio Examples/SM Reach Take.asset");
                if (example && GUILayout.Button("Open SM reach example")) LoadTake(example);
            }
        }

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(1, 64, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.075f, 0.10f, 0.14f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), new Color(0.15f, 0.78f, 0.72f));
            GUI.Label(new Rect(18, rect.y + 10, rect.width - 30, 24), "MASHBOX  /  ANIMATION STUDIO", new GUIStyle(StudioTheme.Styles.boldLabel) { fontSize = 17, normal = { textColor = Color.white } });
            GUI.Label(new Rect(18, rect.y + 36, rect.width - 30, 20), "POSE   ·   KEYFRAME   ·   BAKE", StudioTheme.Styles.miniLabel);
        }

        private void BuildOrFocus()
        {
            if (rig == null) BuildRig();
            else OpenViewport();
        }
        private void BuildRig()
        {
            RecoverMissingCharacter();
            ReleaseRig();
            rig = new AuthoringRig(character, body, clothing, outfitOptions);
            if(take) rig.BuildVehicleParts(take.vehicleParts);
            cachedBoneSearch = null;
            rig.SetNeutralShading(neutralShading);
            if (take && take.bonePaths.Length > 0 && !take.bonePaths.SequenceEqual(rig.Paths))
            {
                ReleaseRig();
                throw new InvalidOperationException("This take's bone paths do not match the selected character.");
            }
            if (take && !rig.HasIK && take.keys.Any(k => k.effectors.Any(e => e.weight > 0)))
            {
                string reason = rig.SolverStatus; ReleaseRig();
                throw new InvalidOperationException("This take requires FBIK. " + reason);
            }
            if (take && !take.character)
            {
                Undo.RecordObject(take, "Restore Studio character reference");
                take.character = character; Save();
            }
            if (take && take.keys.Count > 0) Seek(frame);
            else pose = rig.Rest.Copy(0);
            OpenViewport();
        }
        private void OpenViewport()
        {
            if (!viewport) viewport = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().FirstOrDefault();
            bool migrateHost = viewport && !viewport.dockableHost;
            var previousPivot = migrateHost ? viewport.pivot : Vector3.zero;
            var previousRotation = migrateHost ? viewport.rotation : Quaternion.identity;
            float previousSize = migrateHost ? viewport.size : 1;
            bool previousOrthographic = migrateHost && viewport.orthographic;
            if (migrateHost)
            {
                var previous = viewport;
                viewport = CreateInstance<MashBoxStudioViewport>();
                viewport.dockableHost = true;
                viewport.titleContent = new GUIContent("MashBox Studio");
                viewport.position = previous.position;
                viewport.modelGridVisible = previous.modelGridVisible;
                viewport.cameraMode = previous.cameraMode;
                viewport.Show();
                previous.Close();
            }
            if (!viewport)
            {
                viewport = CreateInstance<MashBoxStudioViewport>();
                viewport.dockableHost = true;
                viewport.titleContent = new GUIContent("MashBox Studio");
                viewport.position = new Rect(150, 100, 1100, 800);
                viewport.Show();
                viewport.position = new Rect(150, 100, 1100, 800);
            }
            viewport.titleContent = new GUIContent("MashBox Studio");
            if (rig != null) viewport.Bind(rig.Scene);
            else
            {
                if (!emptyWorkspace.IsValid()) emptyWorkspace = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                viewport.Bind(emptyWorkspace);
            }
            hadViewport = true;
            viewport.Studio = this;
            UpdateClipSaveState();
            viewport.BindTimeline(DrawViewportTimeline);
            viewport.BindRigControls(DrawViewportRigControls);
            viewport.BindLimbBlend(DrawViewportLimbBlend);
            viewport.BindInspector(DrawViewportInspector);
            viewport.SetInspectorExpanded(inspectorExpanded);
            BindStudioMode();
            if (migrateHost) { viewport.pivot = previousPivot; viewport.rotation = previousRotation; viewport.size = previousSize; viewport.orthographic = previousOrthographic; return; }
            if (rig == null) return;
            var bounds = new Bounds(rig.Root.transform.position + Vector3.up, Vector3.one * 2);
            foreach (var renderer in rig.Root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            viewport.Frame(bounds, true);
        }
        private void NewTake()
        {
            if (!ConfirmReplaceClip()) return;
            FlushEdits();
            if (rig == null) BuildRig();
            var created = CreateInstance<AnimationTake>();
            created.name = "Untitled Take";
            created.hideFlags = HideFlags.HideAndDontSave;
            // A live object reference cannot persist. Prefer the corresponding source asset.
            created.character = EditorUtility.IsPersistent(character) ? character : PrefabUtility.GetCorrespondingObjectFromSource(character);
            if (!created.character)
            {
                DestroyImmediate(created);
                throw new InvalidOperationException("Save this character as a prefab first, or select its source skeleton asset. A take needs a persistent character reference.");
            }
            created.body = body;
            created.clothing = (GameObject[])clothing.Clone(); created.outfitOptions = (OutfitOptions[])outfitOptions.Clone();
            created.bonePaths = (string[])rig.Paths.Clone();
            created.SetKey(rig.Capture(), 0);
            ReleaseActors(); activeActor = -1;
            ReleaseClipSession();
            clipSession = created; memoryTakeSession = true;
            take = created; frame = 0; playing = false; rigSelection.Clear(); hiddenAttachments.Clear();
            Seek(0); Save();
        }

        private void DrawTimeline()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("TAKE & TIMELINE", StudioTheme.Styles.boldLabel);
            bool nextAutoKey = EditorGUILayout.Toggle(new GUIContent("Auto Key", "When enabled, pose edits insert or replace a key at the current frame. S always keys the pose."), autoKey);
            if (nextAutoKey != autoKey) { FinishSceneEdit(); autoKey = nextAutoKey; }
            EditorGUILayout.HelpBox(unkeyedPose
                ? "Unkeyed pose — press S or Key pose to keep it. Scrubbing, playback, or closing the workspace discards unkeyed changes."
                : autoKey ? "Auto Key is ON — pose edits write keys at the current frame."
                : "Auto Key is OFF — pose freely, then press S to insert or replace a pose key.", MessageType.None);
            EditorGUI.BeginChangeCheck();
            int fps = EditorGUILayout.IntField("Frames per second", take.frameRate);
            int end = EditorGUILayout.IntField("Last frame", take.lastFrame);
            bool loop = EditorGUILayout.Toggle("Loop playback / clip", take.loop);
            var interpolation = (PoseInterpolation)EditorGUILayout.EnumPopup("Interpolation", take.interpolation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(take, "Change animation timing");
                take.frameRate = Mathf.Clamp(fps, 1, 120);
                take.lastFrame = Mathf.Clamp(end, 1, 18000);
                take.loop = loop; take.interpolation = interpolation; Save(); Seek(Mathf.Min(frame, take.lastFrame));
            }
            EditorGUILayout.LabelField("Scrub and play using the timeline at the bottom of the viewport.", StudioTheme.Styles.wordWrappedMiniLabel);
            if (GUILayout.Button("Delete selected controls' keys at frame " + frame))
                DeleteSelectedAtCurrentFrame();
        }
        private void DrawControls()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("CONTROL RIG", StudioTheme.Styles.boldLabel);
            if (rig.Animator) DrawRigDiagram();
            else { ikMode = false; if (DrawBikeControls()) return; }
            int mode = GUILayout.Toolbar(ikMode ? 0 : 1, new[] { "IK effectors", "FK bones" });
            if ((mode == 0) != ikMode) { ikMode = mode == 0; control = 0; viewport?.Repaint(); }
            displayOptions = EditorGUILayout.Foldout(displayOptions, "Viewport display", true);
            if (displayOptions)
            {
            EditorGUI.BeginChangeCheck();
            showSkeleton = EditorGUILayout.Toggle("Show skeleton", showSkeleton);
            showGroundGrid = EditorGUILayout.Toggle(new GUIContent("Ground grid", "Ground at Y = 0. Adjust line spacing and opacity in MashBox Studio Settings."), showGroundGrid);
            showFKControls = EditorGUILayout.Toggle("Body / FK rings", showFKControls);
            showPoles = EditorGUILayout.Toggle("Elbow / knee targets", showPoles);
            controlSize = EditorGUILayout.Slider("Control size", controlSize, 0.75f, 3f);
            if (EditorGUI.EndChangeCheck()) viewport?.Repaint();
            EditorGUILayout.HelpBox("W moves; E rotates. Root and Hips can translate. Use IK targets to move hands and feet; gold targets steer elbows and knees. Other FK bones rotate to preserve bone lengths.", MessageType.None);
            EditorGUI.BeginChangeCheck();
            neutralShading = EditorGUILayout.Toggle("Neutral preview material", neutralShading);
            if (EditorGUI.EndChangeCheck()) { rig.SetNeutralShading(neutralShading); viewport?.Repaint(); }
            }
            rotate = GUILayout.Toolbar(rotate ? 1 : 0, new[] { "Move  [W]", "Rotate  [E]" }) == 1;
            if (!rotate && !ikMode && !CanTranslateBone(control))
                EditorGUILayout.HelpBox("Move is available on Root, Hips and IK controls. Choose an IK hand/foot target to move a limb, or press E to rotate this FK bone.", MessageType.Info);
            if (ikMode)
            {
                control = Mathf.Clamp(control, 0, 3);
                control = GUILayout.SelectionGrid(control, LimbNames, 2);
                poleMode = EditorGUILayout.Toggle("Elbow / knee pole control", poleMode);
                using (new EditorGUI.DisabledScope(!rig.HasIK))
                {
                    var effector = pose.effectors[control];
                    EditorGUI.BeginChangeCheck();
                    float weight = EditorGUILayout.Slider("IK blend (0 FK → 1 IK)", effector.weight, 0, 1);
                    Vector3 position = poleMode ? effector.pole : effector.position;
                    Vector3 rotation = effector.rotation.eulerAngles;
                    if(!selectionTransformsDrawn)
                    {
                        position=EditorGUILayout.Vector3Field(poleMode ? "Pole position" : "Target position",position);
                        rotation=EditorGUILayout.Vector3Field("Target rotation",rotation);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        effector.weight = weight;
                        if (poleMode) effector.pole = position; else effector.position = position;
                        effector.rotation = Quaternion.Euler(rotation);
                        pose.effectors[control] = effector; Commit("Edit IK control");
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Match → IK"))
                        {
                            pose.effectors[control] = rig.MatchEffector(control, 1); Commit("Match IK to current pose");
                        }
                        if (GUILayout.Button("Match all → FK"))
                        {
                            pose = rig.Capture(); Commit("Match FK to solved pose");
                        }
                        if (GUILayout.Button("Pin both feet"))
                        {
                            pose.effectors[2] = rig.MatchEffector(2, 1);
                            pose.effectors[3] = rig.MatchEffector(3, 1); Commit("Pin feet");
                        }
                    }
                }
                EditorGUILayout.HelpBox("Match → IK preserves the current pose. Targets stay fixed in character space as hips and spine move. Use FK hips for body motion while the feet remain pinned.", MessageType.None);
            }
            else
            {
                control = Mathf.Clamp(control, 0, rig.Bones.Length - 1);
                boneSearch = EditorGUILayout.TextField("Find bone", boneSearch);
                if (cachedBoneSearch != boneSearch || boneIndices == null)
                {
                    cachedBoneSearch = boneSearch;
                    boneIndices = Enumerable.Range(0, rig.Bones.Length)
                        .Where(i => rig.Paths[i].IndexOf(boneSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    boneLabels = boneIndices.Select(i => i == 0 ? "Root" : rig.Paths[i]).ToArray();
                }
                int selected = Array.IndexOf(boneIndices, control);
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup("Bone", selected, boneLabels);
                if (EditorGUI.EndChangeCheck() && picked >= 0) control = boneIndices[picked];
                if(!selectionTransformsDrawn)
                {
                    EditorGUI.BeginChangeCheck();
                    var bone = BonePose.Read(rig.Bones[control]);
                    using (new EditorGUI.DisabledScope(!CanTranslateBone(control)))
                        bone.position = EditorGUILayout.Vector3Field("Local position", bone.position);
                    bone.rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Local rotation", bone.rotation.eulerAngles));
                    if (EditorGUI.EndChangeCheck()) { PrepareFKEdit(control); pose.bones[control] = bone; Commit("Edit FK bone"); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy pose")) clipboard = pose.Copy(frame);
                using (new EditorGUI.DisabledScope(clipboard == null || clipboard.bones.Length != pose.bones.Length))
                    if (GUILayout.Button("Paste pose")) { pose = clipboard.Copy(frame); Commit("Paste pose"); }
                if (GUILayout.Button("Reset pose")) { pose = rig.Rest.Copy(frame); Commit("Reset pose"); }
            }
        }

        private void DrawClipTools()
        {
            DrawSingleFramePose();
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("CLIPS & GAMEPLAY", StudioTheme.Styles.boldLabel);
            importClip = (AnimationClip)EditorGUILayout.ObjectField("Source animation", importClip, typeof(AnimationClip), false);
            using (new EditorGUI.DisabledScope(!importClip))
                if (GUILayout.Button("Open clip for editing")) OpenClipForEditing(importClip);
            using (new EditorGUI.DisabledScope(!importClip))
                if (GUILayout.Button("Import clip into a new take…")) Guard(ImportClip);
            using (new EditorGUI.DisabledScope(!Selection.activeGameObject || EditorUtility.IsPersistent(Selection.activeGameObject)))
                if (GUILayout.Button("Capture selected live character pose")) Guard(CaptureLive);
            humanoidExport = EditorGUILayout.Toggle("Bake Humanoid muscles", humanoidExport && rig.Animator);
            if (!humanoidExport)
                EditorGUILayout.HelpBox("Transform clips require a Generic Animator with the same bone paths. Use Humanoid muscles for the SM Humanoid Avatar.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            var outputClip = (AnimationClip)EditorGUILayout.ObjectField(new GUIContent("Overwrite clip", "Drag an existing .anim clip here to save this take back to it."), take.outputClip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(take, "Set output animation clip");
                take.outputClip = outputClip; Save();
            }
            if (take.outputClip && !CanOverwriteClip(take.outputClip))
                EditorGUILayout.HelpBox("Choose a standalone .anim clip inside Assets to overwrite, or bake a new clip first. Imported model clips cannot be overwritten.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditingClip && !CanOverwriteClip(take.outputClip)))
                if (GUILayout.Button(EditingClip ? "Save animation" : "Save to referenced clip", GUILayout.Height(32))) Guard(() => { if (EditingClip) SaveClipSession(); else SaveToReferencedClip(); });
            if (GUILayout.Button("Bake new animation clip…")) Guard(Bake);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(sceneDraft ? (autoKey ? "Posing… key commits on release" : "Posing… press S to key") : unkeyedPose ? "Unkeyed pose · S to key" : EditingClip ? (clipSessionDirty ? "Unsaved animation changes" : "Animation saved") : pendingSave ? "Keys queued for save" : "Take saved", StudioTheme.Styles.miniLabel);
                if (GUILayout.Button("Save now", GUILayout.Width(85))) Guard(() => { if (EditingClip) SaveClipSession(); else FlushEdits(); });
            }
        }

        private void Seek(int next)
        {
            playing = false;
            FinishSceneEdit();
            FinishActorEdits();
            unkeyedPose = false;
            frame = Mathf.Clamp(next, 0, take.lastFrame);
            pose = take.Evaluate(frame) ?? rig.Rest.Copy(frame);
            rig.Apply(pose); viewport?.Repaint(); Repaint();
            SampleActors(frame);
        }
        private void Commit(string label, bool forceKey = false)
        {
            sceneDraft = false;
            playing = false;
            if (autoKey || forceKey)
            {
                Undo.RecordObject(take, label);
                if(!WriteAuthoringKey()) return;
                Save();
                unkeyedPose = false;
            }
            else unkeyedPose = true;
            Guard(() => rig.Apply(pose)); viewport?.Repaint();
            EvaluateConstraints();
        }
        private void Save()
        {
            take.SyncSingleFramePose();
            if (clipSession && (take == clipSession || inActorScope)) { MarkClipChanged(); return; }
            if (EditorUtility.IsPersistent(take)) EditorPrefs.SetString("MashBox.LastTake." + Application.dataPath,AssetDatabase.GetAssetPath(take));
            if (pendingSave && pendingSave != take) FlushSave();
            EditorUtility.SetDirty(take);
            pendingSave = take;
            saveAfter = EditorApplication.timeSinceStartup + 1.0;
        }
        private void FlushSave()
        {
            if (!pendingSave) return;
            if (EditorUtility.IsPersistent(pendingSave)) AssetDatabase.SaveAssetIfDirty(pendingSave);
            pendingSave = null;
            Repaint();
        }
        private void FlushEdits() { FinishSceneEdit(); FinishActorEdits(); FlushSave(); }
        private void PreviewSceneEdit(string label)
        {
            playing = false;
            sceneDraft = true; draftLabel = label;
            rig.Apply(pose);
            viewport?.Repaint(); Repaint();
            EvaluateConstraints();
        }
        private void FinishSceneEdit()
        {
            if (!sceneDraft || !take || pose == null) return;
            sceneDraft = false;
            if (!autoKey) { unkeyedPose = true; RecordUnkeyedPoseUndo(); Repaint(); return; }
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(take, draftLabel);
            if(!WriteAuthoringKey()) return;
            unkeyedPose = false;
            Save();
        }
        private void TogglePlay()
        {
            FinishSceneEdit();
            FinishActorEdits();
            unkeyedPose = false;
            if (playing) { playing = false; return; }
            StopStudioAudio(); audioErrors.Clear();
            playing = true; previousTime = EditorApplication.timeSinceStartup;
            playFrame = frame >= take.lastFrame ? 0 : frame;
            audioPlaybackFrame = playFrame;
            PlayAudioRange(playFrame, playFrame, true);
        }
        private bool automaticSessionFailed;
        private void Tick()
        {
            // Bootstrap only on the editor update, never during GUI layout or the
            // nested BuildRig/OpenViewport calls. NewTake creates an in-memory
            // document; no asset is written until the user explicitly saves it.
            if (viewport && !modelingMode && !skinningMode && !take && !automaticSessionFailed &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                EditorApplication.isPlaying == EditorApplication.isPlayingOrWillChangePlaymode)
            {
                try { NewTake(); }
                catch (Exception e) { automaticSessionFailed = true; message = e.GetBaseException().Message; Debug.LogException(e); }
            }
            if (hadViewport && !viewport) { hadViewport = false; ReleaseRig(); ReleaseModelEditor(); }
            PollMotionJob();
            if (sceneDraft && !viewport) FinishSceneEdit();
            if (pendingSave && !sceneDraft && GUIUtility.hotControl == 0 &&
                EditorApplication.timeSinceStartup >= saveAfter && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                FlushSave();
            if (!playing || rig == null || !take) return;
            double now = EditorApplication.timeSinceStartup;
            double advanced = audioPlaybackFrame + (now - previousTime) * take.frameRate; previousTime = now;
            double previous = audioPlaybackFrame;
            int end = Mathf.Max(1, take.lastFrame);
            if (take.loop)
            {
                while (advanced > end)
                {
                    PlayAudioRange(previous, end);
                    audioPreview.StopLoops();
                    advanced -= end; previous = 0;
                    PlayAudioRange(0, 0, true);
                }
                PlayAudioRange(previous, advanced);
            }
            else
            {
                PlayAudioRange(previous, Math.Min(advanced, end));
                // Let the last one-shot finish; any explicit pause, seek or close stops it.
                if (advanced >= end) { advanced = end; isPlaying = false; audioPreview.StopLoops(); }
            }
            audioPlaybackFrame = advanced; playFrame = (float)advanced;
            Guard(() =>
            {
                pose = take.Evaluate(playFrame); frame = Mathf.RoundToInt(playFrame);
                rig.Apply(pose); viewport?.Repaint();
                SampleActors(playFrame);
            });
        }
        private void HandleKeys(Event evt)
        {
            if (EditingClip && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.S &&
                (evt.control || evt.command) && !evt.alt && GUIUtility.hotControl == 0)
            { Guard(() => SaveClipSession(evt.shift)); evt.Use(); return; }
            if (HandleCorrectionGizmoKeys(evt)) return;
            if(HandleRigSelectionKeys(evt)) return;
            if (!inActorScope && activeActor >= 0 && activeActor < actorPreviews.Count && evt.type == EventType.KeyDown && evt.keyCode != KeyCode.Space)
            { InActor(actorPreviews[activeActor],()=>HandleKeys(evt)); return; }
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField || !take || rig == null) return;
            if(evt.shift && !evt.alt && !evt.control && !evt.command && (evt.keyCode==KeyCode.W || evt.keyCode==KeyCode.E))
            { KeySelectedChannel(evt.keyCode==KeyCode.E); evt.Use(); return; }
            if (evt.keyCode == KeyCode.Escape && sceneDraft)
            {
                sceneDraft = false;
                GUIUtility.hotControl = 0;
                Seek(frame); evt.Use(); return;
            }
            if (evt.alt || evt.control || evt.command || evt.shift) return;
            if (evt.keyCode == KeyCode.S) { Commit("Key pose", true); evt.Use(); }
            if (evt.keyCode == KeyCode.Space) { TogglePlay(); evt.Use(); }
            if (evt.keyCode == KeyCode.W) { rotate = false; evt.Use(); }
            if (evt.keyCode == KeyCode.E) { rotate = true; evt.Use(); }
        }

        private void KeySelectedChannel(bool rotationChannel)
        {
            if(pose==null) return;
            if(rigSelection.Count<=1 && ikMode && poleMode && rotationChannel)
            { message="Elbow / knee targets have position only. Use Shift+W."; return; }
            var bones=new int[pose.bones.Length]; var effectors=new int[pose.effectors.Length];
            int mask=rotationChannel ? 2 : 1;
            if(rigSelection.Count>1) AddRigSelectionMasks(bones,effectors,mask);
            else if(ikMode && !IsBike)
            {
                int limb=Mathf.Clamp(control,0,3);
                effectors[limb]=(poleMode ? 4 : mask)|8; // Persist the IK activation required to use this target.
            }
            else
            {
                int index=IsBike ? Array.IndexOf(rig.Bones,BikeJoint(Mathf.Clamp(control,0,BikeJointNames.Length-1))) : control;
                if(index<0 || index>=bones.Length) return;
                bones[index]=mask;
                if (!IsBike) AddBodyIKChannelMask(bones, index, mask);
                if(IsBike && control==3 && rotationChannel)
                    for(int pedal=4;pedal<=5;pedal++)
                    { int p=Array.IndexOf(rig.Bones,BikeJoint(pedal)); if(p>=0) bones[p]=2; }
                var before=take.Evaluate(frame);
                for(int i=0;i<effectors.Length;i++)
                    if(before!=null && rig.Starts[i] && (rig.Bones[index]==rig.Starts[i] || rig.Bones[index].IsChildOf(rig.Starts[i])) &&
                        !Mathf.Approximately(before.effectors[i].weight,pose.effectors[i].weight)) effectors[i]=8;
            }
            Undo.RecordObject(take,rotationChannel ? "Key selected rotation" : "Key selected position");
            if(!WriteAuthoringKey(bones,effectors)) return;
            sceneDraft=false; playing=false; Save();
            var stored=take.Evaluate(frame);
            unkeyedPose=false;
            for(int i=0;i<pose.bones.Length;i++)
                unkeyedPose |= Vector3.Distance(stored.bones[i].position,pose.bones[i].position)>.000001f ||
                    Quaternion.Angle(stored.bones[i].rotation,pose.bones[i].rotation)>.001f ||
                    Vector3.Distance(stored.bones[i].scale,pose.bones[i].scale)>.000001f;
            for(int i=0;i<pose.effectors.Length;i++)
                unkeyedPose |= Vector3.Distance(stored.effectors[i].position,pose.effectors[i].position)>.000001f ||
                    Quaternion.Angle(stored.effectors[i].rotation,pose.effectors[i].rotation)>.001f ||
                    Vector3.Distance(stored.effectors[i].pole,pose.effectors[i].pole)>.000001f ||
                    !Mathf.Approximately(stored.effectors[i].weight,pose.effectors[i].weight);
            message=null; EvaluateConstraints(); viewport?.Repaint(); Repaint();
        }

        private void BeforeSceneGUI(SceneView view)
        {
            if (view == viewport && viewport.HandleWindowShortcut()) return;
            if (view == viewport && skinningMode)
            {
                skinningEditor?.BeforeSkinSceneGUI(view);
                if (view.camera) view.camera.cameraType = CameraType.Preview;
                StudioPreviewLighting.ConfigureStudioBackground(view);
                return;
            }
            if (view == viewport && modelingMode)
            {
                if (view.camera) view.camera.cameraType = CameraType.Preview;
                EnsureModelEditor();
                modelEditor.ConfigureLightingCamera(view);
                if (modelEditor.HandleModelDrop(view, LoadStudioModel) || modelEditor.HandleObjectDuplicate() || modelEditor.HandleObjectDelete()) return;
                modelEditor.BeforeModelSceneGUI(view);
                return;
            }
            // HDRP and scene-wide effects must treat this as an asset preview.
            // SceneView resets cameraType during SetupCamera, so set it immediately before rendering.
            if (view == viewport && view.camera)
            {
                view.camera.cameraType = CameraType.Preview;
                StudioPreviewLighting.ConfigureStudioBackground(view);
            }
            if (view != viewport || rig == null || pose == null || !take) return;
            var evt = Event.current;
            bool frameCommand = evt.commandName == "FrameSelected" &&
                (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand);
            bool frameKey = evt.type == EventType.KeyDown && evt.keyCode == KeyCode.F &&
                !evt.alt && !evt.control && !evt.command && !evt.shift && !EditorGUIUtility.editingTextField;
            if (!frameCommand && !frameKey) return;
            if (evt.type != EventType.ValidateCommand && GUIUtility.hotControl == 0)
            {
                if (!inActorScope && activeActor >= 0 && activeActor < actorPreviews.Count) InActor(actorPreviews[activeActor],()=>FrameRigSelection(view));
                else FrameRigSelection(view);
            }
            evt.Use();
        }

        private void FrameRigSelection(SceneView view)
        {
            Vector3 center;
            if (IsBike) center = BikeJoint(Mathf.Clamp(control,0,BikeJointNames.Length-1)).position;
            else if (ikMode && rig.HasIK)
            {
                int limb = Mathf.Clamp(control, 0, 3);
                var displayed = DisplayEffector(limb);
                var effector = displayed;
                center = rig.Root.transform.TransformPoint(poleMode ? effector.pole : effector.position);
            }
            else center = rig.Bones[Mathf.Clamp(control, 0, rig.Bones.Length - 1)].position;

            var characterBounds = new Bounds(rig.Root.transform.position, Vector3.zero);
            foreach (var renderer in rig.Root.GetComponentsInChildren<Renderer>())
                characterBounds.Encapsulate(renderer.bounds);
            // Keep enough space around a joint to use the transform handles comfortably.
            float diameter = Mathf.Max(0.25f, characterBounds.size.magnitude * 0.3f);
            view.Frame(new Bounds(center, Vector3.one * diameter), false);
            view.Repaint();
        }

        private void DrawStudioSceneGUI(SceneView view)
        {
            if (view == viewport && !drawingActorControls && !inActorScope && Event.current.type == EventType.MouseDown)
                CaptureUnkeyedPoseUndo();
            if (DrawActorScene(view))
            {
                if (!autoKey && !inActorScope && Event.current.rawType == EventType.MouseUp) RecordUnkeyedPoseUndo();
                return;
            }
            if (view != viewport || rig == null || pose == null) return;
            if (showGroundGrid) viewport.DrawGroundGrid();
            if (!take) return;
            bool released = Event.current.rawType == EventType.MouseUp;
            HandleKeys(Event.current);
            if (DrawCorrectionGizmos(view)) return;
            if (DrawBikeScene(view)) { if (released) FinishSceneEdit(); return; }
            if(!skipActorPickers) DrawRigPickers(view);
            if(rigSelection.Count>1 || (rigSelectionExplicit && rigSelection.Count==0)) return;
            if(DrawAttachmentHandle()) { if(released) FinishSceneEdit(); return; }
            if (playing) return;
            if (DrawBodyIKHandle(released)) return;
            float selectedAlpha = ikMode ? RigAlpha(true, Mathf.Clamp(control,0,3)) : RigAlpha(false, BoneLimb(rig.Bones[Mathf.Clamp(control,0,rig.Bones.Length-1)]));
            if (selectedAlpha <= .001f || (ikMode && poleMode && !showPoles))
            { if (released) FinishSceneEdit(); return; }
            EditorGUI.BeginChangeCheck();
            if (ikMode && rig.HasIK)
            {
                control = Mathf.Clamp(control, 0, 3);
                var displayed = DisplayEffector(control);
                var eff = displayed;
                Transform root = rig.Root.transform;
                Vector3 target = root.TransformPoint(poleMode ? eff.pole : eff.position);
                bool locked = rotate && !poleMode ? eff.pinRotation : poleMode ? eff.pinPole : eff.pinPosition;
                if (locked)
                {
                    EditorGUI.EndChangeCheck();
                    if (released) FinishSceneEdit();
                    return;
                }
                if (poleMode)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawDottedLine(rig.Middles[control].position, target, 4);
                }
                if (rotate && !poleMode) eff.rotation = Quaternion.Inverse(root.rotation) * StudioRotationHandle(root.rotation * eff.rotation, target);
                else
                {
                    target = Handles.PositionHandle(target, HandleOrientation(poleMode ? root.rotation : root.rotation * eff.rotation));
                    if (poleMode) eff.pole = root.InverseTransformPoint(target);
                    else eff.position = root.InverseTransformPoint(target);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    eff.weight = 1; pose.effectors[control] = eff; PreviewSceneEdit(poleMode ? "Move elbow / knee target" : "Move IK control");
                }
            }
            else if (!ikMode)
            {
                control = Mathf.Clamp(control, 0, rig.Bones.Length - 1);
                var bone = rig.Bones[control];
                var p = BonePose.Read(bone);
                if (rotate)
                {
                    Quaternion rotation = StudioRotationHandle(bone.rotation, bone.position);
                    p.rotation = bone.parent ? Quaternion.Inverse(bone.parent.rotation) * rotation : rotation;
                }
                else if (CanTranslateBone(control))
                {
                    Vector3 position = Handles.PositionHandle(bone.position, HandleOrientation(bone.rotation));
                    p.position = bone.parent ? bone.parent.InverseTransformPoint(position) : position;
                }
                if (EditorGUI.EndChangeCheck()) { PrepareFKEdit(control); pose.bones[control] = p; PreviewSceneEdit("Move FK control"); }
            }
            else EditorGUI.EndChangeCheck();
            if (released) FinishSceneEdit();
        }

        private void CaptureLive()
        {
            var selected = Selection.activeGameObject;
            var animator = selected.GetComponent<Animator>() ?? selected.GetComponentInParent<Animator>();
            Transform source = animator ? animator.transform : selected.transform;
            var captured = pose.Copy(frame);
            for (int i = 1; i < rig.Paths.Length; i++)
            {
                var bone = source.Find(rig.Paths[i]);
                if (!bone) throw new InvalidOperationException("Live skeleton does not match: " + rig.Paths[i]);
                captured.bones[i] = BonePose.Read(bone);
            }
            for (int i = 0; i < 4; i++) captured.effectors[i].weight = 0;
            rig.Apply(captured); pose = rig.Capture(); Commit("Capture live pose");
        }

        private void ImportClip()
        {
            if (!ConfirmReplaceClip()) return;
            FlushEdits();
            string path = EditorUtility.SaveFilePanelInProject("Import animation as take", importClip.name + " Take", "asset", "The original clip is preserved.");
            if (string.IsNullOrEmpty(path)) return;
            ImportClipToPath(path);
        }

        private void ImportClipToPath(string path)
        {
            if (!importClip.isHumanMotion && !AnimationUtility.GetCurveBindings(importClip).Any(binding => binding.type == typeof(Transform) && Array.IndexOf(rig.Paths,binding.path) >= 0))
                throw new InvalidOperationException("This clip does not match the current skeleton. Open its source FBX or choose the matching character first.");
            var imported = CreateInstance<AnimationTake>();
            imported.character = EditorUtility.IsPersistent(character) ? character : PrefabUtility.GetCorrespondingObjectFromSource(character); imported.body = body;
            if (!imported.character) { DestroyImmediate(imported); throw new InvalidOperationException("Choose a saved character prefab or model before importing a take."); }
            imported.clothing = (GameObject[])(clothing ?? Array.Empty<GameObject>()).Clone(); imported.outfitOptions = (OutfitOptions[])(outfitOptions ?? Array.Empty<OutfitOptions>()).Clone();
            imported.frameRate = Mathf.Clamp(Mathf.RoundToInt(importClip.frameRate),1,120);
            imported.lastFrame = Mathf.Max(1, Mathf.CeilToInt(importClip.length * imported.frameRate));
            if (imported.lastFrame > 18000) { DestroyImmediate(imported); throw new InvalidOperationException("Clip exceeds 18,000 frames. Trim it before importing."); }
            imported.bonePaths = (string[])rig.Paths.Clone();
            imported.interpolation = PoseInterpolation.Linear;
            imported.outputClip = importClip;
            imported.loop = AnimationUtility.GetAnimationClipSettings(importClip).loopTime;
            var avatar = rig.Animator ? rig.Animator.avatar : null;
            try
            {
                if (rig.Animator && !importClip.isHumanMotion)
                {
                    rig.Animator.avatar = null;
                    rig.Animator.Rebind();
                }
                for (int f = 0; f <= imported.lastFrame; f++)
                {
                    if (f % 30 == 0 && EditorUtility.DisplayCancelableProgressBar("Import animation", "Sampling frame " + f, (float)f / imported.lastFrame))
                        throw new OperationCanceledException("Import cancelled.");
                    rig.Apply(rig.Rest);
                    importClip.SampleAnimation(rig.Root, Mathf.Min(importClip.length, (float)f / imported.frameRate));
                    var key = rig.Capture(); key.frame = f; imported.keys.Add(key);
                }
                if (AssetDatabase.GetLabels(importClip).Contains(SinglePoseLabel)) imported.SetSingleFramePose(true);
                bool memoryOnly = string.IsNullOrEmpty(path);
                if (!memoryOnly)
                {
                    AssetDatabase.CreateAsset(imported, AssetDatabase.GenerateUniqueAssetPath(path));
                    Undo.RegisterCreatedObjectUndo(imported,"Import clip as take");
                }
                else { imported.name = importClip.name; imported.hideFlags = HideFlags.HideAndDontSave; }
                FlushSave(); ReleaseActors(); activeActor = -1;
                take = imported;
                Seek(0);
                // Only release the old document after sampling and evaluation succeed.
                ReleaseClipSession();
                if (memoryOnly) { clipSession = imported; clipSessionDirty = false; UpdateClipSaveState(); }
                else Save();
            }
            catch { if (!EditorUtility.IsPersistent(imported)) DestroyImmediate(imported); throw; }
            finally
            {
                if (rig.Animator && rig.Animator.avatar != avatar)
                {
                    rig.Animator.avatar = avatar; rig.Animator.Rebind();
                }
                EditorUtility.ClearProgressBar(); rig.Apply(pose);
            }
        }

        private void Bake()
        {
            FlushEdits();
            string path = EditorUtility.SaveFilePanelInProject("Bake animation clip", take.name, "anim", "Create a clip for an Animator or Animancer.");
            if (string.IsNullOrEmpty(path)) return;
            BakeToClip(null, AssetDatabase.GenerateUniqueAssetPath(path));
        }

        private static bool CanOverwriteClip(AnimationClip clip)
        {
            if (!clip || AssetDatabase.IsSubAsset(clip)) return false;
            string path = AssetDatabase.GetAssetPath(clip);
            return path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase);
        }

        private void SaveToReferencedClip()
        {
            if (!CanOverwriteClip(take.outputClip)) throw new InvalidOperationException("Choose a standalone .anim clip inside Assets to overwrite.");
            FlushEdits();
            BakeToClip(take.outputClip, null);
        }

        private void BakeToClip(AnimationClip target, string path)
        {
            playing = false;
            AnimationClip clip;
            var sampled=SampleConstrainedTake(take);
            try { clip=AnimationClipBaker.Bake(sampled, rig, humanoidExport); }
            finally { DestroyImmediate(sampled); EvaluateConstraints(); }
            try
            {
                if (EditingClip) PreserveClipExtras(take.outputClip, clip);
                if (target)
                {
                    Undo.RegisterCompleteObjectUndo(target, "Save Studio animation clip");
                    clip.name = target.name;
                    EditorUtility.CopySerialized(clip, target);
                    DestroyImmediate(clip);
                    EditorUtility.SetDirty(target);
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, path);
                    target = clip;
                    Undo.RecordObject(take, "Set output animation clip");
                    take.outputClip = target;
                    Save();
                }
                SaveSinglePoseLabel(target, take.singleFramePose); AssetDatabase.SaveAssetIfDirty(target); FlushSave(); EditorGUIUtility.PingObject(target);
                if (EditingClip) { clipSessionDirty = false; UpdateClipSaveState(); }
            }
            catch { if (!EditorUtility.IsPersistent(clip)) DestroyImmediate(clip); throw; }
            finally { rig.Apply(pose); EvaluateConstraints(); }
        }
    }
}
