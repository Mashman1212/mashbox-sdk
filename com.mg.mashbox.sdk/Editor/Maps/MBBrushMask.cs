using System.IO;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Maps;

namespace MashBoxSDK.MapTools
{
    internal static class MBBrushMask
    {
        const string Key = "MashBox.BrushMask.";
        const string Folder = "Assets/Mappy Brush Masks";
        static readonly string[] Names = { "Cloud", "Rock", "Dry Brush", "Speckles", "Ridges", "Soft Leaf" };
        static Texture2D[] presets;
        static Texture2D custom;
        static Texture2D cachedTexture;
        static byte[] cachedPixels;
        static bool loaded;
        static float rotation = EditorPrefs.GetFloat(Key + "Rotation", 0);
        static float previewOpacity = Mathf.Clamp01(EditorPrefs.GetFloat(Key + "PreviewOpacity", .55f));
        static int selected = Mathf.Clamp(EditorPrefs.GetInt(Key + "Preset", 0), 0, 5);
        static bool enabled = EditorPrefs.GetBool(Key + "Enabled", false);
        static Vector3 normal = Vector3.up;
        static BrushMask active;
        static readonly Vector3[] quad = new Vector3[4];

        public static bool Enabled => enabled;
        static float splatFalloff = Mathf.Clamp01(EditorPrefs.GetFloat(Key + "SplatFalloff", 1f));

        // Falloff is the fraction of the radius occupied by the soft outer edge.
        // 0 = solid disk; 1 = the original linear fade from center to edge.
        public static float SampleSplatFalloff(float normalizedDistance)
        {
            if (normalizedDistance >= 1f) return 0f;
            return splatFalloff <= 0f ? 1f : Mathf.Clamp01((1f - normalizedDistance) / splatFalloff);
        }

        public static void DrawSplatFalloffSettings()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 100;
            EditorGUI.BeginChangeCheck();
            splatFalloff = EditorGUILayout.Slider(new GUIContent("Falloff %", "Width of the soft outer edge. 0 = no radial fade; 100 = fade across the entire radius. The mask's gray values still apply."), splatFalloff * 100f, 0, 100) / 100f;
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(Key + "SplatFalloff", splatFalloff);
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField("Inner ring: full strength • Dotted ring: 50%", EditorStyles.wordWrappedMiniLabel);
            EditorGUIUtility.labelWidth = labelWidth;
        }

