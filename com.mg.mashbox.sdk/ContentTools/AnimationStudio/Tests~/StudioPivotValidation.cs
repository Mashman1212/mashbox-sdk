using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioPivotValidation
    {
        static readonly List<string> Results = new List<string>();
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Near(Vector3 a, Vector3 b, string label) => Check(Vector3.Distance(a, b) < .0002f, label);
        static Bounds Bounds(StudioModelEditor editor) => (Bounds)typeof(StudioModelEditor).GetMethod("BoundsFor", Private).Invoke(editor, new object[] { 0 });
        public static void Run()
        {
            StudioModelEditor editor = null;
            GameObject cameraObject = null;
            RenderTexture target = null;
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Pivot Validation.asset"));
                doc.AddPrimitive(PrimitiveType.Cube);
                editor = new StudioModelEditor(() => {}); editor.Load(doc); editor.SelectObject(0);
                var mesh = editor.ActiveMesh;
                var world = mesh.vertices.Select(v => editor.Transforms[0].TransformPoint(v)).ToArray();
                var originalBounds = Bounds(editor);
                cameraObject = new GameObject("Pivot validation camera");
                var camera = cameraObject.AddComponent<Camera>(); camera.cameraType = CameraType.Preview;
                camera.scene = editor.Scene;
                camera.transform.position = new Vector3(2, 1.5f, -4); camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true; camera.orthographicSize = 1.5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                target = new RenderTexture(256, 256, 24); camera.targetTexture = target;
                Func<byte[]> render = () => {
                    camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
                    var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    var pixels = image.GetRawTextureData<byte>().ToArray();
                    Object.DestroyImmediate(image); RenderTexture.active = previous; return pixels;
                };
                var before = render();
                foreach (var pivot in new[] { new Vector3(.5f, -.5f, -.5f), new Vector3(3, -2, 4) })
                {
                    Undo.IncrementCurrentGroup(); doc.Repivot(0, editor.Transforms, pivot);
                    Near(Bounds(editor).center, originalBounds.center, "Repivot leaves outline center on geometry");
                    Near(Bounds(editor).size, originalBounds.size, "Corner and external pivots do not expand the outline");
                    Check(world.Zip(mesh.vertices, (a, b) => Vector3.Distance(a, editor.Transforms[0].TransformPoint(b)) < .0002f).All(x => x), "Repivot preserves world vertices");
                    Check(before.SequenceEqual(render()), "Repivot keeps rendered surface identical");
                    Undo.PerformUndo();
                    Check(before.SequenceEqual(render()), "Pivot Undo keeps rendered surface identical");
                    Undo.PerformRedo();
                    Check(before.SequenceEqual(render()), "Pivot Redo keeps rendered surface identical");
                }
                var corner = world.First(v => v == new Vector3(.5f, .5f, -.5f));
                var screen = camera.WorldToScreenPoint(corner);
                foreach (float dpi in new[] { 1f, 1.25f, 2f })
                {
                    var point = new Vector2(screen.x / dpi, (camera.pixelHeight - screen.y) / dpi);
                    Check(editor.TryPivotSnapVertex(camera, point, dpi, out var snapped), "Vertex snap finds preview geometry at DPI " + dpi);
                    Near(snapped, corner, "Vertex snap returns world-space corner at DPI " + dpi);
                }
                Check(!editor.TryPivotSnapVertex(camera, new Vector2(-1000, -1000), 1, out _), "Vertex snap ignores empty space");
                editor.Transforms[0].gameObject.SetActive(false);
                Check(!editor.TryPivotSnapVertex(camera, new Vector2(screen.x, camera.pixelHeight - screen.y), 1, out _), "Vertex snap ignores hidden models");
                editor.Transforms[0].gameObject.SetActive(true);
                // Nonuniformly scaled and rotated parents must retain exact geometry too.
                doc.nodes.Add(new StudioModelDocument.Node { name = "Child", parent = 0, position = new Vector3(.3f, .2f, .1f) });
                doc.nodes[0].rotation = Quaternion.Euler(17, 38, 9); doc.nodes[0].scale = new Vector3(2, 1, .7f);
                editor.Load(doc); editor.SelectObject(0);
                var child = editor.Transforms[1].position;
                world = mesh.vertices.Select(v => editor.Transforms[0].TransformPoint(v)).ToArray();
                doc.Repivot(0, editor.Transforms, world[0]);
                Check(world.Zip(mesh.vertices, (a, b) => Vector3.Distance(a, editor.Transforms[0].TransformPoint(b)) < .0002f).All(x => x), "Scaled and rotated repivot preserves geometry");
                Near(child, editor.Transforms[1].position, "Repivot preserves child position");
                Near(Bounds(editor).size, editor.Transforms[0].GetComponent<Renderer>().bounds.size, "Scaled outline matches renderer bounds");
                File.WriteAllLines("pivot-result.txt", Results); EditorApplication.Exit(0);
            }
            catch (Exception e) { Results.Add("FAIL: " + e); File.WriteAllLines("pivot-result.txt", Results); Debug.LogException(e); EditorApplication.Exit(1); }
            finally { if (cameraObject) Object.DestroyImmediate(cameraObject); if (target) Object.DestroyImmediate(target); editor?.Dispose(); }
        }
    }
}
