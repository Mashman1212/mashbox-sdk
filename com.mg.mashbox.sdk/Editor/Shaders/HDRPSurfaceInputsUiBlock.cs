#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class HDRPSurfaceInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;

        MaterialProperty baseColor;
        MaterialProperty baseMap;
        MaterialProperty whiteBoost;
        MaterialProperty hueShift;
        MaterialProperty MaskMap;
        MaterialProperty normalMap;
        MaterialProperty normalStrength;
        MaterialProperty metallicRemapMin;
        MaterialProperty metallicRemapMax;
        
        MaterialProperty metallic;
        
        MaterialProperty smoothnessRemapMin; 
        MaterialProperty smoothnessRemapMax; 
        
        MaterialProperty smoothness; 
        
        MaterialProperty aoRemapMin; 
        MaterialProperty aoRemapMax; 
        MaterialProperty alphaMaskBlend;
        MaterialProperty alphaMaskInvert;
        private MaterialProperty cmmColor;

        private MaterialProperty alphaMaskMetalMult;
        
        //added
        private MaterialProperty aOBaseMapBlendMultiplier;
        private MaterialProperty AOBaseMapBlendContrast;
        
        public string BaseMapHelpText { get; set; } = " ";
        public string maskMapHelpText { get; set; } = " ";
        internal class Styles
        {

            public static GUIContent normalMapText = new GUIContent("Normal Map", "Specifies the Normal Map for this Material (BC7/BC5/DXT5(nm)) and controls its strength.");
            public static GUIContent maskMapSText = new GUIContent("Mask Map", "Specifies the Mask Map for this Material - Metallic (R), Ambient occlusion (G), Detail mask (B), Smoothness (A).");
            public static GUIContent baseColorText = new GUIContent("Base Map", "Specifies the base color (RGB) and opacity (A) of the Material.");
            public static GUIContent metallicRemappingText = new GUIContent("Metallic Remapping", "Controls a remap for the metallic channel in the Mask Map.");
            public static GUIContent smoothnessRemappingText = new GUIContent("Smoothness Remapping", "Controls a remap for the smoothness channel in the Mask Map.");
            public static GUIContent aoRemappingText = new GUIContent("AO Remapping", "Controls a remap for the ambient occlusion channel in the Mask Map.");
            //public static GUIContent detailNormalMapText = new GUIContent("Ambient Occlusion Remapping");
        }
            
        public HDRPSurfaceInputsUiBlock(ExpandableBit expandableBit, string BaseMapHelpText = "<b><color=#ff6b6b>(R)Red</color></b>, <b><color=#6be36b>(G)Green</color></b>, <b><color=#6ba8ff>(B)Blue</color></b>, <b><color=#cccccc>(A)Color Mult Mask</color></b>", string maskMapHelpText = "<b><color=#ff6b6b>(R)Metallic</color></b>, <b><color=#6be36b>(G)AO</color></b>, <b><color=#6ba8ff>(B)not used</color></b>, <b><color=#cccccc>(A)Smoothness</color></b>")
        {
            foldoutBit = expandableBit;
            this.BaseMapHelpText = BaseMapHelpText;
            this.maskMapHelpText = maskMapHelpText;
        }
        
        public override void LoadMaterialProperties()//
        {
            baseColor = FindProperty("_BaseColor");
            baseMap = FindProperty("_BaseColorMap");
            whiteBoost = FindProperty("_WhiteBoost");
            hueShift = FindProperty("_HueShift");
            MaskMap = FindProperty("_MaskMap");
            normalMap = FindProperty("_NormalMap");
            normalStrength = FindProperty("_NormalStrength");
            
            metallicRemapMin = FindProperty("_MetallicRemapMin");
            metallicRemapMax = FindProperty("_MetallicRemapMax");
            
            metallic = FindProperty("_Metallic");
            
            smoothnessRemapMin = FindProperty("_SmoothnessRemapMin");
            smoothnessRemapMax = FindProperty("_SmoothnessRemapMax");
            
            smoothness = FindProperty("_Smoothness");
            
            aoRemapMin = FindProperty("_AORemapMin");
            aoRemapMax = FindProperty("_AORemapMax");
            
            alphaMaskBlend = FindProperty("_AlphaMaskBlend");
            alphaMaskInvert = FindProperty("_AlphaMaskInvert");
            alphaMaskMetalMult = FindProperty("_AlphaMaskMetalMult");

            cmmColor = FindProperty("_CCMColor");
            
    
            aOBaseMapBlendMultiplier = FindProperty("_AOBaseMapBlendMultiplier");
            AOBaseMapBlendContrast = FindProperty("_AOBaseMapBlendContrast");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Surface Inputs", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    EditorStyles.helpBox.richText = true;
                    EditorGUILayout.HelpBox(BaseMapHelpText, MessageType.None);
                    materialEditor.TexturePropertySingleLine(Styles.baseColorText, baseMap, baseColor );
                    materialEditor.TextureScaleOffsetProperty(baseMap);

                    if (cmmColor != null)
                    {
                        materialEditor.ColorProperty(cmmColor,"CMM Color");
                    }

                    if (alphaMaskBlend != null)
                        materialEditor.RangeProperty(alphaMaskBlend, "CMM Blend");
                            
                    if(alphaMaskInvert != null)
                        materialEditor.RangeProperty(alphaMaskInvert,"CMM Invert");

                    if (alphaMaskMetalMult != null)
                    {
                        materialEditor.RangeProperty(alphaMaskMetalMult,"CMM Metal Mult");
                    }
                    
                    if(whiteBoost != null)
                        materialEditor.RangeProperty(whiteBoost,"White Boost");

                    if(hueShift != null)
                        materialEditor.RangeProperty(hueShift,"Hue Shift");
                    
                    if (MaskMap != null)
                    {
                        EditorGUILayout.Space(10);
                    
                        EditorGUILayout.HelpBox(maskMapHelpText, MessageType.None);
                        materialEditor.TexturePropertySingleLine(Styles.maskMapSText, MaskMap);

                    }
       
                    if(metallicRemapMin != null)
                        materialEditor.MinMaxShaderProperty(metallicRemapMin, metallicRemapMax, 0.0f, 1.0f, Styles.metallicRemappingText);
                    
                    if(smoothnessRemapMin != null)
                        materialEditor.MinMaxShaderProperty(smoothnessRemapMin, smoothnessRemapMax, 0.0f, 1.0f, Styles.smoothnessRemappingText);
                    
                    if(aoRemapMin != null)
                        materialEditor.MinMaxShaderProperty(aoRemapMin, aoRemapMax, 0.0f, 1.0f, Styles.aoRemappingText);
                    
                    if (smoothness != null)
                        materialEditor.RangeProperty(smoothness,"Smoothness");
                    
                    if (metallic != null)
                        materialEditor.RangeProperty(metallic,"Metallic");
                    
                    
                    if (AOBaseMapBlendContrast != null)
                        materialEditor.RangeProperty(AOBaseMapBlendContrast,"AO Contrast");
                    
                    
                    if (aOBaseMapBlendMultiplier != null)
                        materialEditor.RangeProperty(aOBaseMapBlendMultiplier,"AO Multiplier");
                    


                    if (normalMap != null)
                    {
                        EditorGUILayout.Space(10);
                    
                        materialEditor.TexturePropertySingleLine(Styles.normalMapText, normalMap, normalStrength);
                        materialEditor.TextureScaleOffsetProperty(normalMap);
                    }
                }
            }
        }
    }
}

#endif