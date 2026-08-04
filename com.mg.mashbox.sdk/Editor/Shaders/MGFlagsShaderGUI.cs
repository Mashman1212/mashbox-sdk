#if UNITY_EDITOR
using MGShaders.HDRP.Lit.Editor.EditorGui;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    /// <summary>Custom material inspector for the MG Flags shader.</summary>
    public class MGFlagsShaderGUI : LightingShaderGraphGUI
    {
        private static Texture2D cover;

        public MGFlagsShaderGUI()
        {
            // Shader Graph's default block would draw these properties a second time.
            uiBlocks.RemoveAll(block => block is ShaderGraphUIBlock);

            uiBlocks.Insert(1, new HDRPSurfaceInputsUiBlock(MaterialUIBlock.ExpandableBit.Input));
            uiBlocks.Insert(2, new HDRPStickerInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer1));
            uiBlocks.Insert(3, new HDRPDetailInputsUiBlock(MaterialUIBlock.ExpandableBit.Detail));
            uiBlocks.Insert(4, new HDRPEmissiveInputsUiBlock(MaterialUIBlock.ExpandableBit.Emissive));

            uiBlocks.RemoveAll(block => block is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(block => block is AdvancedOptionsUIBlock);
        }

        protected override void OnMaterialGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawBanner();
            GUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            base.OnMaterialGUI(materialEditor, properties);

            if (!EditorGUI.EndChangeCheck())
                return;

            foreach (Material material in materialEditor.targets)
            {
                ShaderEnforcer.EnforceFlagsShader(material);
                EditorUtility.SetDirty(material);
            }
        }

        private static void DrawBanner()
        {
            if (cover == null)
            {
                cover = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.mg.mashbox.sdk/Editor/Shaders/FlagsShader_Banner.png");

                if (cover == null)
                {
                    cover = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/MashBoxSDK/Shaders/HDRP/Lit/Flags/Textures/FlagsShader_Banner.png");
                }
            }

            if (cover == null)
                return;

            GUILayout.Space(-4);
            Rect rowRect = EditorGUILayout.GetControlRect(false, cover.height, GUILayout.ExpandWidth(true));

            GUI.BeginGroup(rowRect);
            GUI.DrawTexture(
                new Rect((rowRect.width - cover.width) * 0.5f, 0, cover.width, cover.height),
                cover,
                ScaleMode.StretchToFill);
            GUI.EndGroup();

            GUILayout.Space(-6);
        }
    }
}
#endif
