#if UNITY_EDITOR
using MGShaders.HDRP.Lit.Editor.EditorGui;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    public class MGTireShaderGUI : LightingShaderGraphGUI
    {
        private static Texture2D cover;
        

        public MGTireShaderGUI()
        {
            string bMHelpText = "<b><color=#ff6b6b>(R)Red</color></b>,<b><color=#6be36b> (G)Green</color></b>,<b><color=#6ba8ff> (B)Blue</color></b>,<b><color=#cccccc> (A)Mud Mask</color></b>";
            string mMHelpText = "<b><color=#ff6b6b>(R)Metallic</color></b>,<b><color=#6be36b> (G)AO</color></b>,<b><color=#6ba8ff> (B)Height</color></b>,<b><color=#cccccc> (A)Smoothness</color></b>";

            // Remove the ShaderGraphUIBlock to avoid duplicated properties in the UI
            uiBlocks.RemoveAll(b => b is ShaderGraphUIBlock);

            // Add custom blocks
            uiBlocks.Insert(1, new HDRPSurfaceInputsUiBlock(MaterialUIBlock.ExpandableBit.User0, bMHelpText, mMHelpText));
            uiBlocks.Insert(2, new MGTireExtraInputsUiBlock(MaterialUIBlock.ExpandableBit.User1));
            uiBlocks.Insert(3, new MGTireMudBuildupInputsUiBlock(MaterialUIBlock.ExpandableBit.User3));
            uiBlocks.Insert(4, new HDRPStickerInputsUiBlock(MaterialUIBlock.ExpandableBit.User4));
            uiBlocks.Insert(5, new HDRPEmissiveInputsUiBlock(MaterialUIBlock.ExpandableBit.Emissive));

            uiBlocks.RemoveAll(b => b is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(b => b is AdvancedOptionsUIBlock);
            uiBlocks.RemoveAll(b => b is TessellationOptionsUIBlock);
        }

        protected override void OnMaterialGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            DrawBanner();
            GUILayout.Space(6);
            
            
            EditorGUI.BeginChangeCheck();
            
            base.OnMaterialGUI(materialEditor, props);
         
            if (EditorGUI.EndChangeCheck())
            {
                foreach (Material mat in materialEditor.targets)
                {
                    ShaderEnforcer.EnforceTireShader(mat);
                    EditorUtility.SetDirty(mat);
                }
            }
            
        }

        private static Material templateMaterial;
        
        private static void DrawBanner()
        {
            if (cover == null)
            {
                cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.mg.mashbox.sdk/Editor/Shaders/TireShader_Banner.png");

                if (cover == null)
                {
                    cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MashBoxSDK/Shaders/HDRP/Lit/Tire/Textures/TireShader_Banner.png");
                }
            }

            if (cover != null)
            {
                GUILayout.Space(-4);

                Rect rowRect = EditorGUILayout.GetControlRect(false, cover.height, GUILayout.ExpandWidth(true));

                Rect imageRect = new Rect(
                    rowRect.x + (rowRect.width - cover.width) * 0.5f,
                    rowRect.y,
                    cover.width,
                    cover.height
                );

                GUI.BeginGroup(rowRect);
                GUI.DrawTexture(
                    new Rect(
                        (rowRect.width - cover.width) * 0.5f,
                        0,
                        cover.width,
                        cover.height
                    ),
                    cover,
                    ScaleMode.StretchToFill
                );
                GUI.EndGroup();

                GUILayout.Space(-6);
            }
        }
    }
}
#endif