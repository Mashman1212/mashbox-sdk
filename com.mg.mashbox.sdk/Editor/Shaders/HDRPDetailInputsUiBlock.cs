#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class HDRPDetailInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;

        MaterialProperty detailMap;
        MaterialProperty detailAlbedoScale;
        MaterialProperty detailSmoothnessScale;
        MaterialProperty detailNormalScale;
        MaterialProperty detailUseUV2;
        MaterialProperty detailColorMap;
        MaterialProperty detailHueShift;
        MaterialProperty detailColor;
        MaterialProperty detailColorBlend;
        private MaterialProperty detailColorBlendMode;
        
        
        private MaterialProperty UseUV1;
        private MaterialProperty UseUV2;
        private MaterialProperty UseUV3;
        private MaterialProperty ColorUseUV1;
        private MaterialProperty ColorUseUV2;
        private MaterialProperty ColorUseUV3;
        
        public HDRPDetailInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        
        public override void LoadMaterialProperties()
        {
            detailMap = FindProperty("_DetailMap");
            detailAlbedoScale = FindProperty("_DetailAlbedoScale");
            detailSmoothnessScale = FindProperty("_DetailSmoothnessScale");
            detailNormalScale = FindProperty("_DetailNormalScale");
            detailHueShift = FindProperty("_DetailHueShift");
            detailUseUV2 = FindProperty("_DetailUseUV2");
            detailColorMap = FindProperty("_DetailColorMap");
            detailColor = FindProperty("_DetailColor");
            detailColorBlend = FindProperty("_DetailColorBlend");
            detailColorBlendMode = FindProperty("_DetailColorBlendMode");
            
            UseUV1 = FindProperty("_DetailUseUV1");
            UseUV2 = FindProperty("_DetailUseUV2");
            UseUV3 = FindProperty("_DetailUseUV3");
            
            ColorUseUV1 = FindProperty("_DetailColorUseUV1");
            ColorUseUV2 = FindProperty("_DetailColorUseUV2");
            ColorUseUV3 = FindProperty("_DetailColorUseUV3");
            
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Detail Inputs", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Detail"), detailMap);
                    materialEditor.TextureScaleOffsetProperty(detailMap);

                    if (UseUV1 != null && UseUV2 != null && UseUV3 != null)
                    {
                        DrawUVSelector(UseUV1,UseUV2,UseUV3);
                    }
                    
                    if (detailColorMap != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Color Overlay"), detailColorMap,detailColor);
                        materialEditor.TextureScaleOffsetProperty(detailColorMap);
                        
                        if (ColorUseUV1 != null && ColorUseUV2 != null && ColorUseUV3 != null)
                        {
                            DrawUVSelector(ColorUseUV1,ColorUseUV2,ColorUseUV3);
                        }
                        
                        materialEditor.RangeProperty(detailColorBlend,"Overlay Blend");

                        if (detailColorBlendMode != null)
                        {
                            materialEditor.RangeProperty(detailColorBlendMode,"Color Blend Mode");
                        }
                        
                    }
                    
                    if(detailHueShift != null)
                        materialEditor.RangeProperty(detailHueShift,"Hue Shift");
                    
                    if(detailUseUV2 != null && UseUV3 == null)
                        detailUseUV2.floatValue = EditorGUILayout.Toggle("Use UV2", detailUseUV2.floatValue == 1f) ? 1f : 0f;
                    
                    
                    if(detailAlbedoScale != null)
                        materialEditor.RangeProperty(detailAlbedoScale,"Albedo Scale");
                    
                    if(detailNormalScale != null)
                        materialEditor.RangeProperty(detailNormalScale,"Normal Scale");
                    
                    if(detailSmoothnessScale != null)
                        materialEditor.RangeProperty(detailSmoothnessScale,"Smoothness Scale");
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