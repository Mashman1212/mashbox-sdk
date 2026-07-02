#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class HDRPStickerInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;

        MaterialProperty stickerMap;
        MaterialProperty stickerColor;
        MaterialProperty stickerWhiteBoost;
        MaterialProperty stickerHueShift;
        MaterialProperty stickerBlend;
        MaterialProperty stickerRGBBlend;
        
        MaterialProperty stickerMetallic;
        MaterialProperty stickerSmoothness;//accidentally called this in shader.. its actually smoothness
        MaterialProperty stickerDamage;
        MaterialProperty stickerThickness;
        private MaterialProperty MMStickerMetalOverride;
        private MaterialProperty MMStickerSmoothnessOverride;
        private MaterialProperty stickerAlphaContrast;
        
        private MaterialProperty stickerEdge;
        MaterialProperty stickerWear;
        MaterialProperty stickerWearScale;
        MaterialProperty stickerMirrorAxis;
        private MaterialProperty UseUV1;
        private MaterialProperty UseUV2;
        private MaterialProperty UseUV3;
        public HDRPStickerInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        
        public override void LoadMaterialProperties()
        {
            stickerColor = FindProperty("_StickerColor");
            stickerMap = FindProperty("_StickerMap");
            stickerWhiteBoost = FindProperty("_StickerWhiteBoost");
            stickerHueShift = FindProperty("_StickerHueShift");
            stickerMetallic = FindProperty("_StickerMetallic");
            stickerSmoothness = FindProperty("_StickerSmoothness");
            stickerThickness = FindProperty("_StickerThickness");
            stickerEdge = FindProperty("_StickerEdge");
            stickerWear = FindProperty("_StickerWear");
            stickerWearScale = FindProperty("_StickerWearScale");
            UseUV1 = FindProperty("_StickerUseUV1");
            UseUV2 = FindProperty("_StickerUseUV2");
            UseUV3 = FindProperty("_StickerUseUV3");
            stickerBlend = FindProperty("_StickerBlend");
            stickerRGBBlend = FindProperty("_StickerRGBBlend");
            stickerAlphaContrast = FindProperty("_StickerAlphaContrast");
            MMStickerMetalOverride = FindProperty("_MMSticMetalOverride");
            MMStickerSmoothnessOverride = FindProperty("_MMStickerSmoothnessOverride");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Sticker Inputs", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Sticker Map"), stickerMap, stickerColor );
                    materialEditor.TextureScaleOffsetProperty(stickerMap);
                    
                    DrawUVSelector(UseUV1,UseUV2,UseUV3);
                    
                    if(stickerBlend != null)
                        materialEditor.RangeProperty(stickerBlend,"Blend");
                    
                    if(stickerRGBBlend != null)
                        materialEditor.RangeProperty(stickerRGBBlend,"RGB Blend");
                    
                    if(stickerHueShift != null)
                        materialEditor.RangeProperty(stickerHueShift,"Hue Shift");
                    
                    if(stickerWhiteBoost != null)
                        materialEditor.RangeProperty(stickerWhiteBoost,"White Boost");
                    
                    if(stickerSmoothness != null)
                        materialEditor.RangeProperty(stickerSmoothness,"Smoothness");
                    
                    if(stickerMetallic != null)
                        materialEditor.RangeProperty(stickerMetallic,"Metallic");
                    
                    if(MMStickerMetalOverride != null)
                        materialEditor.RangeProperty(MMStickerMetalOverride,"MM Metal Override");
                    
                    if(MMStickerSmoothnessOverride != null)
                        materialEditor.RangeProperty(MMStickerSmoothnessOverride,"MM Smoothness Override");
                    
                    if(stickerThickness != null)
                        materialEditor.RangeProperty(stickerThickness,"Thickness");

                    if(stickerAlphaContrast != null)
                        materialEditor.RangeProperty(stickerAlphaContrast," Alpha Contrast");
                    
                    if(stickerEdge != null)
                        materialEditor.RangeProperty(stickerEdge,"Edge");
                    
                    if(stickerWear != null)
                        materialEditor.ShaderProperty(stickerWear,"Wear");

                    if(stickerWearScale != null)
                        materialEditor.ShaderProperty(stickerWearScale,"Wear Scale");
                    
                    
                    
                }
            }
        }
        
        void DrawUVSelector(MaterialProperty useUV1, MaterialProperty useUV2,MaterialProperty useVU3)
        {
            int current = 0;

            if (useVU3 != null && useVU3.floatValue == 1f)
                current = 3;
            else if (useUV2 != null && useUV2.floatValue == 1f)
                current = 2;
            else if (useUV1 != null && useUV1.floatValue == 1f)
                current = 1;

            EditorGUI.BeginChangeCheck();

            current = EditorGUILayout.Popup("UV Channel", current, new[] { "UV0", "UV1", "UV2", "UV3" });

            if (EditorGUI.EndChangeCheck())
            {
                if (useUV1 != null) useUV1.floatValue = (current == 1) ? 1f : 0f;
                if (useUV2 != null) useUV2.floatValue = (current == 2) ? 1f : 0f;
                if (useVU3 != null) useVU3.floatValue = (current == 3) ? 1f : 0f;
            }
        }
    }
}

#endif