#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validate Seam Fit Hover and Cache")]
        public static void ValidateSeamFitHoverAndCache()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var resources = new List<UnityEngine.Object>();
            var previousOwner = s_ActiveSceneToolOwner;
            bool editing = MBEditorToolState.ActiveEditing;
            var selection = Selection.activeObject;
            var tool = Tools.current;
            MeshSculptWindow window = null;
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); }
            Mesh Grid(int width, float height)
            {
                var vertices = new Vector3[width * width];
                var indices = new int[(width - 1) * (width - 1) * 6];
                for (int z = 0; z < width; z++) for (int x = 0; x < width; x++)
                    vertices[z * width + x] = new Vector3(x, height, z);
                for (int z = 0, t = 0; z < width - 1; z++) for (int x = 0; x < width - 1; x++)
                {
                    int i = z * width + x;
                    indices[t++] = i; indices[t++] = i + width; indices[t++] = i + 1;
                    indices[t++] = i + 1; indices[t++] = i + width; indices[t++] = i + width + 1;
                }
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices, triangles = indices };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
                return mesh;
            }
            GameObject Owner(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            try
            {
                MBEditorToolState.ActiveEditing = false;
                Selection.activeObject = null;
                var terrain = Owner("Seam hover validation").AddComponent<MGTerrain>();
                var mesh = Grid(513, 0);
                terrain.MeshFilter.sharedMesh = mesh;
                var modifier = terrain.gameObject.AddComponent<MeshSculptModifier>();
                modifier.SetTarget(terrain.MeshFilter);
                window = CreateInstance<MeshSculptWindow>();
                window.m_Mode = MeshSculptModifier.SculptMode.SeamFit;
                window.ActivateModifier(modifier);
                Check(window.m_SculptPickingObject == null && terrain.GetComponent<MeshCollider>() == null,
                    "Hover activation must not add or cook a picking collider on collision-less terrain.");
                var ray = new Ray(new Vector3(128.25f, 100, 128.25f), Vector3.down);
                var watch = new System.Diagnostics.Stopwatch();
                watch.Start();
                Check(terrain.RaycastSculptSurface(ray, out var hit, 200) && Mathf.Abs(hit.point.y) < .0001f, "Cold mesh pick missed.");
                watch.Stop();
                double coldMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                for (int i = 0; i < 100; i++) Check(terrain.RaycastSculptSurface(ray, out hit, 200), "Warm mesh pick missed.");
                watch.Stop();
                Debug.Log($"SEAM FIT HOVER: 513x513 grid, cold {coldMs:F3} ms; warm {watch.Elapsed.TotalMilliseconds / 100:F3} ms per pick (synthetic preview scene, not full frame time).");

                Check(!terrain.RaycastSculptSurface(ray, out _, 50), "Picking ignored maximum distance.");
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].y = 3;
                mesh.vertices = vertices; mesh.RecalculateBounds(); terrain.NotifySurfaceMeshChanged();
                Check(terrain.RaycastSculptSurface(ray, out hit, 200) && Mathf.Abs(hit.point.y - 3) < .0001f, "Picking ignored same-mesh deformation.");
                terrain.transform.position = new Vector3(10, 7, 20);
                terrain.transform.localScale = new Vector3(2, 3, 4);
                var localPoint = new Vector3(128.25f, 3, 128.25f);
                var worldPoint = terrain.transform.TransformPoint(localPoint);
                var scaledRay = new Ray(worldPoint + Vector3.up * 30, Vector3.down);
                Check(terrain.RaycastSculptSurface(scaledRay, out hit, 40) && (hit.point - worldPoint).sqrMagnitude < .00001f
                    && Mathf.Abs(hit.distance - 30) < .0001f && Vector3.Dot(hit.normal, Vector3.up) > .99f,
                    "Picking ignored transform, distance or normal.");
                // Same mesh identity and vertex count, changed topology: no stale hit.
                mesh.triangles = new[] { 0, 513, 1 };
                Check(!terrain.RaycastSculptSurface(scaledRay, out _, 40), "Picking hit removed faces.");
                terrain.MeshFilter.sharedMesh = Grid(2, 8);
                worldPoint = terrain.transform.TransformPoint(new Vector3(.25f, 8, .25f));
                Check(terrain.RaycastSculptSurface(new Ray(worldPoint + Vector3.up * 10, Vector3.down), out hit, 20)
                    && (hit.point - worldPoint).sqrMagnitude < .00001f, "Picking ignored mesh replacement.");

                var root = Owner("Seam cache world");
                var world = root.AddComponent<MGTerrainWorld>();
                var tiles = new List<MGTerrain>();
                var colliders = new List<MeshCollider>();
                var originalMeshes = new List<Mesh>();
                for (int n = 0; n < 2; n++)
                {
                    var tile = Owner("Seam cache tile " + n).AddComponent<MGTerrain>();
                    tile.transform.SetParent(root.transform); tile.transform.localPosition = new Vector3(n * 16, 0, 0);
                    tile.MeshFilter.sharedMesh = Grid(17, 0); tile.ConfigureSurfaceGrid(17, 17);
                    var collider = tile.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = tile.MeshFilter.sharedMesh;
                    colliders.Add(collider); originalMeshes.Add(collider.sharedMesh);
                    using (var data = new SerializedObject(tile))
                    {
                        data.FindProperty("m_SceneSculptCopy").boolValue = true;
                        data.FindProperty("m_NeedsCollisionChunks").boolValue = false;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                    tiles.Add(tile);
                }
                world.RefreshChunks();
                var target = Owner("Seam target").AddComponent<MeshFilter>();
                target.gameObject.AddComponent<MeshRenderer>(); target.sharedMesh = Grid(33, .4f);
                window.m_Modifier = MGTerrainTileAuthoring.Modifier(tiles[0]);
                window.m_SeamTargetRoot = target.gameObject;
                window.m_SeamVerticesOnly = false; window.m_SeamNormalBlend = 0;
                window.m_SeamSurfaceOffset = 0; window.m_SeamThreshold = 1;
                window.m_Radius = 4; window.m_Strength = .25f; window.m_IsSculpting = true;
                var dab = new RaycastHit { point = new Vector3(16, 0, 8), normal = Vector3.up };
                Undo.IncrementCurrentGroup();
                window.RecordStroke(dab, false, false);
                for (int n = 0; n < colliders.Count; n++)
                    Check(colliders[n].sharedMesh == originalMeshes[n], "A world dab recooked collision before mouse-up.");
                Check(window.m_SeamBrushes.Count == 2, "World dab did not retain both tile target caches.");
                var retained = new Dictionary<MeshSculptModifier, MeshSeamFitBrush>(window.m_SeamBrushes);
                window.RecordStroke(dab, false, false);
                foreach (var pair in retained)
                    Check(window.m_SeamBrushes.TryGetValue(pair.Key, out var brush) && ReferenceEquals(brush, pair.Value), "World dab rebuilt its target cache.");
                var nextModifier = MGTerrainTileAuthoring.Modifier(tiles[1]);
                window.SwitchModifierDuringStroke(nextModifier);
                Check(window.m_SeamBrushes.Count == 2, "Seam crossing discarded stroke caches.");
                Check(tiles[0].MeshFilter.sharedMesh.vertices[8 * 17 + 16].y > 0, "Seam fitting did not raise terrain.");
                window.StopStroke();
                Check(window.m_SeamBrushes.Count == 0, "Stroke completion retained target caches.");
                for (int n = 0; n < colliders.Count; n++)
                    Check(colliders[n].sharedMesh == tiles[n].MeshFilter.sharedMesh, "Mouse-up did not update touched tile collision.");
                foreach (var tile in tiles) if (!resources.Contains(tile.MeshFilter.sharedMesh)) resources.Add(tile.MeshFilter.sharedMesh);

                var sampler = new MeshSeamFitBrush(new[] { target }, tiles[0].MeshFilter, true);
                sampler.Sample(tiles[0].MeshFilter, dab.point, 4, 1, .25f, 2, 0, true);
                watch.Restart();
                int resultCount = 0;
                for (int i = 0; i < 30; i++) resultCount = sampler.Sample(tiles[0].MeshFilter, dab.point, 4, 1, .25f, 2, 0, true).Length;
                watch.Stop();
                Check(resultCount > 0, "Vertex snapping produced no samples.");
                Debug.Log($"SEAM FIT SAMPLING: warmed vertex fit {watch.Elapsed.TotalMilliseconds / 30:F3} ms ({resultCount} output samples).");
                Debug.Log("SEAM FIT VALIDATION PASSED: collider-free hover, distance/transform/normal, deformation, topology/holes, replacement, world-dab and crossing cache reuse, stroke cleanup and raising.");
            }
            finally
            {
                if (window != null) DestroyImmediate(window);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var resource in resources) if (resource != null) DestroyImmediate(resource);
                MBEditorToolState.ActiveEditing = editing; Selection.activeObject = selection; Tools.current = tool;
                if (previousOwner != null && editing) previousOwner.ActivateSceneTool();
            }
        }
    }
}
#endif
