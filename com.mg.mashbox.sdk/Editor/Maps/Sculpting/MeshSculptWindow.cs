using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.Maps.TerrainSystem;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed class MeshSculptWindow : EditorWindow
    {
        const string ActiveModifierSessionKey = "MashBoxSDK.MeshSculpt.ActiveModifier";
        static readonly float[] BrushInfluenceLevels = { 0.75f, 0.5f, 0.25f };
        static MeshSculptWindow s_ActiveSceneToolOwner;

        enum DirectionMode { SurfaceNormal, WorldUp, Custom }

        [SerializeField] MeshSculptModifier m_Modifier;
        [SerializeField] MeshSculptModifier.SculptMode m_Mode;
        [SerializeField] MeshSculptModifier.StrokeSpace m_StrokeSpace = MeshSculptModifier.StrokeSpace.TargetLocal;
        [SerializeField] DirectionMode m_DirectionMode;
        [SerializeField] Vector3 m_CustomDirection = Vector3.up;
        [SerializeField] float m_Radius = 1f;
        [SerializeField] float m_Strength = 0.1f;
        [SerializeField] float m_Falloff = 2f;
        [SerializeField, Range(0.05f, 1f)] float m_Spacing = 0.2f;
        [SerializeField] GameObject m_SeamTargetRoot;
        [SerializeField] float m_SeamThreshold = 0.5f;
        [SerializeField] float m_SeamNormalBlend = 0.5f;
        [SerializeField] float m_SeamSurfaceOffset = -0.002f;
        [SerializeField] bool m_SeamVerticesOnly = true;
        [SerializeField] bool m_SeamAboveOnly = true;
        [SerializeField] bool m_LoftSeamVerticesOnly = true;
        bool EditingSeamLoft => m_Modifier != null && m_Modifier.LinkedLoft != null;
        [SerializeField] float m_SeamLowerDepth = 0.05f;
        [SerializeField] float m_SeamMinimumRise = 0.02f;
        [SerializeField] bool m_SeamHeightOnly;
        MeshSeamFitBrush m_SeamBrush;
        MGTerrain[] m_SeamTerrains;
        string m_SeamStatus;
        bool IsSeamFit => m_Mode == MeshSculptModifier.SculptMode.SeamFit;
        Vector2 m_Scroll;
        bool m_IsSculpting;
        bool m_HasLastStrokePosition;
        Vector3 m_LastStrokePosition;
        int m_UndoGroup = -1;
        bool m_SceneToolActive;
        bool m_SceneCameraRightMouseHeld;
        bool m_IsAdjustingBrush;
        Vector2 m_BrushAdjustMousePosition;
        bool m_HasBrushAdjustSurface;
        Vector3 m_BrushAdjustHitPoint;
        Vector3 m_BrushAdjustHitNormal;
        GameObject m_SculptPickingObject;
        MeshCollider m_SculptPickingCollider;
        MeshFilter m_SculptPickingTarget;
        int m_SculptPickingGenerationVersion = -1;
        readonly HashSet<MeshSculptModifier> m_StrokeModifiers = new HashSet<MeshSculptModifier>();

        public static void ShowWindow() => GetWindow<MeshSculptWindow>("Mesh Sculpt");

        internal static bool HasActiveSceneTool =>
            s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner.m_SceneToolActive;

        internal static void DeactivateActiveSceneTool()
        {
            if (s_ActiveSceneToolOwner != null)
                s_ActiveSceneToolOwner.DeactivateSceneTool();
        }

        void OnEnable()
        {
            m_Mode = (MeshSculptModifier.SculptMode)MBEditorToolState.SculptMode;
            ClearActiveModifier();
            MBEditorToolState.SculptModeChanged -= OnSharedSculptModeChanged;
            MBEditorToolState.SculptModeChanged += OnSharedSculptModeChanged;
            EditorApplication.hierarchyChanged += InvalidateSeamTerrains;
            if (MBEditorToolState.ActiveEditing)
                ActivateSceneTool();
        }

        void OnDisable()
        {
            MBEditorToolState.SculptModeChanged -= OnSharedSculptModeChanged;
            EditorApplication.hierarchyChanged -= InvalidateSeamTerrains;
            DeactivateSceneTool();
            ClearActiveModifier();
        }
        void OnGUI() => Draw();

        public void ActivateSceneTool()
        {
            if (s_ActiveSceneToolOwner != null && s_ActiveSceneToolOwner != this)
                s_ActiveSceneToolOwner.DeactivateSceneTool();
            s_ActiveSceneToolOwner = this;
            if (m_SceneToolActive) return;
            m_SceneToolActive = true;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            MeshSeamFitBrush.UndoMeshRebuilt += OnSculptUndoRebuilt;
        }

        public void DeactivateSceneTool()
        {
            // StopStroke releases Unity's global IMGUI hot control. Embedded hosts
            // call this for every inactive authoring tool, so repeating cleanup
            // here would cancel unrelated buttons between MouseDown and MouseUp.
            if (!m_SceneToolActive)
            {
                ClearActiveModifier();
                return;
            }

            m_SceneToolActive = false;
            m_SceneCameraRightMouseHeld = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            MeshSeamFitBrush.UndoMeshRebuilt -= OnSculptUndoRebuilt;
            if (s_ActiveSceneToolOwner == this)
                s_ActiveSceneToolOwner = null;
            StopStroke();
            DestroySculptPickingCollider();
            ClearActiveModifier();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (!embeddedInParentWindow) m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Non-Destructive Mesh Sculpt", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Brush strokes are stored as instructions and replayed from the clean mesh. A linked loft replays them after every regeneration.", MessageType.Info);

            m_Modifier = (MeshSculptModifier)EditorGUILayout.ObjectField("Sculpt Modifier", m_Modifier, typeof(MeshSculptModifier), true);
            MBEditorToolState.SculptableOnly = EditorGUILayout.Toggle(new GUIContent("Sculptable Only",
                "Ignore other objects and sculpt through them. Turn off to make additional objects sculptable with Shift+Click."), MBEditorToolState.SculptableOnly);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection")) UseSelection();
                if (GUILayout.Button("Create On Selection")) CreateOnSelection();
            }

            if (m_Modifier != null)
            {
                EditorGUILayout.ObjectField("Target Mesh", m_Modifier.Target, typeof(MeshFilter), true);
                MultiSplineLoft newLoft = (MultiSplineLoft)EditorGUILayout.ObjectField("Linked Loft", m_Modifier.LinkedLoft, typeof(MultiSplineLoft), true);
                if (newLoft != m_Modifier.LinkedLoft)
                {
                    MultiSplineLoft previousLoft = m_Modifier.LinkedLoft;
                    if (previousLoft != null && previousLoft.SculptModifier == m_Modifier)
                    {
                        Undo.RecordObject(previousLoft, "Unlink Sculpt Modifier");
                        previousLoft.SculptModifier = null;
                        EditorUtility.SetDirty(previousLoft);
                    }
                    Undo.RecordObject(m_Modifier, "Link Sculpt Modifier");
                    m_Modifier.LinkToLoft(newLoft);
                    if (newLoft != null)
                    {
                        Undo.RecordObject(newLoft, "Link Sculpt Modifier");
                        newLoft.SculptModifier = m_Modifier;
                    }
                    EditorUtility.SetDirty(m_Modifier);
                }
                EditorGUILayout.LabelField("Recorded Strokes", m_Modifier.StrokeCount.ToString());
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            int requestedIndex = GUILayout.Toolbar(IsSeamFit ? 3 : (int)m_Mode, new[] { "Displace", "Smooth", "Flatten", "Seam Fit" });
            var requestedMode = requestedIndex == 3 ? MeshSculptModifier.SculptMode.SeamFit : (MeshSculptModifier.SculptMode)requestedIndex;
            if (requestedMode != m_Mode)
            {
                ClearActiveModifier();
                m_Mode = requestedMode;
                MBEditorToolState.SculptMode = (MBSculptMode)m_Mode;
            }
            if (!IsSeamFit)
                m_StrokeSpace = (MeshSculptModifier.StrokeSpace)EditorGUILayout.EnumPopup(new GUIContent("Memory Space", "World stays at the same scene position. Target Local follows the sculpted object."), m_StrokeSpace);
            m_Radius = EditorGUILayout.Slider("Radius", m_Radius, 0.01f, 20f);
            m_Strength = m_Mode == MeshSculptModifier.SculptMode.Displace
                ? EditorGUILayout.Slider("Strength", m_Strength, -2f, 2f)
                : EditorGUILayout.Slider("Strength", Mathf.Abs(m_Strength), 0.01f, 1f);
            m_Falloff = EditorGUILayout.Slider("Falloff", m_Falloff, 0.1f, 8f);
            m_Spacing = EditorGUILayout.Slider("Stroke Spacing", m_Spacing, 0.05f, 1f);

            if (IsSeamFit)
            {
                EditorGUILayout.LabelField("Fit Direction", EditingSeamLoft ? "Loft to MG Terrain" : "MG Terrain to Loft");
                m_SeamTargetRoot = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Target Root", "Optional target mesh or parent. Empty uses MG Terrains when editing a loft, and lofts when editing terrain."), m_SeamTargetRoot, typeof(GameObject), true);
                m_SeamThreshold = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Snap Distance (m)", "Maximum world-space distance from a terrain vertex to the target surface."), m_SeamThreshold), 0.001f, 100f);
                if (EditingSeamLoft)
                {
                    bool vertices = EditorGUILayout.Popup("Snap To", m_LoftSeamVerticesOnly ? 1 : 0, new[] { "Nearest Surface", "Nearest Vertex" }) == 1;
                    if (vertices != m_LoftSeamVerticesOnly) m_SeamBrush = null;
                    m_LoftSeamVerticesOnly = vertices;
                }
                else
                {
                    int snapMode = EditorGUILayout.Popup("Snap To", m_SeamAboveOnly ? 2 : m_SeamVerticesOnly ? 1 : 0,
                        new[] { "Nearest Surface", "Nearest Vertex", "Nearest Vertex Above" });
                    bool verticesOnly = snapMode != 0, aboveOnly = snapMode == 2;
                    if (verticesOnly != m_SeamVerticesOnly || aboveOnly != m_SeamAboveOnly) m_SeamBrush = null;
                    m_SeamVerticesOnly = verticesOnly;
                    m_SeamAboveOnly = aboveOnly;
                    if (m_SeamAboveOnly)
                    {
                        m_SeamMinimumRise = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Minimum Rise (m)", "Prefer vertices at least this far above the current terrain vertex. Repeated passes climb past nearly reached shoulder vertices. Surface Offset is accounted for automatically."), m_SeamMinimumRise), 0.0001f, 100f);
                        EditorGUILayout.HelpBox("Repeated passes climb to the next higher vertex, skipping steps already nearly reached. Increase Minimum Rise to skip farther upward; increase Snap Distance to reach more of the shoulder. At the last reachable step, the brush finishes fitting the remaining gap.", MessageType.None);
                    }
                }
                m_SeamHeightOnly = EditorGUILayout.Toggle(new GUIContent("Height Only", "Keep the edited mesh's local X/Z fixed. Off allows full 3D fitting."), m_SeamHeightOnly);
                m_SeamNormalBlend = EditorGUILayout.Slider("Normal Blend", m_SeamNormalBlend, 0f, 1f);
                m_SeamSurfaceOffset = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Surface Offset (m)", "Offset along the target normal after snapping. A small negative offset keeps terrain just under the loft to reduce flickering. Zero fits exactly to the surface."), m_SeamSurfaceOffset), -m_SeamThreshold, m_SeamThreshold);
                m_SeamLowerDepth = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Ctrl Lower Depth (m)", "Maximum lowering per brush sample at full strength. Ctrl-drag lowers vertically in world space without needing a target mesh."), m_SeamLowerDepth), 0.001f, 1f);
                EditorGUILayout.HelpBox("Shift-click terrain or a loft to choose what is edited. Paint its shoulder or face to fit toward the opposite surface. Surface snapping follows triangles; vertex snapping can bunch vertices on coarse meshes. Changes are baked in source local space. Loft regeneration replays them while its vertex layout stays compatible.", MessageType.Info);
                if (!string.IsNullOrEmpty(m_SeamStatus)) EditorGUILayout.HelpBox(m_SeamStatus, MessageType.None);
            }
            else if (m_Mode != MeshSculptModifier.SculptMode.Smooth)
            {
                m_DirectionMode = (DirectionMode)EditorGUILayout.EnumPopup("Direction", m_DirectionMode);
                if (m_DirectionMode == DirectionMode.Custom)
                    m_CustomDirection = EditorGUILayout.Vector3Field("Custom World Direction", m_CustomDirection);
            }

            EditorGUILayout.HelpBox(IsSeamFit
                ? "Shift+Click terrain or a loft to choose the sculptable source. Drag fits it to the target; Ctrl+Left-drag gently lowers the selected mesh. Shift smooths; Ctrl+Shift adds noise. Ctrl+Middle-drag adjusts radius and strength. Undo restores a whole drag."
                : "Drag to sculpt. Ctrl inverts the active brush, Shift temporarily smooths, and Ctrl+Shift temporarily adds deterministic noise. Ctrl+Middle-drag adjusts the brush: horizontal changes radius and vertical changes strength.", MessageType.None);

            using (new EditorGUI.DisabledScope(m_Modifier == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Remove Last"))
                    {
                        Undo.RecordObject(m_Modifier, "Remove Sculpt Stroke");
                        m_Modifier.RemoveLastStroke();
                        m_Modifier.Rebuild();
                        EditorUtility.SetDirty(m_Modifier);
                    }
                    if (GUILayout.Button("Clear All"))
                    {
                        Undo.RecordObject(m_Modifier, "Clear Sculpt Strokes");
                        m_Modifier.ClearStrokes();
                        m_Modifier.Rebuild();
                        EditorUtility.SetDirty(m_Modifier);
                    }
                    if (GUILayout.Button("Replay")) m_Modifier.Rebuild();
                }
            }

            if (!embeddedInParentWindow) EditorGUILayout.EndScrollView();
        }

        void OnSharedSculptModeChanged()
        {
            InvalidateSeamTerrains();
            MeshSculptModifier.SculptMode mode = (MeshSculptModifier.SculptMode)MBEditorToolState.SculptMode;
            if (m_Mode != mode)
                ClearActiveModifier();
            m_Mode = mode;
            Repaint();
            SceneView.RepaintAll();
        }

        internal void UseSelection()
        {
            m_SeamBrush = null;
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
                return;

            MeshSculptModifier selectedModifier = selected.GetComponent<MeshSculptModifier>() ?? selected.GetComponentInParent<MeshSculptModifier>();
            if (selectedModifier == null)
            {
                MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>() ?? selected.GetComponentInParent<MultiSplineLoft>();
                if (loft != null) selectedModifier = loft.SculptModifier;
            }
            if (selectedModifier == null)
            {
                ClearActiveModifier();
                CreateOnSelection();
                if (m_Modifier != null)
                    m_Modifier.Rebuild();
                return;
            }

            m_Modifier = selectedModifier;
            EnsureSelectedModifierTargetsLoft(selected, selectedModifier);
            m_Modifier.Rebuild();
        }

        void ClearActiveModifier()
        {
            if (m_Modifier != null)
            {
                if (m_SceneToolActive)
                    StopStroke();
                DestroySculptPickingCollider();
            }
            m_Modifier = null;
            m_StrokeModifiers.Clear();
            SessionState.EraseString(ActiveModifierSessionKey);
        }

        static void EnsureSelectedModifierTargetsLoft(GameObject selected, MeshSculptModifier modifier)
        {
            if (selected == null || modifier == null) return;
            MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>()
                ?? selected.GetComponentInParent<MultiSplineLoft>()
                ?? modifier.GetComponent<MultiSplineLoft>();
            if (loft == null) return;

            MeshFilter loftMesh = loft.GetComponent<MeshFilter>();
            if (modifier.LinkedLoft != loft || modifier.Target != loftMesh)
            {
                Undo.RecordObject(modifier, "Link Sculpt Modifier To Loft");
                modifier.LinkToLoft(loft);
                EditorUtility.SetDirty(modifier);
            }
            if (loft.SculptModifier != modifier)
            {
                Undo.RecordObject(loft, "Link Sculpt Modifier To Loft");
                loft.SculptModifier = modifier;
                EditorUtility.SetDirty(loft);
            }
        }

        void CreateOnSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return;
            MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>() ?? selected.GetComponentInParent<MultiSplineLoft>();
            MeshFilter meshFilter = (loft != null ? loft.gameObject : selected).GetComponent<MeshFilter>();
            if (meshFilter != null)
                CreateOrActivateModifier(meshFilter);
        }

        static bool IsUsableSceneModifier(MeshSculptModifier modifier)
        {
            return modifier != null
                && modifier.gameObject != null
                && modifier.gameObject.scene.IsValid()
                && modifier.gameObject.scene.isLoaded
                && modifier.Target != null;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            sceneView.wantsMouseMove = true;
            Event current = Event.current;
            MBEditorToolVisuals.RepaintBrushModifiers(current, sceneView);
            if (current.type == EventType.MouseDown && current.button == 1)
                m_SceneCameraRightMouseHeld = true;
            else if (current.type == EventType.MouseUp && current.button == 1)
                m_SceneCameraRightMouseHeld = false;

            if (current.type == EventType.MouseLeaveWindow || current.type == EventType.Ignore)
            {
                m_SceneCameraRightMouseHeld = false;
                StopStroke();
                return;
            }

            bool cameraNavigation = m_SceneCameraRightMouseHeld || Tools.viewToolActive;
            if (cameraNavigation)
            {
                if (m_IsSculpting || m_IsAdjustingBrush)
                    StopStroke();
                return;
            }

            int controlId = GUIUtility.GetControlID("MeshSculptBrush".GetHashCode(), FocusType.Passive);
            if (MBEditorToolState.SculptableOnly && current.type == EventType.Layout && !current.alt)
                HandleUtility.AddDefaultControl(controlId);
            if (m_Modifier != null && m_Modifier.Target != null
                && HandleBrushAdjustment(current, controlId, sceneView))
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            RaycastHit hit;
            MeshFilter hitMeshFilter;
            if (m_IsSculpting)
            {
                if (!TryRaycastSculptSurface(ray, out hit, out hitMeshFilter))
                {
                    if (current.type == EventType.MouseUp && current.button == 0)
                    {
                        StopStroke();
                        current.Use();
                    }
                    return;
                }
            }
            else if (!TryRaycastSculptSurface(ray, out hit, out hitMeshFilter))
            {
                return;
            }

            if (MBEditorToolVisuals.FocusBrushSurface(current, sceneView, hit.point, m_Radius))
                return;

            MeshSculptModifier hoveredModifier = ResolveSculptModifier(hitMeshFilter);
            if (!m_IsSculpting && MBEditorToolState.SculptableOnly && hoveredModifier != null)
                ActivateModifier(hoveredModifier);
            if (current.type == EventType.Layout && !current.alt)
                HandleUtility.AddDefaultControl(controlId);
            if (m_IsSculpting && hoveredModifier != null && hoveredModifier != m_Modifier)
                SwitchModifierDuringStroke(hoveredModifier);

            bool canSculptHit = m_Modifier != null
                && m_Modifier.Target != null
                && hitMeshFilter == m_Modifier.Target;

            if (!canSculptHit)
            {
                if (MBEditorToolState.SculptableOnly)
                    return;
                DrawBrushFalloff(hit.point, hit.normal, new Color(1f, 0.55f, 0.12f, 0.95f));
                DrawActivationLabel(hit);
                sceneView.Repaint();

                if (current.type == EventType.MouseDown
                    && current.button == 0
                    && current.shift
                    && !current.alt)
                {
                    CreateOrActivateModifier(hitMeshFilter);
                    current.Use();
                    sceneView.Repaint();
                }
                return;
            }

            MeshSculptModifier.SculptMode previewMode = GetStrokeMode(current.control, current.shift);
            Color brushColor = previewMode == MeshSculptModifier.SculptMode.Smooth
                ? Color.green
                : previewMode == MeshSculptModifier.SculptMode.Noise
                    ? Color.magenta
                    : previewMode == MeshSculptModifier.SculptMode.Flatten ? Color.yellow : Color.cyan;
            if (IsSeamFit && current.control && !current.shift) brushColor = new Color(1f, 0.4f, 0.2f);
            DrawBrushFalloff(hit.point, hit.normal, brushColor);
            MBEditorToolVisuals.DrawBrushAction(GetBrushAction(previewMode, current.control));
            sceneView.Repaint();

            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                m_IsSculpting = true;
                m_HasLastStrokePosition = false;
                Undo.IncrementCurrentGroup();
                m_UndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Mesh Sculpt Stroke");
                GUIUtility.hotControl = controlId;
                RecordStroke(hit, current.control, current.shift);
                current.Use();
            }
            else if (m_IsSculpting && current.type == EventType.MouseDrag && current.button == 0)
            {
                if (!m_HasLastStrokePosition || Vector3.Distance(hit.point, m_LastStrokePosition) >= m_Radius * m_Spacing)
                    RecordStroke(hit, current.control, current.shift);
                current.Use();
            }
            else if (m_IsSculpting && current.type == EventType.MouseUp && current.button == 0)
            {
                StopStroke();
                current.Use();
            }
        }

        void DrawActivationLabel(RaycastHit hit)
        {
            MBEditorToolVisuals.DrawBrushAction("Shift+Click · Make Sculptable");
        }

        string GetBrushAction(MeshSculptModifier.SculptMode mode, bool control)
        {
            switch (mode)
            {
                case MeshSculptModifier.SculptMode.Smooth: return "Smooth";
                case MeshSculptModifier.SculptMode.Noise: return "Noise";
                case MeshSculptModifier.SculptMode.Flatten: return "Flatten";
                case MeshSculptModifier.SculptMode.SeamFit:
                    if (control) return "Seam Fit · Lower";
                    if (EditingSeamLoft) return "Seam Fit · Fit Loft to Terrain";
                    return m_SeamAboveOnly ? "Seam Fit · Raise to Higher Surface" : "Seam Fit · Fit to Surface";
                default: return control ? "Lower / Push In" : "Raise / Push Out";
            }
        }

        void CreateOrActivateModifier(MeshFilter meshFilter)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;
            if (IsSeamFit && !IsTerrainSurface(meshFilter) && meshFilter.GetComponentInParent<MultiSplineLoft>() == null) return;

            MultiSplineLoft loft = meshFilter.GetComponent<MultiSplineLoft>()
                ?? meshFilter.GetComponentInParent<MultiSplineLoft>();
            GameObject targetObject = loft != null ? loft.gameObject : meshFilter.gameObject;
            MeshFilter targetMesh = loft != null ? loft.GetComponent<MeshFilter>() : meshFilter;
            if (targetMesh == null || targetMesh.sharedMesh == null)
                return;

            MeshSculptModifier modifier = ResolveSculptModifier(targetMesh);
            if (modifier == null)
                modifier = Undo.AddComponent<MeshSculptModifier>(targetObject);

            Undo.RecordObject(modifier, "Configure Sculpt Modifier");
            if (loft != null)
            {
                modifier.LinkToLoft(loft);
                if (loft.SculptModifier != modifier)
                {
                    Undo.RecordObject(loft, "Link Sculpt Modifier");
                    loft.SculptModifier = modifier;
                    EditorUtility.SetDirty(loft);
                }
            }
            else
            {
                modifier.SetTarget(targetMesh);
                if (targetObject.GetComponent<Collider>() == null)
                {
                    MeshCollider collider = Undo.AddComponent<MeshCollider>(targetObject);
                    collider.sharedMesh = targetMesh.sharedMesh;
                }
            }

            EditorUtility.SetDirty(modifier);
            ActivateModifier(modifier);
            modifier.Rebuild();
            RefreshSculptPickingCollider();
        }

        void ActivateModifier(MeshSculptModifier modifier)
        {
            if (!IsUsableSceneModifier(modifier) || modifier == m_Modifier)
                return;

            StopStroke();
            DestroySculptPickingCollider();
            m_Modifier = modifier;
            m_SeamBrush = null;
            EnsureSculptPickingCollider();
            Repaint();
        }

        void SwitchModifierDuringStroke(MeshSculptModifier modifier)
        {
            if (!m_IsSculpting || !IsUsableSceneModifier(modifier) || modifier == m_Modifier)
                return;

            if (m_Modifier != null)
                m_Modifier.FinalizeStrokePreview();
            DestroySculptPickingCollider();
            m_Modifier = modifier;
            m_SeamBrush = null;
            EnsureSculptPickingCollider();
            m_HasLastStrokePosition = false;
            Repaint();
        }

        static MeshSculptModifier ResolveSculptModifier(MeshFilter meshFilter)
        {
            if (meshFilter == null)
                return null;

            MultiSplineLoft loft = meshFilter.GetComponent<MultiSplineLoft>()
                ?? meshFilter.GetComponentInParent<MultiSplineLoft>();
            if (loft != null)
                return loft.SculptModifier != null
                    ? loft.SculptModifier
                    : loft.GetComponent<MeshSculptModifier>();

            return meshFilter.GetComponent<MeshSculptModifier>()
                ?? meshFilter.GetComponentInParent<MeshSculptModifier>();
        }

        void DrawBrushFalloff(Vector3 center, Vector3 normal, Color brushColor)
        {
            Handles.color = brushColor;
            Handles.DrawWireDisc(center, normal, m_Radius);

            // Stroke influence is Pow(1 - distance / radius, falloff). These rings
            // mark the 75%, 50%, and 25% influence contours of that exact curve.
            float exponent = Mathf.Max(0.01f, m_Falloff);
            for (int i = 0; i < BrushInfluenceLevels.Length; i++)
            {
                float normalizedRadius = 1f - Mathf.Pow(BrushInfluenceLevels[i], 1f / exponent);
                Color ringColor = brushColor;
                ringColor.a = Mathf.Lerp(0.8f, 0.25f, i / (float)(BrushInfluenceLevels.Length - 1));
                Handles.color = ringColor;
                Handles.DrawWireDisc(center, normal, m_Radius * normalizedRadius);
            }
        }

        bool HandleBrushAdjustment(Event current, int controlId, SceneView sceneView)
        {
            if (current.type == EventType.MouseDown && current.button == 2 && current.control && !current.alt)
            {
                StopStroke();
                m_IsAdjustingBrush = true;
                m_BrushAdjustMousePosition = current.mousePosition;
                CaptureBrushAdjustmentSurface(current.mousePosition);
                GUIUtility.hotControl = controlId;
                EditorGUIUtility.SetWantsMouseJumping(1);
                current.Use();
            }
            else if (m_IsAdjustingBrush && current.type == EventType.MouseDrag && current.button == 2)
            {
                m_Radius = Mathf.Clamp(m_Radius * Mathf.Exp(current.delta.x * 0.01f), 0.01f, 20f);
                float minimumStrength = m_Mode == MeshSculptModifier.SculptMode.Displace ? -2f : 0.01f;
                float maximumStrength = m_Mode == MeshSculptModifier.SculptMode.Displace ? 2f : 1f;
                m_Strength = Mathf.Clamp(m_Strength - current.delta.y * 0.01f, minimumStrength, maximumStrength);
                current.Use();
                Repaint();
                sceneView.Repaint();
            }
            else if (m_IsAdjustingBrush && current.type == EventType.MouseUp && current.button == 2)
            {
                EndBrushAdjustment();
                current.Use();
                Repaint();
                sceneView.Repaint();
                return true;
            }

            if (!m_IsAdjustingBrush)
                return false;

            DrawBrushAdjustmentGizmo();

            Handles.BeginGUI();
            Rect panelRect = new Rect(
                m_BrushAdjustMousePosition.x + 18f,
                m_BrushAdjustMousePosition.y + 18f,
                250f,
                50f);
            GUI.Box(panelRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(
                new Rect(panelRect.x + 8f, panelRect.y + 4f, panelRect.width - 16f, 18f),
                $"Radius  {m_Radius:0.00}   (drag horizontally)",
                EditorStyles.miniBoldLabel);
            EditorGUI.ProgressBar(
                new Rect(panelRect.x + 8f, panelRect.y + 27f, panelRect.width - 16f, 16f),
                GetNormalizedAdjustmentStrength(),
                $"Strength  {m_Strength:0.00}   (drag vertically)");
            Handles.EndGUI();
            return true;
        }

        void CaptureBrushAdjustmentSurface(Vector2 mousePosition)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            m_HasBrushAdjustSurface = TryRaycastTarget(ray, out RaycastHit hit);
            if (!m_HasBrushAdjustSurface)
                return;

            m_BrushAdjustHitPoint = hit.point;
            m_BrushAdjustHitNormal = hit.normal;
        }

        void DrawBrushAdjustmentGizmo()
        {
            if (!m_HasBrushAdjustSurface)
                return;

            Color brushColor = new Color(1f, 0.82f, 0.12f, 1f);
            DrawBrushFalloff(m_BrushAdjustHitPoint, m_BrushAdjustHitNormal, brushColor);

            float normalizedStrength = GetNormalizedAdjustmentStrength();
            Handles.color = Color.Lerp(
                new Color(1f, 0.25f, 0.12f, 0.9f),
                new Color(0.2f, 1f, 0.35f, 0.95f),
                normalizedStrength);
            Handles.DrawWireDisc(
                m_BrushAdjustHitPoint
                    + m_BrushAdjustHitNormal * HandleUtility.GetHandleSize(m_BrushAdjustHitPoint) * 0.002f,
                m_BrushAdjustHitNormal,
                m_Radius * normalizedStrength);
        }

        void EndBrushAdjustment()
        {
            if (!m_IsAdjustingBrush)
                return;

            m_IsAdjustingBrush = false;
            m_HasBrushAdjustSurface = false;
            EditorGUIUtility.SetWantsMouseJumping(0);
            GUIUtility.hotControl = 0;
        }

        float GetNormalizedAdjustmentStrength()
        {
            return m_Mode == MeshSculptModifier.SculptMode.Displace
                ? Mathf.Clamp01(Mathf.Abs(m_Strength) / 2f)
                : Mathf.Clamp01(m_Strength);
        }

        bool TryRaycastTarget(Ray ray, out RaycastHit targetHit)
        {
            targetHit = default;
            EnsureSculptPickingCollider();
            Physics.SyncTransforms();
            if (m_SculptPickingCollider != null
                && m_SculptPickingCollider.enabled
                && m_SculptPickingCollider.Raycast(ray, out targetHit, float.MaxValue))
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(ray, float.MaxValue, ~0, QueryTriggerInteraction.Collide);
            float closest = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                MeshFilter meshFilter = ResolveSculptMeshFilter(hits[i].collider);
                if (meshFilter != m_Modifier.Target || hits[i].distance >= closest) continue;
                closest = hits[i].distance;
                targetHit = hits[i];
            }
            return closest < float.MaxValue;
        }

        bool TryRaycastSculptSurface(Ray ray, out RaycastHit surfaceHit, out MeshFilter meshFilter)
        {
            if (IsSeamFit)
                return TryRaycastSeamSurface(ray, out surfaceHit, out meshFilter);
            surfaceHit = default;
            meshFilter = null;

            // Loft gameplay collision is split into generated child chunks.
            // Keep a hidden collider for the complete render mesh while sculpting
            // so the brush can pick the editable surface independently of those
            // regenerated chunks.
            EnsureSculptPickingCollider();
            Physics.SyncTransforms();

            RaycastHit[] hits = Physics.RaycastAll(ray, float.MaxValue, ~0, QueryTriggerInteraction.Collide);
            float closest = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                MeshFilter candidate;
                if (hits[i].collider == m_SculptPickingCollider)
                {
                    candidate = m_SculptPickingTarget;
                }
                else
                {
                    candidate = ResolveSculptMeshFilter(hits[i].collider);
                }

                if (candidate == null
                    || candidate.sharedMesh == null
                    || !CanPickSculptSurface(candidate)
                    || hits[i].distance >= closest)
                {
                    continue;
                }

                closest = hits[i].distance;
                surfaceHit = hits[i];
                meshFilter = candidate;
            }

            return meshFilter != null;
        }

        static bool CanPickSculptSurface(MeshFilter surface)
        {
            if (!MBEditorToolState.SculptableOnly) return true;
            MeshSculptModifier modifier = ResolveSculptModifier(surface);
            return IsUsableSceneModifier(modifier) && modifier.Target == surface;
        }

        static MeshFilter ResolveSculptMeshFilter(Collider hitCollider)
        {
            if (hitCollider == null)
                return null;

            // Terrain collision can live on child chunks, while its configured
            // editable filter may be on a different object in the hierarchy.
            MGTerrain terrain = hitCollider.GetComponentInParent<MGTerrain>();
            if (terrain != null) return terrain.MeshFilter;

            // Collider chunks deliberately have no MeshFilter of their own.
            // Resolve them to the owning loft's full generated render mesh.
            MultiSplineLoft loft = hitCollider.GetComponentInParent<MultiSplineLoft>();
            if (loft != null)
                return loft.GetComponent<MeshFilter>();

            return hitCollider.GetComponent<MeshFilter>()
                ?? hitCollider.GetComponentInParent<MeshFilter>();
        }

        void InvalidateSeamTerrains() => m_SeamTerrains = null;

        static bool IsTerrainSurface(MeshFilter filter)
        {
            if (filter == null) return false;
            var terrain = filter.GetComponentInParent<MGTerrain>();
            return terrain != null && terrain.MeshFilter == filter;
        }

        bool TryRaycastSeamSurface(Ray ray, out RaycastHit surfaceHit, out MeshFilter meshFilter)
        {
            surfaceHit = default;
            meshFilter = null;
            EnsureSculptPickingCollider();
            Physics.SyncTransforms();
            float closest = float.MaxValue;

            // Once painting begins, this collider represents the current edited
            // surface. Hover before activation must work without a modifier too.
            bool choosing = !MBEditorToolState.SculptableOnly
                && !m_IsSculpting && Event.current != null && Event.current.shift;
            bool pickTerrain = m_Modifier == null || !EditingSeamLoft || choosing;
            bool pickLoft = m_Modifier == null || EditingSeamLoft || choosing;
            if (m_SculptPickingCollider != null
                && CanPickSculptSurface(m_SculptPickingTarget)
                && m_SculptPickingCollider.Raycast(ray, out var previewHit, closest))
            {
                surfaceHit = previewHit;
                meshFilter = m_SculptPickingTarget;
                closest = previewHit.distance;
            }

            m_SeamTerrains ??= Object.FindObjectsByType<MGTerrain>(FindObjectsSortMode.None);
            foreach (MGTerrain terrain in m_SeamTerrains)
            {
                if (!pickTerrain) break;
                if (terrain == null || !terrain.gameObject.activeInHierarchy
                    || !terrain.gameObject.scene.IsValid() || !terrain.gameObject.scene.isLoaded) continue;
                MeshFilter surface = terrain.MeshFilter;
                if (surface == null || surface.sharedMesh == null || !CanPickSculptSurface(surface)) continue;
                // Query the terrain's registered master/chunk colliders directly.
                // Loft/decor hits must not hide the terrain hover brush.
                if (!terrain.RaycastSurface(ray, out var hit, closest)) continue;
                surfaceHit = hit;
                meshFilter = surface;
                closest = hit.distance;
            }
            if (pickLoft)
            {
                foreach (var hit in Physics.RaycastAll(ray, closest, ~0, QueryTriggerInteraction.Ignore))
                {
                    var loft = hit.collider.GetComponentInParent<MultiSplineLoft>();
                    if (loft == null || hit.distance >= closest) continue;
                    var filter = loft.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !CanPickSculptSurface(filter)) continue;
                    if (EditingSeamLoft && !choosing && filter != m_Modifier.Target) continue;
                    surfaceHit = hit;
                    meshFilter = filter;
                    closest = hit.distance;
                }
            }
            return meshFilter != null;
        }

        void RecordStroke(RaycastHit hit, bool control, bool shift)
        {
            MeshSculptModifier.SculptMode strokeMode = GetStrokeMode(control, shift);
            Vector3 direction = m_DirectionMode == DirectionMode.WorldUp ? Vector3.up : m_DirectionMode == DirectionMode.Custom ? m_CustomDirection.normalized : hit.normal;
            float strength = control && !shift ? -m_Strength : m_Strength;
            var stroke = m_Modifier.CreateStroke(strokeMode,
                strokeMode == MeshSculptModifier.SculptMode.SeamFit ? MeshSculptModifier.StrokeSpace.TargetLocal : m_StrokeSpace,
                hit.point, direction, m_Radius, strength, m_Falloff);
            if (strokeMode == MeshSculptModifier.SculptMode.SeamFit)
            {
                bool editingLoft = EditingSeamLoft;
                Mesh sourceMesh = editingLoft ? m_Modifier.LinkedLoft.GeneratedMesh : m_Modifier.Target.sharedMesh;
                if ((!editingLoft && !IsTerrainSurface(m_Modifier.Target)) || sourceMesh == null || !sourceMesh.isReadable) return;
                bool lower = control && !shift;
                if (!lower && m_SeamBrush == null)
                {
                    var candidates = new List<MeshFilter>();
                    if (m_SeamTargetRoot != null) candidates.AddRange(m_SeamTargetRoot.GetComponentsInChildren<MeshFilter>());
                    else if (editingLoft)
                        foreach (var terrainTarget in Object.FindObjectsByType<MGTerrain>(FindObjectsSortMode.None))
                            candidates.Add(terrainTarget.MeshFilter);
                    else foreach (MultiSplineLoft loft in Object.FindObjectsByType<MultiSplineLoft>(FindObjectsSortMode.InstanceID))
                        candidates.Add(loft.GetComponent<MeshFilter>());
                    m_SeamBrush = new MeshSeamFitBrush(candidates, m_Modifier.Target,
                        editingLoft ? m_LoftSeamVerticesOnly : m_SeamVerticesOnly, editingLoft);
                }
                stroke.seamVertexCount = sourceMesh.vertexCount;
                stroke.seamVertices = lower
                    ? MeshSeamFitBrush.SampleLower(m_Modifier.Target, hit.point, m_Radius, m_Strength, m_Falloff, m_SeamLowerDepth, sourceMesh)
                    : m_SeamBrush.Sample(m_Modifier.Target, hit.point, m_Radius,
                        m_SeamThreshold, m_Strength, m_Falloff, m_SeamNormalBlend, m_SeamHeightOnly, m_SeamSurfaceOffset, !editingLoft && m_SeamAboveOnly, m_SeamMinimumRise, sourceMesh);
                m_SeamStatus = stroke.seamVertices.Length == 0
                    ? "No terrain vertices within both the brush and snap distance of an eligible target."
                    : $"{(lower ? "Lowered" : "Fitted")} {stroke.seamVertices.Length} {(editingLoft ? "loft" : "terrain")} vertices in the latest sample.";
                if (stroke.seamVertices.Length == 0) { Repaint(); return; }
            }
            Undo.RecordObject(m_Modifier, "Mesh Sculpt Stroke");
            MGTerrain terrain = EditingSeamLoft ? null : (m_Modifier.Target.GetComponent<MGTerrain>()
                ?? m_Modifier.Target.GetComponentInParent<MGTerrain>());
            if (terrain != null)
            {
                Undo.RecordObject(terrain, "Mesh Sculpt Stroke");
                if (IsSeamFit)
                {
                    EnsureSeamMasterCollider(terrain);
                    m_Modifier.UpdateMeshCollider = true;
                }
            }
            m_StrokeModifiers.Add(m_Modifier);
            m_Modifier.AddStroke(stroke);
            if (strokeMode == MeshSculptModifier.SculptMode.SeamFit) MeshSeamFitBrush.TrackUndo(m_Modifier);
            m_Modifier.ApplyLatestStrokePreview();
            EditorUtility.SetDirty(m_Modifier);
            m_LastStrokePosition = hit.point;
            m_HasLastStrokePosition = true;
            Repaint();
        }

        MeshSculptModifier.SculptMode GetStrokeMode(bool control, bool shift)
        {
            if (control && shift) return MeshSculptModifier.SculptMode.Noise;
            if (shift) return MeshSculptModifier.SculptMode.Smooth;
            return m_Mode;
        }

        static void EnsureSeamMasterCollider(MGTerrain terrain)
        {
            MeshCollider master = terrain.MeshCollider;
            if (master == null) master = Undo.AddComponent<MeshCollider>(terrain.MeshFilter.gameObject);
            Undo.RecordObject(master, "Fit Terrain Collision");
            master.enabled = true;
            foreach (MeshCollider chunk in terrain.SurfaceColliderChunks)
            {
                if (chunk == null || !chunk.enabled) continue;
                Undo.RecordObject(chunk, "Fit Terrain Collision");
                chunk.enabled = false;
            }
        }

        void EnsureSculptPickingCollider()
        {
            MeshFilter target = m_Modifier != null ? m_Modifier.Target : null;
            if (target == null || target.sharedMesh == null)
            {
                DestroySculptPickingCollider();
                return;
            }

            int generationVersion = m_Modifier.LinkedLoft != null ? m_Modifier.LinkedLoft.GenerationVersion : -1;
            if (m_SculptPickingObject != null && m_SculptPickingTarget == target)
            {
                if (generationVersion != m_SculptPickingGenerationVersion)
                    RefreshSculptPickingCollider();
                return;
            }

            DestroySculptPickingCollider();
            m_SculptPickingObject = new GameObject("MashBox Sculpt Picking Collider")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = target.gameObject.layer
            };
            m_SculptPickingObject.transform.SetParent(target.transform, false);
            m_SculptPickingCollider = m_SculptPickingObject.AddComponent<MeshCollider>();
            m_SculptPickingCollider.sharedMesh = target.sharedMesh;
            m_SculptPickingTarget = target;
            m_SculptPickingGenerationVersion = generationVersion;
        }

        void RefreshSculptPickingCollider()
        {
            if (m_SculptPickingCollider == null || m_SculptPickingTarget == null)
                return;
            m_SculptPickingCollider.sharedMesh = null;
            m_SculptPickingCollider.sharedMesh = m_SculptPickingTarget.sharedMesh;
            m_SculptPickingGenerationVersion = m_Modifier != null && m_Modifier.LinkedLoft != null
                ? m_Modifier.LinkedLoft.GenerationVersion
                : -1;
            Physics.SyncTransforms();
        }

        void DestroySculptPickingCollider()
        {
            if (m_SculptPickingObject != null)
                DestroyImmediate(m_SculptPickingObject);
            m_SculptPickingObject = null;
            m_SculptPickingCollider = null;
            m_SculptPickingTarget = null;
            m_SculptPickingGenerationVersion = -1;
        }

        void StopStroke()
        {
            if (m_IsSculpting && m_Modifier != null)
            {
                m_Modifier.FinalizeStrokePreview();
                RefreshSculptPickingCollider();
            }
            m_IsSculpting = false;
            m_SeamBrush = null;
            EndBrushAdjustment();
            m_HasLastStrokePosition = false;
            GUIUtility.hotControl = 0;
            if (m_UndoGroup >= 0) Undo.CollapseUndoOperations(m_UndoGroup);
            m_UndoGroup = -1;
        }

        void OnSculptUndoRebuilt(MeshSculptModifier modifier)
        {
            if (modifier != null && modifier.Target == m_SculptPickingTarget)
                RefreshSculptPickingCollider();
        }

        void OnUndoRedo()
        {
            // Mesh replay and collider cooking are handled only for components
            // restored by Undo. Unrelated scene edits only refresh the UI.
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
