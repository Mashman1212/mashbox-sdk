using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Editing;
using MashBoxSDK.Maps.Spline;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MapEditImporter
    {
        public static List<string> Conflicts(MapEditDocument document, Scene scene)
        {
            document.Validate();
            var errors = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded) { errors.Add("Choose a loaded destination scene."); return errors; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { errors.Add("Import into an editor in Edit Mode."); return errors; }
            var created = new HashSet<string>(document.changes.Where(c => c.kind != "existing").Select(c => c.id));
            var included = new HashSet<string>(document.changes.Select(c => c.id));
            var lookup = MapEditCodec.Lookup(scene);
            var ids = MapEditCodec.StableIds(scene);
            foreach (var change in document.changes)
            {
                if (change.kind != "existing" && lookup.ContainsKey(change.id)) errors.Add("This created object has already been imported: " + change.after.name);
                if ((change.kind == "existing" || change.kind == "duplicate" || change.kind == "child") &&
                    !(change.kind != "existing" && included.Contains(change.sourceId ?? "")))
                {
                    lookup.TryGetValue(change.sourceId ?? change.id, out var target);
                    if (!target) target = MapEditCodec.ResolvePath(scene, change.sourcePath);
                    if (!target) { errors.Add("Missing source: " + change.sourcePath); continue; }
                    try
                    {
                        var current = MapEditCodec.Capture(target, t => ids.TryGetValue(t, out var id) ? id : MapEditCodec.PathOf(t), change.before.hasMesh);
                        if (!MapEditCodec.Same(current, change.before)) errors.Add("Source changed since the game session: " + change.sourcePath);
                    }
                    catch (Exception error) { errors.Add(change.sourcePath + ": " + error.Message); }
                }
                ValidateReference(change.after.parentId, "parent", lookup, created, errors);
                ValidateReference(change.after.materialSourceId, "material source", lookup, created, errors);
                if (change.after.hasLoft)
                    foreach (var source in change.after.loft.sources) ValidateReference(source.id, "loft spline", lookup, created, errors);
                if (change.kind == "child" && (!created.Contains(change.cloneRootId ?? "") || string.IsNullOrEmpty(change.clonePath)))
                    errors.Add("Missing duplicate root or child path for " + change.id);
            }
            return errors.Distinct().ToList();
        }
        static void ValidateReference(string id, string label, Dictionary<string, Transform> lookup, HashSet<string> created, List<string> errors)
        {
            if (!string.IsNullOrEmpty(id) && !created.Contains(id) && !lookup.ContainsKey(id)) errors.Add("Missing " + label + ": " + id);
        }
        public static void Apply(MapEditDocument document, Scene scene)
        {
            var conflicts = Conflicts(document, scene);
            if (conflicts.Count > 0) throw new InvalidOperationException(string.Join("\n", conflicts.Take(12)));
            var targets = MapEditCodec.Lookup(scene);
            var assets = new List<string>();
            Transform Resolve(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (!targets.TryGetValue(id, out var value)) targets[id] = value = MapEditCodec.ResolvePath(scene, id);
                return value;
            }
            // Resolve before any rename/reparent/delete shifts paths. Duplicate templates before modifying originals.
            foreach (var change in document.changes)
            {
                if (change.kind == "existing" && !targets.ContainsKey(change.id)) targets[change.id] = MapEditCodec.ResolvePath(scene, change.sourcePath);
                Resolve(change.after.parentId);
                Resolve(change.after.materialSourceId);
                if (change.after.hasLoft) foreach (var source in change.after.loft.sources) Resolve(source.id);
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Import runtime map changes");
            try
            {
                var changed = new HashSet<Transform>(document.changes.Where(c => c.kind == "existing").Select(c => Resolve(c.id)).Where(t => t));
                var affectedLofts = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MultiSplineLoft>(true))
                    .Where(loft => changed.Any(t => loft.transform == t || loft.transform.IsChildOf(t)) ||
                        loft.Sources.Any(s => s?.container && changed.Any(t => s.container.transform == t || s.container.transform.IsChildOf(t))))
                    .ToList();
                var undoTargets = new HashSet<Transform>(changed.Concat(affectedLofts.Select(l => l.transform)));
                foreach (var target in undoTargets)
                    if (!undoTargets.Any(other => other != target && target.IsChildOf(other)))
                        Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Import runtime map changes");
                var creations = document.changes.Where(c => c.kind != "existing").ToDictionary(c => c.id);
                var creating = new HashSet<string>();
                Transform Create(string id)
                {
                    if (string.IsNullOrEmpty(id)) return null;
                    if (targets.TryGetValue(id, out var existing) && existing) return existing;
                    if (!creations.TryGetValue(id, out var change)) return Resolve(id);
                    if (!creating.Add(id)) throw new InvalidDataException("Cyclic duplicate source references.");
                    if (change.kind == "child")
                    {
                        var cloneRoot = Create(change.cloneRootId);
                        if (!cloneRoot) throw new InvalidDataException("Missing duplicate root.");
                        var child = MapEditCodec.ResolvePath(scene, change.clonePath, cloneRoot);
                        if (!child) throw new InvalidDataException("Missing duplicate child: " + change.clonePath);
                        targets[id] = child; creating.Remove(id); return child;
                    }
                    GameObject go;
                    if (change.kind == "duplicate")
                    {
                        var source = Create(change.sourceId) ?? MapEditCodec.ResolvePath(scene, change.sourcePath);
                        if (!source) throw new InvalidDataException("The source for duplicate " + change.after.name + " was deleted. Duplicate a surviving object after pulling from Unity.");
                        // Copy the authored hierarchy and its internal references, not the baked runtime hierarchy.
                        go = Object.Instantiate(source.gameObject);
                    }
                    else
                    {
                        go = new GameObject(change.after.name);
                        if (change.kind == "spline") go.AddComponent<SplineContainer>();
                        if (change.kind == "loft") go.AddComponent<MultiSplineLoft>();
                    }
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "Create runtime map object"); targets[change.id] = go.transform;
                    creating.Remove(id);
                    return go.transform;
                }
                foreach (var change in document.changes.Where(c => c.kind != "existing")) Create(change.id);
                foreach (var change in document.changes.Where(c => c.kind == "child"))
                {
                    var root = Resolve(change.cloneRootId);
                    targets[change.id] = MapEditCodec.ResolvePath(scene, change.clonePath, root);
                    if (!targets[change.id]) throw new InvalidDataException("Duplicate child missing: " + change.clonePath);
                }
                foreach (var change in document.changes)
                {
                    Transform target = Resolve(change.id);
                    if (!target) throw new InvalidDataException("Missing edit target: " + change.id);
                    var parent = Resolve(change.after.parentId);
                    if (parent == target || parent && parent.IsChildOf(target)) throw new InvalidDataException("Parent would create a hierarchy cycle.");
                    if (target.parent != parent) Undo.SetTransformParent(target, parent, "Reparent runtime map object");
                    MapEditCodec.Apply(target, change.after, Resolve);
                    if (change.after.hasMesh)
                    {
                        var terrain = target.GetComponent<MGTerrain>();
                        if (!terrain) throw new InvalidDataException("Terrain component missing: " + target.name);
                        // Never mutate an original mesh asset or a mesh shared with another terrain.
                        CloneTerrainMeshes(terrain, document.sessionId, assets);
                        terrain.MeshFilter.sharedMesh.vertices = change.after.vertices;
                        MapEditSession.RefreshTerrain(terrain, true);
                        EditorUtility.SetDirty(terrain.MeshFilter.sharedMesh);
                    }
                    foreach (var component in target.GetComponents<Component>())
                        if (component) { EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
                }
                changed = new HashSet<Transform>(document.changes.Select(c => Resolve(c.id)));
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var loft in root.GetComponentsInChildren<MultiSplineLoft>(true))
                        if (changed.Any(t => t && (loft.transform == t || loft.transform.IsChildOf(t))) ||
                            loft.Sources.Any(s => s?.container && changed.Any(t => t && (s.container.transform == t || s.container.transform.IsChildOf(t)))))
                        {
                            if (loft.VisualsBaked) throw new InvalidOperationException("Choose the source authoring scene, not a scene extracted from a baked map.");
                            // Replacing generated collision through Undo also handles a changed chunk count.
                            var previousChunks = loft.transform.Find("Collider Chunks");
                            if (previousChunks) Undo.DestroyObjectImmediate(previousChunks.gameObject);
                            Mesh mesh = loft.GeneratedMesh ? Object.Instantiate(loft.GeneratedMesh) : new Mesh();
                            loft.SetGeneratedMesh(mesh); loft.Regenerate(); SaveMesh(mesh, document.sessionId, assets);
                            var newChunks = loft.transform.Find("Collider Chunks");
                            if (newChunks) Undo.RegisterCreatedObjectUndo(newChunks.gameObject, "Regenerate loft collision");
                            foreach (var collider in loft.GetComponentsInChildren<MeshCollider>(true))
                                if (collider.sharedMesh && !EditorUtility.IsPersistent(collider.sharedMesh)) SaveMesh(collider.sharedMesh, document.sessionId, assets);
                        }
                foreach (var change in document.changes.Where(c => c.after.deleted))
                    if (Resolve(change.id)) Undo.DestroyObjectImmediate(Resolve(change.id).gameObject);
                var identity = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapEditSceneIdentity>(true)).FirstOrDefault();
                if (!identity)
                {
                    var go = new GameObject("Map edit identities"); SceneManager.MoveGameObjectToScene(go, scene);
                    identity = go.AddComponent<MapEditSceneIdentity>(); Undo.RegisterCreatedObjectUndo(go, "Record imported identities");
                }
                Undo.RecordObject(identity, "Record imported identities");
                foreach (var change in document.changes.Where(c => c.kind != "existing" && !c.after.deleted))
                    identity.objects.Add(new MapEditSceneIdentity.Item { target = Resolve(change.id), id = change.id });
                EditorUtility.SetDirty(identity);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                foreach (string path in assets) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }
        static void CloneTerrainMeshes(MGTerrain terrain, string sessionId, List<string> assets)
        {
            Mesh source = terrain.MeshFilter.sharedMesh;
            var copy = Object.Instantiate(source); SaveMesh(copy, sessionId, assets); terrain.MeshFilter.sharedMesh = copy;
            // Derived collider buffers must also be private before refresh writes vertex arrays.
            foreach (var collider in terrain.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh)
                {
                    var mesh = collider.sharedMesh == source ? copy : Object.Instantiate(collider.sharedMesh);
                    if (mesh != copy) SaveMesh(mesh, sessionId, assets);
                    collider.sharedMesh = mesh;
                }
        }
        static void SaveMesh(Mesh mesh, string sessionId, List<string> assets)
        {
            const string folder = "Assets/Runtime Map Imports";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Runtime Map Imports");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Guid.NewGuid().ToString("N") + ".asset");
            mesh.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(mesh, path); assets.Add(path);
        }
    }
}
