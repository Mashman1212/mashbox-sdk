#if UNITY_EDITOR
using MGShaders.HDRP.Lit.Editor.EditorGui;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    public class MGVehicleShaderGUI : LightingShaderGraphGUI
    {
        private static Texture2D cover;
        
        public MGVehicleShaderGUI()
        {
            // Remove the ShaderGraphUIBlock to avoid having duplicated properties in the UI.
            uiBlocks.RemoveAll(b => b is ShaderGraphUIBlock);

            // Add our own stuff
            uiBlocks.Insert(1, new HDRPSurfaceInputsUiBlock(MaterialUIBlock.ExpandableBit.Input));
            uiBlocks.Insert(2, new HDRPStickerInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer1));
            uiBlocks.Insert(3, new HDRPCoatingFXUiBlock(MaterialUIBlock.ExpandableBit.Layer2));
            uiBlocks.Insert(4, new HDRPDetailInputsUiBlock(MaterialUIBlock.ExpandableBit.Detail));
            uiBlocks.Insert(5, new HDRPEmissiveInputsUiBlock(MaterialUIBlock.ExpandableBit.Emissive));

            uiBlocks.RemoveAll(b => b is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(b => b is AdvancedOptionsUIBlock);
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
                    ShaderEnforcer.EnforceVehicleShader(mat);
                    EditorUtility.SetDirty(mat);
                }
            }
        }

        private static void DrawBanner()
        {
            if (cover == null)
            {
                cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.mg.mashbox.sdk/Shaders/Editor/Shaders/VehicleShader_Banner.png");

                if (cover == null)
                {
                    cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MashBoxSDK/Shaders/HDRP/Lit/Vehicle/Textures/VehicleShader_Banner.png");
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