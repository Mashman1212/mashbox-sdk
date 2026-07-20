#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace EightBitt.Shaders.HDRP.Lit.Editor
{
    public sealed class EightBittPaintMaskInputsUiBlock : MaterialUIBlock
    {
        public enum Section { Albedo, Grunge, Mask, Normal, Paint, Noise }

        private readonly ExpandableBit foldoutBit;
        private readonly Section section;
        private MaterialProperty albedo, contrast, saturation, tint, whiteBalance, lighten;
        private MaterialProperty grunge, scale, offset, rColor, r, rContrast, gColor, g, gContrast;
        private MaterialProperty bColor, b, bContrast, aColor, a, aContrast;
        private MaterialProperty mask, specMin, specMax, roughMin, roughMax, aoMin, aoMax;
        private MaterialProperty normal, normalStrength;
        private MaterialProperty paintMask, paintColor, power, paintContrast;
        private MaterialProperty noiseTexture, noiseScale, noiseOffset, noiseOpacity, dirtSmoothness;

        public EightBittPaintMaskInputsUiBlock(ExpandableBit foldoutBit, Section section)
        {
            this.foldoutBit = foldoutBit;
            this.section = section;
        }

        public override void LoadMaterialProperties()
        {
            albedo = P("_Albedo"); contrast = P("_Contrast"); saturation = P("_Saturation");
            tint = P("_Tint"); whiteBalance = P("_WhiteBalance"); lighten = P("_Lighten");
            grunge = P("_GrungeRGBA"); scale = P("_Scale"); offset = P("_Offset");
            rColor = P("_R_Color"); r = P("_R"); rContrast = P("_R_contrast");
            gColor = P("_G_Color"); g = P("_G"); gContrast = P("_G_contrast");
            bColor = P("_B_Color"); b = P("_B"); bContrast = P("_B_contrast");
            aColor = P("_A_Color"); a = P("_A"); aContrast = P("_A_contrast");
            mask = P("_Mask"); specMin = P("_Spec_Min"); specMax = P("_Spec_Max");
            roughMin = P("_Rough_Min"); roughMax = P("_Rough_Max"); aoMin = P("_AO_Min"); aoMax = P("_AO_Max");
            normal = P("_Normal"); normalStrength = P("_NormalStrength");
            paintMask = P("_Paint_Mask"); paintColor = P("_PaintColor"); power = P("_Power"); paintContrast = P("_Paint_Contrast");
            noiseTexture = P("_Texture2D"); noiseScale = P("_Noise_Scale"); noiseOffset = P("_Noise_Offset");
            noiseOpacity = P("_Noise_Opacity"); dirtSmoothness = P("_dirth_smoothness");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope(Title(), (uint)foldoutBit, materialEditor))
            {
                if (!header.expanded) return;
                switch (section)
                {
                    case Section.Albedo: DrawAlbedo(); break;
                    case Section.Grunge: DrawGrunge(); break;
                    case Section.Mask: DrawMask(); break;
                    case Section.Normal: DrawNormal(); break;
                    case Section.Paint: DrawPaint(); break;
                    case Section.Noise: DrawNoise(); break;
                }
            }
        }

        private MaterialProperty P(string name) => FindProperty(name, false);
        private void Draw(MaterialProperty property, string label)
        {
            if (property != null) materialEditor.ShaderProperty(property, new GUIContent(label));
        }
        private void Texture(MaterialProperty property, string label, MaterialProperty extra = null)
        {
            if (property != null) materialEditor.TexturePropertySingleLine(new GUIContent(label), property, extra);
        }
        private void MinMax(MaterialProperty min, MaterialProperty max, string label)
        {
            if (min != null && max != null)
                materialEditor.MinMaxShaderProperty(min, max, 0f, 1f, new GUIContent(label));
        }
        private static void ChannelHeader(string channel) => EditorGUILayout.LabelField(channel, EditorStyles.boldLabel);

        private string Title()
        {
            switch (section)
            {
                case Section.Albedo: return "Albedo";
                case Section.Grunge: return "Grunge Channels";
                case Section.Mask: return "Material Mask";
                case Section.Normal: return "Normal";
                case Section.Paint: return "Paint Mask";
                default: return "Noise";
            }
        }

        private void DrawAlbedo()
        {
            Texture(albedo, "Albedo"); Draw(contrast, "Contrast"); Draw(saturation, "Saturation");
            Draw(tint, "Tint"); Draw(whiteBalance, "White Balance"); Draw(lighten, "Lighten");
        }

        private void DrawGrunge()
        {
            Texture(grunge, "Grunge RGBA"); Draw(scale, "Scale"); Draw(offset, "Offset");
            ChannelHeader("Red Channel"); Draw(rColor, "Color"); Draw(r, "Strength"); Draw(rContrast, "Contrast");
            ChannelHeader("Green Channel"); Draw(gColor, "Color"); Draw(g, "Strength"); Draw(gContrast, "Contrast");
            ChannelHeader("Blue Channel"); Draw(bColor, "Color"); Draw(b, "Strength"); Draw(bContrast, "Contrast");
            ChannelHeader("Alpha Channel"); Draw(aColor, "Color"); Draw(a, "Strength"); Draw(aContrast, "Contrast");
        }

        private void DrawMask()
        {
            Texture(mask, "Mask"); MinMax(specMin, specMax, "Specular Remapping");
            MinMax(roughMin, roughMax, "Roughness Remapping"); MinMax(aoMin, aoMax, "AO Remapping");
        }

        private void DrawNormal() => Texture(normal, "Normal Map", normalStrength);

        private void DrawPaint()
        {
            Texture(paintMask, "Paint Mask", paintColor); Draw(power, "Power"); Draw(paintContrast, "Contrast");
        }

        private void DrawNoise()
        {
            Texture(noiseTexture, "Noise Texture"); Draw(noiseScale, "Scale"); Draw(noiseOffset, "Offset");
            Draw(noiseOpacity, "Opacity"); Draw(dirtSmoothness, "Dirt Smoothness");
        }
    }
}

#endif
