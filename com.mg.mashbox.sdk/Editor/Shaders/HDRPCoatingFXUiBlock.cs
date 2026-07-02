#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;

namespace MGShaders.HDRP.Lit.Editor.EditorGui
{
    public class HDRPCoatingFXUiBlock : MaterialUIBlock
    {
        ExpandableBit   foldoutBit;

        MaterialProperty coatMaskMap;
        MaterialProperty coatMask;
        MaterialProperty oilSlickStrength;
        MaterialProperty oilSlickAlphaMashMulti;
        MaterialProperty filmThickness;
        MaterialProperty cmmMultInvert;
        MaterialProperty oilSlickColorMult;
        MaterialProperty oilSlickGradient;
        private MaterialProperty filmBlend;
        public HDRPCoatingFXUiBlock(ExpandableBit expandableBit)
        {
            foldoutBit = expandableBit;
        }
        
        public override void LoadMaterialProperties()
        {
            coatMaskMap = FindProperty("_CoatMaskMap");
            coatMask = FindProperty("_CoatMaskStrength");
            oilSlickStrength = FindProperty("_OilSlickStrength");
            oilSlickColorMult = FindProperty("_OilSlickColorMult");
            oilSlickAlphaMashMulti = FindProperty("_OilSlickAlphaMaskMulti");
            filmThickness = FindProperty("_FilmThickness");
            cmmMultInvert = FindProperty("_CoatCMMInvert");
            oilSlickGradient = FindProperty("_OilSlickGradient");
            filmBlend = FindProperty("_FilmBlend");
        }

        public override void OnGUI()
        {
            using (var header = new MaterialHeaderScope("Coating FX", (uint)foldoutBit, materialEditor))
            {
                if (header.expanded)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Coat Mask Map"), coatMaskMap);
                    materialEditor.TextureScaleOffsetProperty(coatMaskMap);
                    
                    materialEditor.RangeProperty(coatMask,"Coat Mask Strength");
                    materialEditor.RangeProperty(oilSlickStrength,"OilSlick Strength");
                    
                    //if(oilSlickColorMult != null)
                    //    materialEditor.ColorProperty(oilSlickColorMult,"Oil Slick Color Mult");
                    
                    if (oilSlickGradient != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Oil Slick Gradient"), oilSlickGradient);
                    }

                    
                    materialEditor.RangeProperty(oilSlickAlphaMashMulti,"CMM Mult");

                    if (cmmMultInvert != null)
                        materialEditor.RangeProperty(cmmMultInvert,"CMM Mult Invert");
                    
                    if (filmBlend != null)
                    {
                        materialEditor.RangeProperty(filmBlend,"Film Blend");
                    }


                    
                    materialEditor.RangeProperty(filmThickness,"Film Thickness");

                }
            }
        }
    }
}

#endif