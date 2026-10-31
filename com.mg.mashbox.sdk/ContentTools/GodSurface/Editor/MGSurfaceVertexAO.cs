using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceVertexAO
    {
        public const string Strength = "_MGSurfaceVertexAOStrength";
        [Serializable] public sealed class Settings
        {
            public int samples = 256;
            public float relativeDistance = .12f, bias = .001f, falloff = 1;
            public bool twoSided = true;
        }
        // Read the editor mesh data without changing FBX importer Read/Write settings.
        public static Mesh ReadableCopy(Mesh source)
        {
            if (!source || source.blendShapeCount != 0) throw new InvalidOperationException("Vertex AO supports static prop meshes without blend shapes.");
            using var input = MeshUtility.AcquireReadOnlyMeshData(source);
            var output = Mesh.AllocateWritableMeshData(1);
            var copy = new Mesh { name = source.name + " [Vertex AO]" };
            try
            {
                output[0].SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                    input[0].GetVertexData<byte>(stream).CopyTo(output[0].GetVertexData<byte>(stream));
                if (source.indexFormat == IndexFormat.UInt16)
                {
                    var indices = input[0].GetIndexData<ushort>();
                    output[0].SetIndexBufferParams(indices.Length, source.indexFormat);
                    indices.CopyTo(output[0].GetIndexData<ushort>());
                }
                else
                {
                    var indices = input[0].GetIndexData<uint>();
                    output[0].SetIndexBufferParams(indices.Length, source.indexFormat);
                    indices.CopyTo(output[0].GetIndexData<uint>());
                }
                var writable = output[0]; writable.subMeshCount = source.subMeshCount;
                for (int part = 0; part < source.subMeshCount; part++)
                    output[0].SetSubMesh(part, input[0].GetSubMesh(part), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            catch { output.Dispose(); Object.DestroyImmediate(copy); throw; }
            try
            {
                Mesh.ApplyAndDisposeWritableMeshData(output, copy, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                copy.bounds = source.bounds;
                return copy;
            }
            catch { Object.DestroyImmediate(copy); throw; }
        }
        public static Mesh BakeMesh(Mesh source, Settings settings, Func<float, bool> cancel = null)
        {
            var copy = ReadableCopy(source);
            try
            {
                float distance = Mathf.Max(copy.bounds.size.x, Mathf.Max(copy.bounds.size.y, copy.bounds.size.z)) * settings.relativeDistance;
                var ao = VDPrecisionBaker.SampleIsolated(copy, distance, settings.bias, Mathf.Clamp(settings.samples, 16, 1024), settings.falloff, settings.twoSided, cancel);
                var colors = copy.colors;
                if (colors.Length != copy.vertexCount) colors = Enumerable.Repeat(Color.white, copy.vertexCount).ToArray();
                for (int i = 0; i < colors.Length; i++) colors[i].r = Mathf.Clamp01(ao[i]);
                copy.colors = colors;
                return copy;
            }
            catch { Object.DestroyImmediate(copy); throw; }
        }
        public static Material[] Materials(MGSurfaceLibrary library) => library.banks
            .SelectMany(b => new[] { b.sharedMaterial }.Concat(b.previewMaterials)).Where(m => m && m.HasProperty(Strength)).Distinct().ToArray();
        public static void SetStrength(MGSurfaceWorld world, MGSurfaceLibrary library, float value)
        {
            var materials = Materials(library);
            Undo.RecordObjects(materials, "Adjust God Surface vertex AO");
            foreach (var material in materials) { material.SetFloat(Strength, Mathf.Clamp01(value)); EditorUtility.SetDirty(material); }
            if (world) world.RequestRebuild();
            SceneView.RepaintAll();
        }
        public static string BakeWorld(MGSurfaceWorld world, Settings settings, bool showProgress = true)
        {
            if (!world || !world.surfaceLibrary) throw new InvalidOperationException("Apply a God Surface library to the world first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bake vertex AO outside Play Mode.");
            if (!Materials(world.surfaceLibrary).Any()) throw new InvalidOperationException("Refresh the God Surface shader before baking AO.");
            // Include inactive children and all LODs, including normal-renderer fallbacks.
            // Mixed-shader props are excluded because they may use red for another purpose.
            var filters = world.EnumerateRenderers().Where(r => r.sharedMaterials.Length > 0 &&
                r.sharedMaterials.All(m => m && world.surfaceLibrary.TryResolve(m, out _, out _)))
                .Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh).Distinct().ToArray();
            var sources = filters.Select(f => f.sharedMesh).Distinct().ToArray();
            if (sources.Length == 0) throw new InvalidOperationException("No props with God Surface materials found in this world's roots.");
            var baked = new Dictionary<Mesh, Mesh>(); string folder = null; bool applied = false;
            try
            {
                // Complete all sampling before touching the scene. Cancellation leaves every assignment intact.
                for (int i = 0; i < sources.Length; i++)
                {
                    int index = i; var source = sources[i];
                    baked.Add(source, BakeMesh(source, settings, p => showProgress && EditorUtility.DisplayCancelableProgressBar(
                        "Bake God Surface vertex AO", $"{source.name} ({index + 1}/{sources.Length})", (index + p) / sources.Length)));
                }
                string parent = Path.GetDirectoryName(AssetDatabase.GetAssetPath(world.surfaceLibrary)).Replace('\\', '/');
                folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/Meshes");
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                int asset = 0;
                foreach (var mesh in baked.Values) AssetDatabase.CreateAsset(mesh, folder + "/Mesh" + asset++ + ".asset");
                AssetDatabase.CreateFolder(folder, "Editor");
                File.WriteAllText(folder + "/Editor/BakeSettings.json", JsonUtility.ToJson(settings, true));
                AssetDatabase.ImportAsset(folder + "/Editor/BakeSettings.json");
                world.Release(); Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Apply God Surface vertex AO");
                try
                {
                    Undo.RecordObjects(filters, "Apply shared AO meshes");
                    foreach (var filter in filters)
                    {
                        filter.sharedMesh = baked[filter.sharedMesh];
                        EditorUtility.SetDirty(filter);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                        EditorSceneManager.MarkSceneDirty(filter.gameObject.scene);
                    }
                    SetStrength(world, world.surfaceLibrary, 1);
                    applied = true;
                }
                catch { Undo.RevertAllDownToGroup(group); throw; }
                finally { Undo.CollapseUndoOperations(group); world.RequestRebuild(); }
                foreach (var mesh in baked.Values) AssetDatabase.SaveAssetIfDirty(mesh);
                foreach (var material in Materials(world.surfaceLibrary)) AssetDatabase.SaveAssetIfDirty(material);
                SceneView.RepaintAll();
                return $"Baked {baked.Count} unique meshes (all LODs), shared by {filters.Length:N0} prop renderers. Vertex AO Strength = 1. Source meshes and colliders unchanged. Scene assignments support Undo; save the scene to keep them. Assets: {folder}";
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
                if (!applied && folder != null) AssetDatabase.DeleteAsset(folder);
                foreach (var mesh in baked.Values) if (mesh && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
            }
        }
    }
}

