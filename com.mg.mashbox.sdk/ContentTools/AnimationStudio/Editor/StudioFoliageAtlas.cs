using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioFoliageAtlas
    {
        static readonly string[][] Aliases = { new[]{"_MainTex","_BaseColorMap","_BaseMap"}, new[]{"Texture2D_364700D4","_NormalMap","_BumpMap"}, new[]{"_MaskMap"}, new[]{"Texture2D_3FAF3BC1","_ThicknessMap"} };
        public static void Build(StudioFoliageMorphSet recipe,string stem,Vector2 woodMin,Vector2 woodSize)
        {
            if(recipe.atlasSize<256||recipe.atlasSize>4096||!Mathf.IsPowerOfTwo(recipe.atlasSize)) throw new ArgumentException("Atlas size must be a power of two from 256 to 4096.");
            var shader=Shader.Find("Hidden/MashBox/Studio/FoliageAtlas");
            if(!shader) throw new InvalidOperationException("Studio foliage atlas shader missing.");
            var pack=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                if(recipe.atlases==null||recipe.atlases.Length!=4)recipe.atlases=new Texture2D[4];
                pack.SetVector("_WoodUV",new Vector4(woodMin.x,woodMin.y,woodSize.x,woodSize.y));
                pack.SetFloat("_Padding",8f/recipe.atlasSize);
                for(int map=0;map<4;map++)
                {
                    Texture2D wood=Resolve(recipe.barkMaps,recipe.barkMaterial,map),leaf=Resolve(recipe.foliageMaps,recipe.foliageMaterial,map);
                    if(map==0&&(!wood||!leaf))throw new InvalidOperationException("Assign wood and foliage base maps.");
                    pack.SetTexture("_Wood",wood);pack.SetTexture("_Leaf",leaf);
                    pack.SetFloat("_WoodPresent",wood?1:0);pack.SetFloat("_LeafPresent",leaf?1:0);
                    pack.SetFloat("_WoodNormal",Normal(wood)?1:0);pack.SetFloat("_LeafNormal",Normal(leaf)?1:0);
                    pack.SetFloat("_Map",map);
                    pack.SetColor("_WoodTint",recipe.barkTint);pack.SetColor("_LeafTint",recipe.foliageTint);
                    pack.SetVector("_WoodST",map==0?new Vector4(recipe.barkBaseTiling.x,recipe.barkBaseTiling.y,recipe.barkBaseOffset.x,recipe.barkBaseOffset.y):new Vector4(1,1,0,0));
                    var rt=RenderTexture.GetTemporary(recipe.atlasSize,recipe.atlasSize,0,RenderTextureFormat.ARGB32,map==0?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);
                    Texture2D result=null;
                    try
                    {
                        GL.sRGBWrite=map==0;Graphics.Blit(Texture2D.whiteTexture,rt,pack);RenderTexture.active=rt;
                        result=new Texture2D(recipe.atlasSize,recipe.atlasSize,TextureFormat.RGBA32,true,map!=0){name=recipe.name+new[]{" Base"," Normal"," Mask"," Thickness"}[map],wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
                        result.ReadPixels(new Rect(0,0,recipe.atlasSize,recipe.atlasSize),0,0);result.Apply(true,false);
                        string path=stem+new[]{"_Base.png","_Normal.png","_Mask.png","_Thickness.png"}[map];
                        System.IO.File.WriteAllBytes(path,result.EncodeToPNG());
                        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType=TextureImporterType.Default; importer.sRGBTexture=map==0;
                        importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=map==0;
                        importer.wrapMode=TextureWrapMode.Clamp; importer.maxTextureSize=recipe.atlasSize;
                        importer.textureCompression=TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
                        recipe.atlases[map]=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                    finally {if(result)Object.DestroyImmediate(result);RenderTexture.ReleaseTemporary(rt);}
                }
            }
            finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;Object.DestroyImmediate(pack);}
        }
        static bool Normal(Texture2D texture)=>texture&&AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter ti&&ti.textureType==TextureImporterType.NormalMap;
        static Texture2D Resolve(Texture2D[] explicitMaps,Material material,int map)
        {
            if(explicitMaps!=null&&explicitMaps.Length>map&&explicitMaps[map])return explicitMaps[map];
            if(material)foreach(string property in Aliases[map])if(material.HasProperty(property)&&material.GetTexture(property) is Texture2D texture)return texture;
            return null;
        }
    }
}