        static void EnsureLibrary()
        {
            if (presets != null) return;
            presets = new Texture2D[Names.Length];
            for (int k = 0; k < Names.Length; k++)
            {
                // Procedural grayscale brush heads have a black border and no baked lighting.
                var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
                texture.name = Names[k]; texture.hideFlags = HideFlags.HideAndDontSave;
                var colors = new Color[128 * 128];
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    float u = x / 127f * 2 - 1, v = y / 127f * 2 - 1;
                    float edge = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - Mathf.Sqrt(u*u + v*v)) * 5));
                    float n = Mathf.PerlinNoise(x * .065f + k * 17, y * .065f + 4);
                    float fine = Mathf.PerlinNoise(x * .24f + 9, y * .24f + k * 7);
                    float value = k == 0 ? Mathf.Clamp01((n - .25f) * 1.8f)
                        : k == 1 ? Mathf.Clamp01((n * .65f + fine * .35f - .3f) * 2.4f)
                        : k == 2 ? Mathf.Pow(Mathf.PerlinNoise(x * .3f, y * .025f), 3) * 3
                        : k == 3 ? Mathf.Clamp01((fine - .58f) * 8)
                        : k == 4 ? Mathf.Pow(1 - Mathf.Abs(Mathf.Sin(u * 16 + n * 6)), 2)
                        : Mathf.Clamp01(1 - Mathf.Abs(u + Mathf.Sin(v * 4) * .12f) * 3) * (.4f + n);
                    float c = Mathf.Clamp01(value) * edge;
                    colors[y * 128 + x] = new Color(c,c,c,1);
                }
                texture.SetPixels(colors); texture.Apply(); presets[k] = texture;
            }
            if (!loaded)
            {
                custom = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(Key + "Custom", "")));
                loaded = true;
            }
        }

        public static void DrawOverlay()
        {
            if (!MBEditorToolState.ActiveEditing || (MBEditorToolState.Mode != MBEditorAuthoringMode.Brush && MBEditorToolState.Mode != MBEditorAuthoringMode.MeshSculpt)) return;
            DrawSettings();
        }

        public static void DrawSettings()
        {
            EnsureLibrary();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            float previousFieldWidth = EditorGUIUtility.fieldWidth;
            EditorGUIUtility.labelWidth = 48;
            EditorGUIUtility.fieldWidth = 40;
            EditorGUI.BeginChangeCheck();
            enabled = EditorGUILayout.ToggleLeft("Use masked brush", enabled);
            if (enabled)
            {
                var content = new GUIContent[Names.Length];
                for (int i = 0; i < content.Length; i++) content[i] = new GUIContent(presets[i], Names[i]);
                int next = GUILayout.SelectionGrid(selected, content, 3, GUILayout.Height(92));
                if (next != selected) { selected = next; custom = null; }
                GUILayout.Label(custom != null ? custom.name : Names[selected], EditorStyles.miniLabel);
                custom = (Texture2D)EditorGUILayout.ObjectField("Custom", custom, typeof(Texture2D), false, GUILayout.Height(18));
                rotation = EditorGUILayout.Slider("Angle", rotation, 0, 360);
                EditorGUIUtility.labelWidth = 100;
                // Preview-only changes do not invalidate the sampled brush texture.
                bool brushChanged = GUI.changed;
                EditorGUI.BeginChangeCheck();
                previewOpacity = EditorGUILayout.Slider(new GUIContent("Preview opacity", "Visibility of the mask gizmo only. Does not change painting or sculpting strength."), previewOpacity * 100f, 0f, 100f) / 100f;
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetFloat(Key + "PreviewOpacity", previewOpacity);
                    SceneView.RepaintAll();
                }
                GUI.changed = brushChanged;
                EditorGUIUtility.labelWidth = 48;
                GUILayout.Label("Comma / period: rotate 15°\nShift: rotate 1° • White paints", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Export mask texture library")) ExportLibrary();
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(Key + "Enabled", enabled); EditorPrefs.SetInt(Key + "Preset", selected);
                EditorPrefs.SetFloat(Key + "Rotation", rotation);
                EditorPrefs.SetString(Key + "Custom", custom != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(custom)) : "");
                cachedTexture = null; active = null; SceneView.RepaintAll();
            }
            if (MBEditorToolState.Mode == MBEditorAuthoringMode.Brush && MBEditorToolState.BrushMode == MBBrushMode.SplatMap)
                DrawSplatFalloffSettings();
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUIUtility.fieldWidth = previousFieldWidth;
        }

        public static void HandleKeys(Event e, bool navigating)
        {
            if (!enabled || !MBEditorToolState.ActiveEditing || navigating || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0 || e.type != EventType.KeyDown || e.alt || e.control || e.command) return;
            if (e.keyCode != KeyCode.Comma && e.keyCode != KeyCode.Period) return;
            rotation = Mathf.Repeat(rotation + (e.keyCode == KeyCode.Comma ? -1 : 1) * (e.shift ? 1 : 15), 360);
            EditorPrefs.SetFloat(Key + "Rotation", rotation); active = null; e.Use(); SceneView.RepaintAll();
        }

        public static void SetNormal(Vector3 value) { normal = value; active = null; }
        public static BrushMask Capture(Vector3 surfaceNormal)
        {
            if (!enabled) return null;
            EnsureLibrary();
            Texture2D texture = custom != null ? custom : presets[selected];
            if (cachedTexture != texture || cachedPixels == null)
            {
                // GPU copy accepts normal imported textures without changing Read/Write settings.
                var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                var copy = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
                try
                {
                    Graphics.Blit(texture, rt); RenderTexture.active = rt;
                    copy.ReadPixels(new Rect(0,0,64,64),0,0); copy.Apply();
                    Color[] colors = copy.GetPixels(); cachedPixels = new byte[colors.Length];
                    for (int i = 0; i < colors.Length; i++) cachedPixels[i] = (byte)Mathf.RoundToInt(colors[i].grayscale * 255);
                    cachedTexture = texture;
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(copy); }
            }
            Vector3 n = surfaceNormal.normalized;
            Vector3 u = Vector3.ProjectOnPlane(Vector3.right, n).normalized;
            if (u.sqrMagnitude < .1f) u = Vector3.ProjectOnPlane(Vector3.forward, n).normalized;
            Vector3 v = Vector3.Cross(u, n).normalized;
            float a = rotation * Mathf.Deg2Rad;
            return new BrushMask { size = 64, pixels = cachedPixels,
                axisU = u * Mathf.Cos(a) + v * Mathf.Sin(a), axisV = -u * Mathf.Sin(a) + v * Mathf.Cos(a) };
        }
        public static float Sample(Vector3 delta, float radius) => enabled ? (active ??= Capture(normal)).Sample(delta, radius) : 1f;
        public static float SampleUV(float x, float y)
        {
            if (!enabled) return 1;
            float a = rotation * Mathf.Deg2Rad;
            return (active ??= Capture(normal)).SampleUV(x * Mathf.Cos(a) + y * Mathf.Sin(a), -x * Mathf.Sin(a) + y * Mathf.Cos(a));
        }
        public static void DrawPreview(Vector3 center, Vector3 surfaceNormal, float radius, bool showSplatFalloff = false)
        {
            SetNormal(surfaceNormal);
            if ((!enabled && !showSplatFalloff) || Event.current.type != EventType.Repaint) return;
            var mask = active = Capture(surfaceNormal);
            Vector3 axisU = mask != null ? mask.axisU : Vector3.ProjectOnPlane(Vector3.right, surfaceNormal).normalized;
            if (axisU.sqrMagnitude < .1f) axisU = Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal).normalized;
            Vector3 axisV = mask != null ? mask.axisV : Vector3.Cross(axisU, surfaceNormal).normalized;
            var color = Handles.color;
            var depth = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Vector3 origin = center + surfaceNormal * Mathf.Max(.005f, radius * .002f);
            if (showSplatFalloff && splatFalloff > 0f)
            {
                Handles.color = new Color(.5f, .9f, 1f, .85f);
                float innerRadius = radius * (1f - splatFalloff);
                if (innerRadius > radius * .005f) Handles.DrawWireDisc(origin, surfaceNormal, innerRadius);
                float halfRadius = radius * (1f - splatFalloff * .5f);
                for (int segment = 0; segment < 48; segment++)
                {
                    float a = segment * Mathf.PI * 2f / 48;
                    float b = (segment + .5f) * Mathf.PI * 2f / 48;
                    Handles.DrawLine(origin + (axisU * Mathf.Cos(a) + axisV * Mathf.Sin(a)) * halfRadius,
                        origin + (axisU * Mathf.Cos(b) + axisV * Mathf.Sin(b)) * halfRadius);
                }
            }
            const int cells = 24;
            for (int y = 0; y < cells; y++) for (int x = 0; x < cells; x++)
            {
                float u = x * 2f / cells - 1, v = y * 2f / cells - 1, step = 2f / cells;
                float sampleU = u + step/2, sampleV = v + step/2;
                float distance = Mathf.Sqrt(sampleU * sampleU + sampleV * sampleV);
                if (distance >= 1f) continue;
                float value = mask != null ? mask.SampleUV(sampleU, sampleV) : 1f;
                if (showSplatFalloff) value *= SampleSplatFalloff(distance);
                if (value * previewOpacity < .001f) continue;
                Handles.color = new Color(.5f, .9f, 1, value * previewOpacity);
                quad[0] = origin + (axisU*u + axisV*v)*radius;
                quad[1] = quad[0] + axisU*step*radius;
                quad[2] = quad[1] + axisV*step*radius;
                quad[3] = quad[0] + axisV*step*radius;
                Handles.DrawAAConvexPolygon(quad);
            }
            Handles.color = color; Handles.zTest = depth;
        }
        [MenuItem("MashBox/Brushes/Export Mask Texture Library")]
        public static void ExportLibrary()
        {
            EnsureLibrary(); Directory.CreateDirectory(Folder);
            for (int i = 0; i < presets.Length; i++)
            {
                string path = Folder + "/" + Names[i] + ".png";
                if (File.Exists(path)) continue;
                File.WriteAllBytes(path, presets[i].EncodeToPNG()); AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
        }
    }
}
