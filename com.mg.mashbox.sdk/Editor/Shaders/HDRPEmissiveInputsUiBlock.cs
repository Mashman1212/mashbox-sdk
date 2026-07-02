#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class HDRPEmissiveInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;

        MaterialProperty emissiveColorMap;
        MaterialProperty emissiveColor;
        MaterialProperty emissiveIntensity;
        MaterialProperty emissiveExposureWeight;
        MaterialProperty emissiveUseUV1;
        MaterialProperty emissiveUseUV2;
        MaterialProperty emissiveUseUV3;
        public HDRPEmissiveInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        
        public override void LoadMaterialProperties()
        {
            emissiveColorMap = FindProperty("_EmissiveColorMap");
            emissiveColor = FindProperty("_EmissiveColor");
            emissiveIntensity = FindProperty("_EmissiveIntensity");
            emissiveExposureWeight = FindProperty("_EmissiveExposureWeight");
            emissiveUseUV1 = FindProperty("_EmissiveUseUV1");
            emissiveUseUV2 = FindProperty("_EmissiveUseUV2");
            emissiveUseUV3 = FindProperty("_EmissiveUseUV3");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Emissive Inputs", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Color"), emissiveColorMap, emissiveColor );
                    materialEditor.TextureScaleOffsetProperty(emissiveColorMap);

                    if(emissiveUseUV1 != null && emissiveUseUV2 != null && emissiveUseUV3 != null)
                        DrawUVSelector(emissiveUseUV1,emissiveUseUV2,emissiveUseUV3);
                    else
                    {
                        if(emissiveUseUV2 != null)
                            emissiveUseUV2.floatValue = EditorGUILayout.Toggle("Use UV2", emissiveUseUV2.floatValue == 1f) ? 1f : 0f;
                    }

                    
                    DrawEmissiveIntensity();
                    materialEditor.RangeProperty(emissiveExposureWeight,"Exposure Weight");
                }
            }
        }
        

        void DrawEmissiveIntensity()
        {
            if (emissiveIntensity == null)
                return;

#if UNITY_2023_1_OR_NEWER
            materialEditor.RangeProperty(emissiveIntensity, "Intensity");
#else
#pragma warning disable 0618
            materialEditor.RangeProperty("_EmissiveIntensity", "Intensity", 0.0f, 100.0f);
#pragma warning restore 0618
#endif
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