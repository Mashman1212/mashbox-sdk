#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.SDKMain
{
    [Serializable]
    internal sealed class UVInspectorTool
    {
        private sealed class ChannelData
        {
            public int Dimension;
            public Vector4[] Values;
            public Vector2 Min;
            public Vector2 Max;
        }

        private sealed class SubmeshData
        {
            public MeshTopology Topology;
            public int[] Indices;
        }

        private static readonly Color[] SubmeshColors =
        {
            new Color(0.20f, 0.78f, 1.00f, 0.92f),
            new Color(1.00f, 0.48f, 0.24f, 0.92f),
            new Color(0.48f, 0.92f, 0.42f, 0.92f),
            new Color(0.94f, 0.42f, 0.86f, 0.92f),
            new Color(1.00f, 0.82f, 0.25f, 0.92f),
            new Color(0.55f, 0.52f, 1.00f, 0.92f)
        };

        private const int ChannelCount = 8;
        private const int MaxDrawnEdges = 60000;

        [SerializeField] private UnityEngine.Object sourceObject;
        [SerializeField] private bool lockSelection;
        [SerializeField] private int selectedChannel;
        [SerializeField] private bool frameAllUVs;
        [SerializeField] private bool showVertices;
        [SerializeField] private float zoom = 1f;
        [SerializeField] private Vector2 pan;
        [SerializeField] private Vector2 scrollPosition;

        private readonly ChannelData[] channels = new ChannelData[ChannelCount];
        private readonly List<SubmeshData> submeshes = new List<SubmeshData>();
        private Mesh cachedMesh;
        private string loadError;
        private bool dragging;
        private Vector2 lastMousePosition;

        public void Draw(bool embeddedInParentWindow = false)
        {
            if (embeddedInParentWindow)
            {
                DrawInspector();
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawInspector();
            EditorGUILayout.EndScrollView();
        }

        private void DrawInspector()
        {
            FollowSelection();

            EditorGUILayout.LabelField("UV Inspector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a Mesh asset or a GameObject with a Mesh Filter or Skinned Mesh Renderer. Choose any populated UV channel, then pan with the middle or right mouse button and zoom with the mouse wheel.",
                MessageType.Info);

            DrawSourceControls();

            Mesh mesh = ResolveMesh(sourceObject);
            if (mesh != cachedMesh)
                LoadMesh(mesh);

            if (mesh == null)
            {
                EditorGUILayout.HelpBox("No mesh is selected.", MessageType.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
                return;
            }

            DrawMeshSummary(mesh);
            DrawChannelControls();

            ChannelData channel = channels[selectedChannel];
            if (channel == null || channel.Values == null || channel.Values.Length == 0)
            {
                EditorGUILayout.HelpBox($"UV{selectedChannel} has no texture coordinates.", MessageType.Warning);
                return;
            }

            DrawPreview(channel);
            DrawLegend();
        }

        private void FollowSelection()
        {
            if (lockSelection)
                return;

            UnityEngine.Object selected = Selection.activeObject;
            if (ResolveMesh(selected) != null)
                sourceObject = selected;
            else if (selected != null && sourceObject == null)
                sourceObject = selected;
        }

        private void DrawSourceControls()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            sourceObject = EditorGUILayout.ObjectField(
                new GUIContent("Mesh Source", "A Mesh asset, GameObject, Mesh Filter, or Skinned Mesh Renderer."),
                sourceObject,
                typeof(UnityEngine.Object),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                lockSelection = sourceObject != null;
                LoadMesh(ResolveMesh(sourceObject));
            }

            lockSelection = GUILayout.Toggle(
                lockSelection,
                new GUIContent(lockSelection ? "Locked" : "Follow Selection", "Keep this mesh loaded or follow the current Unity selection."),
                "Button",
                GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Current Selection", GUILayout.Width(150f)))
            {
                sourceObject = Selection.activeObject;
                lockSelection = false;
                LoadMesh(ResolveMesh(sourceObject));
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                LoadMesh(ResolveMesh(sourceObject));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            ChannelData selectedData = channels[Mathf.Clamp(selectedChannel, 0, ChannelCount - 1)];
            if (selectedData != null)
            {
                EditorGUILayout.LabelField(
                    $"U  {selectedData.Min.x:0.###} to {selectedData.Max.x:0.###}    " +
                    $"V  {selectedData.Min.y:0.###} to {selectedData.Max.y:0.###}",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMeshSummary(Mesh mesh)
        {
            int triangleCount = 0;
            foreach (SubmeshData submesh in submeshes)
            {
                if (submesh.Topology == MeshTopology.Triangles)
                    triangleCount += submesh.Indices.Length / 3;
                else if (submesh.Topology == MeshTopology.Quads)
                    triangleCount += (submesh.Indices.Length / 4) * 2;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(mesh.name, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Vertices  {mesh.vertexCount:N0}", GUILayout.MinWidth(120f));
            EditorGUILayout.LabelField($"Triangles  {triangleCount:N0}", GUILayout.MinWidth(120f));
            EditorGUILayout.LabelField($"Submeshes  {mesh.subMeshCount:N0}", GUILayout.MinWidth(110f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawChannelControls()
        {
            var labels = new string[ChannelCount];
            int firstAvailable = -1;
            for (int i = 0; i < ChannelCount; i++)
            {
                ChannelData data = channels[i];
                bool available = data != null && data.Values != null && data.Values.Length > 0;
                if (available && firstAvailable < 0)
                    firstAvailable = i;

                labels[i] = available
                    ? $"UV{i}  ({data.Values.Length:N0} × {data.Dimension}D)"
                    : $"UV{i}  (empty)";
            }

            if (firstAvailable >= 0 &&
                (selectedChannel < 0 || selectedChannel >= ChannelCount || channels[selectedChannel] == null))
                selectedChannel = firstAvailable;

            EditorGUILayout.BeginVertical("box");
            EditorGUI.BeginChangeCheck();
            selectedChannel = EditorGUILayout.Popup("UV Channel", Mathf.Clamp(selectedChannel, 0, ChannelCount - 1), labels);
            if (EditorGUI.EndChangeCheck())
            {
                zoom = 1f;
                pan = Vector2.zero;
            }

            EditorGUILayout.BeginHorizontal();
            frameAllUVs = EditorGUILayout.ToggleLeft(
                new GUIContent("Frame all UVs", "Fit coordinates outside the standard 0–1 tile into the preview."),
                frameAllUVs,
                GUILayout.Width(116f));
            showVertices = EditorGUILayout.ToggleLeft("Vertices", showVertices, GUILayout.Width(76f));
            GUILayout.Label("Zoom", GUILayout.Width(38f));
            zoom = GUILayout.HorizontalSlider(zoom, 0.2f, 12f, GUILayout.MinWidth(90f));
            GUILayout.Label($"{zoom:0.0}×", GUILayout.Width(38f));
            if (GUILayout.Button("Reset View", GUILayout.Width(82f)))
            {
                zoom = 1f;
                pan = Vector2.zero;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreview(ChannelData channel)
        {
            float previewHeight = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 42f, 280f, 620f);
            Rect rect = GUILayoutUtility.GetRect(100f, previewHeight, GUILayout.ExpandWidth(true));
            HandlePreviewInput(rect, channel);

            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.055f, 0.065f, 0.08f, 1f)
                : new Color(0.88f, 0.89f, 0.91f, 1f);
            EditorGUI.DrawRect(rect, background);

            Rect inner = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            GetView(channel, inner, out Vector2 center, out float unitsAcross);
            float scale = Mathf.Min(inner.width, inner.height) / unitsAcross * zoom;
            center += pan;

            GUI.BeginClip(rect);
            Rect localInner = new Rect(inner.x - rect.x, inner.y - rect.y, inner.width, inner.height);
            Vector2 localCenter = new Vector2(localInner.center.x, localInner.center.y);

            DrawGrid(localInner, localCenter, center, scale);
            DrawUnitTile(localCenter, center, scale);
            DrawWireframe(channel, localInner, localCenter, center, scale);

            GUI.EndClip();
            DrawPreviewBorder(rect);
        }

        private void HandlePreviewInput(Rect rect, ChannelData channel)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition) && !dragging)
                return;

            Rect inner = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            GetView(channel, inner, out _, out float unitsAcross);
            float scale = Mathf.Min(inner.width, inner.height) / unitsAcross * zoom;

            if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -evt.delta.y), 0.2f, 12f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && (evt.button == 1 || evt.button == 2))
            {
                dragging = true;
                lastMousePosition = evt.mousePosition;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && dragging)
            {
                Vector2 delta = evt.mousePosition - lastMousePosition;
                pan += new Vector2(-delta.x / scale, delta.y / scale);
                lastMousePosition = evt.mousePosition;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                evt.Use();
            }
        }

        private void GetView(ChannelData channel, Rect rect, out Vector2 center, out float unitsAcross)
        {
            if (!frameAllUVs)
            {
                center = new Vector2(0.5f, 0.5f);
                unitsAcross = 1.12f;
                return;
            }

            center = (channel.Min + channel.Max) * 0.5f;
            Vector2 size = channel.Max - channel.Min;
            float aspect = rect.width / Mathf.Max(1f, rect.height);
            unitsAcross = Mathf.Max(size.x, size.y * aspect);
            unitsAcross = Mathf.Max(0.05f, unitsAcross * 1.12f);
        }

        private static void DrawGrid(Rect clipRect, Vector2 screenCenter, Vector2 uvCenter, float scale)
        {
            float minU = uvCenter.x + (clipRect.xMin - screenCenter.x) / scale;
            float maxU = uvCenter.x + (clipRect.xMax - screenCenter.x) / scale;
            float minV = uvCenter.y - (clipRect.yMax - screenCenter.y) / scale;
            float maxV = uvCenter.y - (clipRect.yMin - screenCenter.y) / scale;
            float minorStep = scale >= 220f ? 0.1f : scale >= 90f ? 0.25f : scale >= 40f ? 0.5f : 1f;

            Handles.BeginGUI();
            int firstU = Mathf.FloorToInt(minU / minorStep);
            int lastU = Mathf.CeilToInt(maxU / minorStep);
            for (int i = firstU; i <= lastU && i - firstU < 160; i++)
            {
                float u = i * minorStep;
                bool major = Mathf.Abs(u - Mathf.Round(u)) < 0.0001f;
                Handles.color = major
                    ? new Color(0.55f, 0.62f, 0.70f, 0.34f)
                    : new Color(0.55f, 0.62f, 0.70f, 0.12f);
                float x = screenCenter.x + (u - uvCenter.x) * scale;
                Handles.DrawLine(new Vector3(x, clipRect.yMin), new Vector3(x, clipRect.yMax));
            }

            int firstV = Mathf.FloorToInt(minV / minorStep);
            int lastV = Mathf.CeilToInt(maxV / minorStep);
            for (int i = firstV; i <= lastV && i - firstV < 160; i++)
            {
                float v = i * minorStep;
                bool major = Mathf.Abs(v - Mathf.Round(v)) < 0.0001f;
                Handles.color = major
                    ? new Color(0.55f, 0.62f, 0.70f, 0.34f)
                    : new Color(0.55f, 0.62f, 0.70f, 0.12f);
                float y = screenCenter.y - (v - uvCenter.y) * scale;
                Handles.DrawLine(new Vector3(clipRect.xMin, y), new Vector3(clipRect.xMax, y));
            }
            Handles.EndGUI();
        }

        private static void DrawUnitTile(Vector2 screenCenter, Vector2 uvCenter, float scale)
        {
            Vector3[] corners =
            {
                ToScreen(Vector2.zero, screenCenter, uvCenter, scale),
                ToScreen(Vector2.right, screenCenter, uvCenter, scale),
                ToScreen(Vector2.one, screenCenter, uvCenter, scale),
                ToScreen(Vector2.up, screenCenter, uvCenter, scale),
                ToScreen(Vector2.zero, screenCenter, uvCenter, scale)
            };

            Handles.BeginGUI();
            Handles.color = new Color(0.95f, 0.97f, 1f, 0.78f);
            Handles.DrawAAPolyLine(2.2f, corners);
            Handles.EndGUI();
        }

        private void DrawWireframe(ChannelData channel, Rect clipRect, Vector2 screenCenter, Vector2 uvCenter, float scale)
        {
            Handles.BeginGUI();
            int edgeBudget = MaxDrawnEdges;
            for (int submeshIndex = 0; submeshIndex < submeshes.Count && edgeBudget > 0; submeshIndex++)
            {
                SubmeshData submesh = submeshes[submeshIndex];
                var points = new List<Vector3>(Mathf.Min(edgeBudget * 2, submesh.Indices.Length * 2));
                AddEdges(points, submesh, channel.Values, screenCenter, uvCenter, scale, ref edgeBudget);
                Handles.color = SubmeshColors[submeshIndex % SubmeshColors.Length];
                if (points.Count > 0)
                    Handles.DrawLines(points.ToArray());
            }

            if (showVertices)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.72f);
                int step = Mathf.Max(1, channel.Values.Length / 3000);
                for (int i = 0; i < channel.Values.Length; i += step)
                {
                    Vector3 point = ToScreen(channel.Values[i], screenCenter, uvCenter, scale);
                    if (clipRect.Contains(point))
                        Handles.DrawSolidDisc(point, Vector3.forward, 1.6f);
                }
            }
            Handles.EndGUI();
        }

        private static void AddEdges(
            List<Vector3> points,
            SubmeshData submesh,
            Vector4[] uvs,
            Vector2 screenCenter,
            Vector2 uvCenter,
            float scale,
            ref int edgeBudget)
        {
            int[] indices = submesh.Indices;
            switch (submesh.Topology)
            {
                case MeshTopology.Triangles:
                    for (int i = 0; i + 2 < indices.Length && edgeBudget >= 3; i += 3)
                    {
                        AddEdge(points, indices[i], indices[i + 1], uvs, screenCenter, uvCenter, scale);
                        AddEdge(points, indices[i + 1], indices[i + 2], uvs, screenCenter, uvCenter, scale);
                        AddEdge(points, indices[i + 2], indices[i], uvs, screenCenter, uvCenter, scale);
                        edgeBudget -= 3;
                    }
                    break;
                case MeshTopology.Quads:
                    for (int i = 0; i + 3 < indices.Length && edgeBudget >= 4; i += 4)
                    {
                        AddEdge(points, indices[i], indices[i + 1], uvs, screenCenter, uvCenter, scale);
                        AddEdge(points, indices[i + 1], indices[i + 2], uvs, screenCenter, uvCenter, scale);
                        AddEdge(points, indices[i + 2], indices[i + 3], uvs, screenCenter, uvCenter, scale);
                        AddEdge(points, indices[i + 3], indices[i], uvs, screenCenter, uvCenter, scale);
                        edgeBudget -= 4;
                    }
                    break;
                case MeshTopology.Lines:
                    for (int i = 0; i + 1 < indices.Length && edgeBudget > 0; i += 2)
                    {
                        AddEdge(points, indices[i], indices[i + 1], uvs, screenCenter, uvCenter, scale);
                        edgeBudget--;
                    }
                    break;
                case MeshTopology.LineStrip:
                    for (int i = 0; i + 1 < indices.Length && edgeBudget > 0; i++)
                    {
                        AddEdge(points, indices[i], indices[i + 1], uvs, screenCenter, uvCenter, scale);
                        edgeBudget--;
                    }
                    break;
            }
        }

        private static void AddEdge(
            List<Vector3> points,
            int indexA,
            int indexB,
            Vector4[] uvs,
            Vector2 screenCenter,
            Vector2 uvCenter,
            float scale)
        {
            if ((uint)indexA >= uvs.Length || (uint)indexB >= uvs.Length)
                return;
            if (!IsFinite(uvs[indexA]) || !IsFinite(uvs[indexB]))
                return;

            points.Add(ToScreen(uvs[indexA], screenCenter, uvCenter, scale));
            points.Add(ToScreen(uvs[indexB], screenCenter, uvCenter, scale));
        }

        private static Vector3 ToScreen(Vector2 uv, Vector2 screenCenter, Vector2 uvCenter, float scale)
        {
            return new Vector3(
                screenCenter.x + (uv.x - uvCenter.x) * scale,
                screenCenter.y - (uv.y - uvCenter.y) * scale,
                0f);
        }

        private static Vector3 ToScreen(Vector4 uv, Vector2 screenCenter, Vector2 uvCenter, float scale)
        {
            return ToScreen(new Vector2(uv.x, uv.y), screenCenter, uvCenter, scale);
        }

        private static bool IsFinite(Vector4 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static void DrawPreviewBorder(Rect rect)
        {
            Color border = EditorGUIUtility.isProSkin
                ? new Color(0.38f, 0.43f, 0.50f, 1f)
                : new Color(0.30f, 0.34f, 0.40f, 1f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
        }

        private void DrawLegend()
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < submeshes.Count; i++)
            {
                Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
                EditorGUI.DrawRect(swatch, SubmeshColors[i % SubmeshColors.Length]);
                GUILayout.Label($"Submesh {i}", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void LoadMesh(Mesh mesh)
        {
            cachedMesh = mesh;
            loadError = null;
            submeshes.Clear();
            Array.Clear(channels, 0, channels.Length);

            if (mesh == null)
                return;

            try
            {
                using (Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData meshData = meshDataArray[0];
                    LoadChannels(mesh, meshData);
                    LoadSubmeshes(meshData);
                }

                if (channels[selectedChannel] == null)
                {
                    for (int i = 0; i < ChannelCount; i++)
                    {
                        if (channels[i] == null)
                            continue;

                        selectedChannel = i;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                loadError = $"Could not read mesh data from “{mesh.name}”.\n{exception.Message}";
            }
        }

        private void LoadChannels(Mesh mesh, Mesh.MeshData meshData)
        {
            int vertexCount = meshData.vertexCount;
            for (int channelIndex = 0; channelIndex < ChannelCount; channelIndex++)
            {
                VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channelIndex);
                if (!mesh.HasVertexAttribute(attribute))
                    continue;

                var nativeUVs = new NativeArray<Vector4>(vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                try
                {
                    meshData.GetUVs(channelIndex, nativeUVs);
                    var values = nativeUVs.ToArray();
                    CalculateBounds(values, out Vector2 min, out Vector2 max);
                    channels[channelIndex] = new ChannelData
                    {
                        Dimension = mesh.GetVertexAttributeDimension(attribute),
                        Values = values,
                        Min = min,
                        Max = max
                    };
                }
                finally
                {
                    nativeUVs.Dispose();
                }
            }
        }

        private void LoadSubmeshes(Mesh.MeshData meshData)
        {
            for (int submeshIndex = 0; submeshIndex < meshData.subMeshCount; submeshIndex++)
            {
                SubMeshDescriptor descriptor = meshData.GetSubMesh(submeshIndex);
                var nativeIndices = new NativeArray<int>(
                    descriptor.indexCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                try
                {
                    meshData.GetIndices(nativeIndices, submeshIndex, true);
                    submeshes.Add(new SubmeshData
                    {
                        Topology = descriptor.topology,
                        Indices = nativeIndices.ToArray()
                    });
                }
                finally
                {
                    nativeIndices.Dispose();
                }
            }
        }

        private static void CalculateBounds(Vector4[] values, out Vector2 min, out Vector2 max)
        {
            if (values.Length == 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }

            int firstValid = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    continue;

                firstValid = i;
                break;
            }

            if (firstValid < 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }

            min = new Vector2(values[firstValid].x, values[firstValid].y);
            max = min;
            for (int i = firstValid + 1; i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    continue;

                Vector2 uv = new Vector2(values[i].x, values[i].y);
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
        }

        private static Mesh ResolveMesh(UnityEngine.Object source)
        {
            if (source is Mesh mesh)
                return mesh;
            if (source is MeshFilter meshFilter)
                return meshFilter.sharedMesh;
            if (source is SkinnedMeshRenderer skinnedMeshRenderer)
                return skinnedMeshRenderer.sharedMesh;
            if (source is Component component)
                return ResolveMesh(component.gameObject);
            if (!(source is GameObject gameObject))
                return null;

            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                return filter.sharedMesh;

            SkinnedMeshRenderer renderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            return renderer != null ? renderer.sharedMesh : null;
        }
    }
}

#endif
