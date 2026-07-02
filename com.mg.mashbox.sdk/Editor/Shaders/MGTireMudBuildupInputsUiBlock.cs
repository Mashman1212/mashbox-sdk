#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class MGTireMudBuildupInputsUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;
        private MaterialProperty mudColor;
        private MaterialProperty mudBaseMap;
        private MaterialProperty mudNormalMap;
        private MaterialProperty mudNormalStrength;
        private MaterialProperty mudMetallic;
        private MaterialProperty mudSmoothness;
        private MaterialProperty mudBuildup;
        private MaterialProperty mudBuildupInvert;
        private MaterialProperty mudBuildColorMultRate;
        private MaterialProperty mudMaskMin;
        private MaterialProperty mudMaskMax;
        private MaterialProperty mudGrungeScale;
        private MaterialProperty mudBuildupMeshMaskMin;
        private MaterialProperty mudBuildupMeshMaskMax;
        private static GUIStyle bigBoldHeader;
 
        class Styles
        {

            public static GUIContent mudBuildupText = new GUIContent("Mud Buildup", "Mud build up, also applies to xxx_Tire_Mud_Buildup expansion");
            public static GUIContent mudBuildupInvertText = new GUIContent("Mud Buildup Invert", "Controls growth direction in/out");
            public static GUIContent mudBuildColorMultRateText = new GUIContent("Mud Buildup Color Mult Rate", "How fast the color fades in vs the mud building up");
            public static GUIContent mudMaskMinText = new GUIContent("Mud Mask Edge 1", "Controls minimum edge cutoff for mud mask");
            public static GUIContent mudMaskMaxText = new GUIContent("Mud Mask Edge 2", "Controls maximum edge cutoff for mud mask");
            public static GUIContent mudGrungeScaleText = new GUIContent("Mud Grunge Scale", "Controls how much the grunge effect is applied");
            public static GUIContent mudBuildupMeshMaskText = new GUIContent("Mud Buildup Mesh Mask Cutoffs", "Controls minimum & maximum edge cutoff for mud mesh mask");
        }
        
        public MGTireMudBuildupInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        
        public override void LoadMaterialProperties()
        {
            mudColor = FindProperty("_MudColor");
            mudBaseMap = FindProperty("_MudBaseMap");
            mudNormalMap = FindProperty("_MudNormalMap");
            mudNormalStrength = FindProperty("_MudNormalStrength");
            mudMetallic = FindProperty("_MudMetallic");
            mudSmoothness = FindProperty("_MudSmoothness");
            mudBuildup = FindProperty("_MudBuildup");
            mudBuildupInvert = FindProperty("_MudBuildupInvert");
            mudBuildColorMultRate = FindProperty("_MudBuildColorMultRate");
            mudMaskMin = FindProperty("_MudMaskEdge1");
            mudMaskMax = FindProperty("_MudMaskEdge2");
            mudGrungeScale = FindProperty("_MudGrungeScale");
            mudBuildupMeshMaskMin = FindProperty("_MudBuildupMeshMaskMin");
            mudBuildupMeshMaskMax = FindProperty("_MudBuildupMeshMaskMax");
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
            
            using (var header = new MaterialHeaderScope("Mud Buildup", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    
                    EditorGUILayout.LabelField("Mud Mask", bigBoldHeader); 
                    materialEditor.ShaderProperty(mudBuildup, Styles.mudBuildupText);
                    materialEditor.ShaderProperty(mudBuildupInvert, Styles.mudBuildupInvertText);
                    materialEditor.ShaderProperty(mudBuildColorMultRate, Styles.mudBuildColorMultRateText);
                    materialEditor.ShaderProperty(mudMaskMin, Styles.mudMaskMinText);
                    materialEditor.ShaderProperty(mudMaskMax, Styles.mudMaskMaxText);
                    
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(new GUIContent("For Buildup Mesh Only"));
                    materialEditor.ShaderProperty(mudGrungeScale, Styles.mudGrungeScaleText);
                    materialEditor.MinMaxShaderProperty(mudBuildupMeshMaskMin, mudBuildupMeshMaskMax, 0.0f, 10.0f, Styles.mudBuildupMeshMaskText);
                    
                    EditorGUILayout.Space(12);
                    EditorGUILayout.LabelField("Mud Appearance", bigBoldHeader);
                    materialEditor.ShaderProperty(mudColor, "Mud Color");
                    materialEditor.TexturePropertySingleLine(new GUIContent("Mud Base Map"), mudBaseMap);
                    materialEditor.TexturePropertySingleLine(new GUIContent("Mud Normal Map"), mudNormalMap);
                    materialEditor.RangeProperty(mudNormalStrength,"Normal Strength");
                    materialEditor.RangeProperty(mudMetallic,"Metallic");
                    materialEditor.RangeProperty(mudSmoothness,"Smoothness");
                }
            }
        }
    }
}

#endif