using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool clothMode;
        [SerializeField] private GameObject clothCharacter, clothGarment;
        [SerializeField] private GameObject[] clothOutfit = Array.Empty<GameObject>();
        [SerializeField] private MashBoxClothProfile clothProfile;
        [SerializeField] private AnimationClip clothClip;
        [SerializeField] private int clothRendererIndex;
        [SerializeField] private float clothBrushRadius = .08f, clothBrushValue = .15f, clothBrushStrength = .5f;
        private AuthoringRig clothRig;
        private StudioPreviewLighting clothLighting;
        private MashBoxClothSession clothSession;
        private Renderer[] clothRenderers = Array.Empty<Renderer>();
        private Vector2 clothTargetScroll, clothInspectorScroll;
        private bool clothRunning, clothPainting, clothShowWeights = true, clothShowColliders = true, clothStroke;
        private int clothControl, clothTool;
        private double clothLastTime;
        private float clothAnimationTime;
        private float clothSimulationTime;
        private string clothStatus;
        private Mesh clothPickMesh;
        private readonly List<Vector3> clothPickVertices = new List<Vector3>();
        private int[] clothTriangles = Array.Empty<int>();
        private int[][] clothPaintNeighbors = Array.Empty<int[]>();
        private float[] clothSmoothTargets = Array.Empty<float>();
        private int[] clothSeamVertices = Array.Empty<int>();
        private Vector3 clothGrabPosition;
        private int clothGrabParticle = -1;
        private Renderer ClothRenderer => clothRendererIndex >= 0 && clothRendererIndex < clothRenderers.Length ? clothRenderers[clothRendererIndex] : null;

        [MenuItem("MashBox/MashBox Studio/Cloth", false, 124)]
        private static void OpenClothWorkspace() => OpenWorkspace(window => window.OpenClothMode());

        private void OpenClothMode()
        {
            LeavePaintingMode(); FlushEdits(); playing = false;
            skinningEditor?.EndStroke(); skinningEditor?.SuspendLighting(); retopologyEditor?.SuspendLighting();
            modelEditor?.CancelMarkingMenu(); modelEditor?.CloseToolOptions(); modelEditor?.SuspendLighting();
            skinningMode = retopologyMode = modelingMode = paintingMode = false; clothMode = true;
            if (!clothCharacter) clothCharacter = character ? character : ResolveSdkSkeleton();
            if (!viewport) OpenViewport(); else BindClothMode();
            FrameCloth();
        }

        private void BindClothMode()
        {
            if (clothRig == null) BuildClothPreview();
            viewport.Bind(clothRig.Scene); viewport.wantsMouseMove = true;
            viewport.SetRetopologyPanels(false);
            viewport.BindSkinInfluences(null); viewport.BindSkinPreviewTimeline(null); viewport.BindSkinPractice(null);
            viewport.BindTimeline(null); viewport.BindLimbBlend(null); viewport.BindAttachments(null);
            viewport.BindModes(DrawStudioModes, true);
            viewport.BindRigControls(DrawClothTargets); viewport.BindInspector(DrawClothInspector);
            viewport.BindModelToolOptions(null); viewport.BindMarkingMenu(null); viewport.BindStatistics(DrawClothStatistics);
            viewport.BindViewportControls(DrawStudioMenuBar, DrawClothToolbar, DrawStudioSettings);
            viewport.BindModelInput(null, null, null); viewport.BindStatusBar(false);
            viewport.SyncStudioToolbarState(true); viewport.SetInspectorExpanded(true);
            viewport.BindCompactPreviewTimeline(DrawClothTransport);
            StudioPreferenceStore.SetString("Studio.Mode", "Cloth");
        }

        private void BuildClothPreview()
        {
            EnsureClothModelsReadable(clothGarment ? new[] { clothGarment } : clothOutfit ?? Array.Empty<GameObject>());
            var selectedMesh = ClothEditable ? MashBoxClothSession.GetMesh(ClothRenderer) : null;
            RememberClothProfile();
            var garments = ClothSources(out var bodyMeshes, out var bustMeshes);
            // Validate and build the replacement before releasing a working
            // preview. An incompatible garment must not blank the workspace.
            var replacement = new AuthoringRig(clothCharacter ? clothCharacter : ResolveSdkSkeleton(), null,
                garments.Where(g => g && g.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0).ToArray());
            try
            {
                foreach (var garment in garments)
                    foreach (var filter in garment.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var original = filter.GetComponent<MeshRenderer>(); if (!filter.sharedMesh || !original) continue;
                        var copy = new GameObject("Preview • " + filter.name); copy.transform.SetParent(replacement.Root.transform, false);
                        copy.transform.localPosition = garment.transform.InverseTransformPoint(filter.transform.position);
                        copy.transform.localRotation = Quaternion.Inverse(garment.transform.rotation) * filter.transform.rotation;
                        copy.transform.localScale = (garment.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix).lossyScale;
                        copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh; copy.AddComponent<MeshRenderer>().sharedMaterials = original.sharedMaterials;
                    }
                foreach (var root in replacement.Scene.GetRootGameObjects()) foreach (var light in root.GetComponentsInChildren<Light>()) light.enabled = false;
            }
            catch { replacement.Dispose(); throw; }
            ReleaseClothPreview();clothRig = replacement;
            clothLighting = new StudioPreviewLighting(clothRig.Scene); clothLighting.ApplyMaterialPreview();
            clothRenderers = clothRig.Root.GetComponentsInChildren<Renderer>(true).Where(r => MashBoxClothSession.GetMesh(r)).ToArray();
            BuildClothMeshRows(garments, bodyMeshes, bustMeshes);
            InitializeClothDisplay();
            ClothAction(ApplyClothClipping);
            int matching = Array.FindIndex(clothMeshRows, p => !p.referenceOnly && (selectedMesh ? p.sourceMesh == selectedMesh : clothProfile && clothProfile.Matches(p.sourceMesh)));
            clothRendererIndex = matching >= 0 ? matching : Array.FindIndex(clothMeshRows, p => !p.referenceOnly);
            clothProfile = ClothEditable ? clothProfiles.Find(p => p && p.Matches(MashBoxClothSession.GetMesh(ClothRenderer))) : null;
            clothPickMesh = new Mesh { name = "Cloth brush pose", hideFlags = HideFlags.HideAndDontSave };
            RefreshClothPickMesh();
            if (viewport) viewport.Bind(clothRig.Scene);
            SampleClothMotion(0); RefreshClothPickMesh(); clothLastTime = EditorApplication.timeSinceStartup;
        }

        private void LeaveClothMode()
        {
            if (!clothMode && clothRig == null) return;
            EndClothStroke(); clothMode = false; ReleaseClothPreview();
        }
        private void ReleaseClothPreview()
        {
            EndClothStroke(); clothRunning = false; clothPainting = false;
            ReleaseClothClipping();
            clothSession?.Dispose(); clothSession = null;
            clothLighting?.Dispose(); clothLighting = null; clothRig?.Dispose(); clothRig = null;
            ReleaseClothDisplay();
            if (clothPickMesh) DestroyImmediate(clothPickMesh); clothPickMesh = null;
            clothRenderers = Array.Empty<Renderer>(); clothMeshRows = Array.Empty<StudioSkinningDocument.Part>();
            clothMeshSources = Array.Empty<GameObject>(); clothGrabParticle = -1;
        }
        private void FrameCloth()
        {
            if (!viewport || clothRenderers.Length == 0) return;
            var visible = clothRenderers.Where(r => r.gameObject.activeSelf).ToArray();
            if (visible.Length == 0) return;
            var bounds = visible[0].bounds;
            foreach (var renderer in visible) bounds.Encapsulate(renderer.bounds);
            viewport.Frame(bounds, true);
        }
        private void ClothAction(Action action)
        {
            try { action(); clothStatus = null; }
            catch (Exception e) { clothRunning = false; clothStatus = e.GetBaseException().Message; }
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }
        private void StartClothSession()
        {
            if (!ClothVisible) throw new InvalidOperationException("Select a visible garment to simulate. Default references are locked.");
            EndClothStroke(); clothPainting = false;
            clothSession?.Dispose(); clothSession = null;
            clothSession = new MashBoxClothSession(ClothRenderer, clothProfile, clothRig.Root.transform);
            clothSimulationTime = 0;
            clothGrabParticle = -1; clothLastTime = EditorApplication.timeSinceStartup;
        }
        private void StopClothSession()
        {
            clothRunning = false; clothSession?.Dispose(); clothSession = null; clothGrabParticle = -1;
            RefreshClothPickMesh();
        }
        private void TickCloth()
        {
            if ((clothGrabDragging || clothResizing) && (!viewport || EditorWindow.focusedWindow != viewport || !UnityEditorInternal.InternalEditorUtility.isApplicationActive)) EndClothStroke();
            if (!clothMode || !viewport || !clothRunning || clothRig == null || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Min(.1f, (float)(now - clothLastTime)); clothLastTime = now;
            try
            {
                if (HasClothMotion) SampleClothMotion(clothAnimationTime + delta);
                if (clothSession != null)
                {
                    int steps = clothSession.Steps;
                    clothSession.Advance(delta);
                    clothSimulationTime += (clothSession.Steps - steps) / (float)Mathf.Clamp(clothProfile.frequency, 15, 120);
                    if (clothSession.Steps != steps || clothProfile.interpolateMotion) viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
                }
                if (HasClothMotion) viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            }
            catch (Exception e) { clothRunning = false; clothStatus = e.GetBaseException().Message; viewport.Repaint(); }
        }

        private void DrawClothTargets()
        {
            DrawClothMeshList();
            if (!string.IsNullOrEmpty(clothStatus)) EditorGUILayout.HelpBox(clothStatus, MessageType.Warning);
        }

        private void DrawClothProfileControls()
        {
            GUILayout.Label(clothMeshRows[clothRendererIndex].name, EditorStyles.boldLabel);
            var selectedMesh = MashBoxClothSession.GetMesh(ClothRenderer);
            if (selectedMesh && !selectedMesh.isReadable)
            {
                EditorGUILayout.HelpBox("This mesh needs Read/Write enabled for CPU cloth simulation.", MessageType.Warning);
                if (GUILayout.Button("Enable Read/Write on this model")) ClothAction(() =>
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(selectedMesh)) as ModelImporter;
                    if (!importer) throw new InvalidOperationException("Select the mesh importer and enable Read/Write manually.");
                    StopClothSession(); importer.isReadable = true; importer.SaveAndReimport(); BuildClothPreview();
                });
            }
            var profile = (MashBoxClothProfile)EditorGUILayout.ObjectField("Cloth profile", clothProfile, typeof(MashBoxClothProfile), false);
            if (profile != clothProfile)
            {
                StopClothSession(); EndClothStroke();
                clothProfiles.RemoveAll(p => !p || p.Matches(selectedMesh));
                clothProfile = profile; RememberClothProfile();
            }
            using (new EditorGUI.DisabledScope(!ClothEditable))
                if (GUILayout.Button("Create profile for this mesh…")) ClothAction(() =>
                {
                    string path = EditorUtility.SaveFilePanelInProject("Cloth profile", ClothRenderer.name + " Cloth", "asset", "Save movement painting and solver settings.");
                    if (string.IsNullOrEmpty(path)) return;
                    var created = CreateInstance<MashBoxClothProfile>();
                    try
                    {
                        created.Initialize(MashBoxClothSession.GetMesh(ClothRenderer));
                        RefreshClothPickMesh();
                        // Imported garments often use Z-up mesh coordinates. Seed pins in the visible character pose.
                        float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
                        foreach (var vertex in clothPickVertices)
                        { float y = ClothRenderer.transform.TransformPoint(vertex).y; lowest = Mathf.Min(lowest, y); highest = Mathf.Max(highest, y); }
                        float pinHeight = Mathf.Lerp(lowest, highest, .85f);
                        for (int i = 0; i < clothPickVertices.Count; i++)
                            created.motion[i] = ClothRenderer.transform.TransformPoint(clothPickVertices[i]).y >= pinHeight ? 0 : .15f;
                        AssetDatabase.CreateAsset(created, path);
                    }
                    catch { if (!AssetDatabase.Contains(created)) DestroyImmediate(created); throw; }
                    StopClothSession(); clothProfile = created; RememberClothProfile(); GenerateClothCapsules(); AssetDatabase.SaveAssetIfDirty(clothProfile);
                });
            bool valid = clothProfile && clothProfile.Matches(MashBoxClothSession.GetMesh(ClothRenderer));
            if (clothProfile && !valid) EditorGUILayout.HelpBox("This profile belongs to another mesh. Select the matching garment or create a new profile.", MessageType.Warning);
        }

        private void DrawClothStatistics()
        {
            if (!ClothEditable) return;
            var style = StudioTheme.Styles.miniLabel;
            var session = clothSession;
            if (session == null)
            {
                var mesh = MashBoxClothSession.GetMesh(ClothRenderer);
                GUI.Label(new Rect(0, 0, 256, 18), $"Cloth • {(mesh ? mesh.vertexCount : 0):N0} render vertices", style);
                GUI.Label(new Rect(0, 17, 256, 18), "Play to measure simulation performance", style);
                return;
            }
            const string timingHint = "Editor CPU timings for the latest update. Compare player builds for final performance budgets.";
            GUI.Label(new Rect(0, 0, 256, 18), $"Particles {session.Solver.ParticleCount:N0}   Verts {session.SourceMesh.vertexCount:N0}", style);
            GUI.Label(new Rect(0, 17, 256, 18), $"Constraints {session.Solver.ConstraintCount:N0} • {(clothRunning ? "Running" : "Paused")}", style);
            GUI.Label(new Rect(0, 34, 256, 18), new GUIContent($"Total {session.TotalMilliseconds:F2} ms   Solve {session.SolveMilliseconds:F2} ms", timingHint), style);
            GUI.Label(new Rect(0, 51, 256, 18), new GUIContent($"Skin bake {session.BakeMilliseconds:F2} ms   Dropped {session.DroppedSteps:N0}", timingHint), style);
        }

        private void DrawClothInspector()
        {
            clothInspectorScroll = EditorGUILayout.BeginScrollView(clothInspectorScroll);
            DrawClothDisplayControls();
            if (!ClothEditable)
            {
                EditorGUILayout.HelpBox("Add a garment, then select it in the mesh list to paint and simulate cloth. Default reference visibility can be toggled on the left.", MessageType.Info);
                EditorGUILayout.EndScrollView(); return;
            }
            DrawClothProfileControls();
            if (!clothProfile) { EditorGUILayout.HelpBox("Create or select a cloth profile to paint and tune it.", MessageType.Info); EditorGUILayout.EndScrollView(); return; }
            bool valid = ClothVisible && clothProfile.Matches(MashBoxClothSession.GetMesh(ClothRenderer));
            GUILayout.Label("MOVEMENT PAINT", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!valid))
            {
                clothBrushRadius = EditorGUILayout.Slider("Radius (m)", clothBrushRadius, .005f, .5f);
                clothBrushValue = EditorGUILayout.Slider("Movement (m)", clothBrushValue, 0, 1);
                clothBrushStrength = EditorGUILayout.Slider("Strength", clothBrushStrength, .01f, 1);
                EditorGUI.BeginChangeCheck();
                clothShowWeights = EditorGUILayout.Toggle("Show weight colors", clothShowWeights);
                clothPaintThrough = EditorGUILayout.Toggle(new GUIContent("Pass-through", "Paint through all layers of the selected garment along the view direction."), clothPaintThrough);
                if (EditorGUI.EndChangeCheck()) viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
                EditorGUILayout.HelpBox("Blue = pinned; yellow/red = free. Paint with LMB; Hold Shift to smooth movement distances. Paint Movement = 0 to pin. Alt orbits. Ctrl + middle-drag or [ / ] changes radius. Pass-through paints all layers; otherwise the brush uses a shallow surface depth band. Ctrl+Z undoes a stroke.", MessageType.None);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Pin all")) FillCloth(0);
                if (GUILayout.Button("Fill movement")) FillCloth(clothBrushValue);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            var serialized = new SerializedObject(clothProfile); serialized.Update();
            EditorGUI.BeginChangeCheck();
            DrawClothStiffness(serialized);
            GUILayout.Label("WIND + SOLVER", EditorStyles.boldLabel);
            foreach (string field in new[] { "animationFollow", "animationInertia", "wind", "gust", "gravity", "damping", "thickness", "particleBudget", "frequency", "interpolateMotion", "substeps", "iterations", "teleportDistance" })
                EditorGUILayout.PropertyField(serialized.FindProperty(field));
            EditorGUILayout.PropertyField(serialized.FindProperty("preserveAuthoredShading"), new GUIContent("Preserve authored shading"));
            using (new EditorGUI.DisabledScope(serialized.FindProperty("preserveAuthoredShading").boolValue))
                EditorGUILayout.PropertyField(serialized.FindProperty("normalsEverySteps"));
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
            }
            GUILayout.Label("Tuning applies live. Particle Budget applies on Rebuild simulation; all other values preserve the current simulation.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Rebuild simulation (apply particle budget)")) ClothAction(() => { bool running = clothRunning; StartClothSession(); clothRunning = running; });
            GUILayout.Space(8); GUILayout.Label("BODY COLLISIONS", EditorStyles.boldLabel);
            clothShowColliders = EditorGUILayout.Toggle("Show capsules", clothShowColliders);
            if (GUILayout.Button("Generate character capsules")) ClothAction(GenerateClothCapsules);
            if (GUILayout.Button(new GUIContent("Fit knee & ankle ends", "Widen ankles and move larger knee spheres backward for calf coverage. Keeps hip and arm colliders unchanged."))) ClothAction(FitClothAnkleEnds);
            serialized.Update(); EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.FindProperty("backstop"), new GUIContent("Enable backstop"));
            using (new EditorGUI.DisabledScope(!serialized.FindProperty("backstop").boolValue))
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("backstopDistance"));
                EditorGUILayout.PropertyField(serialized.FindProperty("backstopRadius"));
            }
            EditorGUILayout.PropertyField(serialized.FindProperty("bodyCollisions"), new GUIContent("Enable body collisions"));
            EditorGUILayout.PropertyField(serialized.FindProperty("localBoneCollisions"), new GUIContent("Local bone collisions","Restrict each region to colliders associated with its skin weights. Prevents unrelated limbs pushing the garment."));
            EditorGUILayout.PropertyField(serialized.FindProperty("collisionFriction"));
            EditorGUILayout.PropertyField(serialized.FindProperty("bodyCapsules"), true);
            if (EditorGUI.EndChangeCheck()) { serialized.ApplyModifiedProperties(); viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); }
            EditorGUILayout.HelpBox("Capsules follow bone paths relative to the character root. Adjust their radii for your body; cloth has no self-collision in this version.", MessageType.None);
            if (clothSession != null)
            {
                GUILayout.Space(8); GUILayout.Label("PULL TEST", EditorStyles.boldLabel);
                if (GUILayout.Button("Release grab")) { EndClothGrab(); }
                clothGrabRadius = EditorGUILayout.Slider("Grab radius (m)", clothGrabRadius, .005f, .5f);
                GUILayout.Label("Click and drag to pull particles inside the sphere. Release to let go. Ctrl + middle-drag changes radius; [ / ] also resize. Pinned particles stay pinned.", EditorStyles.wordWrappedMiniLabel);
            }
            if (GUILayout.Button("Save cloth profile")) { EndClothStroke(); AssetDatabase.SaveAssetIfDirty(clothProfile); }
            EditorGUILayout.HelpBox("Runtime: add MashBox / Cloth to your garment. Assign this profile, garment renderer and character collision root. Disable the old cloth component on that garment before using the new one.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawClothTransport()
        {
            var row = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            float x = row.x + 6, y = row.y + 5;
            bool valid = CanSimulateCloth || HasClothMotion;
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUI.Button(new Rect(x, y, 26, 26), new GUIContent(clothRunning ? "Ⅱ" : "▶", "Play / pause cloth simulation"))) ClothAction(() =>
                {
                    EndClothStroke(); clothPainting = false;
                    ResumeClothMotionFromPose();
                    if (clothSession == null && CanSimulateCloth) StartClothSession();
                    clothRunning = !clothRunning; clothLastTime = EditorApplication.timeSinceStartup;
                });
                x += 32;
                if (GUI.Button(new Rect(x, y, 26, 26), new GUIContent("■", "Stop and return to the animated mesh"))) ClothAction(() =>
                { StopClothSession(); SampleClothMotion(0); RefreshClothPickMesh(); clothSimulationTime = 0; });
                x += 32;
                if (GUI.Button(new Rect(x, y, 26, 26), new GUIContent("›", "Pause and advance one simulation step"))) ClothAction(() =>
                {
                    EndClothStroke(); clothPainting = false; clothRunning = false;
                    ResumeClothMotionFromPose();
                    if (clothSession == null && CanSimulateCloth) StartClothSession();
                    float dt = clothSession != null ? 1f / Mathf.Clamp(clothProfile.frequency, 15, 120) : 1f / Mathf.Max(1, clothClip.frameRate);
                    SampleClothMotion(clothAnimationTime + dt);
                    if (clothSession != null) { clothSession.Step(dt); clothSimulationTime += dt; }
                });
                x += 32;
                if (GUI.Button(new Rect(x, y, 26, 26), new GUIContent("↺", "Reset cloth and motion to the beginning"))) ClothAction(() =>
                {
                    clothRunning = false; SampleClothMotion(0); clothSimulationTime = 0;
                    clothSession?.Reset(); clothGrabParticle = -1; RefreshClothPickMesh();
                });
            }
            x += 36;
            DrawClothPoseButtons(ref x, y);
            DrawClothMotionSelector(ref x, y, row.xMax);
            var label = new GUIStyle(StudioTheme.Styles.miniLabel) { alignment = TextAnchor.MiddleRight };
            float timeWidth = row.width > 400 ? 130 : 75;
            float timeX = row.xMax - timeWidth - 8;
            if (timeX < x) return;
            var track = new Rect(x, row.y + 10, Mathf.Max(0, timeX - x - 12), 16);
            if (clothClip && !clothClip.legacy && track.width > 20)
            {
                EditorGUI.BeginChangeCheck();
                float next = GUI.HorizontalSlider(track, Mathf.Repeat(clothAnimationTime, Mathf.Max(.001f, clothClip.length)), 0, Mathf.Max(.001f, clothClip.length));
                if (EditorGUI.EndChangeCheck()) ClothAction(() =>
                {
                    // Scrubbing chooses a fresh pose; it does not pretend to replay cloth history.
                    clothRunning = false; SampleClothMotion(next); clothSimulationTime = 0;
                    clothSession?.Reset(); clothGrabParticle = -1; RefreshClothPickMesh();
                });
                GUI.Label(new Rect(timeX, y, timeWidth, 26), $"{Mathf.Repeat(clothAnimationTime, Mathf.Max(.001f, clothClip.length)):0.00} / {clothClip.length:0.00}s", label);
            }
            else
            {
                if (track.width > 20)
                {
                    EditorGUI.DrawRect(new Rect(track.x, track.center.y - 1, track.width, 2), new Color(.35f, .35f, .35f));
                    float cursor = track.x + Mathf.Repeat(clothSimulationTime, 10) / 10 * track.width;
                    EditorGUI.DrawRect(new Rect(cursor, track.y + 2, 2, 12), new Color(.5f, 1, .87f));
                }
                GUI.Label(new Rect(timeX, y, timeWidth, 26), $"{clothSimulationTime:0.00}s", label);
            }
        }
        internal static bool ClothMotionDiscontinuity(float previous,float next,float duration)
        {
            if(duration<=0)return false;
            return next<previous || Mathf.FloorToInt(previous/duration)!=Mathf.FloorToInt(next/duration);
        }
        private void SampleClothMotion(float time)
        {
            bool discontinuity = HasClothMotion && ClothMotionDiscontinuity(clothAnimationTime,time,clothClip.length);
            clothPoseOverride = false;
            clothAnimationTime = time;
            if (clothRig == null) return;
            // Restore unanimated channels when changing clips or returning to Rest Pose.
            if (clothAPoseMotion) { ApplyClothPose(2); return; }
            for (int i = 0; i < clothRig.Bones.Length; i++) clothRig.Rest.bones[i].Apply(clothRig.Bones[i]);
            if (HasClothMotion)
                clothClip.SampleAnimation(clothRig.Root, Mathf.Repeat(time, Mathf.Max(.001f, clothClip.length)));
            // Reset AFTER sampling the destination pose. Never sweep colliders or
            // carry inertia/interpolation across an animation wrap or rewind.
            if(discontinuity && clothSession!=null)
            {
                clothSession.Reset();clothGrabParticle=-1;clothSimulationTime=0;
            }
        }

        private void FillCloth(float value)
        {
            EndClothStroke(); Undo.RecordObject(clothProfile, "Fill cloth movement");
            for (int i = 0; i < clothProfile.motion.Length; i++) clothProfile.motion[i] = value;
            EditorUtility.SetDirty(clothProfile); viewport?.Repaint();
        }
        private void GenerateClothCapsules()
        {
            if (!clothProfile || clothRig == null) return;
            Undo.RecordObject(clothProfile, "Generate cloth body capsules");
            var result = new List<ClothBoneCapsule>();
            Transform Bone(HumanBodyBones bone)
            {
                if (clothRig.Animator && clothRig.Animator.isHuman) return clothRig.Animator.GetBoneTransform(bone);
                string key = bone.ToString();
                return clothRig.Bones.FirstOrDefault(b => b.name.Replace("_", "").Replace(" ", "").EndsWith(key, StringComparison.OrdinalIgnoreCase));
            }
            void Add(HumanBodyBones from, HumanBodyBones to, float ratio, float endRatio = 0)
            {
                var a = Bone(from); var b = Bone(to); if (!a || !b) return;
                var scale = a.lossyScale;
                float radiusScale = Mathf.Max(.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                var capsule = new ClothBoneCapsule { startBone = AnimationUtility.CalculateTransformPath(a, clothRig.Root.transform),
                    endBone = AnimationUtility.CalculateTransformPath(b, clothRig.Root.transform), radius = Mathf.Max(.015f, Vector3.Distance(a.position, b.position) * ratio) / radiusScale,
                    endRadius = endRatio > 0 ? Mathf.Max(.01f, Vector3.Distance(a.position, b.position) * endRatio) /
                        Mathf.Max(.0001f, Mathf.Abs(b.lossyScale.x), Mathf.Abs(b.lossyScale.y), Mathf.Abs(b.lossyScale.z)) : 0 };
                if (from == HumanBodyBones.LeftLowerLeg || from == HumanBodyBones.RightLowerLeg) FitClothAnkle(ref capsule, a, b);
                result.Add(capsule);
            }
            Add(HumanBodyBones.Hips, HumanBodyBones.Chest, .55f);
            if (result.Count == 0) Add(HumanBodyBones.Hips, HumanBodyBones.Spine, .8f);
            Add(HumanBodyBones.Chest, HumanBodyBones.Neck, .5f);
            Add(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, .23f); Add(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, .17f);
            Add(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, .23f); Add(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .17f);
            Add(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, .18f, .12f); Add(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .12f, .075f);
            Add(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, .18f, .12f); Add(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .12f, .075f);
            clothProfile.bodyCapsules = result.ToArray(); FitClothAnkleEnds(); EditorUtility.SetDirty(clothProfile);
            if (result.Count == 0) throw new InvalidOperationException("No humanoid bones found. Add body capsules manually using this character's bone paths.");
        }

        internal static void FitClothAnkle(ref ClothBoneCapsule capsule, Transform shin, Transform foot)
        {
            var toe = foot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != foot && t.name.IndexOf("toe", StringComparison.OrdinalIgnoreCase) >= 0);
            // Rig-local forward is only a fallback; toe direction follows the foot
            // even when fitting an already animated character.
            var forward = toe ? (toe.position - foot.position).normalized : shin.root.forward;
            float length = Vector3.Distance(shin.position, foot.position);
            capsule.endOffset = foot.InverseTransformVector(forward * (length * .025f));
            var scale = foot.lossyScale;
            capsule.endRadius = Mathf.Max(capsule.endRadius > 0 ? capsule.endRadius : capsule.radius,
                length * .13f / Mathf.Max(.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
        internal static void FitClothKnee(ref ClothBoneCapsule capsule, Transform shin, Transform foot)
        {
            float length = Vector3.Distance(shin.position, foot.position);
            var axis = (foot.position - shin.position).normalized;
            var forward = Vector3.ProjectOnPlane(shin.root.forward, axis).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.ProjectOnPlane(shin.root.up, axis).normalized;
            var scale = shin.lossyScale;
            float radiusScale = Mathf.Max(.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            // Expand rear coverage while preserving the previous front extent.
            float oldRadius = Mathf.Max(.001f, capsule.radius) * radiusScale;
            float newRadius = Mathf.Max(oldRadius, length * .15f);
            capsule.startOffset += shin.InverseTransformVector(-forward * (newRadius - oldRadius));
            capsule.radius = newRadius / radiusScale;
        }

        private void FitClothAnkleEnds()
        {
            if (!clothProfile || clothRig == null) return;
            Undo.RecordObject(clothProfile, "Fit cloth knee and ankle colliders");
            for (int i = 0; i < clothProfile.bodyCapsules.Length; i++)
            {
                var capsule = clothProfile.bodyCapsules[i];
                var a = clothRig.Root.transform.Find(capsule.startBone ?? "");
                var b = clothRig.Root.transform.Find(capsule.endBone ?? "");
                if (!a || !b || !b.name.EndsWith("Foot", StringComparison.OrdinalIgnoreCase)) continue;
                FitClothAnkle(ref capsule, a, b); FitClothKnee(ref capsule, a, b);
                clothProfile.bodyCapsules[i] = capsule;
                // The thigh's end and shin's start share the same knee bone.
                for (int j = 0; j < clothProfile.bodyCapsules.Length; j++)
                {
                    var thigh = clothProfile.bodyCapsules[j];
                    if (j == i || thigh.endBone != capsule.startBone) continue;
                    thigh.endOffset = capsule.startOffset; thigh.endRadius = capsule.radius;
                    clothProfile.bodyCapsules[j] = thigh;
                }
            }
            EditorUtility.SetDirty(clothProfile);
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void RefreshClothPickMesh()
        {
            clothPickVertices.Clear(); clothTriangles = Array.Empty<int>();
            clothPaintNeighbors = Array.Empty<int[]>(); clothSmoothTargets = Array.Empty<float>();
            var mesh = MashBoxClothSession.GetMesh(ClothRenderer); if (!mesh || !mesh.isReadable) return;
            if (ClothRenderer is SkinnedMeshRenderer skin) { skin.BakeMesh(clothPickMesh, false); clothPickMesh.GetVertices(clothPickVertices); }
            else mesh.GetVertices(clothPickVertices);
            clothTriangles = mesh.triangles;
            clothSeamVertices = MashBoxClothSeams.Build(mesh);
            if(clothProfile && clothProfile.motion.Length==mesh.vertexCount)
            {
                var repaired=(float[])clothProfile.motion.Clone();
                if(MashBoxClothSeams.Synchronize(repaired,clothSeamVertices))
                {
                    Undo.RecordObject(clothProfile,"Repair cloth seam weights");clothProfile.motion=repaired;EditorUtility.SetDirty(clothProfile);
                }
            }
            clothPaintNeighbors = BuildClothPaintNeighbors(mesh.vertexCount, clothTriangles, clothSeamVertices);
            clothSmoothTargets = new float[mesh.vertexCount];
        }
        internal static int[][] BuildClothPaintNeighbors(int count, int[] triangles, int[] seams = null)
        {
            var neighbors = new HashSet<int>[count];
            for (int i = 0; i < count; i++) neighbors[i] = new HashSet<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    int a = triangles[i + edge], b = triangles[i + (edge + 1) % 3];
                    if (a == b || a < 0 || b < 0 || a >= count || b >= count) continue;
                    if(seams!=null){a=seams[a];b=seams[b];}
                    if(a!=b){neighbors[a].Add(b); neighbors[b].Add(a);}
                }
            var result = new int[count][];
            for (int i = 0; i < count; i++) result[i] = neighbors[seams==null?i:seams[i]].OrderBy(n => n).ToArray();
            return result;
        }

        internal static void ClothSmoothTargets(float[] movement, int[][] neighbors, float[] targets)
        {
            // Snapshot the entire dab before writing any values, so smoothing
            // cannot propagate differently depending on vertex iteration order.
            for (int i = 0; i < movement.Length; i++)
            {
                float sum = movement[i];
                foreach (int neighbor in neighbors[i]) sum += movement[neighbor];
                targets[i] = sum / (neighbors[i].Length + 1);
            }
        }

        private void EndClothStroke()
        {
            EndClothGrab(); EndClothBrushResize();
            if (clothStroke && clothProfile) { EditorUtility.SetDirty(clothProfile); Undo.FlushUndoRecordObjects(); }
            clothStroke = false;
            if (clothControl != 0 && GUIUtility.hotControl == clothControl) GUIUtility.hotControl = 0;
            clothControl = 0;
        }
        private void BeforeClothScene(SceneView view)
        {
            HandleClothMaterialDrop(view);
            if (view.camera) view.camera.cameraType = CameraType.Preview;
            clothLighting?.ConfigureCamera(view); StudioPreviewLighting.ConfigureStudioBackground(view);
            if (clothLighting != null && view.camera)
            {
                // Match Painting's camera-relative key/fill: orbiting must not
                // turn the inspected surface away from both studio lights.
                clothLighting.Key.transform.rotation = view.camera.transform.rotation * Quaternion.Euler(20, -25, 0);
                clothLighting.Fill.transform.rotation = view.camera.transform.rotation * Quaternion.Euler(-15, 30, 0);
            }
        }
        private void DrawClothScene(SceneView view)
        {
            DrawClothWireframe();
            if (viewport.modelGridVisible) viewport.DrawGroundGrid();
            if (viewport.ModelCanvasRect.Contains(Event.current.mousePosition)) HandleClothDrop();
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.F && !EditorGUIUtility.editingTextField) { FrameCloth(); evt.Use(); }
            if (DrawClothClippingSelector()) return;
            if (!ClothVisible) return;
            // SceneView.Repaint bypasses Studio's image-cache invalidation. Mark overlays
            // dirty on input, including leaving the surface, instead of repainting while idle.
            if (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag || evt.type == EventType.MouseLeaveWindow ||
                ((evt.type == EventType.KeyDown || evt.type == EventType.KeyUp) && (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift || evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt)))
                if (clothPainting || clothTool == 1) viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay);
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { EndClothStroke(); clothPainting = false; clothTool = 0; viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); evt.Use(); }
            if (evt.rawType == EventType.MouseUp || evt.type == EventType.Ignore) EndClothStroke();
            if (clothProfile && clothShowColliders && evt.type == EventType.Repaint) DrawClothCapsules();
            bool valid = clothProfile && clothProfile.Matches(MashBoxClothSession.GetMesh(ClothRenderer));
            if (!valid) return;
            var matrix = ClothRenderer.transform.localToWorldMatrix;
            var vertices = clothSession != null ? clothSession.Vertices : clothPickVertices;
            if (clothShowWeights && clothPainting && evt.type == EventType.Repaint)
            { DrawClothWeightSurface(vertices, matrix); DrawClothWireframe(); }
            int id = GUIUtility.GetControlID("MashBoxClothBrush".GetHashCode(), FocusType.Passive);
            bool toolActive = (clothPainting || (clothTool == 1 && clothSession != null)) &&
                viewport.ModelCanvasRect.Contains(evt.mousePosition) && !viewport.IsOverPreviewTimeline(evt.mousePosition) && !viewport.IsOverViewportControls(evt.mousePosition);
            if (HandleClothBrushResize(view, toolActive)) return;
            if (HandleClothGrabDrag(evt)) return;
            if (toolActive && !evt.alt && evt.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (toolActive && !evt.alt && (evt.type == EventType.MouseMove || evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag || evt.type == EventType.Repaint))
            {
                var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (ClothHit(ray, vertices, matrix, out var point, out var normal))
                {
                    if (clothPainting)
                    {
                        if (evt.type == EventType.Repaint)
                            using (new Handles.DrawingScope(evt.shift ? Color.cyan : Color.yellow, Matrix4x4.identity)) Handles.DrawWireDisc(point, normal, clothBrushRadius);
                        if (evt.button == 0 && ((evt.type == EventType.MouseDown && HandleUtility.nearestControl == id) || (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id)))
                        {
                            if (!clothStroke) { Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(clothProfile, evt.shift ? "Smooth cloth movement" : "Paint cloth movement"); clothStroke = true; clothControl = id; GUIUtility.hotControl = id; }
                            if (evt.shift) ClothSmoothTargets(clothProfile.motion, clothPaintNeighbors, clothSmoothTargets);
                            // Restrict brush to the hit surface's depth band to avoid painting through the garment.
                            for (int i = 0; i < vertices.Count; i++)
                            {
                                var offset = matrix.MultiplyPoint3x4(vertices[i]) - point;
                                float falloff = ClothPaintFalloff(offset, normal, ray.direction, clothBrushRadius, clothPaintThrough);
                                if (falloff <= 0) continue;
                                float value = evt.shift ? clothSmoothTargets[i] : clothBrushValue;
                                float next = Mathf.Lerp(clothProfile.motion[i], value, clothBrushStrength * falloff);
                                clothProfile.motion[i] = !evt.shift && next < .0005f ? 0 : next;
                            }
                            MashBoxClothSeams.Synchronize(clothProfile.motion,clothSeamVertices);
                            EditorUtility.SetDirty(clothProfile); viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Overlay); evt.Use();
                        }
                    }
                    else
                    {
                        if (evt.type == EventType.Repaint) DrawClothGrabSphere(point);
                        if (evt.type == EventType.MouseDown && evt.button == 0 && HandleUtility.nearestControl == id)
                        {
                            if (clothSession.Solver.BeginGrab(point, clothGrabRadius) > 0)
                            {
                                clothGrabPosition = point;
                                clothGrabPlane = new Plane(view.camera.transform.forward, point);
                                clothGrabDragging = true; clothControl = id; GUIUtility.hotControl = id;
                            }
                            evt.Use();
                        }
                    }
                }
            }
        }
        private bool ClothHit(Ray ray, IReadOnlyList<Vector3> vertices, Matrix4x4 matrix, out Vector3 hit, out Vector3 normal)
        {
            hit = normal = default; float nearest = float.MaxValue;
            var inverse = matrix.inverse;
            var origin = inverse.MultiplyPoint3x4(ray.origin);
            // Keep the direction unnormalized so intersection distances stay in world units.
            var direction = inverse.MultiplyVector(ray.direction);
            float winding = Mathf.Sign(matrix.determinant);
            var normalMatrix = inverse.transpose;
            for (int i = 0; i < clothTriangles.Length; i += 3)
            {
                Vector3 a = vertices[clothTriangles[i]], b = vertices[clothTriangles[i + 1]], c = vertices[clothTriangles[i + 2]];
                var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(direction, e2); float det = Vector3.Dot(e1, p);
                if (det * winding <= 1e-9f) continue;
                var t = origin - a; float u = Vector3.Dot(t, p) / det; if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(t, e1); float v = Vector3.Dot(direction, q) / det; if (v < 0 || u + v > 1) continue;
                float distance = Vector3.Dot(e2, q) / det;
                if (distance < 0 || distance >= nearest) continue;
                nearest = distance; hit = ray.GetPoint(distance); normal = normalMatrix.MultiplyVector(Vector3.Cross(e1, e2)).normalized * winding;
            }
            return nearest < float.MaxValue;
        }
        private void DrawClothCapsules()
        {
            Handles.color = new Color(.3f, 1, .7f, .7f);
            foreach (var capsule in clothProfile.bodyCapsules ?? Array.Empty<ClothBoneCapsule>())
            {
                var a = clothRig.Root.transform.Find(capsule.startBone ?? ""); var b = clothRig.Root.transform.Find(capsule.endBone ?? ""); if (!a || !b) continue;
                var shape = capsule.ToWorld(a, b);
                var direction = (shape.b - shape.a).normalized; if (direction.sqrMagnitude < .1f) direction = Vector3.up;
                float r = shape.radius, endRadius = shape.endRadius;
                float length = Vector3.Distance(shape.a, shape.b);
                var side = Vector3.Cross(direction, Vector3.up).normalized; if (side.sqrMagnitude < .1f) side = Vector3.right;
                var across = Vector3.Cross(direction, side).normalized;
                if (length <= Mathf.Abs(endRadius - r) + .0000001f)
                {
                    var center = endRadius > r ? shape.b : shape.a;
                    float radius = Mathf.Max(r, endRadius);
                    Handles.DrawWireDisc(center, direction, radius);
                    Handles.DrawWireDisc(center, side, radius);
                    Handles.DrawWireDisc(center, across, radius);
                    continue;
                }
                float slope = (endRadius - r) / length;
                float cosine = Mathf.Sqrt(1 - slope * slope);
                Handles.DrawWireDisc(shape.a - direction * (r * slope), direction, r * cosine);
                Handles.DrawWireDisc(shape.b - direction * (endRadius * slope), direction, endRadius * cosine);
                // Four tangent rails and two cap profiles show the rounded cone.
                foreach (var radial in new[] { side, -side, across, -across })
                {
                    var normal = radial * cosine - direction * slope;
                    Handles.DrawLine(shape.a + normal * r, shape.b + normal * endRadius);
                }
                float angle = Mathf.Asin(slope) * Mathf.Rad2Deg;
                Handles.DrawWireArc(shape.a, across, side * cosine - direction * slope, 180 - 2 * angle, r);
                Handles.DrawWireArc(shape.b, across, -side * cosine - direction * slope, 180 + 2 * angle, endRadius);
                Handles.DrawWireArc(shape.a, -side, across * cosine - direction * slope, 180 - 2 * angle, r);
                Handles.DrawWireArc(shape.b, -side, -across * cosine - direction * slope, 180 + 2 * angle, endRadius);
            }
        }
    }
}
