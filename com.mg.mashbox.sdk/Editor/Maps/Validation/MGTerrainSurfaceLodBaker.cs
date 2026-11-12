#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainSurfaceLodBaker
    {
        static Queue<MGTerrain> remaining;
        static MGTerrain terrain;
        static MGTerrain.SurfaceLodBake work;
        static MGTerrainSurfaceLodAsset asset;
        static readonly List<MGTerrainSurfaceLodAsset.Level> levels = new();
        static bool snapshotted;
        static int levelIndex, total, completed, sourceDirty;
        static string sourcePath, sourceHash, resultPath;
        static double nextProgress;
        public static bool IsBaking => remaining != null;
        [MenuItem("Tools/MashBox/MG Terrain/Bake Surface HLOD %#F6")]
        public static void BakeSelectedWorld()
        {
            var selected = Selection.activeGameObject;
            var world = selected != null ? selected.GetComponentInParent<MGTerrainWorld>() : null;
            if (world == null)
            {
                var worlds = UnityEngine.Object.FindObjectsByType<MGTerrainWorld>(FindObjectsSortMode.None);
                if (worlds.Length != 1) { Debug.LogError("Select the terrain world to bake."); return; }
                world = worlds[0];
            }
            Bake(world);
        }
        public static void Bake(MGTerrainWorld world)
        {
            if (IsBaking || world == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            remaining = new Queue<MGTerrain>(world.Chunks.Where(t => t != null));
            total = remaining.Count; completed = 0;
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalDiagnostics","SurfaceHLOD");
            Directory.CreateDirectory(folder); resultPath = Path.Combine(folder,"bake-results.txt");
            File.WriteAllText(resultPath,$"Started explicit HLOD bake for {total} tiles.\n");
            EditorApplication.update += Step;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        static void OnPlayMode(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) Cancel(); }
        public static void Cancel() => Finish("Cancelled; completed tile bakes retained.");
        static void Step()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now >= nextProgress)
                {
                    nextProgress = now + .5;
                    if (EditorUtility.DisplayCancelableProgressBar("Bake terrain HLOD",$"{completed}/{total} tiles — {(terrain != null ? terrain.name : "preparing")}", total > 0 ? (float)completed / total : 1)) { Cancel(); return; }
                }
                if (terrain == null)
                {
                    if (remaining.Count == 0) { Finish("COMPLETE"); return; }
                    terrain = remaining.Dequeue();
                    if (terrain == null || terrain.MeshFilter == null || terrain.MeshFilter.sharedMesh == null) throw new InvalidOperationException("Terrain source missing.");
                    var source = terrain.MeshFilter.sharedMesh;
                    sourcePath = AssetDatabase.GetAssetPath(source);
                    if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/",StringComparison.Ordinal)) throw new InvalidOperationException("Save the editable terrain mesh under Assets before baking.");
                    // Save only the source being baked, so stale detection also survives reloads.
                    AssetDatabase.SaveAssetIfDirty(source);
                    sourceDirty = EditorUtility.GetDirtyCount(source);
                    sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
                    File.AppendAllText(resultPath, terrain.name + ": source " + sourcePath + "\n");
                    work = new MGTerrain.SurfaceLodBake(terrain); snapshotted = false; levelIndex = 0; levels.Clear();
                    return;
                }
                if (EditorUtility.GetDirtyCount(terrain.MeshFilter.sharedMesh) != sourceDirty) throw new InvalidOperationException("Terrain changed during baking; run Bake again after editing.");
                if (!snapshotted) { snapshotted = work.SnapshotNext(); return; }
                if (!work.Complete) return;
                if (levelIndex < work.LevelCount) { levels.Add(work.CreateLevel(levelIndex++)); return; }
                if (levels.Count == 0) throw new InvalidOperationException(terrain.name + ": source has no safe reduced geometry; original retained.");
                string parent = Path.GetDirectoryName(sourcePath).Replace('\\','/');
                string folder = parent + "/HLOD";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent,"HLOD");
                // New version is committed only after all levels exist; cancellation never
                // destroys the previously assigned bake. Old versions can be removed explicitly.
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + " HLOD.asset");
                asset = ScriptableObject.CreateInstance<MGTerrainSurfaceLodAsset>();
                asset.Source = terrain.MeshFilter.sharedMesh; asset.SourceVertexCount = asset.Source.vertexCount;
                asset.SourceDependencyHash = sourceHash; asset.Levels = levels.ToArray();
                AssetDatabase.CreateAsset(asset,path);
                foreach (var level in levels) AssetDatabase.AddObjectToAsset(level.Mesh,asset);
                EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                Undo.RecordObject(terrain,"Assign baked terrain HLOD");
                terrain.AssignBakedSurfaceLod(asset); EditorUtility.SetDirty(terrain);
                PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                File.AppendAllText(resultPath,$"{terrain.name}: {path}; triangles {string.Join(", ",levels.Select(l=>l.TriangleCount))}\n");
                levels.Clear(); asset = null; work.Dispose(); work = null; terrain = null; completed++;
            }
            catch (Exception e) { Debug.LogException(e); Finish("FAILED: " + e.Message); }
        }
        static void Finish(string message)
        {
            EditorApplication.update -= Step; AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorUtility.ClearProgressBar(); work?.Dispose(); work = null;
            foreach (var level in levels) if (level.Mesh != null && !AssetDatabase.Contains(level.Mesh)) UnityEngine.Object.DestroyImmediate(level.Mesh);
            levels.Clear(); remaining = null; terrain = null; asset = null;
            if (!string.IsNullOrEmpty(resultPath)) File.AppendAllText(resultPath,message+"\n");
            Debug.Log("Terrain HLOD bake: " + message);
            // One repaint after an explicit bake, never one per generated LOD.
            SceneView.RepaintAll();
        }
    }
}
#endif
