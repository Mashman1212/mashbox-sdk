#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    internal static class MGLitTrailLinkedMaterialUtility
    {
        private const int LayerCount = 8;
        private const string UseLinkedMaterialTag = "MashBox.MGLitTrail.UseLinkedMaterial";
        private const string LinkedMaterialTag = "MashBox.MGLitTrail.LinkedMaterial";
        private const string TerrainLayerTagPrefix = "MashBox.MGLitTrail.TerrainLayer.";

        internal static bool UsesLinkedMaterial(Material material)
        {
            return material != null &&
                   string.Equals(
                       material.GetTag(UseLinkedMaterialTag, false, string.Empty),
                       "True",
                       System.StringComparison.OrdinalIgnoreCase);
        }

        internal static Material GetLinkedMaterial(Material material)
        {
            if (material == null)
                return null;

            string guid = material.GetTag(LinkedMaterialTag, false, string.Empty);
            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
        }

        internal static void SetUsesLinkedMaterial(Material material, bool enabled)
        {
            if (material == null || UsesLinkedMaterial(material) == enabled)
                return;

            material.SetOverrideTag(UseLinkedMaterialTag, enabled ? "True" : string.Empty);
            if (enabled)
                material.SetOverrideTag("MashBox.MGLitTrail.UseSharedArrays", string.Empty);
            EditorUtility.SetDirty(material);

            if (enabled)
                Synchronize(material);
            else
                MGLitTrailTextureArrayBuilder.AssignExistingArrays(material);
        }

        internal static bool SetLinkedMaterial(Material material, Material linkedMaterial)
        {
            if (material == null || linkedMaterial == material)
                return false;

            string path = linkedMaterial != null ? AssetDatabase.GetAssetPath(linkedMaterial) : string.Empty;
            material.SetOverrideTag(
                LinkedMaterialTag,
                !string.IsNullOrEmpty(path) ? AssetDatabase.AssetPathToGUID(path) : string.Empty);
            EditorUtility.SetDirty(material);
            return linkedMaterial == null || Synchronize(material);
        }

        internal static bool Synchronize(Material material)
        {
            return Synchronize(material, new HashSet<Material>());
        }

        private static bool Synchronize(Material material, HashSet<Material> stack)
        {
            if (!UsesLinkedMaterial(material))
                return true;

            Material source = GetLinkedMaterial(material);
            if (source == null || source == material || !stack.Add(material))
                return false;

            try
            {
                if (UsesLinkedMaterial(source) && !Synchronize(source, stack))
                    return false;
                if (source.shader == null || material.shader != source.shader)
                    return false;

                bool changed = CopyShaderProperties(source, material);
                changed |= CopyTerrainLayerTags(source, material);
                if (changed)
                    EditorUtility.SetDirty(material);
                return true;
            }
            finally
            {
                stack.Remove(material);
            }
        }

        private static bool CopyShaderProperties(Material source, Material destination)
        {
            Shader shader = source.shader;
            bool changed = false;
            for (int index = 0; index < shader.GetPropertyCount(); index++)
            {
                string propertyName = shader.GetPropertyName(index);
                if (IsLocalControlProperty(propertyName))
                    continue;

                switch (shader.GetPropertyType(index))
                {
                    case ShaderPropertyType.Color:
                        changed |= SetColor(destination, propertyName, source.GetColor(propertyName));
                        break;
                    case ShaderPropertyType.Vector:
                        changed |= SetVector(destination, propertyName, source.GetVector(propertyName));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    case ShaderPropertyType.Int:
                        changed |= SetFloat(destination, propertyName, source.GetFloat(propertyName));
                        break;
                    case ShaderPropertyType.Texture:
                        changed |= SetTexture(destination, propertyName, source.GetTexture(propertyName));
                        if (shader.GetPropertyTextureDimension(index) == TextureDimension.Tex2D)
                        {
                            changed |= SetTextureScale(destination, propertyName, source.GetTextureScale(propertyName));
                            changed |= SetTextureOffset(destination, propertyName, source.GetTextureOffset(propertyName));
                        }
                        break;
                }
            }

            return changed;
        }

        private static bool SetColor(Material material, string name, Color value)
        {
            if (material.GetColor(name) == value)
                return false;
            material.SetColor(name, value);
            return true;
        }

        private static bool SetVector(Material material, string name, Vector4 value)
        {
            if (material.GetVector(name) == value)
                return false;
            material.SetVector(name, value);
            return true;
        }

        private static bool SetFloat(Material material, string name, float value)
        {
            if (Mathf.Approximately(material.GetFloat(name), value))
                return false;
            material.SetFloat(name, value);
            return true;
        }

        private static bool SetTexture(Material material, string name, Texture value)
        {
            if (material.GetTexture(name) == value)
                return false;
            material.SetTexture(name, value);
            return true;
        }

        private static bool SetTextureScale(Material material, string name, Vector2 value)
        {
            if (material.GetTextureScale(name) == value)
                return false;
            material.SetTextureScale(name, value);
            return true;
        }

        private static bool SetTextureOffset(Material material, string name, Vector2 value)
        {
            if (material.GetTextureOffset(name) == value)
                return false;
            material.SetTextureOffset(name, value);
            return true;
        }

        private static bool CopyTerrainLayerTags(Material source, Material destination)
        {
            bool changed = false;
            for (int index = 0; index < LayerCount; index++)
            {
                string tagName = TerrainLayerTagPrefix + index.ToString("00");
                string value = source.GetTag(tagName, false, string.Empty);
                if (destination.GetTag(tagName, false, string.Empty) == value)
                    continue;

                destination.SetOverrideTag(tagName, value);
                changed = true;
            }

            return changed;
        }

        private static bool IsLocalControlProperty(string propertyName)
        {
            return propertyName == "_ControlMap1" ||
                   propertyName == "_ControlMap2" ||
                   propertyName == "_ControlUV2";
        }
    }

    [InitializeOnLoad]
    internal static class MGLitTrailLinkedMaterialSynchronizer
    {
        private static bool synchronizationQueued;
        private static bool isSynchronizing;

        static MGLitTrailLinkedMaterialSynchronizer()
        {
            EditorApplication.projectChanged += QueueSynchronization;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        internal static void SynchronizeAll()
        {
            if (isSynchronizing)
                return;

            isSynchronizing = true;
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material"))
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (MGLitTrailLinkedMaterialUtility.UsesLinkedMaterial(material))
                        MGLitTrailLinkedMaterialUtility.Synchronize(material);
                }
            }
            finally
            {
                isSynchronizing = false;
            }
        }

        private static UndoPropertyModification[] OnPostprocessModifications(
            UndoPropertyModification[] modifications)
        {
            foreach (UndoPropertyModification modification in modifications)
            {
                if (modification.currentValue?.target is Material)
                {
                    QueueSynchronization();
                    break;
                }
            }

            return modifications;
        }

        private static void QueueSynchronization()
        {
            if (synchronizationQueued || isSynchronizing)
                return;

            synchronizationQueued = true;
            EditorApplication.delayCall += () =>
            {
                synchronizationQueued = false;
                SynchronizeAll();
            };
        }
    }

    internal sealed class MGLitTrailLinkedMaterialBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            MGLitTrailLinkedMaterialSynchronizer.SynchronizeAll();
            AssetDatabase.SaveAssets();
        }
    }

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
        private const string ArrayResolutionTagName = "MashBox.MGLitTrail.ArrayResolution";
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
        private static int selectedArrayTextureResolutionIndex = 2;

        private sealed class LayerDragData
        {
            public Material material;
            public int sourceIndex;
        }

        private sealed class ControlTextureGenerationPopup : PopupWindowContent
        {
            private readonly Material material;
            private readonly MaterialEditor materialEditor;
            private int controlResolutionIndex;
            private int arrayResolutionIndex;

            public ControlTextureGenerationPopup(Material material, MaterialEditor materialEditor)
            {
                this.material = material;
                this.materialEditor = materialEditor;
                controlResolutionIndex = selectedControlTextureResolutionIndex;
                arrayResolutionIndex = GetArrayResolutionIndex(material);
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360f, 168f);
            }

            public override void OnGUI(Rect rect)
            {
                bool usesExternalArrays = MGLitTrailTextureArrayBuilder.UsesExternalArraySource(material);
                EditorGUILayout.LabelField(
                    usesExternalArrays ? "Generate Trail Control Textures" : "Generate Trail Texture Assets",
                    EditorStyles.boldLabel);
                EditorGUILayout.Space(3f);
                controlResolutionIndex = EditorGUILayout.Popup(
                    new GUIContent("Control Resolution"),
                    controlResolutionIndex,
                    ControlTextureResolutionLabels);
                if (!usesExternalArrays)
                {
                    arrayResolutionIndex = EditorGUILayout.Popup(
                        new GUIContent("Array Resolution"),
                        arrayResolutionIndex,
                        ControlTextureResolutionLabels);
                }
                EditorGUILayout.HelpBox(
                    usesExternalArrays
                        ? "Creates both control maps beside the material. Linked texture arrays are left unchanged. Layer 0 starts at full weight."
                        : "Creates both control maps plus the Base Map, Height, and packed Surface texture arrays beside the material. Layer 0 starts at full weight.",
                    MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                        editorWindow.Close();
                    if (GUILayout.Button("Generate", GUILayout.Width(90f)))
                    {
                        selectedControlTextureResolutionIndex = controlResolutionIndex;
                        selectedArrayTextureResolutionIndex = arrayResolutionIndex;
                        int arrayResolution = ControlTextureResolutions[arrayResolutionIndex];
                        material.SetOverrideTag(ArrayResolutionTagName, arrayResolution.ToString());
                        if (GenerateControlTextures(material, ControlTextureResolutions[controlResolutionIndex]) &&
                            !usesExternalArrays)
                            MGLitTrailTextureArrayBuilder.Build(material, arrayResolution, true);
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
            public float normalStrength;
            public Texture maskMap;
            public Vector2 maskScale;
            public Vector2 maskOffset;
            public Vector4 mappingTiling;
            public Vector4 mappingOffset;
            public float heightBlend;
            public float heightRemapMin;
            public float heightRemapMax;
            public float tessellationRemapMin;
            public float tessellationRemapMax;
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
            MGLitTrailLinkedMaterialUtility.Synchronize(material);
            SynchronizeLayerTextureTransforms(material);
            MGLitTrailTextureArrayBuilder.AssignExistingArrays(material);
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
            MGLitTrailLinkedMaterialUtility.Synchronize(material);

            EditorGUILayout.LabelField("MASHBOX • MG LIT TRAIL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Terrain Layers are editor-only authoring inputs. Their diffuse data becomes the Base Map array, while normal and mask data is packed into the Height and Surface arrays. Linked materials can share those arrays, layers, and shader values while retaining independent control maps.",
                MessageType.Info);

            DrawControlMaps(materialEditor, properties);
            bool usesLinkedMaterial = MGLitTrailLinkedMaterialUtility.UsesLinkedMaterial(material);
            using (new EditorGUI.DisabledScope(usesLinkedMaterial))
            {
                DrawPuddleControls(materialEditor, properties);
                DrawGlobalHeightControls(materialEditor, properties);
                DrawAuxiliaryNormals(materialEditor, properties);
                DrawTerrainLayers(materialEditor, properties, material);

                GUILayout.Space(6f);
                base.OnMaterialGUI(materialEditor, properties);
            }

            if (usesLinkedMaterial)
            {
                EditorGUILayout.HelpBox(
                    "Linked values and Terrain Layers are read-only here. Edit the linked material to update them; this material keeps its own control maps and Use UV2 setting.",
                    MessageType.Info);
            }

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

                // New trail shaders sample all three layer textures through explicit
                // Vector2 mapping properties instead of Unity texture transforms.
                if (material.HasProperty("_Tiling" + suffix) ||
                    material.HasProperty("_Offset" + suffix))
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
            MaterialProperty controlUv2 = FindOptionalProperty("_ControlUV2", properties);
            if (controlMap1 == null && controlMap2 == null && controlUv2 == null)
                return;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Control Maps", EditorStyles.boldLabel);
            if (controlMap1 != null)
                materialEditor.TexturePropertySingleLine(new GUIContent("Control Map 1 (IDs 0–3)"), controlMap1);
            if (controlMap2 != null)
                materialEditor.TexturePropertySingleLine(new GUIContent("Control Map 2 (IDs 4–7)"), controlMap2);

            if (controlUv2 != null)
            {
                materialEditor.ShaderProperty(
                    controlUv2,
                    new GUIContent(
                        "Use UV2",
                        "Sample both control maps from UV2 instead of the shader's alternate control UV channel."));
            }

            Material material = materialEditor.target as Material;
            string materialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            bool usesLinkedMaterial = DrawTextureArraySources(materialEditor, material);
            using (new EditorGUI.DisabledScope(
                       material == null ||
                       materialEditor.targets.Length != 1 ||
                       string.IsNullOrEmpty(materialPath)))
            {
                if (GUILayout.Button(
                        new GUIContent(
                            usesLinkedMaterial ? "Generate Control Textures..." : "Generate Trail Texture Assets...",
                            usesLinkedMaterial
                                ? "Create and assign two paintable control maps without changing the linked texture arrays."
                                : "Create and assign two paintable control maps plus the Base Map, Height, and packed Surface arrays beside this material.")))
                {
                    Rect buttonRect = GUILayoutUtility.GetLastRect();
                    PopupWindow.Show(buttonRect, new ControlTextureGenerationPopup(material, materialEditor));
                }

                using (new EditorGUI.DisabledScope(usesLinkedMaterial))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Rebuild Texture Arrays",
                                "Rebuild only the Base Map, Height, and packed Surface arrays. Existing painted control maps are not changed.")))
                    {
                        MGLitTrailTextureArrayBuilder.Build(material, GetArrayResolution(material), true);
                        materialEditor?.Repaint();
                    }
                }

                bool hasAssignedControlMap =
                    controlMap1 != null && controlMap1.textureValue is Texture2D ||
                    controlMap2 != null && controlMap2.textureValue is Texture2D;
                using (new EditorGUI.DisabledScope(!hasAssignedControlMap))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Apply BC7 to Control Maps",
                                "Apply linear BC7 Standalone import settings without changing the painted PNG pixels.")))
                    {
                        ApplyControlMapCompression(controlMap1, controlMap2);
                        materialEditor?.Repaint();
                    }
                }
            }

            if (string.IsNullOrEmpty(materialPath))
                EditorGUILayout.HelpBox("Save this material as an asset before generating trail texture assets.", MessageType.None);

            DrawGeneratedArrays(material, usesLinkedMaterial);
        }

        private static bool DrawTextureArraySources(
            MaterialEditor materialEditor,
            Material material)
        {
            if (material == null)
                return false;

            GUILayout.Space(5f);
            EditorGUILayout.LabelField("Material Linking", EditorStyles.boldLabel);

            bool usesLinkedMaterial = MGLitTrailLinkedMaterialUtility.UsesLinkedMaterial(material);
            using (new EditorGUI.DisabledScope(materialEditor.targets.Length != 1))
            {
                EditorGUI.BeginChangeCheck();
                bool requestedLinkedMaterial = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Use Linked Material",
                        "Inherit texture arrays, Terrain Layers, and shader values from another trail material while keeping local control maps."),
                    usesLinkedMaterial);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, "Change Linked Trail Material");
                    MGLitTrailLinkedMaterialUtility.SetUsesLinkedMaterial(material, requestedLinkedMaterial);
                    usesLinkedMaterial = requestedLinkedMaterial;
                }
            }

            if (!usesLinkedMaterial)
            {
                EditorGUILayout.LabelField("Texture Arrays", EditorStyles.boldLabel);
                int resolutionIndex = GetArrayResolutionIndex(material);
                EditorGUI.BeginChangeCheck();
                resolutionIndex = EditorGUILayout.Popup(
                    new GUIContent(
                        "Generated Resolution",
                        "Resolution used for the Base Map, Height, and Surface arrays on the next rebuild."),
                    resolutionIndex,
                    ControlTextureResolutionLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, "Change Trail Array Resolution");
                    selectedArrayTextureResolutionIndex = resolutionIndex;
                    material.SetOverrideTag(
                        ArrayResolutionTagName,
                        ControlTextureResolutions[resolutionIndex].ToString());
                    EditorUtility.SetDirty(material);
                }
                return false;
            }

            Material linkedMaterial = MGLitTrailLinkedMaterialUtility.GetLinkedMaterial(material);
            using (new EditorGUI.DisabledScope(materialEditor.targets.Length != 1))
            {
                EditorGUI.BeginChangeCheck();
                Material requestedMaterial = EditorGUILayout.ObjectField(
                    new GUIContent("Linked Material", "Material whose arrays, layers, and shader values this material inherits."),
                    linkedMaterial,
                    typeof(Material),
                    false) as Material;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, "Set Linked Trail Material");
                    MGLitTrailLinkedMaterialUtility.SetLinkedMaterial(material, requestedMaterial);
                    linkedMaterial = requestedMaterial;
                }
            }

            if (linkedMaterial == null)
                EditorGUILayout.HelpBox("Assign a trail material to link.", MessageType.Warning);
            else if (linkedMaterial == material)
                EditorGUILayout.HelpBox("A material cannot link to itself.", MessageType.Error);
            else if (linkedMaterial.shader != material.shader)
                EditorGUILayout.HelpBox("The linked material must use the same shader.", MessageType.Error);
            else if (!MGLitTrailLinkedMaterialUtility.Synchronize(material))
                EditorGUILayout.HelpBox("The material link is circular or otherwise invalid.", MessageType.Error);
            else if (MGLitTrailTextureArrayBuilder.GetAssignedArray(
                         material, MGLitTrailTextureArrayBuilder.BaseMapArrayPropertyNames) == null ||
                     MGLitTrailTextureArrayBuilder.GetAssignedArray(
                         material, MGLitTrailTextureArrayBuilder.HeightArrayPropertyNames) == null ||
                     MGLitTrailTextureArrayBuilder.GetAssignedArray(
                         material, MGLitTrailTextureArrayBuilder.SurfaceArrayPropertyNames) == null)
                EditorGUILayout.HelpBox(
                    "The linked material does not currently provide all three texture arrays.",
                    MessageType.Warning);

            return true;
        }

        private static void DrawGeneratedArrays(Material material, bool linked)
        {
            if (material == null)
                return;

            Texture2DArray baseMapArray = MGLitTrailTextureArrayBuilder.GetAssignedArray(
                material, MGLitTrailTextureArrayBuilder.BaseMapArrayPropertyNames);
            if (!linked)
                baseMapArray ??=
                MGLitTrailTextureArrayBuilder.LoadGeneratedArray(
                    material, MGLitTrailTextureArrayBuilder.ArrayKind.BaseMap);
            Texture2DArray heightArray = MGLitTrailTextureArrayBuilder.GetAssignedArray(
                material, MGLitTrailTextureArrayBuilder.HeightArrayPropertyNames);
            if (!linked)
                heightArray ??=
                MGLitTrailTextureArrayBuilder.LoadGeneratedArray(
                    material, MGLitTrailTextureArrayBuilder.ArrayKind.Height);
            Texture2DArray surfaceArray = MGLitTrailTextureArrayBuilder.GetAssignedArray(
                material, MGLitTrailTextureArrayBuilder.SurfaceArrayPropertyNames);
            if (!linked)
                surfaceArray ??=
                MGLitTrailTextureArrayBuilder.LoadGeneratedArray(
                    material, MGLitTrailTextureArrayBuilder.ArrayKind.Surface);

            if (baseMapArray == null && heightArray == null && surfaceArray == null)
                return;

            GUILayout.Space(3f);
            EditorGUILayout.LabelField(linked ? "Linked Texture Arrays" : "Generated Texture Arrays", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Base Map Array (BC7 sRGB)", baseMapArray, typeof(Texture2DArray), false);
                EditorGUILayout.ObjectField("Height Array (BC4 Linear)", heightArray, typeof(Texture2DArray), false);
                EditorGUILayout.ObjectField("Surface Array (BC7 Linear)", surfaceArray, typeof(Texture2DArray), false);
            }

            if (!MGLitTrailTextureArrayBuilder.HasAnyArrayProperty(material))
            {
                EditorGUILayout.HelpBox(
                    "The arrays are generated and ready. Add Texture2DArray properties named _BaseMapArray (sRGB), _HeightMapArray (linear), and _SurfaceMapArray (linear) to the Shader Graph to bind them automatically.",
                    MessageType.Info);
            }
        }

        private static int GetArrayResolutionIndex(Material material)
        {
            string stored = material != null
                ? material.GetTag(ArrayResolutionTagName, false, string.Empty)
                : string.Empty;
            if (int.TryParse(stored, out int resolution))
            {
                int index = System.Array.IndexOf(ControlTextureResolutions, resolution);
                if (index >= 0)
                    return index;
            }

            return selectedArrayTextureResolutionIndex;
        }

        private static bool GenerateControlTextures(Material material, int resolution)
        {
            if (material == null)
                return false;

            string materialPath = AssetDatabase.GetAssetPath(material);
            string materialDirectory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(materialDirectory))
            {
                EditorUtility.DisplayDialog(
                    "Generate Control Textures",
                    "Save this material as an asset before generating its control textures.",
                    "OK");
                return false;
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
                return false;

            if ((File.Exists(Path.GetFullPath(controlMap1Path)) && !AssetDatabase.MakeEditable(controlMap1Path)) ||
                (File.Exists(Path.GetFullPath(controlMap2Path)) && !AssetDatabase.MakeEditable(controlMap2Path)))
            {
                EditorUtility.DisplayDialog(
                    "Control Textures Are Read-Only",
                    "The existing control textures could not be checked out or made editable.",
                    "OK");
                return false;
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
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Control Texture Generation Failed",
                    exception.Message,
                    "OK");
                return false;
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
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.compressionQuality = 100;
                importer.crunchedCompression = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;

                TextureImporterPlatformSettings standaloneSettings =
                    importer.GetPlatformTextureSettings("Standalone");
                standaloneSettings.name = "Standalone";
                standaloneSettings.overridden = true;
                standaloneSettings.maxTextureSize = 8192;
                standaloneSettings.format = TextureImporterFormat.BC7;
                standaloneSettings.textureCompression = TextureImporterCompression.CompressedHQ;
                standaloneSettings.compressionQuality = 100;
                standaloneSettings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(standaloneSettings);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void ApplyControlMapCompression(
            MaterialProperty controlMap1,
            MaterialProperty controlMap2)
        {
            var paths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            AddControlMapPath(controlMap1, paths);
            AddControlMapPath(controlMap2, paths);

            foreach (string path in paths)
                ImportControlTexture(path);
        }

        private static void AddControlMapPath(
            MaterialProperty property,
            HashSet<string> paths)
        {
            if (property?.textureValue is not Texture2D texture)
                return;

            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
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
            MaterialProperty tessellationAmplitudeMaster =
                FindOptionalProperty("_TesselationAmplitudeMaster", properties);
            MaterialProperty pomAmplitudeMaster =
                FindOptionalProperty("_POMAmplitudeMaster", properties);
            if (heightTransition == null &&
                tessellationAmplitudeMaster == null &&
                pomAmplitudeMaster == null)
                return;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Height & Displacement", EditorStyles.boldLabel);

            if (heightTransition != null)
            {
                materialEditor.ShaderProperty(
                    heightTransition,
                    new GUIContent(
                        "Height Transition",
                        "Controls the width of the transition between height-blended terrain layers."));
            }

            if (tessellationAmplitudeMaster != null)
            {
                materialEditor.ShaderProperty(
                    tessellationAmplitudeMaster,
                    new GUIContent(
                        "Tessellation Amplitude Master",
                        "Globally scales the tessellation displacement amplitude for all trail layers."));
            }

            if (pomAmplitudeMaster != null)
            {
                materialEditor.ShaderProperty(
                    pomAmplitudeMaster,
                    new GUIContent(
                        "POM Amplitude Master",
                        "Globally scales the parallax occlusion mapping amplitude for all trail layers."));
            }
        }

        private static void DrawAuxiliaryNormals(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            MaterialProperty auxiliaryNormal00 = FindOptionalProperty("_AuxNormalMap00", properties);
            MaterialProperty auxiliaryNormal01 = FindOptionalProperty("_AuxNormalMap01", properties);
            MaterialProperty auxiliaryDisplacement00 = FindOptionalProperty("_AuxNormalDisplacementMap00", properties);
            MaterialProperty auxiliaryDisplacement01 = FindOptionalProperty("_AuxNormalDisplacementMap01", properties);
            MaterialProperty auxiliaryDisplacementStrength00 = FindOptionalProperty("_AuxNormalDisplacementStrength00", properties);
            MaterialProperty auxiliaryDisplacementStrength01 = FindOptionalProperty("_AuxNormalDisplacementStrength01", properties);
            MaterialProperty auxiliaryNormalTiling00 = FindOptionalProperty("_AuxNormalTiling00", properties);
            MaterialProperty auxiliaryNormalTiling01 = FindOptionalProperty("_AuxNormalTiling01", properties);
            MaterialProperty auxiliaryNormalStrength00 = FindOptionalProperty("_AuxNormalStrength00", properties);
            MaterialProperty auxiliaryNormalStrength01 = FindOptionalProperty("_AuxNormalStrength01", properties);
            if (auxiliaryNormal00 == null &&
                auxiliaryNormal01 == null &&
                auxiliaryDisplacement00 == null &&
                auxiliaryDisplacement01 == null &&
                auxiliaryDisplacementStrength00 == null &&
                auxiliaryDisplacementStrength01 == null &&
                auxiliaryNormalTiling00 == null &&
                auxiliaryNormalTiling01 == null &&
                auxiliaryNormalStrength00 == null &&
                auxiliaryNormalStrength01 == null)
                return;

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Auxiliary Normals", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Vertex color masks: Red (R) drives Auxiliary Normal 1. " +
                "Green (G) drives Auxiliary Normal 2.",
                MessageType.Info);
            if (auxiliaryNormal00 != null)
            {
                materialEditor.TexturePropertySingleLine(
                    new GUIContent(
                        "Aux Normal Map 1",
                        "Blended by the mesh vertex-color Red (R) channel."),
                    auxiliaryNormal00);
            }
            if (auxiliaryNormalStrength00 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryNormalStrength00,
                    new GUIContent("Normal Strength 1"));
            }
            if (auxiliaryDisplacement00 != null)
            {
                materialEditor.TexturePropertySingleLine(
                    new GUIContent("Displacement Map 1"),
                    auxiliaryDisplacement00);
            }
            if (auxiliaryDisplacementStrength00 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryDisplacementStrength00,
                    new GUIContent("Displacement Strength 1"));
            }
            if (auxiliaryNormalTiling00 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryNormalTiling00,
                    new GUIContent("Normal Tiling 1"));
            }
            if (auxiliaryNormal01 != null)
            {
                materialEditor.TexturePropertySingleLine(
                    new GUIContent(
                        "Aux Normal Map 2",
                        "Blended by the mesh vertex-color Green (G) channel."),
                    auxiliaryNormal01);
            }
            if (auxiliaryNormalStrength01 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryNormalStrength01,
                    new GUIContent("Normal Strength 2"));
            }
            if (auxiliaryDisplacement01 != null)
            {
                materialEditor.TexturePropertySingleLine(
                    new GUIContent("Displacement Map 2"),
                    auxiliaryDisplacement01);
            }
            if (auxiliaryDisplacementStrength01 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryDisplacementStrength01,
                    new GUIContent("Displacement Strength 2"));
            }
            if (auxiliaryNormalTiling01 != null)
            {
                materialEditor.ShaderProperty(
                    auxiliaryNormalTiling01,
                    new GUIContent("Normal Tiling 2"));
            }
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
                MaterialProperty normalStrength = FindOptionalProperty("_NormalStrength" + suffix, properties);
                MaterialProperty maskMap = FindOptionalProperty("_MaskMap" + suffix, properties);
                MaterialProperty mappingTiling = FindOptionalProperty("_Tiling" + suffix, properties);
                MaterialProperty mappingOffset = FindOptionalProperty("_Offset" + suffix, properties);
                MaterialProperty heightBlend = FindOptionalProperty("_HeightBlend" + suffix, properties);
                MaterialProperty heightRemapMin = FindOptionalProperty("_HeightRemapMin" + suffix, properties);
                MaterialProperty heightRemapMax = FindOptionalProperty("_HeightRemapMax" + suffix, properties);
                MaterialProperty tessellationRemapMin =
                    FindOptionalProperty("_TesselationRemapMin" + suffix, properties);
                MaterialProperty tessellationRemapMax =
                    FindOptionalProperty("_TesselationRemapMax" + suffix, properties);
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
                        normalStrength,
                        maskMap,
                        mappingTiling,
                        mappingOffset,
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
                        heightRemapMin,
                        heightRemapMax,
                        tessellationRemapMin,
                        tessellationRemapMax,
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
                MGLitTrailTextureArrayBuilder.QueueBuild(material, GetArrayResolution(material));
            }
        }

        private static LayerValues ReadLayerValues(Material material, int index)
        {
            string suffix = index.ToString("00");
            string baseProperty = "_BaseMap" + suffix;
            string normalProperty = "_NormalMap" + suffix;
            string maskProperty = "_MaskMap" + suffix;
            string mappingTilingProperty = "_Tiling" + suffix;
            string mappingOffsetProperty = "_Offset" + suffix;
            string heightBlendProperty = "_HeightBlend" + suffix;
            string heightRemapMinProperty = "_HeightRemapMin" + suffix;
            string heightRemapMaxProperty = "_HeightRemapMax" + suffix;
            string tessellationRemapMinProperty = "_TesselationRemapMin" + suffix;
            string tessellationRemapMaxProperty = "_TesselationRemapMax" + suffix;
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
                normalStrength = GetFloat(material, "_NormalStrength" + suffix, 1f),
                maskMap = GetTexture(material, maskProperty),
                maskScale = GetTextureScale(material, maskProperty),
                maskOffset = GetTextureOffset(material, maskProperty),
                mappingTiling = GetVector(material, mappingTilingProperty, new Vector4(1f, 1f, 0f, 0f)),
                mappingOffset = GetVector(material, mappingOffsetProperty, Vector4.zero),
                heightBlend = GetFloat(material, heightBlendProperty),
                heightRemapMin = GetFloat(material, heightRemapMinProperty),
                heightRemapMax = GetFloat(material, heightRemapMaxProperty, 1f),
                tessellationRemapMin = GetFloat(material, tessellationRemapMinProperty),
                tessellationRemapMax = GetFloat(material, tessellationRemapMaxProperty, 1f),
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
            SetFloat(material, "_NormalStrength" + suffix, values.normalStrength);
            SetTexture(
                material,
                "_MaskMap" + suffix,
                values.maskMap,
                values.baseScale,
                values.baseOffset);
            SetVector(material, "_Tiling" + suffix, values.mappingTiling);
            SetVector(material, "_Offset" + suffix, values.mappingOffset);
            SetFloat(material, "_HeightBlend" + suffix, values.heightBlend);
            SetFloat(material, "_HeightRemapMin" + suffix, values.heightRemapMin);
            SetFloat(material, "_HeightRemapMax" + suffix, values.heightRemapMax);
            SetFloat(material, "_TesselationRemapMin" + suffix, values.tessellationRemapMin);
            SetFloat(material, "_TesselationRemapMax" + suffix, values.tessellationRemapMax);
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

        private static float GetFloat(Material material, string propertyName, float fallback = 0f)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static Vector4 GetVector(Material material, string propertyName, Vector4 fallback)
        {
            return material.HasProperty(propertyName) ? material.GetVector(propertyName) : fallback;
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

        private static void SetVector(Material material, string propertyName, Vector4 value)
        {
            if (material.HasProperty(propertyName))
                material.SetVector(propertyName, value);
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
            MaterialProperty normalStrength,
            MaterialProperty maskMap,
            MaterialProperty mappingTiling,
            MaterialProperty mappingOffset,
            MaterialProperty planarMap)
        {
            if (baseMap == null &&
                normalStrength == null &&
                mappingTiling == null &&
                mappingOffset == null &&
                planarMap == null)
                return;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Mapping", EditorStyles.miniBoldLabel);
            if (normalStrength != null)
            {
                materialEditor.ShaderProperty(
                    normalStrength,
                    new GUIContent("Normal Strength", "Scales the tangent-space normal intensity for this layer."));
            }
            if (planarMap != null)
            {
                materialEditor.ShaderProperty(
                    planarMap,
                    new GUIContent(
                        "Planar Mapping",
                        "Uses the shader's planar projection for this texture layer."));
            }

            if (mappingTiling != null || mappingOffset != null)
            {
                if (mappingTiling != null)
                {
                    materialEditor.ShaderProperty(
                        mappingTiling,
                        new GUIContent("Tiling", "UV tiling shared by this layer's base, normal, and mask maps."));
                }

                if (mappingOffset != null)
                {
                    materialEditor.ShaderProperty(
                        mappingOffset,
                        new GUIContent("Offset", "UV offset shared by this layer's base, normal, and mask maps."));
                }

                return;
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
            MaterialProperty heightRemapMin,
            MaterialProperty heightRemapMax,
            MaterialProperty tessellationRemapMin,
            MaterialProperty tessellationRemapMax,
            MaterialProperty heightOffset,
            MaterialProperty heightAmplitude,
            MaterialProperty heightContrast,
            MaterialProperty heightInfluence)
        {
            if (heightBlend == null &&
                heightRemapMin == null &&
                heightRemapMax == null &&
                tessellationRemapMin == null &&
                tessellationRemapMax == null &&
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

            if (heightRemapMin != null && heightRemapMax != null)
            {
                materialEditor.MinMaxShaderProperty(
                    heightRemapMin,
                    heightRemapMax,
                    0f,
                    1f,
                    new GUIContent(
                        "Height Remapping",
                        "Sets the minimum and maximum bounds used to remap this layer's sampled mask-map height."));
            }
            else if (heightRemapMin != null)
            {
                materialEditor.ShaderProperty(heightRemapMin, new GUIContent("Height Remap Min"));
            }
            else if (heightRemapMax != null)
            {
                materialEditor.ShaderProperty(heightRemapMax, new GUIContent("Height Remap Max"));
            }

            if (tessellationRemapMin != null && tessellationRemapMax != null)
            {
                materialEditor.MinMaxShaderProperty(
                    tessellationRemapMin,
                    tessellationRemapMax,
                    0f,
                    1f,
                    new GUIContent(
                        "Tessellation Remapping",
                        "Sets the minimum and maximum sampled-height range used for this layer's tessellation displacement."));
            }
            else if (tessellationRemapMin != null)
            {
                materialEditor.ShaderProperty(
                    tessellationRemapMin,
                    new GUIContent("Tessellation Remap Min"));
            }
            else if (tessellationRemapMax != null)
            {
                materialEditor.ShaderProperty(
                    tessellationRemapMax,
                    new GUIContent("Tessellation Remap Max"));
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
                MGLitTrailTextureArrayBuilder.QueueBuild(material, GetArrayResolution(material));
            }

            if (terrainLayer != null && terrainLayer.diffuseTexture != null)
                TerrainLayersByDiffuse[terrainLayer.diffuseTexture] = terrainLayer;
        }

        private static int GetArrayResolution(Material material)
        {
            int index = GetArrayResolutionIndex(material);
            return ControlTextureResolutions[Mathf.Clamp(index, 0, ControlTextureResolutions.Length - 1)];
        }

        private static MaterialProperty FindOptionalProperty(string name, MaterialProperty[] properties)
        {
            return FindProperty(name, properties, false);
        }

        private static MaterialProperty FindFirstOptionalProperty(
            string[] names,
            MaterialProperty[] properties)
        {
            foreach (string name in names)
            {
                MaterialProperty property = FindOptionalProperty(name, properties);
                if (property != null)
                    return property;
            }

            return null;
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

            bool hasBaseProperty = material.HasProperty(baseProperty);
            bool hasNormalProperty = material.HasProperty(normalProperty);
            bool hasMaskProperty = material.HasProperty(maskProperty);
            if (!hasBaseProperty && !hasNormalProperty && !hasMaskProperty)
                return;

            bool changed =
                (hasBaseProperty && GetTexture(material, baseProperty) != diffuse) ||
                (hasNormalProperty && GetTexture(material, normalProperty) != normal) ||
                (hasMaskProperty && GetTexture(material, maskProperty) != mask);
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
