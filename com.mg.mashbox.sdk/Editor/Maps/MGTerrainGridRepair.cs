#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainGridRepair
    {
        static readonly Queue<MGTerrain> Pending = new Queue<MGTerrain>();
        static readonly HashSet<MGTerrain> Queued = new HashSet<MGTerrain>();
        static readonly HashSet<Scene> SeamScenes = new HashSet<Scene>();

        static MGTerrainGridRepair()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => QueueScene(scene);
            EditorApplication.delayCall += QueueLoadedScenes;
        }

        [MenuItem("Tools/MashBox/MG Terrain/Repair Loaded Tile Grids")]
        static void QueueLoadedScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
        }

        static void QueueScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SeamScenes.Add(scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var tile in root.GetComponentsInChildren<MGTerrain>(true))
                    if (Queued.Add(tile)) Pending.Enqueue(tile);
            EditorApplication.update -= ProcessNext;
            if (Pending.Count > 0) EditorApplication.update += ProcessNext;
        }

        static void ProcessNext()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Pending.Clear();
                Queued.Clear();
                SeamScenes.Clear();
            }
            else if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && Pending.Count > 0)
            {
                var tile = Pending.Dequeue();
                Queued.Remove(tile);
                try { Repair(tile); }
                catch (Exception error) { Debug.LogException(error, tile); }
            }
            // No permanent update callback or per-frame mesh polling.
            if (Pending.Count == 0)
            {
                EditorApplication.update -= ProcessNext;
                foreach (var scene in SeamScenes)
                    try { MGTerrainSeamRepair.Repair(scene); }
                    catch (Exception error) { Debug.LogException(error); }
                SeamScenes.Clear();
            }
        }

        internal static bool Repair(MGTerrain tile)
        {
            if (tile == null || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorUtility.IsPersistent(tile) || !tile.gameObject.scene.IsValid()
                || !tile.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(tile.gameObject.scene)) return false;
            var filter = tile.MeshFilter;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return false;
            using var data = new SerializedObject(tile);
            int width = data.FindProperty("m_SurfaceGridWidth").intValue;
            int height = data.FindProperty("m_SurfaceGridHeight").intValue;
            var stored = data.FindProperty("m_SurfaceGridFootprint");
            var hasStored = data.FindProperty("m_HasSurfaceGridFootprint");
            bool known = hasStored.boolValue;
            var footprint = stored.vector4Value;
            var vertices = mesh.vertices;
            if (!TryRestore(vertices, mesh.uv, width, height, known, ref footprint, out int changed)) return false;

            if (!known || !tile.HeightOnlySculpt || changed > 0)
            {
                Undo.RecordObject(tile, "Restore Terrain Grid");
                stored.vector4Value = footprint;
                hasStored.boolValue = true;
                data.FindProperty("m_HeightOnlySculpt").boolValue = true;
                data.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
            }
            if (changed == 0) return false;

            // Keep shared/imported source meshes and the pre-repair geometry intact.
            var repaired = Object.Instantiate(mesh);
            repaired.hideFlags = HideFlags.None;
            repaired.vertices = vertices;
            repaired.RecalculateBounds();
            repaired.RecalculateNormals();
            repaired.RecalculateTangents();
            MGTerrainSceneAssets.Create(repaired, tile, "GridRepaired");
            Undo.RecordObject(filter, "Restore Terrain Grid");
            filter.sharedMesh = repaired;
            data.Update();
            data.FindProperty("m_EditableSculptMesh").objectReferenceValue = repaired;
            data.ApplyModifiedProperties();
            var collider = tile.MeshCollider;
            if (collider != null)
            {
                Undo.RecordObject(collider, "Restore Terrain Grid");
                collider.sharedMesh = null;
                collider.sharedMesh = repaired;
            }
            tile.NotifySurfaceMeshChanged();
            tile.RefreshSurfaceCollidersFromMesh();
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(tile);
            SceneView.RepaintAll();
            Debug.Log($"MG Terrain restored X/Z grid positions for {changed:N0} vertices on '{tile.name}', preserving all heights.", tile);
            return true;
        }

        // Pure geometry planning: do not touch meshes until the entire layout is validated.
        internal static bool TryRestore(Vector3[] vertices, Vector2[] uv, int width, int height,
            bool known, ref Vector4 footprint, out int changed)
        {
            changed = 0;
            if (width < 2 || height < 2 || (long)width * height != vertices.Length || uv.Length != vertices.Length)
                return false;
            const float uvTolerance = .00001f;
            for (int i = 0; i < vertices.Length; i++)
            {
                int x = i % width, z = i / width;
                if (!Finite(vertices[i].x) || !Finite(vertices[i].y) || !Finite(vertices[i].z)
                    || !Finite(uv[i].x) || !Finite(uv[i].y)
                    || Math.Abs(uv[i].x - uv[x].x) > uvTolerance
                    || Math.Abs(uv[i].y - uv[z * width].y) > uvTolerance
                    || (x > 0 && uv[i].x <= uv[i - 1].x)
                    || (z > 0 && uv[i].y <= uv[i - width].y)) return false;
            }
            if (Math.Abs(uv[0].x) > uvTolerance || Math.Abs(uv[0].y) > uvTolerance
                || Math.Abs(uv[width - 1].x - 1) > uvTolerance
                || Math.Abs(uv[(height - 1) * width].y - 1) > uvTolerance) return false;
            if (!known)
            {
                // Median scale/offset ignores isolated skewed vertices, including damaged edges.
                // Refuse ambiguous legacy meshes instead of deriving a grid from their damaged bounds.
                if (!InferAxis(vertices, uv, width, true, out float originX, out float sizeX)
                    || !InferAxis(vertices, uv, width, false, out float originZ, out float sizeZ)) return false;
                footprint = new Vector4(originX, originZ, sizeX, sizeZ);
            }
            if (!Finite(footprint.x) || !Finite(footprint.y) || !Finite(footprint.z) || !Finite(footprint.w)
                || footprint.z <= 0 || footprint.w <= 0) return false;
            float tolerance = Math.Max(.00001f, Math.Max(footprint.z, footprint.w) * .000001f);
            for (int i = 0; i < vertices.Length; i++)
            {
                float x = footprint.x + uv[i].x * footprint.z;
                float z = footprint.y + uv[i].y * footprint.w;
                if (Math.Abs(vertices[i].x - x) <= tolerance && Math.Abs(vertices[i].z - z) <= tolerance) continue;
                vertices[i].x = x;
                vertices[i].z = z;
                changed++;
            }
            return true;
        }

        static bool InferAxis(Vector3[] vertices, Vector2[] uv, int width, bool xAxis, out float origin, out float size)
        {
            int stride = xAxis ? width / 2 : (vertices.Length / width / 2) * width;
            var samples = new List<float>(vertices.Length);
            for (int i = stride; i < vertices.Length; i++)
            {
                if (xAxis && i % width < stride) continue;
                float delta = xAxis ? vertices[i].x - vertices[i - stride].x : vertices[i].z - vertices[i - stride].z;
                float span = xAxis ? uv[i].x - uv[i - stride].x : uv[i].y - uv[i - stride].y;
                samples.Add(delta / span);
            }
            samples.Sort();
            size = samples[samples.Count / 2];
            origin = 0;
            if (!Finite(size) || size <= 0) return false;
            samples.Clear();
            for (int i = 0; i < vertices.Length; i++)
                samples.Add((xAxis ? vertices[i].x : vertices[i].z) - (xAxis ? uv[i].x : uv[i].y) * size);
            samples.Sort();
            origin = samples[samples.Count / 2];
            float tolerance = Math.Max(.00001f, size * .00001f);
            int aligned = 0;
            foreach (float sample in samples) if (Math.Abs(sample - origin) <= tolerance) aligned++;
            return Finite(origin) && aligned >= vertices.Length * .6f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
#endif
