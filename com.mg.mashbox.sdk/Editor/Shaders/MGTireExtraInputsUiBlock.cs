#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class MGTireExtraInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;
        
        private MaterialProperty treadTiling;
        private MaterialProperty sidewallColor;
        private MaterialProperty sidewallMaskMap;
        private MaterialProperty sidewallMaskBlend;
        private MaterialProperty sidewallMetallic;
        private MaterialProperty sidewallSmoothness;
        private MaterialProperty tessellationHeight;
        private MaterialProperty useTessellation;

        private MaterialProperty patternOverlayMap;
        private MaterialProperty patternOverlayColor;
        
        private MaterialProperty treadWear;
        private MaterialProperty treadWearColor;
        private MaterialProperty tessellationFactorMinDistance;
        private MaterialProperty tessellationFactorMaxDistance;
        
        private static GUIStyle bigBoldHeader;

        private string BaseMapHelpText;
        private string maskMapHelpText;     
 
        class Styles
        {
            public static GUIContent treadTilingText = new GUIContent("Tiling", "Horizontal tiling of the tread pattern");
			public static GUIContent tessellationHeightText = new GUIContent("Tessellation Height", "Controlled by Maskmap B Channel");
            public static GUIContent sidewallColor = new GUIContent("Color");
            public static GUIContent sidewallMaskBlend = new GUIContent("Mask Blend", "Horizontal tiling of the tread pattern");
			public static GUIContent useTessellationText = new GUIContent("Tread Tessellation", "Toggle Tessellation, off is recommended.");
        }

        public MGTireExtraInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        public override void LoadMaterialProperties()
        {
            treadTiling = FindProperty("_TreadTiling");
            sidewallColor = FindProperty("_SidewallColor");
            sidewallMaskMap = FindProperty("_SidewallMaskMap");
            sidewallMaskBlend = FindProperty("_SidewallMaskBlend");
            sidewallMetallic = FindProperty("_SidewallMetallic");
            sidewallSmoothness = FindProperty("_SidewallSmoothness");
            tessellationHeight = FindProperty("_TessellationHeight");
			useTessellation = FindProperty("_UseTessellation");
            
            treadWear = FindProperty("_TreadWear");
            treadWearColor = FindProperty("_TreadWearColor");
            
            patternOverlayMap = FindProperty("_PatternOverlayMap");
            patternOverlayColor = FindProperty("_PatternOverlayColor");
            
            tessellationFactorMinDistance = FindProperty("_TessellationFactorMinDistance");
            tessellationFactorMaxDistance = FindProperty("_TessellationFactorMaxDistance");


        }

        public override void OnGUI()
        {
            if (bigBoldHeader == null)
            {
                bigBoldHeader = new GUIStyle();
                bigBoldHeader.fontSize = 14;
                bigBoldHeader.fontStyle = FontStyle.Bold;
                bigBoldHeader.normal.textColor = new Color(0.224f, 0.655f, 0.741f, 1f);
            }
            
            using (var header = new MaterialHeaderScope("Sidewalls & Tread", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    EditorGUILayout.LabelField("Tire Tread", bigBoldHeader);
					materialEditor.ShaderProperty(treadTiling,Styles.treadTilingText);
                    materialEditor.ShaderProperty(useTessellation,Styles.useTessellationText);
                    materialEditor.ShaderProperty(tessellationHeight,Styles.tessellationHeightText);
                    materialEditor.ShaderProperty(treadWear,"Tread Wear");
                    materialEditor.ShaderProperty(treadWearColor,"Tread Wear Color");

                    EditorGUILayout.Space(12);
                    EditorGUILayout.LabelField("Pattern Overlay", bigBoldHeader);
                    materialEditor.TexturePropertySingleLine(new GUIContent("Pattern Overlay Map"), patternOverlayMap,patternOverlayColor);
                    materialEditor.TextureScaleOffsetProperty(patternOverlayMap);
                    EditorGUILayout.LabelField("Sidewalls", bigBoldHeader);
                    materialEditor.TexturePropertySingleLine(new GUIContent("Sidewall Mask Map"), sidewallMaskMap);
                    materialEditor.TextureScaleOffsetProperty(sidewallMaskMap);
                    materialEditor.ShaderProperty(sidewallColor,Styles.sidewallColor);
                    materialEditor.ShaderProperty(sidewallMaskBlend,Styles.sidewallMaskBlend);
                    materialEditor.RangeProperty(sidewallMetallic,"Metallic");
                    materialEditor.RangeProperty(sidewallSmoothness,"Smoothness");
                }
            }

            ForceTessellationValues();
        }
        
        private void ForceTessellationValues()
        {
            foreach (Material mat in materialEditor.targets)
            {
                Undo.RecordObject(mat, "Force Tessellation Values");

                if (mat.HasProperty("_TessellationFactorMinDistance"))
                    mat.SetFloat("_TessellationFactorMinDistance", 0.0f);

                if (mat.HasProperty("_TessellationFactorMaxDistance"))
                    mat.SetFloat("_TessellationFactorMaxDistance", 1.0f);

                if (mat.HasProperty("_TessellationFactor"))
                    mat.SetFloat("_TessellationFactor", 50.0f);
            }

            // Sync inspector UI
            if (tessellationFactorMinDistance != null)
                tessellationFactorMinDistance.floatValue = 0.0f;

            if (tessellationFactorMaxDistance != null)
                tessellationFactorMaxDistance.floatValue = 1.0f;

            // If you expose it as a MaterialProperty, sync it too:
            var tessFactorProp = FindProperty("_TessellationFactor", false);
            if (tessFactorProp != null)
                tessFactorProp.floatValue = 50.0f;
        }
    }
    
    
}
#endif