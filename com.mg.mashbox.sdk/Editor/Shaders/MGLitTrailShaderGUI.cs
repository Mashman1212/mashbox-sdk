#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    /// <summary>
    /// Material inspector for MG_Lit_Trail. Terrain layers are used as convenient
    /// import sources; the material itself stores the resolved texture references.
    /// </summary>
    public sealed class MGLitTrailShaderGUI : LightingShaderGraphGUI
    {
        private const int LayerCount = 8;
        private const string LayerDragDataKey = "MashBox.MGLitTrail.LayerDrag";
        private const string TerrainLayerTagPrefix = "MashBox.MGLitTrail.TerrainLayer.";
        private const string ControlMap1PropertyName = "_ControlMap1";
        private const string ControlMap2PropertyName = "_ControlMap2";
        private static readonly Dictionary<Texture2D, TerrainLayer> TerrainLayersByDiffuse = new();
        private static readonly List<TerrainLayer> CachedTerrainLayers = new();
        private static readonly int[] ControlTextureResolutions = { 256, 512, 1024, 2048, 4096 };
        private static readonly string[] ControlTextureResolutionLabels =
        {
            "256 x 256",
            "512 x 512",
            "1024 x 1024",
            "2048 x 2048",
            "4096 x 4096"
        };
        private static bool terrainLayerCacheBuilt;
        private static int selectedControlTextureResolutionIndex = 2;

        private sealed class LayerDragData
        {
            public Material material;
            public int sourceIndex;
        }

        private sealed class ControlTextureGenerationPopup : PopupWindowContent
        {
            private readonly Material material;
            private readonly MaterialEditor materialEditor;
            private int resolutionIndex;

            public ControlTextureGenerationPopup(Material material, MaterialEditor materialEditor)
            {
                this.material = material;
                this.materialEditor = materialEditor;
                resolutionIndex = selectedControlTextureResolutionIndex;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(340f, 126f);
            }

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Generate Trail Control Textures", EditorStyles.boldLabel);
                EditorGUILayout.Space(3f);
                resolutionIndex = EditorGUILayout.Popup(
                    new GUIContent("Resolution"),
                    resolutionIndex,
                    ControlTextureResolutionLabels);
                EditorGUILayout.HelpBox(
                    "Creates Control Map 1 and 2 beside the material. Layer 0 starts at full weight.",
                    MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                        editorWindow.Close();
                    if (GUILayout.Button("Generate", GUILayout.Width(90f)))
                    {
                        selectedControlTextureResolutionIndex = resolutionIndex;
                        GenerateControlTextures(material, ControlTextureResolutions[resolutionIndex]);
                        materialEditor?.Repaint();
                        editorWindow.Close();
                    }
                }
            }
        }

        private struct LayerValues
        {
            public string terrainLayerGuid;
            public Texture baseMap;
            public Vector2 baseScale;
            public Vector2 baseOffset;
            public Texture normalMap;
            public Vector2 normalScale;
            public Vector2 normalOffset;
            public Texture maskMap;
            public Vector2 maskScale;
            public Vector2 maskOffset;
            public float heightBlend;
            public float heightOffset;
            public float heightAmplitude;
            public float heightContrast;
            public float heightInfluence;
            public float planarMap;
            public float temperature;
            public float saturation;
            public float contrast;
            public float darken;
            public float lighten;
            public Color color;
            public float whiteBalance;
        }

        public MGLitTrailShaderGUI()
        {
            uiBlocks.RemoveAll(block => block is ShaderGraphUIBlock);
            uiBlocks.RemoveAll(block => block is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(block => block is AdvancedOptionsUIBlock);
        }

        public override void ValidateMaterial(Material material)
        {
            SetDepthOffsetDisabled(material);
            SynchronizeLayerTextureTransforms(material);
            base.ValidateMaterial(material);
        }

        protected override void OnMaterialGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            if (material == null)
                return;

            // Set this before HDRP draws/validates the material so its keywords and passes
            // are synchronized against the enforced value during the same inspector event.
            EnforceDepthOffsetDisabled(materialEditor.targets);

            EditorGUILayout.LabelField("MASHBOX • MG LIT TRAIL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign a Terrain Layer to import its diffuse, normal, and mask textures into a trail layer. " +
                "The imported texture references are saved on the material.",
                MessageType.Info);

            DrawControlMaps(materialEditor, properties);
            DrawPuddleControls(materialEditor, properties);
            DrawGlobalHeightControls(materialEditor, properties);
            DrawTerrainLayers(materialEditor, properties, material);

            GUILayout.Space(6f);
            base.OnMaterialGUI(materialEditor, properties);

            // Keep the trail contract intact if HDRP's hidden surface UI changes the value.
            EnforceDepthOffsetDisabled(materialEditor.targets);
        }

        private static void EnforceDepthOffsetDisabled(Object[] targets)
        {
            foreach (Object target in targets)
            {
                if (target is Material material)
                    SetDepthOffsetDisabled(material);
            }
        }

        private static void SetDepthOffsetDisabled(Material material)
        {
            if (material == null ||
                !material.HasProperty("_DepthOffsetEnable") ||
                material.GetFloat("_DepthOffsetEnable") < 0.5f)
                return;

            material.SetFloat("_DepthOffsetEnable", 0f);
            EditorUtility.SetDirty(material);
        }

        private static void SynchronizeLayerTextureTransforms(Material material)
        {
            if (material == null)
                return;

            bool changed = false;
            for (int index = 0; index < LayerCount; index++)
            {
                string suffix = index.ToString("00");
                string baseProperty = "_BaseMap" + suffix;
                if (!material.HasProperty(baseProperty))
                    continue;

                Vector2 scale = material.GetTextureScale(baseProperty);
                Vector2 offset = material.GetTextureOffset(baseProperty);
                changed |= SetTextureTransformIfDifferent(material, "_NormalMap" + suffix, scale, offset);
                changed |= SetTextureTransformIfDifferent(material, "_MaskMap" + suffix, scale, offset);
            }

            if (changed)
                EditorUtility.SetDirty(material);
        }

        private static bool SetTextureTransformIfDifferent(
            Material material,
            string propertyName,
            Vector2 scale,
            Vector2 offset)
        {
            if (!material.HasProperty(propertyName) ||
                (material.GetTextureScale(propertyName) == scale &&
                 material.GetTextureOffset(propertyName) == offset))
                return false;

            material.SetTextureScale(propertyName, scale);
            material.SetTextureOffset(propertyName, offset);
            return true;
        }

        private static void DrawControlMaps(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            MaterialProperty controlMap1 = FindOptionalProperty(ControlMap1PropertyName, properties);
            MaterialProperty controlMap2 = FindOptionalProperty(ControlMap2PropertyName, properties);
            if (controlMap1 == null && controlMap2 == null)
                return;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Control Maps", EditorStyles.boldLabel);
            if (controlMap1 != null)
                materialEditor.TexturePropertySingleLine(new GUIContent("Control Map 1 (IDs 0–3)"), controlMap1);
            if (controlMap2 != null)
                materialEditor.TexturePropertySingleLine(new GUIContent("Control Map 2 (IDs 4–7)"), controlMap2);

            Material material = materialEditor.target as Material;
            string materialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            using (new EditorGUI.DisabledScope(
                       material == null ||
                       materialEditor.targets.Length != 1 ||
                       string.IsNullOrEmpty(materialPath)))
            {
                if (GUILayout.Button(
                        new GUIContent(
                            "Generate Control Textures...",
                            "Create and assign two paintable control-map textures beside this material.")))
                {
                    Rect buttonRect = GUILayoutUtility.GetLastRect();
                    PopupWindow.Show(buttonRect, new ControlTextureGenerationPopup(material, materialEditor));
                }
            }

            if (string.IsNullOrEmpty(materialPath))
                EditorGUILayout.HelpBox("Save this material as an asset before generating control textures.", MessageType.None);
        }

        private static void GenerateControlTextures(Material material, int resolution)
        {
            if (material == null)
                return;

            string materialPath = AssetDatabase.GetAssetPath(material);
            string materialDirectory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(materialDirectory))
            {
                EditorUtility.DisplayDialog(
                    "Generate Control Textures",
                    "Save this material as an asset before generating its control textures.",
                    "OK");
                return;
            }

            string materialName = Path.GetFileNameWithoutExtension(materialPath);
            string controlMap1Path = $"{materialDirectory}/{materialName}_ControlMap1.png";
            string controlMap2Path = $"{materialDirectory}/{materialName}_ControlMap2.png";
            bool replacesExistingAssets = File.Exists(Path.GetFullPath(controlMap1Path)) ||
                                          File.Exists(Path.GetFullPath(controlMap2Path));
            if (replacesExistingAssets &&
                !EditorUtility.DisplayDialog(
                    "Replace Control Textures?",
                    "One or both generated control textures already exist beside this material. Replace them?",
                    "Replace",
                    "Cancel"))
                return;

            if ((File.Exists(Path.GetFullPath(controlMap1Path)) && !AssetDatabase.MakeEditable(controlMap1Path)) ||
                (File.Exists(Path.GetFullPath(controlMap2Path)) && !AssetDatabase.MakeEditable(controlMap2Path)))
            {
                EditorUtility.DisplayDialog(
                    "Control Textures Are Read-Only",
                    "The existing control textures could not be checked out or made editable.",
                    "OK");
                return;
            }

            try
            {
                WriteControlTexture(controlMap1Path, resolution, new Color32(255, 0, 0, 0));
                WriteControlTexture(controlMap2Path, resolution, new Color32(0, 0, 0, 0));

                Texture2D controlMap1 = ImportControlTexture(controlMap1Path);
                Texture2D controlMap2 = ImportControlTexture(controlMap2Path);
                Undo.RecordObject(material, "Generate Trail Control Textures");
                material.SetTexture(ControlMap1PropertyName, controlMap1);
                material.SetTexture(ControlMap2PropertyName, controlMap2);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                Selection.activeObject = material;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Control Texture Generation Failed",
                    exception.Message,
                    "OK");
            }
        }

        private static void WriteControlTexture(string assetPath, int resolution, Color32 initialColor)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color32[resolution * resolution];
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                    pixels[pixelIndex] = initialColor;

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static Texture2D ImportControlTexture(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.isReadable = true;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void DrawPuddleControls(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            MaterialProperty puddleLevel = FindOptionalProperty("_PuddleLevel", properties);
            MaterialProperty puddleFeather = FindOptionalProperty("_PuddleFeather", properties);
            if (puddleLevel == null && puddleFeather == null)
                return;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Puddles", EditorStyles.boldLabel);

            if (puddleLevel != null)
                materialEditor.ShaderProperty(puddleLevel, new GUIContent("Puddle Level"));
            if (puddleFeather != null)
                materialEditor.ShaderProperty(
                    puddleFeather,
                    new GUIContent("Puddle Feather", "Softens the transition around the puddle level."));
        }

        private static void DrawGlobalHeightControls(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            MaterialProperty heightTransition = FindOptionalProperty("_HeightTransition", properties);
            if (heightTransition == null)
                return;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Height Blending", EditorStyles.boldLabel);
            materialEditor.ShaderProperty(
                heightTransition,
                new GUIContent(
                    "Height Transition",
                    "Controls the width of the transition between height-blended terrain layers."));
        }

        private static void DrawTerrainLayers(
            MaterialEditor materialEditor,
            MaterialProperty[] properties,
            Material material)
        {
            EnsureTerrainLayerCache();

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Terrain Layers", EditorStyles.boldLabel);

            for (int index = 0; index < LayerCount; index++)
            {
                string suffix = index.ToString("00");
                MaterialProperty baseMap = FindOptionalProperty("_BaseMap" + suffix, properties);
                MaterialProperty normalMap = FindOptionalProperty("_NormalMap" + suffix, properties);
                MaterialProperty maskMap = FindOptionalProperty("_MaskMap" + suffix, properties);
                MaterialProperty heightBlend = FindOptionalProperty("_HeightBlend" + suffix, properties);
                MaterialProperty heightOffset = FindOptionalProperty("_HeightOffset" + suffix, properties);
                MaterialProperty heightAmplitude = FindOptionalProperty("_HeightAmplitude" + suffix, properties);
                MaterialProperty heightContrast = FindOptionalProperty("_HeightContrast" + suffix, properties);
                MaterialProperty heightInfluence = FindOptionalProperty("_HeightInfluence" + suffix, properties);
                MaterialProperty planarMap = FindOptionalProperty("_PlanarMap" + suffix, properties);
                MaterialProperty temperature = FindOptionalProperty("_Temperature" + suffix, properties);
                MaterialProperty saturation = FindOptionalProperty("_Saturation" + suffix, properties);
                MaterialProperty contrast = FindOptionalProperty("_Contrast" + suffix, properties);
                MaterialProperty darken = FindOptionalProperty("_Darken" + suffix, properties);
                MaterialProperty lighten = FindOptionalProperty("_Lighten" + suffix, properties);
                MaterialProperty color = FindOptionalProperty("_Color" + suffix, properties);
                MaterialProperty whiteBalance = FindOptionalProperty("_WhiteBalance" + suffix, properties);
                Texture2D currentDiffuse = baseMap != null ? baseMap.textureValue as Texture2D : null;
                TerrainLayer currentLayer = ResolveTerrainLayer(material, index, currentDiffuse);
                if (currentLayer != null)
                    SynchronizeTerrainLayerTextures(material, index, currentLayer);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string foldoutKey = GetLayerFoldoutKey(material, index);
                    bool expanded = SessionState.GetBool(foldoutKey, true);
                    string layerName = currentLayer != null ? currentLayer.name : "Unassigned";

                    EditorGUI.BeginChangeCheck();
                    Rect headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
                    Rect dragHandleRect = new Rect(headerRect.x, headerRect.y, 18f, headerRect.height);
                    Rect foldoutRect = new Rect(
                        headerRect.x + dragHandleRect.width,
                        headerRect.y,
                        headerRect.width - dragHandleRect.width,
                        headerRect.height);
                    EditorGUI.LabelField(
                        dragHandleRect,
                        new GUIContent("≡", "Drag to reorder this texture layer."),
                        EditorStyles.boldLabel);
                    expanded = EditorGUI.Foldout(
                        foldoutRect,
                        expanded,
                        $"Layer {index} - {layerName}",
                        true,
                        EditorStyles.foldoutHeader);
                    if (EditorGUI.EndChangeCheck())
                        SessionState.SetBool(foldoutKey, expanded);

                    HandleLayerDragAndDrop(
                        materialEditor,
                        material,
                        index,
                        headerRect,
                        dragHandleRect);

                    if (!expanded)
                        continue;

                    EditorGUI.BeginChangeCheck();
                    TerrainLayer selectedLayer = (TerrainLayer)EditorGUILayout.ObjectField(
                        new GUIContent("Terrain Layer"),
                        currentLayer,
                        typeof(TerrainLayer),
                        false);
                    bool terrainLayerChanged = EditorGUI.EndChangeCheck();
                    if (terrainLayerChanged)
                        ApplyTerrainLayer(materialEditor.targets, index, selectedLayer);

                    Texture2D diffusePreview = terrainLayerChanged
                        ? selectedLayer != null ? selectedLayer.diffuseTexture : null
                        : currentLayer != null ? currentLayer.diffuseTexture : currentDiffuse;
                    DrawDiffusePreview(diffusePreview);
                    DrawLayerMappingControls(
                        materialEditor,
                        baseMap,
                        normalMap,
                        maskMap,
                        planarMap);
                    DrawLayerColorControls(
                        materialEditor,
                        temperature,
                        saturation,
                        contrast,
                        darken,
                        lighten,
                        color,
                        whiteBalance);
                    DrawHeightControls(
                        materialEditor,
                        heightBlend,
                        heightOffset,
                        heightAmplitude,
                        heightContrast,
                        heightInfluence);
                }
            }

            if (GUILayout.Button("Refresh Terrain Layer Lookup"))
            {
                RebuildTerrainLayerCache();
                materialEditor.Repaint();
            }
        }

        private static void HandleLayerDragAndDrop(
            MaterialEditor materialEditor,
            Material material,
            int index,
            Rect headerRect,
            Rect dragHandleRect)
        {
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(
                $"MGLitTrailLayerDrag{index}".GetHashCode(),
                FocusType.Passive,
                dragHandleRect);

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                dragHandleRect.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(
                    LayerDragDataKey,
                    new LayerDragData { material = material, sourceIndex = index });
                DragAndDrop.objectReferences = new Object[] { material };
                DragAndDrop.StartDrag($"Move Layer {index}");
                GUIUtility.hotControl = 0;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                current.Use();
                return;
            }

            LayerDragData dragData = DragAndDrop.GetGenericData(LayerDragDataKey) as LayerDragData;
            if (dragData == null ||
                dragData.material != material ||
                !headerRect.Contains(current.mousePosition))
                return;

            if (current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                current.Use();
            }
            else if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                DragAndDrop.SetGenericData(LayerDragDataKey, null);
                MoveLayer(materialEditor.targets, dragData.sourceIndex, index);
                materialEditor.Repaint();
                GUI.changed = true;
                current.Use();
            }
        }

        private static void MoveLayer(Object[] targets, int sourceIndex, int destinationIndex)
        {
            if (sourceIndex == destinationIndex ||
                sourceIndex < 0 ||
                sourceIndex >= LayerCount ||
                destinationIndex < 0 ||
                destinationIndex >= LayerCount)
                return;

            Undo.RecordObjects(targets, $"Move Trail Layer {sourceIndex} to {destinationIndex}");
            foreach (Object target in targets)
            {
                if (target is not Material material)
                    continue;

                LayerValues movedValues = ReadLayerValues(material, sourceIndex);
                if (sourceIndex < destinationIndex)
                {
                    for (int index = sourceIndex; index < destinationIndex; index++)
                        WriteLayerValues(material, index, ReadLayerValues(material, index + 1));
                }
                else
                {
                    for (int index = sourceIndex; index > destinationIndex; index--)
                        WriteLayerValues(material, index, ReadLayerValues(material, index - 1));
                }

                WriteLayerValues(material, destinationIndex, movedValues);
                EditorUtility.SetDirty(material);
            }
        }

        private static LayerValues ReadLayerValues(Material material, int index)
        {
            string suffix = index.ToString("00");
            string baseProperty = "_BaseMap" + suffix;
            string normalProperty = "_NormalMap" + suffix;
            string maskProperty = "_MaskMap" + suffix;
            string heightBlendProperty = "_HeightBlend" + suffix;
            string heightOffsetProperty = "_HeightOffset" + suffix;
            string heightAmplitudeProperty = "_HeightAmplitude" + suffix;
            string heightContrastProperty = "_HeightContrast" + suffix;
            string heightInfluenceProperty = "_HeightInfluence" + suffix;
            string planarMapProperty = "_PlanarMap" + suffix;

            return new LayerValues
            {
                terrainLayerGuid = material.GetTag(GetTerrainLayerTag(index), false, string.Empty),
                baseMap = GetTexture(material, baseProperty),
                baseScale = GetTextureScale(material, baseProperty),
                baseOffset = GetTextureOffset(material, baseProperty),
                normalMap = GetTexture(material, normalProperty),
                normalScale = GetTextureScale(material, normalProperty),
                normalOffset = GetTextureOffset(material, normalProperty),
                maskMap = GetTexture(material, maskProperty),
                maskScale = GetTextureScale(material, maskProperty),
                maskOffset = GetTextureOffset(material, maskProperty),
                heightBlend = GetFloat(material, heightBlendProperty),
                heightOffset = GetFloat(material, heightOffsetProperty),
                heightAmplitude = GetFloat(material, heightAmplitudeProperty),
                heightContrast = GetFloat(material, heightContrastProperty),
                heightInfluence = GetFloat(material, heightInfluenceProperty),
                planarMap = GetFloat(material, planarMapProperty),
                temperature = GetFloat(material, "_Temperature" + suffix),
                saturation = GetFloat(material, "_Saturation" + suffix),
                contrast = GetFloat(material, "_Contrast" + suffix),
                darken = GetFloat(material, "_Darken" + suffix),
                lighten = GetFloat(material, "_Lighten" + suffix),
                color = GetColor(material, "_Color" + suffix),
                whiteBalance = GetFloat(material, "_WhiteBalance" + suffix)
            };
        }

        private static void WriteLayerValues(Material material, int index, LayerValues values)
        {
            string suffix = index.ToString("00");
            material.SetOverrideTag(GetTerrainLayerTag(index), values.terrainLayerGuid ?? string.Empty);
            SetTexture(
                material,
                "_BaseMap" + suffix,
                values.baseMap,
                values.baseScale,
                values.baseOffset);
            SetTexture(
                material,
                "_NormalMap" + suffix,
                values.normalMap,
                values.baseScale,
                values.baseOffset);
            SetTexture(
                material,
                "_MaskMap" + suffix,
                values.maskMap,
                values.baseScale,
                values.baseOffset);
            SetFloat(material, "_HeightBlend" + suffix, values.heightBlend);
            SetFloat(material, "_HeightOffset" + suffix, values.heightOffset);
            SetFloat(material, "_HeightAmplitude" + suffix, values.heightAmplitude);
            SetFloat(material, "_HeightContrast" + suffix, values.heightContrast);
            SetFloat(material, "_HeightInfluence" + suffix, values.heightInfluence);
            SetFloat(material, "_PlanarMap" + suffix, values.planarMap);
            SetFloat(material, "_Temperature" + suffix, values.temperature);
            SetFloat(material, "_Saturation" + suffix, values.saturation);
            SetFloat(material, "_Contrast" + suffix, values.contrast);
            SetFloat(material, "_Darken" + suffix, values.darken);
            SetFloat(material, "_Lighten" + suffix, values.lighten);
            SetColor(material, "_Color" + suffix, values.color);
            SetFloat(material, "_WhiteBalance" + suffix, values.whiteBalance);
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static Vector2 GetTextureScale(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureScale(propertyName) : Vector2.one;
        }

        private static Vector2 GetTextureOffset(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureOffset(propertyName) : Vector2.zero;
        }

        private static float GetFloat(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : 0f;
        }

        private static Color GetColor(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : Color.white;
        }

        private static void SetTexture(
            Material material,
            string propertyName,
            Texture texture,
            Vector2 scale,
            Vector2 offset)
        {
            if (!material.HasProperty(propertyName))
                return;

            material.SetTexture(propertyName, texture);
            material.SetTextureScale(propertyName, scale);
            material.SetTextureOffset(propertyName, offset);
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static string GetLayerFoldoutKey(Material material, int index)
        {
            string materialPath = AssetDatabase.GetAssetPath(material);
            string materialId = string.IsNullOrEmpty(materialPath)
                ? material.name
                : AssetDatabase.AssetPathToGUID(materialPath);
            return $"MashBox.MGLitTrail.LayerFoldout.{materialId}.{index}";
        }

        private static void DrawLayerMappingControls(
            MaterialEditor materialEditor,
            MaterialProperty baseMap,
            MaterialProperty normalMap,
            MaterialProperty maskMap,
            MaterialProperty planarMap)
        {
            if (baseMap == null && planarMap == null)
                return;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Mapping", EditorStyles.miniBoldLabel);
            if (planarMap != null)
            {
                materialEditor.ShaderProperty(
                    planarMap,
                    new GUIContent(
                        "Planar Mapping",
                        "Uses the shader's planar projection for this texture layer."));
            }

            if (baseMap == null)
                return;

            Vector4 transform = baseMap.textureScaleAndOffset;
            Vector2 tiling = new Vector2(transform.x, transform.y);
            Vector2 offset = new Vector2(transform.z, transform.w);
            EditorGUI.showMixedValue = baseMap.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            tiling = EditorGUILayout.Vector2Field("Tiling", tiling);
            offset = EditorGUILayout.Vector2Field("Offset", offset);
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo("Layer Tiling & Offset");
                Vector4 sharedTransform = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
                baseMap.textureScaleAndOffset = sharedTransform;
                if (normalMap != null)
                    normalMap.textureScaleAndOffset = sharedTransform;
                if (maskMap != null)
                    maskMap.textureScaleAndOffset = sharedTransform;
            }

            EditorGUI.showMixedValue = false;
        }

        private static void DrawLayerColorControls(
            MaterialEditor materialEditor,
            MaterialProperty temperature,
            MaterialProperty saturation,
            MaterialProperty contrast,
            MaterialProperty darken,
            MaterialProperty lighten,
            MaterialProperty color,
            MaterialProperty whiteBalance)
        {
            if (temperature == null &&
                saturation == null &&
                contrast == null &&
                darken == null &&
                lighten == null &&
                color == null &&
                whiteBalance == null)
                return;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Color Adjustments", EditorStyles.miniBoldLabel);
            if (temperature != null)
                materialEditor.ShaderProperty(temperature, new GUIContent("Temperature"));
            if (saturation != null)
                materialEditor.ShaderProperty(saturation, new GUIContent("Saturation"));
            if (contrast != null)
                materialEditor.ShaderProperty(contrast, new GUIContent("Contrast"));
            if (darken != null)
                materialEditor.ShaderProperty(darken, new GUIContent("Darken"));
            if (lighten != null)
                materialEditor.ShaderProperty(lighten, new GUIContent("Lighten"));
            if (color != null)
                materialEditor.ShaderProperty(color, new GUIContent("Color"));
            if (whiteBalance != null)
                materialEditor.ShaderProperty(whiteBalance, new GUIContent("White Balance"));
        }

        private static void DrawHeightControls(
            MaterialEditor materialEditor,
            MaterialProperty heightBlend,
            MaterialProperty heightOffset,
            MaterialProperty heightAmplitude,
            MaterialProperty heightContrast,
            MaterialProperty heightInfluence)
        {
            if (heightBlend == null &&
                heightOffset == null &&
                heightAmplitude == null &&
                heightContrast == null &&
                heightInfluence == null)
                return;

            GUILayout.Space(2f);
            EditorGUILayout.LabelField("Height Blending", EditorStyles.miniBoldLabel);

            if (heightBlend != null)
            {
                DrawHeightSlider(
                    materialEditor,
                    heightBlend,
                    new GUIContent("Height Blend", "Enables or disables sampled-height blending for this layer."),
                    0f,
                    1f);
            }

            if (heightOffset != null)
            {
                DrawHeightSlider(
                    materialEditor,
                    heightOffset,
                    new GUIContent("Height Offset", "Raises or lowers this layer's sampled height before blending."),
                    -1f,
                    1f);
            }

            if (heightAmplitude != null)
            {
                materialEditor.ShaderProperty(
                    heightAmplitude,
                    new GUIContent(
                        "Height Amplitude",
                        "Scales the amount of sampled height variation used for this layer's blending."));
            }

            if (heightContrast != null)
            {
                DrawHeightSlider(
                    materialEditor,
                    heightContrast,
                    new GUIContent("Height Contrast", "Expands or compresses the contrast of this layer's sampled height."),
                    0.5f,
                    4f);
            }

            if (heightInfluence != null)
            {
                DrawHeightSlider(
                    materialEditor,
                    heightInfluence,
                    new GUIContent("Height Influence", "Controls how strongly this layer's sampled height affects blending."),
                    0f,
                    2f);
            }
        }

        private static void DrawHeightSlider(
            MaterialEditor materialEditor,
            MaterialProperty property,
            GUIContent label,
            float minimum,
            float maximum)
        {
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(label, property.floatValue, minimum, maximum);
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo(label.text);
                property.floatValue = value;
            }

            EditorGUI.showMixedValue = false;
        }

        private static void DrawDiffusePreview(Texture2D diffuse)
        {
            if (diffuse == null)
                return;

            const float previewHeight = 64f;
            Rect row = EditorGUILayout.GetControlRect(false, previewHeight);
            Rect preview = new Rect(row.x, row.y, row.width, previewHeight);
            EditorGUI.DrawRect(preview, new Color(0.08f, 0.08f, 0.08f, 1f));
            GUI.DrawTexture(preview, diffuse, ScaleMode.ScaleAndCrop, false);
        }

        private static void ApplyTerrainLayer(Object[] targets, int index, TerrainLayer terrainLayer)
        {
            string suffix = index.ToString("00");
            string baseProperty = "_BaseMap" + suffix;
            string normalProperty = "_NormalMap" + suffix;
            string maskProperty = "_MaskMap" + suffix;

            Undo.RecordObjects(targets, $"Assign Trail Terrain Layer {index}");
            foreach (Object target in targets)
            {
                if (target is not Material material)
                    continue;

                string terrainLayerPath = terrainLayer != null
                    ? AssetDatabase.GetAssetPath(terrainLayer)
                    : string.Empty;
                string terrainLayerGuid = !string.IsNullOrEmpty(terrainLayerPath)
                    ? AssetDatabase.AssetPathToGUID(terrainLayerPath)
                    : string.Empty;
                material.SetOverrideTag(GetTerrainLayerTag(index), terrainLayerGuid);

                Vector2 sharedScale = GetTextureScale(material, baseProperty);
                Vector2 sharedOffset = GetTextureOffset(material, baseProperty);
                SetTexture(
                    material,
                    baseProperty,
                    terrainLayer != null ? terrainLayer.diffuseTexture : null,
                    sharedScale,
                    sharedOffset);
                SetTexture(
                    material,
                    normalProperty,
                    terrainLayer != null ? terrainLayer.normalMapTexture : null,
                    sharedScale,
                    sharedOffset);
                SetTexture(
                    material,
                    maskProperty,
                    terrainLayer != null ? terrainLayer.maskMapTexture : null,
                    sharedScale,
                    sharedOffset);

                EditorUtility.SetDirty(material);
            }

            if (terrainLayer != null && terrainLayer.diffuseTexture != null)
                TerrainLayersByDiffuse[terrainLayer.diffuseTexture] = terrainLayer;
        }

        private static MaterialProperty FindOptionalProperty(string name, MaterialProperty[] properties)
        {
            return FindProperty(name, properties, false);
        }

        private static string GetTerrainLayerTag(int index)
        {
            return TerrainLayerTagPrefix + index.ToString("00");
        }

        private static TerrainLayer ResolveTerrainLayer(
            Material material,
            int index,
            Texture2D diffuse)
        {
            string storedGuid = material.GetTag(GetTerrainLayerTag(index), false, string.Empty);
            if (!string.IsNullOrEmpty(storedGuid))
            {
                string storedPath = AssetDatabase.GUIDToAssetPath(storedGuid);
                TerrainLayer storedLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(storedPath);
                if (storedLayer != null)
                    return storedLayer;
            }

            TerrainLayer terrainLayer = null;
            if (diffuse != null)
                TerrainLayersByDiffuse.TryGetValue(diffuse, out terrainLayer);

            terrainLayer ??= FindTerrainLayerByAnyTexture(material, index);
            if (terrainLayer != null)
            {
                string path = AssetDatabase.GetAssetPath(terrainLayer);
                string guid = AssetDatabase.AssetPathToGUID(path);
                material.SetOverrideTag(GetTerrainLayerTag(index), guid);
                EditorUtility.SetDirty(material);
            }

            return terrainLayer;
        }

        private static TerrainLayer FindTerrainLayerByAnyTexture(Material material, int index)
        {
            string suffix = index.ToString("00");
            Texture baseMap = GetTexture(material, "_BaseMap" + suffix);
            Texture normalMap = GetTexture(material, "_NormalMap" + suffix);
            Texture maskMap = GetTexture(material, "_MaskMap" + suffix);

            TerrainLayer bestLayer = null;
            int bestMatchCount = 0;
            foreach (TerrainLayer candidate in CachedTerrainLayers)
            {
                int matchCount = 0;
                if (baseMap != null && candidate.diffuseTexture == baseMap)
                    matchCount++;
                if (normalMap != null && candidate.normalMapTexture == normalMap)
                    matchCount++;
                if (maskMap != null && candidate.maskMapTexture == maskMap)
                    matchCount++;

                if (matchCount > bestMatchCount)
                {
                    bestLayer = candidate;
                    bestMatchCount = matchCount;
                }
            }

            return bestLayer;
        }

        private static void SynchronizeTerrainLayerTextures(
            Material material,
            int index,
            TerrainLayer terrainLayer)
        {
            string suffix = index.ToString("00");
            string baseProperty = "_BaseMap" + suffix;
            string normalProperty = "_NormalMap" + suffix;
            string maskProperty = "_MaskMap" + suffix;
            Texture diffuse = terrainLayer.diffuseTexture;
            Texture normal = terrainLayer.normalMapTexture;
            Texture mask = terrainLayer.maskMapTexture;

            bool changed =
                GetTexture(material, baseProperty) != diffuse ||
                GetTexture(material, normalProperty) != normal ||
                GetTexture(material, maskProperty) != mask;
            if (!changed)
                return;

            Vector2 sharedScale = GetTextureScale(material, baseProperty);
            Vector2 sharedOffset = GetTextureOffset(material, baseProperty);
            SetTexture(material, baseProperty, diffuse, sharedScale, sharedOffset);
            SetTexture(material, normalProperty, normal, sharedScale, sharedOffset);
            SetTexture(material, maskProperty, mask, sharedScale, sharedOffset);
            EditorUtility.SetDirty(material);
        }

        private static void EnsureTerrainLayerCache()
        {
            if (!terrainLayerCacheBuilt)
                RebuildTerrainLayerCache();
        }

        private static void RebuildTerrainLayerCache()
        {
            TerrainLayersByDiffuse.Clear();
            CachedTerrainLayers.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TerrainLayer terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (terrainLayer == null)
                    continue;

                CachedTerrainLayers.Add(terrainLayer);
                if (terrainLayer.diffuseTexture != null)
                    TerrainLayersByDiffuse.TryAdd(terrainLayer.diffuseTexture, terrainLayer);
            }

            terrainLayerCacheBuilt = true;
        }
    }
}

#endif
