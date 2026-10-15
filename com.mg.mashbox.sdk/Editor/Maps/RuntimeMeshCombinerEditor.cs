using System.Collections.Generic;
using MashBoxSDK.EditorResources;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

[CustomEditor(typeof(RuntimeMeshCombiner))]
public sealed class RuntimeMeshCombinerEditor : Editor
{
    private readonly MeshCombinerMaterialPreview materialPreview = new MeshCombinerMaterialPreview();

    private readonly DataSizePreview dataSizePreview = new DataSizePreview();

    public override bool RequiresConstantRepaint() => true;

    public override void OnInspectorGUI()
    {
        MashBoxInspectorHeaderUtility.DrawScriptHeader();
        bool settingsChanged = DrawDefaultInspector();
        RuntimeMeshCombiner combiner = (RuntimeMeshCombiner)target;
        if (!string.IsNullOrEmpty(combiner.LastSpatialCombineSummary))
            EditorGUILayout.HelpBox("Last spatial combine: " + combiner.LastSpatialCombineSummary, MessageType.Info);
        dataSizePreview.Draw(combiner, serializedObject, settingsChanged);
        materialPreview.Draw(combiner.GetMaterialPreviewMeshFilters, true, settingsChanged);
    }

    private sealed class DataSizePreview
    {
        private double nextRefresh;
        private long sourceGpuBytes, sourceCpuBytes, combinedGpuBytes;
        private long vertices, indices;
        private int sourceCount, uniqueCount, eligibleCount;
        private bool spatial, cpuCopy;
        private bool shadowProxyPossible;

        public void Draw(RuntimeMeshCombiner combiner, SerializedObject settings, bool forceRefresh)
        {
            if (forceRefresh || EditorApplication.timeSinceStartup >= nextRefresh)
            {
                Refresh(combiner, settings);
                nextRefresh = EditorApplication.timeSinceStartup + 1.0;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data Size Estimator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source Renderers / Unique Meshes", $"{sourceCount:N0} / {uniqueCount:N0}");
            SizeField("Source GPU Buffers", sourceGpuBytes, "Shared meshes are counted once, regardless of how many renderers use them.");
            SizeField("Source CPU Read/Write Copy", sourceCpuBytes, "Readable source meshes retain a CPU copy of their vertex and index buffers.");
            SizeField("Source Buffer Total", sourceGpuBytes + sourceCpuBytes, "Estimated GPU buffers plus retained CPU copies.");
            EditorGUILayout.LabelField("Eligible Combine Sources", eligibleCount.ToString("N0"));
            EditorGUILayout.LabelField("Unsplit Vertices / Indices", $"{vertices:N0} / {indices:N0}");
            SizeField("Combined GPU (Unsplit Estimate)", combinedGpuBytes, "Includes a full vertex copy per source submesh and the output index width.");
            SizeField("Combined CPU Copy (Estimate)", cpuCopy ? combinedGpuBytes : 0,
                "Spatial outputs release their CPU copy when Add Mesh Collider is off. Other outputs retain it.");
            SizeField("Source + Combined (Estimate)", sourceGpuBytes + sourceCpuBytes + combinedGpuBytes * (cpuCopy ? 2 : 1),
                "Source buffers remain loaded when source renderers are disabled. Temporary combine allocations are excluded.");
            EditorGUILayout.HelpBox(
                "Mesh vertex/index buffers only; this is not compressed download or build size. " +
                "Textures, materials, object overhead, collider cooking and temporary combine memory are excluded. " +
                (spatial
                    ? "Spatial splitting can change vertex counts and layouts, or retain sources with oversized triangles; combined figures are an unsplit planning estimate."
                    : "Combined vertex data assumes float channels shared across the output; Unity may choose a different layout.") +
                (shadowProxyPossible ? " A shadow proxy may add another combined mesh and CPU copy; these are excluded." : ""),
                MessageType.Info);
            if (GUILayout.Button("Refresh Data Size Estimate"))
            {
                Refresh(combiner, settings);
                nextRefresh = EditorApplication.timeSinceStartup + 1.0;
            }
        }

        private void Refresh(RuntimeMeshCombiner combiner, SerializedObject settings)
        {
            sourceGpuBytes = sourceCpuBytes = combinedGpuBytes = vertices = indices = 0;
            sourceCount = uniqueCount = eligibleCount = 0;
            spatial = settings.FindProperty("useSpatialChunks").boolValue;
            cpuCopy = !spatial || settings.FindProperty("addMeshCollider").boolValue;
            var seen = new HashSet<Mesh>();
            var dimensions = new Dictionary<VertexAttribute, int>();
            var materials = new HashSet<Material>();
            // Use a union of channels as mixed layouts can expand when combined.

            foreach (MeshFilter filter in combiner.GetMaterialPreviewMeshFilters())
            {
                Mesh mesh = filter.sharedMesh;
                sourceCount++;
                if (seen.Add(mesh))
                {
                    long bytes = MeshBufferBytes(mesh);
                    sourceGpuBytes += bytes;
                    if (mesh.isReadable) sourceCpuBytes += bytes;
                }
                if (!combiner.IsDataSizePreviewSource(filter)) continue;
                eligibleCount++;
                long copiedVertices = (long)mesh.vertexCount * mesh.subMeshCount;
                vertices += copiedVertices;
                CollectDimensions(mesh, dimensions);
                Material[] slots = filter.GetComponent<MeshRenderer>().sharedMaterials;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    indices += (long)mesh.GetIndexCount(submesh);
                    materials.Add(submesh < slots.Length ? slots[submesh] : null);
                }
            }
            uniqueCount = seen.Count;
            int unionStride = 0;
            foreach (int dimension in dimensions.Values) unionStride += dimension * sizeof(float);
            // Spatial chunks use 16-bit indices; the single output uses 32-bit above 65,535 vertices.
            // Use the channel union for a conservative mixed-layout planning estimate.
            combinedGpuBytes = vertices * unionStride + indices * (spatial || vertices <= 65535 ? 2 : 4);
            shadowProxyPossible = !spatial && materials.Count > 1 &&
                settings.FindProperty("combineShadowSubmeshes").boolValue;
        }

        private static long MeshBufferBytes(Mesh mesh)
        {
            long bytes = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
                bytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
            int indexBytes = mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                bytes += (long)mesh.GetIndexCount(submesh) * indexBytes;
            return bytes;
        }

        private static void CollectDimensions(Mesh mesh, Dictionary<VertexAttribute, int> dimensions)
        {
            foreach (VertexAttributeDescriptor attribute in mesh.GetVertexAttributes())
            {
                dimensions.TryGetValue(attribute.attribute, out int previous);
                dimensions[attribute.attribute] = Mathf.Max(previous, attribute.dimension);
            }
        }

        private static void SizeField(string label, long bytes, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(label, tooltip),
                new GUIContent(FormatBytes(bytes), $"{bytes:N0} bytes"));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes:N0} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:N2} KiB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} MiB";
            return $"{bytes / (1024.0 * 1024 * 1024):N2} GiB";
        }
    }
}
