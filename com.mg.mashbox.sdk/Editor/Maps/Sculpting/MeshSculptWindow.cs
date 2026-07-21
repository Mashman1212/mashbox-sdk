using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed class MeshSculptWindow : EditorWindow
    {
        static readonly float[] BrushInfluenceLevels = { 0.75f, 0.5f, 0.25f };

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
        [SerializeField] bool m_BrushActive = true;

        Vector2 m_Scroll;
        bool m_IsSculpting;
        bool m_HasLastStrokePosition;
        Vector3 m_LastStrokePosition;
        int m_UndoGroup = -1;
        bool m_SceneToolActive;
        bool m_IsAdjustingBrush;
        Vector2 m_BrushAdjustMousePosition;

        public static void ShowWindow() => GetWindow<MeshSculptWindow>("Mesh Sculpt");

        void OnEnable() => ActivateSceneTool();
        void OnDisable() => DeactivateSceneTool();
        void OnGUI() => Draw();

        public void ActivateSceneTool()
        {
            if (m_SceneToolActive) return;
            m_SceneToolActive = true;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            Selection.selectionChanged += OnSelectionChanged;
            UseSelection();
            SetBrushActive(true);
        }

        public void DeactivateSceneTool()
        {
            if (!m_SceneToolActive) return;
            m_SceneToolActive = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Selection.selectionChanged -= OnSelectionChanged;
            StopStroke();
        }

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (!embeddedInParentWindow) m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Non-Destructive Mesh Sculpt", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Brush strokes are stored as instructions and replayed from the clean mesh. A linked loft replays them after every regeneration.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            m_Modifier = (MeshSculptModifier)EditorGUILayout.ObjectField("Sculpt Modifier", m_Modifier, typeof(MeshSculptModifier), true);
            if (EditorGUI.EndChangeCheck() && m_Modifier != null)
                SetBrushActive(true);
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
            m_Mode = (MeshSculptModifier.SculptMode)GUILayout.Toolbar((int)m_Mode, new[] { "Displace", "Smooth", "Flatten" });
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

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(m_BrushActive ? "Brush Active" : "Brush Paused");
                if (GUILayout.Button(m_BrushActive ? "Pause" : "Enable", GUILayout.Width(90f)))
                    SetBrushActive(!m_BrushActive);
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

        void UseSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return;
            MeshSculptModifier selectedModifier = selected.GetComponent<MeshSculptModifier>() ?? selected.GetComponentInParent<MeshSculptModifier>();
            if (selectedModifier == null)
            {
                MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>();
                if (loft != null) selectedModifier = loft.SculptModifier;
            }
            if (selectedModifier == null) return;

            m_Modifier = selectedModifier;
            EnsureSelectedModifierTargetsLoft(selected, selectedModifier);
            m_Modifier.Rebuild();
            SetBrushActive(true);
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
            UseSelection();
            Repaint();
            SceneView.RepaintAll();
        }

        void CreateOnSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return;
            MultiSplineLoft loft = selected.GetComponent<MultiSplineLoft>();
            MeshFilter meshFilter = selected.GetComponent<MeshFilter>();
            if (meshFilter == null) return;

            m_Modifier = selected.GetComponent<MeshSculptModifier>();
            if (m_Modifier == null) m_Modifier = Undo.AddComponent<MeshSculptModifier>(selected);
            Undo.RecordObject(m_Modifier, "Configure Sculpt Modifier");
            m_Modifier.SetTarget(meshFilter);
            if (loft != null)
            {
                m_Modifier.LinkToLoft(loft);
                Undo.RecordObject(loft, "Link Sculpt Modifier");
                loft.SculptModifier = m_Modifier;
                EditorUtility.SetDirty(loft);
            }

            if (loft == null && selected.GetComponent<Collider>() == null)
            {
                MeshCollider collider = Undo.AddComponent<MeshCollider>(selected);
                collider.sharedMesh = meshFilter.sharedMesh;
            }
            EditorUtility.SetDirty(m_Modifier);
        }

        void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;
            bool hasShortcutModifier = current.shift || current.alt || current.control || current.command;
            if (current.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && !hasShortcutModifier)
            {
                if (current.keyCode == KeyCode.B) { SetBrushActive(true); current.Use(); }
            }
            if (!m_BrushActive || m_Modifier == null || m_Modifier.Target == null) return;

            int controlId = GUIUtility.GetControlID("MeshSculptBrush".GetHashCode(), FocusType.Passive);
            if (HandleBrushAdjustment(current, controlId, sceneView))
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!TryRaycastTarget(ray, out RaycastHit hit)) return;

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
                GUIUtility.hotControl = controlId;
                current.Use();
            }
            else if (m_IsAdjustingBrush && current.type == EventType.MouseDrag && current.button == 2)
            {
                m_Radius = Mathf.Clamp(m_Radius * Mathf.Exp(current.delta.x * 0.01f), 0.01f, 20f);
                float minimumStrength = m_Mode == MeshSculptModifier.SculptMode.Displace ? -2f : 0.01f;
                float maximumStrength = m_Mode == MeshSculptModifier.SculptMode.Displace ? 2f : 1f;
                m_Strength = Mathf.Clamp(m_Strength - current.delta.y * 0.01f, minimumStrength, maximumStrength);
                m_BrushAdjustMousePosition = current.mousePosition;
                current.Use();
                Repaint();
                sceneView.Repaint();
            }
            else if (m_IsAdjustingBrush && current.type == EventType.MouseUp && current.button == 2)
            {
                m_IsAdjustingBrush = false;
                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
                sceneView.Repaint();
                return true;
            }

            if (!m_IsAdjustingBrush)
                return false;

            Handles.BeginGUI();
            GUI.Label(new Rect(m_BrushAdjustMousePosition.x + 18f, m_BrushAdjustMousePosition.y + 18f, 240f, 22f),
                $"Radius {m_Radius:0.00}   Strength {m_Strength:0.00}", EditorStyles.helpBox);
            Handles.EndGUI();
            return true;
        }

        bool TryRaycastTarget(Ray ray, out RaycastHit targetHit)
        {
            targetHit = default;
            RaycastHit[] hits = Physics.RaycastAll(ray, float.MaxValue);
            float closest = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                MeshFilter meshFilter = hits[i].collider.GetComponent<MeshFilter>() ?? hits[i].collider.GetComponentInParent<MeshFilter>();
                if (meshFilter != m_Modifier.Target || hits[i].distance >= closest) continue;
                closest = hits[i].distance;
                targetHit = hits[i];
            }
            return closest < float.MaxValue;
        }

        void RecordStroke(RaycastHit hit, bool control, bool shift)
        {
            MeshSculptModifier.SculptMode strokeMode = GetStrokeMode(control, shift);
            Vector3 direction = m_DirectionMode == DirectionMode.WorldUp ? Vector3.up : m_DirectionMode == DirectionMode.Custom ? m_CustomDirection.normalized : hit.normal;
            float strength = control && !shift ? -m_Strength : m_Strength;
            Undo.RecordObject(m_Modifier, "Mesh Sculpt Stroke");
            m_Modifier.AddStroke(m_Modifier.CreateStroke(strokeMode, m_StrokeSpace, hit.point, direction, m_Radius, strength, m_Falloff));
            m_Modifier.Rebuild();
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

        void SetBrushActive(bool active)
        {
            m_BrushActive = active;
            if (!active) StopStroke();
            SceneView.RepaintAll();
            Repaint();
        }

        void StopStroke()
        {
            m_IsSculpting = false;
            m_IsAdjustingBrush = false;
            m_HasLastStrokePosition = false;
            GUIUtility.hotControl = 0;
            if (m_UndoGroup >= 0) Undo.CollapseUndoOperations(m_UndoGroup);
            m_UndoGroup = -1;
        }

        void OnUndoRedo()
        {
            if (m_Modifier != null) m_Modifier.Rebuild();
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
