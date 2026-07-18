#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    /// <summary>
    /// Draws the decal overlay properties used by MG Lit Basic materials.
    /// </summary>
    public class HDRPDecalInputsUiBlock : MaterialUIBlock
    {
        private readonly ExpandableBit foldoutBit;

        private MaterialProperty decalMap;
        private MaterialProperty decalColor;
        private MaterialProperty decalWhiteBoost;
        private MaterialProperty decalHueShift;
        private MaterialProperty decalBlend;
        private MaterialProperty useUV1;
        private MaterialProperty useUV2;
        private MaterialProperty useUV3;

        public HDRPDecalInputsUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }

        public override void LoadMaterialProperties()
        {
            decalMap = FindProperty("_DecalMap", false);
            decalColor = FindProperty("_DecalColor", false);
            decalWhiteBoost = FindProperty("_DecalWhiteBoost", false);
            decalHueShift = FindProperty("_DecalHueShift", false);
            decalBlend = FindProperty("_DecalBlend", false);
            useUV1 = FindProperty("_DecalUseUV1", false);
            useUV2 = FindProperty("_DecalUseUV2", false);
            useUV3 = FindProperty("_DecalUseUV3", false);
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Decal Overlay", (uint)foldoutBit, materialEditor))
            {
                if (!header.expanded)
                    return;

                if (decalMap != null)
                {
                    if (decalColor != null)
                        materialEditor.TexturePropertySingleLine(new GUIContent("Decal Map"), decalMap, decalColor);
                    else
                        materialEditor.TexturePropertySingleLine(new GUIContent("Decal Map"), decalMap);

                    materialEditor.TextureScaleOffsetProperty(decalMap);
                }

                if (useUV1 != null || useUV2 != null || useUV3 != null)
                    DrawUVSelector();

                if (decalBlend != null)
                    materialEditor.RangeProperty(decalBlend, "Blend");

                if (decalHueShift != null)
                    materialEditor.RangeProperty(decalHueShift, "Hue Shift");

                if (decalWhiteBoost != null)
                    materialEditor.RangeProperty(decalWhiteBoost, "White Boost");
            }
        }

        private void DrawUVSelector()
        {
            int current = 0;

            if (useUV3 != null && useUV3.floatValue == 1f)
                current = 3;
            else if (useUV2 != null && useUV2.floatValue == 1f)
                current = 2;
            else if (useUV1 != null && useUV1.floatValue == 1f)
                current = 1;

            EditorGUI.BeginChangeCheck();
            current = EditorGUILayout.Popup("UV Channel", current, new[] { "UV0", "UV1", "UV2", "UV3" });

            if (!EditorGUI.EndChangeCheck())
                return;

            if (useUV1 != null) useUV1.floatValue = current == 1 ? 1f : 0f;
            if (useUV2 != null) useUV2.floatValue = current == 2 ? 1f : 0f;
            if (useUV3 != null) useUV3.floatValue = current == 3 ? 1f : 0f;
        }
    }
}

#endif
