using System;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGGrassInteractionValidation
    {
        // Opt-in local validation request, scoped to an exact project Assets path.
        // This lets CI/development validate an already-open editor without changing scenes.
        [InitializeOnLoadMethod]
        static void CheckRequestedValidation()
        {
            string request = Path.Combine(Path.GetTempPath(), "mg-grass-interaction-request.txt");
            if (!File.Exists(request) || !string.Equals(File.ReadAllText(request).Trim().Replace('\\', '/'),
                Application.dataPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(request);
                Run();
            };
        }
        static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }

        [MenuItem("Tools/MashBox/MG Terrain/Validation/Grass Interaction GPU")]
        public static void Run()
        {
            string reportPath = Path.Combine(Path.GetTempPath(), "mg-grass-interaction-result.txt");
            ComputeShader shader = null;
            RenderTexture a = null, b = null;
            Texture2D read = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Check(SystemInfo.supportsComputeShaders, "Compute shaders unavailable: GPU validation cannot run.");
                var source = Resources.Load<ComputeShader>("MGGrassInteraction");
                Check(source != null, "Compute resource imported.");
                shader = UnityEngine.Object.Instantiate(source);
                int scroll = shader.FindKernel("ScrollRecover"), stamp = shader.FindKernel("Stamp");
                // Deliberately non-multiple-of-eight to exercise dispatch guards.
                const int size = 65;
                a = Texture(size); b = Texture(size);
                read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                shader.SetInt("_Resolution", size);
                shader.SetFloat("_Recovery", 0);
                shader.SetInts("_Scroll", 0, 0, 1, 0);
                shader.SetTexture(scroll, "_Source", a);
                shader.SetTexture(scroll, "_Map", b);
                shader.Dispatch(scroll, 9, 9, 1);
                Color[] Read(RenderTexture texture)
                {
                    RenderTexture.active = texture;
                    read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    read.Apply(false, false);
                    return read.GetPixels();
                }
                var cleared = Read(b);
                foreach (var pixel in cleared) Check(pixel == Color.clear, "New map clears to neutral.");

                // Negative world origin and a sweep across x=0 (an arbitrary terrain seam).
                shader.SetVector("_MapWorld", new Vector4(-32.5f, -32.5f, 1, 100));
                shader.SetVector("_BrushFrom", new Vector4(-8, 102, 0, 3));
                shader.SetVector("_BrushTo", new Vector4(8, 102, 0, 1));
                shader.SetVector("_BrushDirection", new Vector4(-1, 0, 1, 1.5f));
                shader.SetInts("_DispatchRect", 0, 0, size, size);
                shader.SetTexture(stamp, "_Map", b);
                shader.Dispatch(stamp, 9, 9, 1);
                var painted = Read(b);
                int center = 32 * size + 32;
                Check(Mathf.Abs(painted[center].a - 1) < 0.002f, "Sweep center pressure.");
                Check(painted[center].r < -0.99f && Mathf.Abs(painted[center].g) < 0.002f, "Signed negative heading survives RGBAHalf.");
                Check(Mathf.Abs(painted[center].b / painted[center].a - 2) < 0.002f, "Premultiplied relative contact height.");
                for (int x = 24; x <= 40; x++) Check(painted[32 * size + x].a > 0.99f, "Swept path has no seam / sample gaps.");
                Check(painted[0].a == 0 && painted[size * size - 1].a == 0, "Unrelated pixels untouched.");

                shader.SetFloat("_Recovery", 0.25f);
                shader.SetInts("_Scroll", 2, -3, 0, 0);
                shader.SetTexture(scroll, "_Source", b);
                shader.SetTexture(scroll, "_Map", a);
                shader.Dispatch(scroll, 9, 9, 1);
                var moved = Read(a);
                int relocated = 35 * size + 30;
                Check(Mathf.Abs(moved[relocated].a - 0.75f) < 0.002f, "Scroll sign and elapsed recovery.");
                Check(Mathf.Abs(moved[relocated].b / moved[relocated].a - 2) < 0.002f, "Recovery preserves contact height.");
                for (int y = 0; y < size; y++) Check(moved[y * size + 64].a == 0, "Exposed columns clear.");
                for (int x = 0; x < size; x++) Check(moved[x].a == 0, "Exposed rows clear.");

                shader.SetFloat("_Recovery", 1);
                shader.SetInts("_Scroll", 0, 0, 0, 0);
                shader.SetTexture(scroll, "_Source", a);
                shader.SetTexture(scroll, "_Map", b);
                shader.Dispatch(scroll, 9, 9, 1);
                foreach (var pixel in Read(b)) Check(pixel == Color.clear, "Recovery returns all channels to neutral.");

                // A tiny brush at the corner between four texels must still paint.
                shader.SetTexture(stamp, "_Map", b);
                shader.SetVector("_BrushFrom", new Vector4(0.5f, 102, 0.5f, 0.01f));
                shader.SetVector("_BrushTo", new Vector4(0.5f, 102, 0.5f, 1));
                shader.Dispatch(stamp, 9, 9, 1);
                Check(Read(b)[center].a > 0, "Sub-texel footprint leaves actual GPU pressure at a texel corner.");

                shader.SetFloat("_Recovery", 0);
                shader.SetInts("_Scroll", size, 0, 0, 0);
                shader.SetTexture(scroll, "_Source", b);
                shader.SetTexture(scroll, "_Map", a);
                shader.Dispatch(scroll, 9, 9, 1);
                foreach (var pixel in Read(a)) Check(pixel == Color.clear, "Window teleport discards old history.");

                // Recover a non-aligned subrectangle in place and compare every pixel with the full pass.
                int recover = shader.FindKernel("RecoverRegion");
                shader.SetFloat("_Recovery", 0.05f);
                shader.SetInts("_Scroll", 0, 0, 0, 0);
                shader.SetTexture(scroll, "_Source", b);
                shader.SetTexture(scroll, "_Map", a);
                shader.Dispatch(scroll, 9, 9, 1);
                var expected = Read(a);
                shader.SetInts("_DispatchRect", 31, 31, 5, 5);
                shader.SetTexture(recover, "_Map", b);
                shader.Dispatch(recover, 1, 1, 1);
                var regional = Read(b);
                for (int i = 0; i < expected.Length; i++)
                    Check(Vector4.Distance(expected[i], regional[i]) < 0.002f, "Regional recovery matches full-map recovery.");
                var bounds = new RectInt(10, 20, 8, 6);
                Check(MGGrassInteractionMap.ShiftFootprint(bounds, 2, -3, 65).Equals(new RectInt(8, 23, 8, 6)), "Footprint follows texture scroll sign.");
                Check(MGGrassInteractionMap.ShiftFootprint(bounds, 65, 0, 65).width == 0, "Teleport removes footprint.");
                Check(MGGrassInteractionMap.UnionFootprint(bounds, new RectInt(30, 40, 2, 3)).Equals(new RectInt(10, 20, 22, 23)), "Multiple brushes retain both footprints.");

                var origin = new Vector2(-32, -32);
                var outside = MGGrassInteractionMap.BrushRectangle(new Vector3(-100, 0, 0), new Vector3(-90, 0, 0), 1, origin, 1, 64);
                Check(outside.width == 0, "Off-window brush rejected rather than clamped to edge.");
                var crossing = MGGrassInteractionMap.BrushRectangle(new Vector3(-33, 0, 0), new Vector3(-31, 0, 0), 1, origin, 1, 64);
                Check(crossing.x == 0 && crossing.width == 2, "Partially overlapping swept brush clips correctly.");
                var tiny = MGGrassInteractionMap.BrushRectangle(Vector3.zero, Vector3.zero, 0.0625f, origin, 0.125f, 512);
                Check(tiny.width > 0 && tiny.height > 0, "Sub-texel brush dispatch is nonzero.");

                ValidateGlobalInteractors();

                Shader grass = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shared Materials/MG_GodGrass_V2.shadergraph");
                if (grass != null)
                    foreach (var message in ShaderUtil.GetShaderMessages(grass))
                        Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, "God Grass import: " + message.message);
                string report = "PASS: real GPU compute clear, signed direction, swept seam crossing, height encoding, off-map rejection, sub-texel bounds, non-8 dispatch guards, scrolling, exposed borders, timed recovery, regional/full-map recovery equivalence, multi-brush footprint union and scrolling, automatic interactor registration and world replacement; God Grass imported shader messages checked. Visual bending and target-device frame time still require scene profiling.";
                File.WriteAllText(reportPath, report);
                Debug.Log(report);
            }
            catch (Exception error) { File.WriteAllText(reportPath, "FAIL: " + error); Debug.LogException(error); }
            finally
            {
                RenderTexture.active = previous;
                if (a != null) { a.Release(); UnityEngine.Object.DestroyImmediate(a); }
                if (b != null) { b.Release(); UnityEngine.Object.DestroyImmediate(b); }
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
                if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
            }
        }
        static void ValidateGlobalInteractors()
        {
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            var registryField = typeof(MGGrassInteractor).GetField("Instances", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var getStroke = typeof(MGGrassInteractor).GetMethod("GetStroke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                // Player arrives first, with no reference into the subsequently loaded world.
                var player = new GameObject("Validation player").AddComponent<MGGrassInteractor>();
                var registry = (System.Collections.IList)registryField.GetValue(null);
                Check(registry.Contains(player), "Player registers before a world exists.");
                Check(typeof(MGGrassInteractor).GetField("interactionMap") == null, "Interactor has no serialized scene-map filter.");
                var world = new GameObject("Validation world");
                world.SetActive(false); // Exercise strokes without taking over the user's shader globals.
                var map = world.AddComponent<MGGrassInteractionMap>();
                object[] args = { map, Vector3.zero, Vector3.zero, 0f, Vector2.zero, 0f };
                Check((bool)getStroke.Invoke(player, args), "Existing player paints a newly loaded world without assignment.");
                player.transform.position = Vector3.right;
                Check((bool)getStroke.Invoke(player, args) && (Vector3)args[1] == Vector3.zero, "Stroke retains movement within the same world.");
                // Another actor arrives after the world.
                var second = new GameObject("Validation second player").AddComponent<MGGrassInteractor>();
                Check(registry.Contains(second) && (bool)getStroke.Invoke(second, args), "Later and multiple players register automatically.");
                UnityEngine.Object.DestroyImmediate(world);
                player.transform.position = Vector3.right * 2;
                var replacement = new GameObject("Validation replacement world");
                replacement.SetActive(false);
                args[0] = replacement.AddComponent<MGGrassInteractionMap>();
                Check((bool)getStroke.Invoke(player, args) && (Vector3)args[1] == (Vector3)args[2], "Replacement world starts fresh; no trail bridges unloaded worlds.");
                Check(player.InteractionMap == MGGrassInteractionMap.Active, "Read-only accessor resolves global owner.");
                player.enabled = false;
                Check(!registry.Contains(player) && !(bool)getStroke.Invoke(player, args), "Disabled player unregisters.");
                player.enabled = true;
                Check(registry.Contains(player) && (bool)getStroke.Invoke(player, args) && (Vector3)args[1] == (Vector3)args[2], "Re-enabled player resumes with fresh history.");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
            }
        }

        static RenderTexture Texture(int size)
        {
            var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, useMipMap = false, filterMode = FilterMode.Bilinear };
            Check(texture.Create(), "Allocate GPU validation texture.");
            return texture;
        }
    }
}
