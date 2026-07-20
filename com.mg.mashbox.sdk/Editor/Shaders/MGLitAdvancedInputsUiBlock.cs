#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    /// <summary>Organized property groups shared by the Advanced surface shaders.</summary>
    public sealed class MGLitAdvancedInputsUiBlock : MaterialUIBlock
    {
        public enum Section
        {
            Surface,
            Decals,
            Dirt,
            Detail,
            Emissive,
            AdvancedLighting
        }

        private readonly ExpandableBit foldoutBit;
        private readonly Section section;

        private MaterialProperty baseMap, baseColor, tilingOffset, albedoMultiply, finalAlbedoTint;
        private MaterialProperty whiteBalance, whiteBoost, saturation, contrast, tint;
        private MaterialProperty alphaRemapMin, alphaRemapMax, alphaClipThreshold;
        private MaterialProperty maskMap, metallicMin, metallicMax, smoothnessMin, smoothnessMax, aoMin, aoMax;
        private MaterialProperty normalMap, normalScale, vertexInfluence, vertexBlend, favourVertexAlpha;

        private MaterialProperty useDecals, decalLayer, decalNormal, decalsTilingOffset, decalBlend;
        private MaterialProperty decalAlbedoMultiply, multiplyDecalAlbedo, decalAlbedoTint;
        private MaterialProperty decalNormalStrength, decalSmoothness, decalSmoothnessAmount;
        private MaterialProperty decalMetalness, decalMetalnessAmount, metalAoSubtractsDecals;
        private MaterialProperty decalMetalSubtractMin, decalMetalSubtractMax, decalAoSubtractMin, decalAoSubtractMax;
        private MaterialProperty decalLevelsMin, decalLevelsMax, decalLevelsStrength;
        private MaterialProperty enableProjectionOffset, projectionOffsetAmount, vertexAlphaFadesDecals;

        private MaterialProperty dirtTexture, useDirt, dirtStrength, dirtScaling, dirtContrast;
        private MaterialProperty dirtAsRoughness, dirtRoughnessStrength, dirtRoughnessContrast;

        private MaterialProperty detailMap, detailNormalScale, switchUv0Uv2;
        private MaterialProperty emissiveMap, emissiveColor, emissiveIntensity;

        private MaterialProperty specOcclusionMultiplier, specOcclusionContrast, specOcclusionReduction, specOcclusionMaxClamp;
        private MaterialProperty monoShSmoothnessReduction, monoShNormalStrength, monoShEmissionStrength;

        public MGLitAdvancedInputsUiBlock(ExpandableBit foldoutBit, Section section)
        {
            this.foldoutBit = foldoutBit;
            this.section = section;
        }

        public override void LoadMaterialProperties()
        {
            baseMap = P("_BaseColorMap"); baseColor = P("_BaseColor"); tilingOffset = P("_Tiling_and_Offset");
            albedoMultiply = P("_Albedo_Multiply"); finalAlbedoTint = P("_Final_Albedo_Tint");
            whiteBalance = P("_White_Balance"); whiteBoost = P("_WhiteBoost"); saturation = P("_Saturation");
            contrast = P("_Contrast"); tint = P("_Tint"); alphaRemapMin = P("_Alpha_Remap_Min");
            alphaRemapMax = P("_Alpha_Remap_Max"); alphaClipThreshold = P("_Alpha_Clip_Threshold");
            maskMap = P("_MaskMap"); metallicMin = P("_MetallicRemapMin"); metallicMax = P("_MetallicRemapMax");
            smoothnessMin = P("_SmoothnessRemapMin"); smoothnessMax = P("_SmoothnessRemapMax");
            aoMin = P("_AORemapMin"); aoMax = P("_AORemapMax"); normalMap = P("_NormalMap"); normalScale = P("_NormalScale");
            vertexInfluence = P("_VrtxColorInfuence"); vertexBlend = P("_Enable_Vertex_Alpha_Blend");
            favourVertexAlpha = P("_Favour_Vertex_Over_BaseMap_Alpha");

            useDecals = P("_Use_Decals"); decalLayer = P("_Decal_Layer"); decalNormal = P("_Decal_Normal");
            decalsTilingOffset = P("_Decals_Tiling_and_Offset"); decalBlend = P("_Decal_Blend_Strength");
            decalAlbedoMultiply = P("_Decal_Albedo_Multiply"); multiplyDecalAlbedo = P("_Multiply_Decal_Albedo");
            decalAlbedoTint = P("_Decal_Albedo_Tint"); decalNormalStrength = P("_Decal_Normal_Strength");
            decalSmoothness = P("_Decal_Smoothness"); decalSmoothnessAmount = P("_Decal_Smoothness_Amount");
            decalMetalness = P("_Decal_Metalness"); decalMetalnessAmount = P("_Decal_Metalness_Amount");
            metalAoSubtractsDecals = P("_Metal_AO_Subtracts_Decals"); decalMetalSubtractMin = P("_Decal_Metal_Subtract_Min");
            decalMetalSubtractMax = P("_Decal_Metal_Subtract_Max"); decalAoSubtractMin = P("_Decal_AO_Subtract_Min");
            decalAoSubtractMax = P("_Decal_AO_Subtract_Max"); decalLevelsMin = P("_Decal_Levels_Min");
            decalLevelsMax = P("_Decal_Levels_Max"); decalLevelsStrength = P("_Decal_Levels_Strength");
            enableProjectionOffset = P("_Enable_Decal_Projection_Offset"); projectionOffsetAmount = P("_Projection_Offset_Amount");
            vertexAlphaFadesDecals = P("_Vertex_Alpha_Fades_Decals");

            dirtTexture = P("_Dirt_Texture"); useDirt = P("_Use_Dirt_Overlay"); dirtStrength = P("_Dirt_Strength");
            dirtScaling = P("_Dirt_Scaling"); dirtContrast = P("_Dirt_Contrast");
            dirtAsRoughness = P("_Use_Dirt_Overlay_as_Roughness"); dirtRoughnessStrength = P("_Dirt_Roughness_Strength");
            dirtRoughnessContrast = P("_Dirt_Roughness_Contrast");

            detailMap = P("_DetailMap"); detailNormalScale = P("_DetailNormalScale"); switchUv0Uv2 = P("_Switch_UV0_and_UV2");
            emissiveMap = P("_EmissiveColorMap"); emissiveColor = P("_EmissiveColorLDR"); emissiveIntensity = P("_EmissiveIntensity");

            specOcclusionMultiplier = P("_Specular_Occlusion_Multiplier");
            specOcclusionContrast = P("_Specular_Occlusion_Contraster");
            specOcclusionReduction = P("_Specular_Occlusion_Reduce");
            specOcclusionMaxClamp = P("_Specular_Occlusion_Max_Clamp");
            monoShSmoothnessReduction = P("_MonoSH_Smoothness_Reduction");
            monoShNormalStrength = P("_MonoSH_Normal_Strength");
            monoShEmissionStrength = P("_MonoSH_Emission_Strength");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope(Title(), (uint)foldoutBit, materialEditor))
            {
                if (!header.expanded) return;
                switch (section)
                {
                    case Section.Surface: DrawSurface(); break;
                    case Section.Decals: DrawDecals(); break;
                    case Section.Dirt: DrawDirt(); break;
                    case Section.Detail: DrawDetail(); break;
                    case Section.Emissive: DrawEmissive(); break;
                    case Section.AdvancedLighting: DrawAdvancedLighting(); break;
                }
            }
        }

        private MaterialProperty P(string name) => FindProperty(name, false);
        private void Draw(MaterialProperty property, string label)
        {
            if (property != null) materialEditor.ShaderProperty(property, new GUIContent(label));
        }
        private void Texture(MaterialProperty texture, string label, MaterialProperty extra = null)
        {
            if (texture != null) materialEditor.TexturePropertySingleLine(new GUIContent(label), texture, extra);
        }
        private void MinMax(MaterialProperty min, MaterialProperty max, string label)
        {
            if (min != null && max != null) materialEditor.MinMaxShaderProperty(min, max, 0f, 1f, new GUIContent(label));
        }

        private string Title()
        {
            switch (section)
            {
                case Section.Surface: return "Surface Inputs";
                case Section.Decals: return "Decal Projection";
                case Section.Dirt: return "Dirt Overlay";
                case Section.Detail: return "Detail Inputs";
                case Section.Emissive: return "Emissive Inputs";
                default: return "Advanced Lighting";
            }
        }

        private void DrawSurface()
        {
            Texture(baseMap, "Base Map", baseColor);
            Draw(tilingOffset, "Tiling and Offset"); Draw(albedoMultiply, "Albedo Multiply"); Draw(finalAlbedoTint, "Final Albedo Tint");
            Draw(whiteBalance, "White Balance"); Draw(whiteBoost, "White Boost"); Draw(saturation, "Saturation");
            Draw(contrast, "Contrast"); Draw(tint, "Tint"); MinMax(alphaRemapMin, alphaRemapMax, "Alpha Remapping");
            Draw(alphaClipThreshold, "Alpha Clip Threshold");
            EditorGUILayout.Space(); Texture(maskMap, "Mask Map");
            MinMax(metallicMin, metallicMax, "Metallic Remapping"); MinMax(smoothnessMin, smoothnessMax, "Smoothness Remapping");
            MinMax(aoMin, aoMax, "AO Remapping");
            EditorGUILayout.Space(); Texture(normalMap, "Normal Map", normalScale);
            Draw(vertexInfluence, "Vertex Color Influence"); Draw(vertexBlend, "Enable Vertex Alpha Blend");
            Draw(favourVertexAlpha, "Favour Vertex Over Base Alpha");
        }

        private void DrawDecals()
        {
            Draw(useDecals, "Use Decals"); Texture(decalLayer, "Decal Layer"); Texture(decalNormal, "Decal Normal");
            Draw(decalsTilingOffset, "Tiling and Offset"); Draw(decalBlend, "Blend Strength");
            Draw(multiplyDecalAlbedo, "Multiply Albedo"); Draw(decalAlbedoMultiply, "Albedo Multiply"); Draw(decalAlbedoTint, "Albedo Tint");
            Draw(decalNormalStrength, "Normal Strength"); Draw(decalSmoothness, "Smoothness"); Draw(decalSmoothnessAmount, "Smoothness Amount");
            Draw(decalMetalness, "Metalness"); Draw(decalMetalnessAmount, "Metalness Amount"); Draw(metalAoSubtractsDecals, "Metal/AO Subtracts Decals");
            MinMax(decalMetalSubtractMin, decalMetalSubtractMax, "Metal Subtraction"); MinMax(decalAoSubtractMin, decalAoSubtractMax, "AO Subtraction");
            MinMax(decalLevelsMin, decalLevelsMax, "Levels"); Draw(decalLevelsStrength, "Levels Strength");
            Draw(enableProjectionOffset, "Enable Projection Offset"); Draw(projectionOffsetAmount, "Projection Offset Amount");
            Draw(vertexAlphaFadesDecals, "Vertex Alpha Fades Decals");
        }

        private void DrawDirt()
        {
            Texture(dirtTexture, "Dirt Texture"); Draw(useDirt, "Use Dirt Overlay"); Draw(dirtStrength, "Strength");
            Draw(dirtScaling, "Scaling"); Draw(dirtContrast, "Contrast"); Draw(dirtAsRoughness, "Use as Roughness");
            Draw(dirtRoughnessStrength, "Roughness Strength"); Draw(dirtRoughnessContrast, "Roughness Contrast");
        }

        private void DrawDetail()
        {
            Texture(detailMap, "Detail Normal"); Draw(detailNormalScale, "Normal Scale"); Draw(switchUv0Uv2, "Switch UV0 and UV2");
        }

        private void DrawEmissive()
        {
            Texture(emissiveMap, "Emissive Map", emissiveColor); Draw(emissiveIntensity, "Intensity");
        }

        private void DrawAdvancedLighting()
        {
            EditorGUILayout.LabelField("Specular Occlusion", EditorStyles.boldLabel);
            Draw(specOcclusionMultiplier, "Multiplier"); Draw(specOcclusionContrast, "Contrast");
            Draw(specOcclusionReduction, "Reduction"); Draw(specOcclusionMaxClamp, "Maximum Clamp");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("MonoSH", EditorStyles.boldLabel);
            Draw(monoShSmoothnessReduction, "Smoothness Reduction"); Draw(monoShNormalStrength, "Normal Strength");
            Draw(monoShEmissionStrength, "Emission Strength");
        }
    }
}

#endif
