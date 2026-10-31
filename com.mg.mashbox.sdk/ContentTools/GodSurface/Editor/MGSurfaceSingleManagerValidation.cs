using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceSingleManagerValidation
    {
        public static string Run()
        {
            var log = new StringBuilder();
            void Check(bool value, string text) { if (!value) throw new Exception(text); log.AppendLine("PASS " + text); }
            var scene = EditorSceneManager.NewPreviewScene();
            var otherScene = EditorSceneManager.NewPreviewScene();
            Material material = null;
            try
            {
                GameObject Root(string name, Scene targetScene)
                { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, targetScene); return go; }
                GameObject Prop(string name, Transform parent, Scene targetScene)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; SceneManager.MoveGameObjectToScene(go, targetScene);
                    go.transform.SetParent(parent, false); go.GetComponent<MeshRenderer>().sharedMaterial = material; return go;
                }
                material = new Material(Shader.Find("HDRP/Lit"));
                var host = Root("Manager", scene); var world = host.AddComponent<MGSurfaceWorld>(); world.previewInEditor = true;
                var rocks = Root("Rocks", scene); var walls = Root("Walls", scene);
                var a = Prop("Rock", rocks.transform, scene); var b = Prop("Wall", walls.transform, scene);
                var outside = Prop("Unlisted", null, scene);
                walls.transform.position = new Vector3(100,0,0);
                var foreign = Prop("Foreign scene", null, otherScene);
                Check(world.EnumerateRenderers().Count() == 0, "Empty roots list registers nothing");
                world.roots = new[] { rocks.transform, a.transform, walls.transform, rocks.transform, foreign.transform };
                Check(world.EnumerateRenderers().Count() == 2, "External roots work, overlapping roots deduplicate, cross-scene roots are ignored");
                Check(MGSurfaceWorld.ForProp(a.transform) == world && MGSurfaceWorld.ForProp(outside.transform) == null,
                    "Movable props resolve their manager through the explicit root list");
                var duplicateHost = Root("Extra manager", scene); var duplicate = duplicateHost.AddComponent<MGSurfaceWorld>();
                Check(!duplicate.enabled && !duplicate.IsSceneManager && world.IsSceneManager, "Extra component is blocked without replacing the existing manager");
                duplicate.previewInEditor = true; duplicate.roots = new[] { rocks.transform }; duplicate.Rebuild();
                Check(!duplicate.IsRendering && !string.IsNullOrEmpty(duplicate.LastError), "Extra manager cannot allocate or draw a registry");
                bool refused = false; try { MGSurfaceSingleManager.Validate(scene); } catch (BuildFailedException) { refused = true; }
                Check(refused, "Bake and build validation reject extra components even when disabled");
                Undo.DestroyObjectImmediate(duplicate); MGSurfaceSingleManager.Validate(scene);
                world.Rebuild();
                Check(world.InstanceCount == 2 && world.CellCount == 2, "One registry draws disjoint root hierarchies in their original positions");
                var propMarker = a.AddComponent<MGSurfaceProp>();
                var collider = a.GetComponent<BoxCollider>(); var mesh = a.GetComponent<MeshFilter>().sharedMesh;
                Vector3 before = a.transform.position;
                a.transform.position += Vector3.forward * 3;
                propMarker.SendMessage("LateUpdate"); world.SendMessage("LateUpdate");
                Check(world.CellBounds.Any(bounds => bounds.Contains(a.GetComponent<MeshRenderer>().bounds.center)), "Movable external props update registered cell bounds");
                a.transform.SetParent(null, true); world.SendMessage("LateUpdate");
                // Remove the explicit leaf entry so the moved prop genuinely leaves every registered hierarchy.
                world.roots = new[] { rocks.transform, walls.transform }; propMarker.Notify(); world.RequestRebuild(); world.SendMessage("LateUpdate");
                Check(world.InstanceCount == 1 && !a.GetComponent<MeshRenderer>().forceRenderingOff, "Leaving registered roots restores normal rendering");
                a.transform.SetParent(rocks.transform, true); propMarker.Notify(); world.RequestRebuild(); world.Rebuild();
                var roots = world.roots.ToArray(); bool preview = world.previewInEditor; float distance = world.cellDrawDistance;
                var replacement = MGSurfaceSingleManager.MoveToEmptyObject(world);
                Check(!world && replacement && replacement.transform.childCount == 0 && replacement.IsSceneManager,
                    "Migration creates one empty manager and removes only the original manager component");
                Check(host && roots.SequenceEqual(replacement.roots) && replacement.previewInEditor == preview && replacement.cellDrawDistance == distance,
                    "Migration preserves the original object, root references and settings");
                replacement.Rebuild();
                Check(replacement.InstanceCount == 2 && collider && collider.enabled && a.GetComponent<MeshFilter>().sharedMesh == mesh && a.transform.parent == rocks.transform,
                    "Migration preserves prop hierarchies, source meshes and colliders");
                replacement.Release();
                log.AppendLine("SUCCESS"); return log.ToString();
            }
            finally
            {
                foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                foreach (var root in otherScene.GetRootGameObjects()) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene); EditorSceneManager.ClosePreviewScene(otherScene);
                if (material) Object.DestroyImmediate(material);
            }
        }
    }
}
