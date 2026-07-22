#if UNITY_EDITOR

using MGShaders.HDRP.Lit.Editor.EditorGui;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MashBoxSDK.Shaders.HDRP.Lit.Editor.EditorGui
{
    public abstract class MGLitAdvancedShaderGUIBase : LightingShaderGraphGUI
    {
        private Texture2D cover;
        protected abstract string BannerAssetName { get; }
        protected virtual string InspectorNotice => null;
        protected abstract void Enforce(Material material);

        protected MGLitAdvancedShaderGUIBase(bool includeAdvancedLighting)
        {
            uiBlocks.RemoveAll(block => block is ShaderGraphUIBlock);
            uiBlocks.Insert(1, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.Input, MGLitAdvancedInputsUiBlock.Section.Surface));
            uiBlocks.Insert(2, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer1, MGLitAdvancedInputsUiBlock.Section.Decals));
            uiBlocks.Insert(3, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.Layer2, MGLitAdvancedInputsUiBlock.Section.Dirt));
            uiBlocks.Insert(4, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.Detail, MGLitAdvancedInputsUiBlock.Section.Detail));
            uiBlocks.Insert(5, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.Emissive, MGLitAdvancedInputsUiBlock.Section.Emissive));
            if (includeAdvancedLighting)
                uiBlocks.Insert(6, new MGLitAdvancedInputsUiBlock(MaterialUIBlock.ExpandableBit.User0, MGLitAdvancedInputsUiBlock.Section.AdvancedLighting));
            uiBlocks.RemoveAll(block => block is SurfaceOptionUIBlock);
            uiBlocks.RemoveAll(block => block is AdvancedOptionsUIBlock);
        }

        protected override void OnMaterialGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawBanner();
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(InspectorNotice))
            {
                EditorGUILayout.HelpBox(InspectorNotice, MessageType.Warning);
                GUILayout.Space(4);
            }

            EditorGUI.BeginChangeCheck();
            base.OnMaterialGUI(materialEditor, properties);
            if (!EditorGUI.EndChangeCheck()) return;

            foreach (Material material in materialEditor.targets)
            {
                Enforce(material);
                EditorUtility.SetDirty(material);
            }
        }

        private void DrawBanner()
        {
            if (cover == null)
            {
                cover = AssetDatabase.LoadAssetAtPath<Texture2D>($"Packages/com.mg.mashbox.sdk/Editor/Shaders/{BannerAssetName}");
                if (cover == null)
                    cover = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/MashBoxSDK/Editor/Shaders/{BannerAssetName}");
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

    public sealed class MGLitAdvancedShaderGUI : MGLitAdvancedShaderGUIBase
    {
        public MGLitAdvancedShaderGUI() : base(false) { }

        protected override string BannerAssetName => "MGLitAdvancedShader_Banner.png";
        protected override void Enforce(Material material) => ShaderEnforcer.EnforceLitAdvancedShader(material);
    }

    public sealed class MGLitAdvancedMonoSHShaderGUI : MGLitAdvancedShaderGUIBase
    {
        public MGLitAdvancedMonoSHShaderGUI() : base(true) { }

        protected override string BannerAssetName => "MGLitAdvancedMonoSHShader_Banner.png";
        protected override string InspectorNotice =>
            "This shader is designed for MonoSH lighting produced with the third-party Bakery lightmapper. " +
            "Only use it with the correct Bakery bake settings and a working knowledge of spherical harmonics.";
        protected override void Enforce(Material material) => ShaderEnforcer.EnforceLitAdvancedMonoSHShader(material);
    }
}

#endif
