using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Shaders
{
    public static class ShaderEnforcer
    {
        public enum ShaderType
        {
            Vehicle,
            Clothing,
            Hair,
            Tire,
            Skin,
            Chain,
            Griptape,
            LitBasic,
            LitBasicAlphaClip,
            LitBasicTriplane,
            LitAdvanced,
            LitAdvancedMonoSH,
            EightBittPaintMaskAdvanced
        }

        
        private static readonly string[] MGVehiclePreservedProperties =
        {
            "_BaseColor",
            "_CCMColor",
            "_BaseColorMap",
            "_BaseColorMap_ST",
            "_MaskMap",
            "_MaskMap_ST",
            "_NormalMap",
            "_NormalMap_ST",
            "_NormalStrength",

            "_MetallicRemapMin",
            "_MetallicRemapMax",
            "_SmoothnessRemapMin",
            "_SmoothnessRemapMax",
            "_AORemapMin",
            "_AORemapMax",

            "_AlphaMaskBlend",
            "_AlphaMaskInvert",
            "_AlphaMaskMetalMult",

            "_WhiteBoost",
            "_HueShift",

            // --- STICKERS ---
            "_StickerMap",
            "_StickerMap_ST",
            "_StickerUseUV1",
            "_StickerUseUV2",
            "_StickerUseUV3",
            "_StickerColor",
            "_StickerBlend",
            "_StickerMetallic",
            "_StickerSmoothness",
            "_StickerRGBBlend",
            "_StickerWhiteBoost",
            "_StickerOffset",
            "_StickerThickness",
            "_StickerThickness",
            "_StickerHueShift",
            "_Thickness",
            "_StickerEdge",
            "_StickerAlphaContrast",
            "_MMSticMetalOverride",
            "_MMStickerSmoothnessOverride",

            // --- DETAIL NORMAL ---
            "_DetailMap",
            "_DetailMap_ST",
            "_DetailUseUV1",
            "_DetailUseUV2",
            "_DetailUseUV3",
            "_DetailNormalScale",

            // --- DETAIL COLOR ---
            "_DetailColorMap",
            "_DetailColorMap_ST",
            "_DetailColor",
            "_DetailHueShift",
            "_DetailColorUseUV1",
            "_DetailColorUseUV2",
            "_DetailColorUseUV3",
            "_DetailColorBlend",
            "_DetailColorBlendMode",

            // --- COAT / OIL ---
            "_CoatMaskMap",
            "_CoatMaskStrength",
            "_OilSlickStrength",
            "_OilSlickAlphaMaskMulti",
            "_CoatCMMInvert",
            "_FilmThickness",
            "_FilmBlend",
            "_OilSlickGradient",
            "_OilSlickColorMult",

            // --- EMISSIVE ---
            "_EmissiveColorMap",
            "_EmissiveColorMap_ST",
            "_EmissiveUseUV2",
            "_EmissiveUseUV1",
            "_EmissiveUseUV2",
            "_EmissiveUseUV3",
            "_EmissiveColor",
            "_EmissiveIntensity",
            "_EmissiveExposureWeight"
        };
        
        private static readonly string[] MGClothingPreservedProperties =
        {
            "_BaseColor",
            "_BaseColorMap",
            "_MaskMap",
            "_NormalMap",
            "_NormalStrength",
            
            "_HueShift",
            "_StickerHueShift",
            "_DetailHueShift",

            "_SmoothnessRemapMin",
            "_SmoothnessRemapMax",
            "_MetallicRemapMin",
            "_MetallicRemapMax",
            "_AORemapMin",
            "_AORemapMax",

            "_WhiteBoost",

            "_AlphaMaskBlend",
            "_AlphaMaskInvert",

            "_StickerMap",
            "_StickerColor",
            "_StickerWhiteBoost",
            "_StickerHueShift",
            "_StickerBlend",
            "_StickerUseUV1",
            "_StickerUseUV2",
            "_StickerUseUV3",
            
            "_EmissiveColorMap",
            "_EmissiveColor",
            "_EmissiveUseUV2",
            "_EmissiveUseUV1",
            "_EmissiveUseUV2",
            "_EmissiveUseUV3",
            "_EmissiveIntensity",
            "_EmissiveExposureWeight",

            "_DetailMap",
            "_DetailUseUV1",
            "_DetailUseUV2",
            "_DetailUseUV3",
            "_DetailAlbedoScale",
            "_DetailSmoothnessScale",
            "_DetailNormalScale"
        };

        private static readonly string[] MGLitBasicPreservedProperties =
        {
            "_BaseColor",
            "_BaseMap",
            "_BaseColorMap",
            "_MaskMap",
            "_NormalMap",
            "_NormalStrength",
            "_TexWorldScale",
            "_AlphaClipThreshold",
            "_AlphaClipThresholdShadow",

            "_HueShift",
            "_DetailHueShift",
            "_WhiteBalance",
            "_Saturation",
            "_Darken",
            "_Contrast",
            "_Lighten",
            "_Tint",

            "_SmoothnessRemapMin",
            "_SmoothnessRemapMax",
            "_MetallicRemapMin",
            "_MetallicRemapMax",
            "_AORemapMin",
            "_AORemapMax",

            "_WhiteBoost",

            "_AlphaMaskBlend",
            "_AlphaMaskInvert",

            "_DecalMap",
            "_DecalColor",
            "_DecalWhiteBoost",
            "_DecalHueShift",
            "_DecalBlend",
            "_DecalWorldScale",
            "_DecalUseUV1",
            "_DecalUseUV2",
            "_DecalUseUV3",

            "_EmissiveColorMap",
            "_EmissiveColor",
            "_EmissiveUseUV1",
            "_EmissiveUseUV2",
            "_EmissiveUseUV3",
            "_EmissiveIntensity",
            "_EmissiveExposureWeight",

            "_DetailMap",
            "_DetailWorldScale",
            "_DetailUseUV1",
            "_DetailUseUV2",
            "_DetailUseUV3",
            "_DetailAlbedoScale",
            "_DetailSmoothnessScale",
            "_DetailNormalScale"
        };

        private static readonly string[] MGLitAdvancedPreservedProperties =
        {
            "_BaseColorMap", "_BaseColor", "_Tiling_and_Offset", "_Albedo_Multiply", "_Final_Albedo_Tint",
            "_White_Balance", "_WhiteBoost", "_Saturation", "_Contrast", "_Tint",
            "_Alpha_Remap_Min", "_Alpha_Remap_Max", "_Alpha_Clip_Threshold",
            "_MaskMap", "_MetallicRemapMin", "_MetallicRemapMax", "_SmoothnessRemapMin", "_SmoothnessRemapMax",
            "_AORemapMin", "_AORemapMax", "_NormalMap", "_NormalScale",
            "_VrtxColorInfuence", "_Enable_Vertex_Alpha_Blend", "_Favour_Vertex_Over_BaseMap_Alpha",

            "_Use_Decals", "_Decal_Layer", "_Decal_Normal", "_Decals_Tiling_and_Offset", "_Decal_Blend_Strength",
            "_Decal_Albedo_Multiply", "_Multiply_Decal_Albedo", "_Decal_Albedo_Tint", "_Decal_Normal_Strength",
            "_Decal_Smoothness", "_Decal_Smoothness_Amount", "_Decal_Metalness", "_Decal_Metalness_Amount",
            "_Metal_AO_Subtracts_Decals", "_Decal_Metal_Subtract_Min", "_Decal_Metal_Subtract_Max",
            "_Decal_AO_Subtract_Min", "_Decal_AO_Subtract_Max", "_Decal_Levels_Min", "_Decal_Levels_Max",
            "_Decal_Levels_Strength", "_Enable_Decal_Projection_Offset", "_Projection_Offset_Amount",
            "_Vertex_Alpha_Fades_Decals",

            "_Dirt_Texture", "_Use_Dirt_Overlay", "_Dirt_Strength", "_Dirt_Scaling", "_Dirt_Contrast",
            "_Use_Dirt_Overlay_as_Roughness", "_Dirt_Roughness_Strength", "_Dirt_Roughness_Contrast",
            "_DetailMap", "_DetailNormalScale", "_Switch_UV0_and_UV2",
            "_EmissiveColorMap", "_EmissiveColorLDR", "_EmissiveIntensity",

            "_Specular_Occlusion_Multiplier", "_Specular_Occlusion_Contraster", "_Specular_Occlusion_Reduce",
            "_Specular_Occlusion_Max_Clamp", "_MonoSH_Smoothness_Reduction", "_MonoSH_Normal_Strength",
            "_MonoSH_Emission_Strength"
        };

        private static readonly string[] EightBittPaintMaskAdvancedPreservedProperties =
        {
            "_Albedo", "_Contrast", "_Saturation", "_Tint", "_WhiteBalance", "_Lighten",
            "_GrungeRGBA", "_Scale", "_Offset",
            "_R_Color", "_R", "_R_contrast", "_G_Color", "_G", "_G_contrast",
            "_B_Color", "_B", "_B_contrast", "_A_Color", "_A", "_A_contrast",
            "_Mask", "_Spec_Min", "_Spec_Max", "_Rough_Min", "_Rough_Max", "_AO_Min", "_AO_Max",
            "_Normal", "_NormalStrength",
            "_Paint_Mask", "_PaintColor", "_Power", "_Paint_Contrast",
            "_Texture2D", "_Noise_Scale", "_Noise_Offset", "_Noise_Opacity", "_dirth_smoothness"
        };

        
        private static readonly string[] MGChainPreservedProperties =
        {
            "_BaseColor",
            "_BaseColorMap",
            "_BaseColor",
            "_NormalMap",
            "_NormalStrength",
            "_Metallic",
            "_Smoothness",
            "_EmissiveColor",
            "_AlphaClipThreshold",
            "_EmissiveIntensity",
            "_EmissiveExposureWeight"
        };
        
        private static readonly string[] MGTirePreservedProperties =
        {
            "_TreadTiling",
            "_UseTessellation",
            "_TessellationHeight",
            "_TreadWear",
            "_TreadWearColor",
            "_BaseColor",
            "_WhiteBoost",
            "_BaseColorMap",
            "_MaskMap",
            "_NormalMap",
            "_NormalStrength",
            "_MetallicRemapMin",
            "_MetallicRemapMax",
            "_SmoothnessRemapMin",
            "_SmoothnessRemapMax",
            "_AORemapMin",
            "_AORemapMax",
            "_AOBaseMapBlendMultiplier",
            "_AOBaseMapBlendContrast",
            "_SidewallColor",
            "_SidewallMaskMap",
            "_SidewallMaskBlend",
            "_SidewallMetallic",
            "_SidewallSmoothness",
            "_StickerColor",
            "_StickerMap",
            "_StickerWhiteBoost",
            "_StickerMetallic",
            "_StickerSmoothness",
            "_StickerThickness",
            "_StickerWear",
            "_StickerWearScale",
            "_StickerOffset",
            "_StickerBlend",
            "_StickerRGBBlend",
            "_StickerUseUV1",
            "_StickerUseUV2",
            "_StickerUseUV3",
            "_MudColor",
            "_MudBaseMap",
            "_MudNormalMap",
            "_MudNormalStrength",
            "_MudMetallic",
            "_MudSmoothness",
            "_MudBuildup",
            "_MudBuildupInvert",
            "_MudBuildColorMultRate",
            "_MudMaskEdge1",
            "_MudMaskEdge2",
            "_MudGrungeScale",
            "_MudBuildupMeshMaskMin",
            "_MudBuildupMeshMaskMax",
            "_TessellationFactor",
            "_TessellationFactorMaxDistance",
            "_EmissiveColor",
            "_EmissiveColorMap",
            "_EmissiveExposureWeight",
            "_EmissiveIntensity",
            "_EmissiveUseUV1",
            "_EmissiveUseUV2",
            "_EmissiveUseUV3",
            
            "_PatternOverlayMap",
            "_PatternOverlayColor"
        };
        
        private static readonly string[] GriptapePreservedProperties =
        {
            "_WearAlphaCutMap",
            "_WearAlphaCutMap",
            "_EdgeWearColor",
            "_EdgeWearHighlight",
            "_EdgeWear",
            "_DirtColor",
            "_GripTapeDirt",
            "_BaseColor",
            "_BaseColorMap",
            "_MaskMap",
            "_NormalMap",
            "_NormalStrength",
            "_SmoothnessRemap",
            "_MetallicRemap",
            "_AORemap"
        };



        private static readonly Shader VehicleShader = Shader.Find("MGShaders/HDRP/Lit/MG_Vehicle");
        private static Material _vehicleTemplateMat;
        private static Material VehicleTemplateMat
        {
            get
            {
                if (!_vehicleTemplateMat)
                {
                    _vehicleTemplateMat = Resources.Load<Material>("MG_Vehicle_Template");
                }

                return _vehicleTemplateMat;
            }
        }

        
        private static readonly Shader ClothingShader = Shader.Find("MGShaders/HDRP/Lit/MG_Clothing");
        private static Material _clothingTemplateMat;
        private static Material ClothingTemplateMat
        {
            get
            {
                if (!_clothingTemplateMat)
                {
                    _clothingTemplateMat = Resources.Load<Material>("MG_Clothing_Template");
                }

                return _clothingTemplateMat;
            }
        }

        private static readonly Shader LitBasicShader = Shader.Find("MGShaders/HDRP/Lit/MG_Lit_Basic");
        private static Material _litBasicTemplateMat;
        private static Material LitBasicTemplateMat
        {
            get
            {
                if (!_litBasicTemplateMat)
                {
                    _litBasicTemplateMat = Resources.Load<Material>("MG_Lit_Basic_Template");
                }

                return _litBasicTemplateMat;
            }
        }

        private static readonly Shader LitBasicAlphaClipShader = Shader.Find("MGShaders/HDRP/Lit/MG_Lit_Basic_AlphaClip");
        private static Material _litBasicAlphaClipTemplateMat;
        private static Material LitBasicAlphaClipTemplateMat
        {
            get
            {
                if (!_litBasicAlphaClipTemplateMat)
                {
                    _litBasicAlphaClipTemplateMat = Resources.Load<Material>("MG_Lit_Basic_AlphaClip_Template");
                }

                return _litBasicAlphaClipTemplateMat;
            }
        }

        private static readonly Shader LitBasicTriplaneShader = Shader.Find("MGShaders/HDRP/Lit/MG_Lit_Basic_Triplane");
        private static Material _litBasicTriplaneTemplateMat;
        private static Material LitBasicTriplaneTemplateMat
        {
            get
            {
                if (!_litBasicTriplaneTemplateMat)
                {
                    _litBasicTriplaneTemplateMat = Resources.Load<Material>("MG_Lit_Basic_Triplane_Template");
                }

                return _litBasicTriplaneTemplateMat;
            }
        }

        private static readonly Shader LitAdvancedShader = Shader.Find("MGShaders/HDRP/Lit/MG_Lit_Advanced");
        private static Material _litAdvancedTemplateMat;
        private static Material LitAdvancedTemplateMat
        {
            get
            {
                if (!_litAdvancedTemplateMat)
                    _litAdvancedTemplateMat = Resources.Load<Material>("MG_Lit_Advanced_Template");
                return _litAdvancedTemplateMat;
            }
        }

        private static readonly Shader LitAdvancedMonoSHShader = Shader.Find("MGShaders/HDRP/Lit/MG_Lit_Advanced_MonoSH");
        private static Material _litAdvancedMonoSHTemplateMat;
        private static Material LitAdvancedMonoSHTemplateMat
        {
            get
            {
                if (!_litAdvancedMonoSHTemplateMat)
                    _litAdvancedMonoSHTemplateMat = Resources.Load<Material>("MG_Lit_Advanced_MonoSH_Template");
                return _litAdvancedMonoSHTemplateMat;
            }
        }

        private static readonly Shader EightBittPaintMaskAdvancedShader =
            Shader.Find("8Bitt/HDRP/Lit/8Bitt_PaintMask_Advanced");
        private static Material _eightBittPaintMaskAdvancedTemplateMat;
        private static Material EightBittPaintMaskAdvancedTemplateMat
        {
            get
            {
                if (!_eightBittPaintMaskAdvancedTemplateMat)
                    _eightBittPaintMaskAdvancedTemplateMat = Resources.Load<Material>("8Bitt_PaintMask_Advanced_Template");
                return _eightBittPaintMaskAdvancedTemplateMat;
            }
        }
        
        
        private static readonly Shader TireShader = Shader.Find("MGShaders/HDRP/Lit/MG_Tire");
        private static Material _tireTemplateMat;
        private static Material TireTemplateMat
        {
            get
            {
                if (!_tireTemplateMat)
                {
                    _tireTemplateMat = Resources.Load<Material>("MG_Tire_Template");
                }

                return _tireTemplateMat;
            }
        }
        
        
        private static readonly Shader ChainShader = Shader.Find("MGShaders/HDRP/Lit/MG_Chain");
        private static Material _chainTemplateMat;
        private static Material ChainTemplateMat
        {
            get
            {
                if (!_chainTemplateMat)
                {
                    _chainTemplateMat = Resources.Load<Material>("MG_Chain_Template");
                }

                return _chainTemplateMat;
            }
        }
        private static readonly Shader GriptapeShader = Shader.Find("MGShaders/HDRP/Lit/Griptape");
        private static Material _gripTapeTemplateMat;
        private static Material GriptapeTemplateMat
        {
            get
            {
                if (!_gripTapeTemplateMat)
                {
                    _gripTapeTemplateMat = Resources.Load<Material>("MG_Griptape_Template");
                }

                return _gripTapeTemplateMat;
            }
        }

        
        public static void EnforceShaderOnChildren(ShaderType shaderType, GameObject go)
        {
            if (!go) return;

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;

                    switch (shaderType)
                    {
                        case ShaderType.Vehicle:
                            EnforceVehicleShader(materials[i]);
                            break;

                        case ShaderType.Clothing:
                            EnforceClothingShader(materials[i]);
                            break;
                        case ShaderType.LitBasic:
                            EnforceLitBasicShader(materials[i]);
                            break;
                        case ShaderType.LitBasicAlphaClip:
                            EnforceLitBasicAlphaClipShader(materials[i]);
                            break;
                        case ShaderType.LitBasicTriplane:
                            EnforceLitBasicTriplaneShader(materials[i]);
                            break;
                        case ShaderType.LitAdvanced:
                            EnforceLitAdvancedShader(materials[i]);
                            break;
                        case ShaderType.LitAdvancedMonoSH:
                            EnforceLitAdvancedMonoSHShader(materials[i]);
                            break;
                        case ShaderType.EightBittPaintMaskAdvanced:
                            EnforceEightBittPaintMaskAdvancedShader(materials[i]);
                            break;
                        case ShaderType.Tire:
                            EnforceTireShader(materials[i]);
                            break;
                        case ShaderType.Chain:
                            EnforceChainShader(materials[i]);
                            break;
                        case ShaderType.Griptape:
                            EnforceGriptapeShader(materials[i]);
                            break;
                    }
                }

                renderer.sharedMaterials = materials;
            }
        }

        // ---------------------------------------------------------
        public static void EnforceVehicleShader(Material mat)
        {
            if (mat == VehicleTemplateMat)
                return;

            // ✅ CACHE ORIGINAL STATE
            Texture originalDetail = null;
            if (mat.HasProperty("_DetailMap"))
                originalDetail = mat.GetTexture("_DetailMap");

            if (!PrepareMaterialForShaderEnforcement(mat, VehicleShader))
                return;

            ApplyTemplateWithPreserve(mat, VehicleTemplateMat, MGVehiclePreservedProperties);

            // ✅ FIX DETAIL SCALES BASED ON ORIGINAL INTENT
            if (originalDetail == null)
            {
                if (mat.HasProperty("_DetailNormalScale"))
                    mat.SetFloat("_DetailNormalScale", 0f);

                if (mat.HasProperty("_DetailSmoothnessScale"))
                    mat.SetFloat("_DetailSmoothnessScale", 0f);

                // only if vehicle shader actually uses this
                if (mat.HasProperty("_DetailAlbedoScale"))
                    mat.SetFloat("_DetailAlbedoScale", 0f);
            }
            
            mat.shaderKeywords = VehicleTemplateMat.shaderKeywords;
        }
        public static void EnforceClothingShader(Material mat)
        {
            if (mat == ClothingTemplateMat)
                return;

            // ✅ CACHE BEFORE SHADER CHANGE
            Texture originalDetail = null;
            if (mat.HasProperty("_DetailMap"))
                originalDetail = mat.GetTexture("_DetailMap");

            if (!PrepareMaterialForShaderEnforcement(mat, ClothingShader))
                return;

            ApplyTemplateWithPreserve(mat, ClothingTemplateMat, MGClothingPreservedProperties);

            // ✅ USE ORIGINAL STATE (not current)
            if (originalDetail == null || originalDetail.name == "DefaultNormal" || originalDetail.name.Contains("DefaultTexture2D"))
            {
                mat.SetFloat("_DetailNormalScale", 0.0f);
                mat.SetFloat("_DetailSmoothnessScale", 0.0f);
                mat.SetFloat("_DetailAlbedoScale", 0.0f);
            }
            
            mat.shaderKeywords = ClothingTemplateMat.shaderKeywords;
        }

        public static void EnforceLitBasicShader(Material mat)
        {
            if (mat == LitBasicTemplateMat)
                return;

            Texture originalDetail = null;
            if (mat != null && mat.HasProperty("_DetailMap"))
                originalDetail = mat.GetTexture("_DetailMap");

            if (!PrepareMaterialForShaderEnforcement(mat, LitBasicShader))
                return;

            ApplyTemplateWithPreserve(mat, LitBasicTemplateMat, MGLitBasicPreservedProperties);

            if (originalDetail == null || originalDetail.name == "DefaultNormal" || originalDetail.name.Contains("DefaultTexture2D"))
            {
                if (mat.HasProperty("_DetailNormalScale"))
                    mat.SetFloat("_DetailNormalScale", 0.0f);

                if (mat.HasProperty("_DetailSmoothnessScale"))
                    mat.SetFloat("_DetailSmoothnessScale", 0.0f);

                if (mat.HasProperty("_DetailAlbedoScale"))
                    mat.SetFloat("_DetailAlbedoScale", 0.0f);
            }

            if (LitBasicTemplateMat != null)
                mat.shaderKeywords = LitBasicTemplateMat.shaderKeywords;

            EnforceDecalReception(mat);
            EnforceSSRReception(mat);
        }

        public static void EnforceLitBasicAlphaClipShader(Material mat)
        {
            if (mat == LitBasicAlphaClipTemplateMat)
                return;

            Texture originalDetail = null;
            if (mat != null && mat.HasProperty("_DetailMap"))
                originalDetail = mat.GetTexture("_DetailMap");

            if (!PrepareMaterialForShaderEnforcement(mat, LitBasicAlphaClipShader))
                return;

            ApplyTemplateWithPreserve(mat, LitBasicAlphaClipTemplateMat, MGLitBasicPreservedProperties);

            if (originalDetail == null || originalDetail.name == "DefaultNormal" || originalDetail.name.Contains("DefaultTexture2D"))
            {
                if (mat.HasProperty("_DetailNormalScale"))
                    mat.SetFloat("_DetailNormalScale", 0.0f);

                if (mat.HasProperty("_DetailSmoothnessScale"))
                    mat.SetFloat("_DetailSmoothnessScale", 0.0f);

                if (mat.HasProperty("_DetailAlbedoScale"))
                    mat.SetFloat("_DetailAlbedoScale", 0.0f);
            }

            if (LitBasicAlphaClipTemplateMat != null)
                mat.shaderKeywords = LitBasicAlphaClipTemplateMat.shaderKeywords;

            EnforceDecalReception(mat);
            EnforceSSRReception(mat);

            // Alpha clipping is an invariant of this shader variant. Keep the
            // thresholds user-authored, but never allow template enforcement to
            // turn the alpha-test render state back off.
            if (mat.HasProperty("_AlphaCutoffEnable"))
                mat.SetFloat("_AlphaCutoffEnable", 1.0f);

            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetOverrideTag("RenderType", "TransparentCutout");
        }

        public static void EnforceLitBasicTriplaneShader(Material mat)
        {
            if (mat == LitBasicTriplaneTemplateMat)
                return;

            Texture originalDetail = null;
            if (mat != null && mat.HasProperty("_DetailMap"))
                originalDetail = mat.GetTexture("_DetailMap");

            bool hasOriginalTexWorldScale = mat != null && mat.HasProperty("_TexWorldScale");
            float originalTexWorldScale = hasOriginalTexWorldScale
                ? mat.GetFloat("_TexWorldScale")
                : 1.0f;

            if (!PrepareMaterialForShaderEnforcement(mat, LitBasicTriplaneShader))
                return;

            ApplyTemplateWithPreserve(mat, LitBasicTriplaneTemplateMat, MGLitBasicPreservedProperties);

            if (hasOriginalTexWorldScale && mat.HasProperty("_TexWorldScale"))
                mat.SetFloat("_TexWorldScale", originalTexWorldScale);

            if (originalDetail == null || originalDetail.name == "DefaultNormal" || originalDetail.name.Contains("DefaultTexture2D"))
            {
                if (mat.HasProperty("_DetailNormalScale"))
                    mat.SetFloat("_DetailNormalScale", 0.0f);

                if (mat.HasProperty("_DetailSmoothnessScale"))
                    mat.SetFloat("_DetailSmoothnessScale", 0.0f);

                if (mat.HasProperty("_DetailAlbedoScale"))
                    mat.SetFloat("_DetailAlbedoScale", 0.0f);
            }

            if (LitBasicTriplaneTemplateMat != null)
                mat.shaderKeywords = LitBasicTriplaneTemplateMat.shaderKeywords;

            EnforceDecalReception(mat);
            EnforceSSRReception(mat);
        }

        public static void EnforceLitAdvancedShader(Material mat)
        {
            EnforceAdvancedShader(mat, LitAdvancedShader, LitAdvancedTemplateMat);
        }

        public static void EnforceLitAdvancedMonoSHShader(Material mat)
        {
            EnforceAdvancedShader(mat, LitAdvancedMonoSHShader, LitAdvancedMonoSHTemplateMat);
        }

        public static void EnforceEightBittPaintMaskAdvancedShader(Material mat)
        {
            if (mat == EightBittPaintMaskAdvancedTemplateMat)
                return;

            if (!PrepareMaterialForShaderEnforcement(mat, EightBittPaintMaskAdvancedShader))
                return;

            ApplyTemplateWithPreserve(mat, EightBittPaintMaskAdvancedTemplateMat,
                EightBittPaintMaskAdvancedPreservedProperties);

            if (EightBittPaintMaskAdvancedTemplateMat != null)
                mat.shaderKeywords = EightBittPaintMaskAdvancedTemplateMat.shaderKeywords;

            EnforceDecalReception(mat);
            EnforceSSRReception(mat);
        }

        private static void EnforceAdvancedShader(Material mat, Shader shader, Material template)
        {
            if (mat == template)
                return;

            if (!PrepareMaterialForShaderEnforcement(mat, shader))
                return;

            ApplyTemplateWithPreserve(mat, template, MGLitAdvancedPreservedProperties);

            if (template != null)
                mat.shaderKeywords = template.shaderKeywords;

            EnforceDecalReception(mat);
            EnforceSSRReception(mat);
        }

        private static void EnforceDecalReception(Material mat)
        {
            if (mat.HasProperty("_SupportDecals"))
                mat.SetFloat("_SupportDecals", 1.0f);

            mat.DisableKeyword("_DISABLE_DECALS");
        }

        private static void EnforceSSRReception(Material mat)
        {
            if (mat.HasProperty("_ReceivesSSR"))
                mat.SetFloat("_ReceivesSSR", 1.0f);

            mat.DisableKeyword("_DISABLE_SSR");
        }


        public static void EnforceTireShader(Material mat)
        {
            if (mat == TireTemplateMat)
                return;
            
            if (!PrepareMaterialForShaderEnforcement(mat, TireShader))
                return;
            ApplyTemplateWithPreserve(mat, TireTemplateMat, MGTirePreservedProperties);
            
            mat.shaderKeywords = TireTemplateMat.shaderKeywords;
        }

        public static void EnforceChainShader(Material mat)
        {
            if (mat == ChainTemplateMat)
                return;
            
            if (!PrepareMaterialForShaderEnforcement(mat, ChainShader))
                return;
            ApplyTemplateWithPreserve(mat, ChainTemplateMat, MGChainPreservedProperties);
            
            mat.shaderKeywords = ChainTemplateMat.shaderKeywords;
        }
        
        public static void EnforceGriptapeShader(Material mat)
        {
            if (mat == GriptapeTemplateMat)
                return;
            
            if (!PrepareMaterialForShaderEnforcement(mat, GriptapeShader))
                return;
            ApplyTemplateWithPreserve(mat, GriptapeTemplateMat, GriptapePreservedProperties);
            
            mat.shaderKeywords = GriptapeTemplateMat.shaderKeywords;
        }
        
        private struct TextureData
        {
            public Texture texture;
            public Vector2 scale;
            public Vector2 offset;
        }

        private static bool PrepareMaterialForShaderEnforcement(Material mat, Shader targetShader)
        {
            if (mat == null || targetShader == null)
                return false;

            if (mat.shader == targetShader)
                return true;

            #if UNITY_EDITOR
                if (IsMaterialVariant(mat))
                    return false;
            #endif

            mat.shader = targetShader;
            return true;
        }

        #if UNITY_EDITOR
        private static bool IsMaterialVariant(Material mat)
        {
            var serializedObject = new UnityEditor.SerializedObject(mat);
            var parentProperty = serializedObject.FindProperty("m_Parent");
            return parentProperty != null && parentProperty.objectReferenceValue != null;
        }
        #endif
        
        private static void ApplyTemplateWithPreserve(Material target, Material source, string[] preservedProperties)
        {
            if (target == null || source == null)
                return;
            

            // --- CACHE ---
            var cachedValues = new System.Collections.Generic.Dictionary<string, object>();

            foreach (var prop in preservedProperties)
            {
                if (!target.HasProperty(prop))
                    continue;

                int index = target.shader.FindPropertyIndex(prop);
                if (index < 0)
                    continue;

                var type = target.shader.GetPropertyType(index);

                switch (type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        cachedValues[prop] = target.GetColor(prop);
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        cachedValues[prop] = target.GetVector(prop);
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        cachedValues[prop] = target.GetFloat(prop);
                        break;

                    case ShaderPropertyType.Texture:
                        cachedValues[prop] = new TextureData
                        {
                            texture = target.GetTexture(prop),
                            scale = target.GetTextureScale(prop),
                            offset = target.GetTextureOffset(prop)
                        };
                        break;
                }
            }
            
            // --- COPY EVERYTHING ---
            target.CopyPropertiesFromMaterial(source);

            // --- RESTORE ---
            foreach (var kvp in cachedValues)
            {
                string prop = kvp.Key;
                object val = kvp.Value;

                switch (val)
                {
                    case Color c:
                        target.SetColor(prop, c);
                        break;

                    case Vector4 v:
                        target.SetVector(prop, v);
                        break;

                    case float f:
                        target.SetFloat(prop, f);
                        break;

                    case TextureData td:
                        if (td.texture != null)
                        {
                            target.SetTexture(prop, td.texture);
                            target.SetTextureScale(prop, td.scale);
                            target.SetTextureOffset(prop, td.offset);
                        }
                        break;
                    
                    case Texture t:
                        target.SetTexture(prop, t);
                        break;
                }
            }

            // Preserve keywords if needed
            // target.shaderKeywords = source.shaderKeywords;

            #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(target);
            #endif
        }
        
        private static void CopyMaterialSafe(Material target, Material source)
        {
            var shader = source.shader;
            int count = shader.GetPropertyCount();

            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);

                if (!target.HasProperty(name))
                    continue;

                var type = shader.GetPropertyType(i);

                switch (type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        target.SetColor(name, source.GetColor(name));
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        target.SetVector(name, source.GetVector(name));
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        target.SetFloat(name, source.GetFloat(name));
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        target.SetTexture(name, source.GetTexture(name));
                        target.SetTextureScale(name, source.GetTextureScale(name));
                        target.SetTextureOffset(name, source.GetTextureOffset(name));
                        break;
                }
            }

            // Copy keywords safely
            target.shaderKeywords = source.shaderKeywords;

            // Copy render queue & flags if needed
            target.renderQueue = source.renderQueue;
        }
    }
}
