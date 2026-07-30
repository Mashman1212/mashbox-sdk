using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
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
        bool m_ClearingVisualSelection;

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
            RestoreActiveModifier();
            MBEditorToolState.SculptModeChanged -= OnSharedSculptModeChanged;
            MBEditorToolState.SculptModeChanged += OnSharedSculptModeChanged;
            if (MBEditorToolState.ActiveEditing)
                ActivateSceneTool();
        }

        void OnDisable()
        {
            MBEditorToolState.SculptModeChanged -= OnSharedSculptModeChanged;
            DeactivateSceneTool();
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
            Selection.selectionChanged += OnSelectionChanged;
            UseSelection();
            ClearVisualSelection();
        }

        public void DeactivateSceneTool()
        {
            // StopStroke releases Unity's global IMGUI hot control. Embedded hosts
            // call this for every inactive authoring tool, so repeating cleanup
            // here would cancel unrelated buttons between MouseDown and MouseUp.
            if (!m_SceneToolActive)
                return;

            m_SceneToolActive = false;
            m_SceneCameraRightMouseHeld = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Selection.selectionChanged -= OnSelectionChanged;
            if (s_ActiveSceneToolOwner == this)
                s_ActiveSceneToolOwner = null;
            StopStroke();
            DestroySculptPickingCollider();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (!embeddedInParentWindow) m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Non-Destructive Mesh Sculpt", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Brush strokes are stored as instructions and replayed from the clean mesh. A linked loft replays them after every regeneration.", MessageType.Info);

            m_Modifier = (MeshSculptModifier)EditorGUILayout.ObjectField("Sculpt Modifier", m_Modifier, typeof(MeshSculptModifier), true);
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
            var requestedMode = (MeshSculptModifier.SculptMode)GUILayout.Toolbar((int)m_Mode, new[] { "Displace", "Smooth", "Flatten" });
            if (requestedMode != m_Mode)
            {
                m_Mode = requestedMode;
                MBEditorToolState.SculptMode = (MBSculptMode)m_Mode;
            }
            m_StrokeSpace = (MeshSculptModifier.StrokeSpace)EditorGUILayout.EnumPopup(new GUIContent("Memory Space", "World stays at the same scene position. Target Local follows the sculpted object."), m_StrokeSpace);
            m_Radius = EditorGUILayout.Slider("Radius", m_Radius, 0.01f, 20f);
            m_Strength = m_Mode == MeshSculptModifier.SculptMode.Displace
                ? EditorGUILayout.Slider("Strength", m_Strength, -2f, 2f)
                : EditorGUILayout.Slider("Strength", Mathf.Abs(m_Strength), 0.01f, 1f);
            m_Falloff = EditorGUILayout.Slider("Falloff", m_Falloff, 0.1f, 8f);
            m_Spacing = EditorGUILayout.Slider("Stroke Spacing", m_Spacing, 0.05f, 1f);

            if (m_Mode != MeshSculptModifier.SculptMode.Smooth)
            {
                m_DirectionMode = (DirectionMode)EditorGUILayout.EnumPopup("Direction", m_DirectionMode);
                if (m_DirectionMode == DirectionMode.Custom)
                    m_CustomDirection = EditorGUILayout.Vector3Field("Custom World Direction", m_CustomDirection);
            }

            EditorGUILayout.HelpBox("Drag to sculpt. Ctrl inverts the active brush, Shift temporarily smooths, and Ctrl+Shift temporarily adds deterministic noise. Ctrl+Middle-drag adjusts the brush: horizontal changes radius and vertical changes strength.", MessageType.None);

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
            m_Mode = (MeshSculptModifier.SculptMode)MBEditorToolState.SculptMode;
            Repaint();
            SceneView.RepaintAll();
        }

        void UseSelection()
        {
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
            RememberActiveModifier();
            EnsureSelectedModifierTargetsLoft(selected, selectedModifier);
            m_Modifier.Rebuild();
        }

        void ClearActiveModifier()
        {
            if (m_Modifier == null)
                return;

            StopStroke();
            DestroySculptPickingCollider();
            m_Modifier = null;
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

        void OnSelectionChanged()
        {
            if (m_ClearingVisualSelection)
                return;

            UseSelection();
            ClearVisualSelection();
            Repaint();
            SceneView.RepaintAll();
        }

        void ClearVisualSelection()
        {
            if (m_ClearingVisualSelection || Selection.objects == null || Selection.objects.Length == 0)
                return;

            m_ClearingVisualSelection = true;
            Selection.objects = System.Array.Empty<UnityEngine.Object>();
            m_ClearingVisualSelection = false;
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

        void RememberActiveModifier()
        {
            if (m_Modifier == null)
            {
                SessionState.EraseString(ActiveModifierSessionKey);
                return;
            }

            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(m_Modifier);
            SessionState.SetString(ActiveModifierSessionKey, globalId.ToString());
        }

        void RestoreActiveModifier()
        {
            if (m_Modifier != null)
                return;

            string savedId = SessionState.GetString(ActiveModifierSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(savedId)
                && GlobalObjectId.TryParse(savedId, out GlobalObjectId globalId))
            {
                MeshSculptModifier restored = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as MeshSculptModifier;
                if (IsUsableSceneModifier(restored))
                {
                    m_Modifier = restored;
                    return;
                }
            }

            SessionState.EraseString(ActiveModifierSessionKey);

            // Older sessions did not remember a GlobalObjectId. Recover
            // automatically when the loaded scenes contain one unambiguous
            // sculpt target, which also repairs the first reload after upgrading.
            MeshSculptModifier uniqueModifier = null;
            MeshSculptModifier[] modifiers = Resources.FindObjectsOfTypeAll<MeshSculptModifier>();
            for (int i = 0; i < modifiers.Length; i++)
            {
                MeshSculptModifier candidate = modifiers[i];
                if (!IsUsableSceneModifier(candidate))
                    continue;
                if (uniqueModifier != null)
                    return;
                uniqueModifier = candidate;
            }

            if (uniqueModifier == null)
                return;

            m_Modifier = uniqueModifier;
            RememberActiveModifier();
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
            Event current = Event.current;
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
            if (m_Modifier != null && m_Modifier.Target != null
                && HandleBrushAdjustment(current, controlId, sceneView))
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            RaycastHit hit;
            MeshFilter hitMeshFilter;
            if (m_IsSculpting)
            {
                if (!TryRaycastTarget(ray, out hit))
                {
                    if (current.type == EventType.MouseUp && current.button == 0)
                    {
                        StopStroke();
                        current.Use();
                    }
                    return;
                }
                hitMeshFilter = m_Modifier != null ? m_Modifier.Target : null;
            }
            else if (!TryRaycastSculptSurface(ray, out hit, out hitMeshFilter))
            {
                return;
            }

            MeshSculptModifier hoveredModifier = ResolveSculptModifier(hitMeshFilter);
            if (!m_IsSculpting && hoveredModifier != null && hoveredModifier != m_Modifier)
                ActivateModifier(hoveredModifier);

            bool canSculptHit = m_Modifier != null
                && m_Modifier.Target != null
                && hitMeshFilter == m_Modifier.Target;

            if (!canSculptHit)
            {
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
            DrawBrushFalloff(hit.point, hit.normal, brushColor);
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
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(1f, 0.65f, 0.2f, 1f);
            float handleSize = HandleUtility.GetHandleSize(hit.point);
            Handles.Label(
                hit.point + hit.normal * handleSize * 0.22f,
                "Shift+Click Make Sculptable",
                style);
        }

        void CreateOrActivateModifier(MeshFilter meshFilter)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

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
            RememberActiveModifier();
            EnsureSculptPickingCollider();
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

        static MeshFilter ResolveSculptMeshFilter(Collider hitCollider)
        {
            if (hitCollider == null)
                return null;

            // Collider chunks deliberately have no MeshFilter of their own.
            // Resolve them to the owning loft's full generated render mesh.
            MultiSplineLoft loft = hitCollider.GetComponentInParent<MultiSplineLoft>();
            if (loft != null)
                return loft.GetComponent<MeshFilter>();

            return hitCollider.GetComponent<MeshFilter>()
                ?? hitCollider.GetComponentInParent<MeshFilter>();
        }

        void RecordStroke(RaycastHit hit, bool control, bool shift)
        {
            MeshSculptModifier.SculptMode strokeMode = GetStrokeMode(control, shift);
            Vector3 direction = m_DirectionMode == DirectionMode.WorldUp ? Vector3.up : m_DirectionMode == DirectionMode.Custom ? m_CustomDirection.normalized : hit.normal;
            float strength = control && !shift ? -m_Strength : m_Strength;
            Undo.RecordObject(m_Modifier, "Mesh Sculpt Stroke");
            m_Modifier.AddStroke(m_Modifier.CreateStroke(strokeMode, m_StrokeSpace, hit.point, direction, m_Radius, strength, m_Falloff));
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
            EndBrushAdjustment();
            m_HasLastStrokePosition = false;
            GUIUtility.hotControl = 0;
            if (m_UndoGroup >= 0) Undo.CollapseUndoOperations(m_UndoGroup);
            m_UndoGroup = -1;
        }

        void OnUndoRedo()
        {
            if (m_Modifier != null)
            {
                m_Modifier.Rebuild();
                RefreshSculptPickingCollider();
            }
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
