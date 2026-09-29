using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGSplatPrecisionValidation
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        static readonly string Request = Path.Combine(Path.GetTempPath(), "mg-splat-precision.request");
        static readonly string Report = Path.Combine(Path.GetTempPath(), "mg-splat-precision.txt");

        static MGSplatPrecisionValidation() { EditorApplication.delayCall += RunRequested; }
        static void RunRequested()
        {
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!string.Equals(File.ReadAllText(Request).Trim(), Application.dataPath, StringComparison.OrdinalIgnoreCase)) return;
            File.Delete(Request);
            Run();
        }

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Splat Brush Precision")]
        internal static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.NewPreviewScene();
            var owned = new List<Object>();
            var results = new List<string>();
            var brushType = typeof(MGBrushWindow);
            var ownerField = brushType.GetField("s_ActiveSceneToolOwner", Flags);
            var paletteField = brushType.GetField("s_DecorPaletteOwner", Flags);
            var oldOwner = (MGBrushWindow)ownerField.GetValue(null);
            var oldPalette = paletteField.GetValue(null);
            var enabledField = typeof(MBBrushMask).GetField("enabled", Flags);
            var falloffField = typeof(MBBrushMask).GetField("splatFalloff", Flags);
            var oldEnabled = enabledField.GetValue(null);
            var oldFalloff = falloffField.GetValue(null);
            const string Prefix = "MashBoxSDK.EditorTools.";
            var floatPrefs = new Dictionary<string, float?>();
            var intPrefs = new Dictionary<string, int?>();
            foreach (string name in new[] { "BrushRadius", "BrushStrength" })
                floatPrefs[name] = EditorPrefs.HasKey(Prefix + name) ? EditorPrefs.GetFloat(Prefix + name) : (float?)null;
            foreach (string name in new[] { "SplatUvChannel", "SplatPaintMode", "SplatTextureId" })
                intPrefs[name] = EditorPrefs.HasKey(Prefix + name) ? EditorPrefs.GetInt(Prefix + name) : (int?)null;
            MGBrushWindow brush = null;
            try
            {
                enabledField.SetValue(null, false);
                falloffField.SetValue(null, 0f);
                EditorPrefs.SetFloat(Prefix + "BrushRadius", .125f);
                EditorPrefs.SetFloat(Prefix + "BrushStrength", 1f);
                EditorPrefs.SetInt(Prefix + "SplatUvChannel", 0);
                EditorPrefs.SetInt(Prefix + "SplatPaintMode", (int)MBSplatPaintMode.TextureId);
                EditorPrefs.SetInt(Prefix + "SplatTextureId", 2);
                brush = ScriptableObject.CreateInstance<MGBrushWindow>();
                brush.hideFlags = HideFlags.HideAndDontSave;
                void Set(string field, object value) => brushType.GetField(field, Flags).SetValue(brush, value);
                bool Call(string method, params object[] args) => (bool)brushType.GetMethod(method, Flags).Invoke(brush, args);
                Set("splatPartialPreviewUploadUnavailable", true);
                var map = new Texture2D(2048, 2048, TextureFormat.RGBA32, false, true);
                var companion = new Texture2D(2048, 2048, TextureFormat.RGBA32, false, true);
                owned.Add(map); owned.Add(companion);
                Set("splatMapTexture", map); Set("splatCompanionMapTexture", companion);
                var initial = new Color32[2048 * 2048];
                for (int i = 0; i < initial.Length; i++) initial[i] = new Color32(255, 0, 0, 0);
                map.SetPixels32(initial);
                companion.SetPixels32(new Color32[initial.Length]);

                // A 4 m collider chunk retaining its parent 512 m tile's UVs.
                Mesh MakeMesh(float min, float size)
                {
                    var mesh = new Mesh(); owned.Add(mesh);
                    mesh.vertices = new[] { new Vector3(min, 0, min), new Vector3(min + size, 0, min),
                        new Vector3(min, 0, min + size), new Vector3(min + size, 0, min + size) };
                    mesh.uv = new[] { new Vector2(min, min) / 512, new Vector2(min + size, min) / 512,
                        new Vector2(min, min + size) / 512, new Vector2(min + size, min + size) / 512 };
                    mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; mesh.RecalculateBounds(); return mesh;
                }
                var go = new GameObject("Transient splat precision fixture");
                SceneManager.MoveGameObjectToScene(go, scene);
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = MakeMesh(254, 4);
                RaycastHit Hit(float x, float z)
                {
                    Check(collider.Raycast(new Ray(new Vector3(x, 10, z), Vector3.down), out var hit, 20), "Fixture raycast failed");
                    return hit;
                }
                void CheckPixels(string label, params Vector2Int[] expected)
                {
                    var actual = new HashSet<int>();
                    var pixels = map.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++) if (pixels[i].b != 0) actual.Add(i);
                    var wanted = new HashSet<int>();
                    foreach (var p in expected) wanted.Add(p.y * 2048 + p.x);
                    Check(actual.SetEquals(wanted), label + ": expected " + wanted.Count + " painted texels, got " + actual.Count);
                    foreach (int i in actual)
                    {
                        Check(pixels[i].b == 255 && pixels[i].r == 0, label + ": incorrect texture weights");
                        map.SetPixel(i % 2048, i / 2048, new Color(1, 0, 0, 0));
                    }
                    results.Add("PASS: " + label);
                }

                var center = Hit(256.125f, 256.125f);
                Check(!Call("TryPaintStaticPlanarSplat", center, center.textureCoord, false), "Chunk incorrectly used planar shortcut");
                Check(Call("PaintSplatTextureFromWorldFootprint", center, false), "Chunk paint was unhandled");
                CheckPixels("512 m / 2K chunk: 0.25 m diameter paints exactly one texel", new Vector2Int(1024, 1024));
                var adjacent = Hit(256.375f, 256.125f);
                Call("PaintSplatTextureFromWorldFootprint", adjacent, false);
                CheckPixels("Moving 0.25 m paints the adjacent texel", new Vector2Int(1025, 1024));
                var gap = Hit(256.25f, 256.25f);
                Check(Call("PaintSplatTextureFromWorldFootprint", gap, false), "Sub-texel gap must not fall back to a larger pixel brush");
                CheckPixels("A dab between texel centers does not expand");
                EditorPrefs.SetFloat(Prefix + "BrushRadius", .375f);
                Call("PaintSplatTextureFromWorldFootprint", center, false);
                var disk = new List<Vector2Int>();
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) disk.Add(new Vector2Int(1024 + x, 1024 + y));
                CheckPixels("Larger chunk brush keeps its requested footprint", disk.ToArray());
                EditorPrefs.SetFloat(Prefix + "BrushRadius", .125f);
                collider.sharedMesh = MakeMesh(0, 512);
                center = Hit(256.125f, 256.125f);
                Check(Call("TryPaintStaticPlanarSplat", center, center.textureCoord, false), "Full tile did not use planar shortcut");
                CheckPixels("Planar tile: 0.25 m diameter paints exactly one aligned texel", new Vector2Int(1024, 1024));
                var edge = Hit(511.875f, 511.875f);
                Call("TryPaintStaticPlanarSplat", edge, edge.textureCoord, false);
                CheckPixels("Planar tile: last texel is paintable", new Vector2Int(2047, 2047));
                Call("PaintSplatTextureFromWorldFootprint", edge, false);
                CheckPixels("Triangle path: last texel is paintable", new Vector2Int(2047, 2047));
                File.WriteAllLines(Report, results);
                Debug.Log("Splat brush precision validation PASSED (" + results.Count + " checks).");
            }
            catch (Exception exception)
            {
                File.WriteAllText(Report, string.Join("\n", results) + "\nFAIL: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                if (brush != null) Object.DestroyImmediate(brush);
                foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
                EditorSceneManager.ClosePreviewScene(scene);
                enabledField.SetValue(null, oldEnabled); falloffField.SetValue(null, oldFalloff);
                foreach (var pair in floatPrefs)
                    if (pair.Value.HasValue) EditorPrefs.SetFloat(Prefix + pair.Key, pair.Value.Value); else EditorPrefs.DeleteKey(Prefix + pair.Key);
                foreach (var pair in intPrefs)
                    if (pair.Value.HasValue) EditorPrefs.SetInt(Prefix + pair.Key, pair.Value.Value); else EditorPrefs.DeleteKey(Prefix + pair.Key);
                paletteField.SetValue(null, oldPalette);
                if (oldOwner != null) oldOwner.ActivateSceneTool(); else ownerField.SetValue(null, null);
            }
        }

        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
