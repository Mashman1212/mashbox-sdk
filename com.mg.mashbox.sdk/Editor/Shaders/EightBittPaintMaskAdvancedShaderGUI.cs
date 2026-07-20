#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace EightBitt.Shaders.HDRP.Lit.Editor
{
    public sealed class EightBittPaintMaskAdvancedShaderGUI : LightingShaderGraphGUI
    {
        private static Texture2D cover;

        public EightBittPaintMaskAdvancedShaderGUI()
        {
            uiBlocks.RemoveAll(block => block is ShaderGraphUIBlock);
            uiBlocks.Insert(1, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.Input, EightBittPaintMaskInputsUiBlock.Section.Albedo));
            uiBlocks.Insert(2, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer1, EightBittPaintMaskInputsUiBlock.Section.Paint));
            uiBlocks.Insert(3, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer2, EightBittPaintMaskInputsUiBlock.Section.Grunge));
            uiBlocks.Insert(4, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.Detail, EightBittPaintMaskInputsUiBlock.Section.Mask));
            uiBlocks.Insert(5, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.Emissive, EightBittPaintMaskInputsUiBlock.Section.Normal));
            uiBlocks.Insert(6, new EightBittPaintMaskInputsUiBlock(MaterialUIBlock.ExpandableBit.User0, EightBittPaintMaskInputsUiBlock.Section.Noise));
            uiBlocks.RemoveAll(block => block is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(block => block is AdvancedOptionsUIBlock);
        }

        protected override void OnMaterialGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawBanner();
            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            base.OnMaterialGUI(materialEditor, properties);
            if (!EditorGUI.EndChangeCheck()) return;

            foreach (Material material in materialEditor.targets)
            {
                MashBoxSDK.Shaders.ShaderEnforcer.EnforceEightBittPaintMaskAdvancedShader(material);
                EditorUtility.SetDirty(material);
            }
        }

        private static void DrawBanner()
        {
            if (cover == null)
            {
                cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.mg.mashbox.sdk/Editor/Shaders/EightBittPaintMaskAdvancedShader_Banner.png");
                if (cover == null)
                    cover = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MashBoxSDK/Editor/Shaders/EightBittPaintMaskAdvancedShader_Banner.png");
            }

            if (cover == null) return;
            GUILayout.Space(-4);
            Rect row = EditorGUILayout.GetControlRect(false, cover.height, GUILayout.ExpandWidth(true));
            GUI.BeginGroup(row);
            GUI.DrawTexture(new Rect((row.width - cover.width) * 0.5f, 0, cover.width, cover.height), cover, ScaleMode.StretchToFill);
            GUI.EndGroup();
            GUILayout.Space(-6);
        }
    }
}

#endif
