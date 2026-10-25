using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool showUVChecker;
        private Material uvCheckerMaterial;
        private Texture2D uvCheckerTexture;
        private void EnsureUVChecker()
        {
            if(uvCheckerMaterial) return;
            const int size=512,cell=16;
            uvCheckerTexture=new Texture2D(size,size,TextureFormat.RGBA32,true) {
                name="Studio UV checker",hideFlags=HideFlags.HideAndDontSave,
                wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,anisoLevel=4
            };
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                pixels[y*size+x]=((x/cell+y/cell)&1)==0 ? new Color32(215,215,215,255) : new Color32(48,55,60,255);
            uvCheckerTexture.SetPixels32(pixels); uvCheckerTexture.Apply(true,true);
            uvCheckerMaterial=new Material(shaded) { name="UV checker preview",hideFlags=HideFlags.HideAndDontSave };
            uvCheckerMaterial.SetTexture("_MainTex",uvCheckerTexture);
            uvCheckerMaterial.SetColor("_Color",Color.white);
            uvCheckerMaterial.SetFloat("_UseArray",0); uvCheckerMaterial.SetFloat("_UseNormal",0);
            uvCheckerMaterial.SetFloat("_VertexAOStrength",0); uvCheckerMaterial.SetFloat("_Unlit",.7f);
        }
        private void DrawUVCheckerButton(Rect rect)
        {
            if(DrawSkinModeButton(rect,false,"Toggle UV checker on the selected mesh"))
            {
                showUVChecker=!showUVChecker;
                if(showUVChecker) EnsureUVChecker();
                UpdateDisplay();
            }
            if(Event.current.type!=EventType.Repaint) return;
            for(int y=0;y<4;y++) for(int x=0;x<4;x++)
                EditorGUI.DrawRect(new Rect(rect.x+8+x*4,rect.y+7+y*4,4,4),((x+y)&1)==0 ? new Color(.8f,.85f,.85f) : new Color(.22f,.26f,.26f));
            if(showUVChecker) EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
        }
        private void DisposeUVChecker()
        {
            if(uvCheckerMaterial) Object.DestroyImmediate(uvCheckerMaterial);
            if(uvCheckerTexture) Object.DestroyImmediate(uvCheckerTexture);
        }
    }
}
