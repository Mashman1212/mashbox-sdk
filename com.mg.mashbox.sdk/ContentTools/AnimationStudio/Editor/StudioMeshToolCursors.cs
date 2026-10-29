using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    // Cached vector cursor masters with compact semantic tool badges.
    internal static class StudioMeshToolCursors
    {
        internal enum Kind { None, MultiCut, EdgeLoop, TargetWeld, Extrude, Bevel, EdgeFlow, Soften, Combine }
        private static readonly Dictionary<Kind,Texture2D> textures=new Dictionary<Kind,Texture2D>();
        internal static Vector2 Hotspot => new Vector2(2,2);
        static StudioMeshToolCursors()
        { AssemblyReloadEvents.beforeAssemblyReload+=Dispose;EditorApplication.quitting+=Dispose; }
        internal static void Dispose()
        { foreach(var texture in textures.Values)if(texture)UnityEngine.Object.DestroyImmediate(texture);textures.Clear(); }
        internal static Texture2D Get(Kind kind)
        {
            if(kind==Kind.None)return null;
            if(!textures.TryGetValue(kind,out var texture)||!texture)textures[kind]=texture=Create(kind);
            return texture;
        }
        // Traced from the supplied source; only the rounded butt and slot corners are regularized.
        private static readonly float[] KnifeBlade = {104,15,109,15,148,54,130,129,27,27,27,25};
        private static readonly float[] KnifeBladeEdge = {18,26,24,26,129,131,126,147,7,29};
        private static readonly float[] KnifeHandle = { 154f, 50f, 161f, 50f, 374f, 250f, 375.6304f, 251.5975f, 377.1677f, 253.2876f, 378.6083f, 255.066f, 379.9486f, 256.9286f, 381.1854f, 258.871f, 382.3158f, 260.8882f, 383.3371f, 262.9757f, 384.2467f, 265.1282f, 385.0424f, 267.3407f, 385.7225f, 269.6077f, 386.2852f, 271.9239f, 386.7291f, 274.2836f, 387.0533f, 276.6812f, 387.2568f, 279.1108f, 387.3393f, 281.5667f, 387.3005f, 284.0429f, 387.1405f, 286.5335f, 386.8597f, 289.0324f, 386.4589f, 291.5337f, 385.9388f, 294.0312f, 385.3009f, 296.5191f, 384.5466f, 298.9912f, 383.6778f, 301.4417f, 382.6965f, 303.8646f, 381.6051f, 306.2541f, 380.4063f, 308.6044f, 379.1029f, 310.91f, 377.6981f, 313.1651f, 376.1952f, 315.3645f, 374.5979f, 317.5027f, 372.91f, 319.5747f, 371.1356f, 321.5754f, 369.279f, 323.5001f, 367.3446f, 325.3441f, 365.3371f, 327.1029f, 363.2614f, 328.7723f, 361.1223f, 330.3484f, 358.9252f, 331.8272f, 356.6753f, 333.2053f, 354.3779f, 334.4792f, 352.0387f, 335.6461f, 349.6632f, 336.7029f, 347.2573f, 337.6473f, 344.8266f, 338.4768f, 342.3771f, 339.1896f, 339.9146f, 339.7839f, 337.4452f, 340.2582f, 334.9746f, 340.6115f, 332.5089f, 340.8428f, 330.054f, 340.9517f, 327.6159f, 340.9378f, 325.2003f, 340.8012f, 322.8133f, 340.5422f, 320.4603f, 340.1614f, 318.1472f, 339.6598f, 315.8796f, 339.0386f, 313.6628f, 338.2992f, 311.5023f, 337.4435f, 309.4031f, 336.4734f, 307.3705f, 335.3915f, 305.4092f, 334.2001f, 303.524f, 332.9023f, 301.7195f, 331.5012f, 300f, 330f, 130f, 168f, 128f, 156f };

        internal static bool KnifeContains(float x,float y)
        {
            x=(x-2)/.073f+8;y=(y-2)/.073f+28;
            if(KnifePathContains(KnifeBlade,x,y)||KnifePathContains(KnifeBladeEdge,x,y))return true;
            if(!KnifePathContains(KnifeHandle,x,y))return false;
            bool slot=KnifeRoundBox(x,y,254.1f,210.1f,92,21.8f,8);
            bool outer=KnifeRoundBox(x,y,230.5f,188.5f,38.9f,31.9f,13.2f);
            bool slider=KnifeRoundBox(x,y,230.5f,188.5f,34.7f,27.7f,9);
            return (!slot&&!outer)||slider;
        }
        private static bool KnifePathContains(float[] path,float x,float y)
        {
            bool inside=false;
            for(int i=0,j=path.Length-2;i<path.Length;j=i,i+=2)
                if((path[i+1]>y)!=(path[j+1]>y)&&x<(path[j]-path[i])*(y-path[i+1])/(path[j+1]-path[i+1])+path[i])inside=!inside;
            return inside;
        }
        private static bool KnifeRoundBox(float x,float y,float cx,float cy,float halfLength,float halfWidth,float radius)
        {
            x-=cx;y-=cy;
            const float cosine=.72896863f,sine=.68454711f;
            float a=Math.Max(0,Math.Abs(x*cosine+y*sine)-halfLength+radius);
            float b=Math.Max(0,Math.Abs(-x*sine+y*cosine)-halfWidth+radius);
            return a*a+b*b<=radius*radius;
        }

        private static readonly float[] KnifeBladeEnvelope = {7,29,109,15,148,54,130,129,126,147};
        internal static int KnifeLayerAt(float x,float y)
        {
            if(KnifeContains(x,y))return 2;
            x=(x-2)/.073f+8;y=(y-2)/.073f+28;
            if(KnifePathContains(KnifeBladeEnvelope,x,y)||KnifePathContains(KnifeHandle,x,y))return 1;
            return KnifeNearPath(KnifeBladeEnvelope,x,y)||KnifeNearPath(KnifeHandle,x,y)?1:0;
        }
        private static bool KnifeNearPath(float[] path,float x,float y)
        {
            const float radius=.7f/.073f;
            for(int i=0,j=path.Length-2;i<path.Length;j=i,i+=2)
            {
                float dx=path[j]-path[i],dy=path[j+1]-path[i+1];
                float t=Math.Max(0,Math.Min(1,((x-path[i])*dx+(y-path[i+1])*dy)/(dx*dx+dy*dy)));
                float a=x-path[i]-t*dx,b=y-path[i+1]-t*dy;
                if(a*a+b*b<=radius*radius)return true;
            }
            return false;
        }

        private static readonly float[] ToolPointer={2,2,2,16,6,12,11,12};
        private static readonly float[] LoopCube={1,3,6,.5f,11,3,11,9,6,11.5f,1,9};
        private static readonly float[] LoopBand={1,4.5f,6,2,11,4.5f,11,7,6,9.5f,1,7};
        private static readonly float[] LoopSpokes={1,3,6,6,11,3};
        private static readonly float[] ExtrudeCube={1,5,6,2,11,5,11,8,6,11,1,8};
        private static readonly float[] ExtrudeFace={1,5,6,8,11,5};
        private static readonly float[] ExtrudeArrow={4,2.5f,6,.5f,8,2.5f};
        private static readonly float[] BevelFace={1,2,7,2,11,6,11,11,1,11};
        private static readonly float[] BevelInset={6,2,6,7,11,7};
        private static readonly float[] BevelCut={7,2,11,6};
        private static readonly float[] FlowCurve={1,9,1.6f,7.5f,2.5f,5.8f,3.5f,4.5f,4.7f,3.7f,6,3.4f,7.3f,3.7f,8.5f,4.5f,9.5f,5.8f,10.4f,7.5f,11,9};
        private static readonly float[] SoftCorner={2,10,2,6,2.15f,4.7f,2.6f,3.6f,3.4f,2.8f,4.5f,2.2f,6,2,11,2};
        private static readonly float[] SoftAccent={4.4f,10,4.4f,6,4.6f,5.2f,5.2f,4.6f,6,4.4f,11,4.4f};
        private static float CursorPathDistance(float[] path,float x,float y,bool closed=false)
        {
            float best=float.MaxValue;
            int count=path.Length/2;
            for(int i=0;i<(closed?count:count-1);i++)
            {
                int a=i*2,b=((i+1)%count)*2;float dx=path[b]-path[a],dy=path[b+1]-path[a+1];
                float t=Math.Max(0,Math.Min(1,((x-path[a])*dx+(y-path[a+1])*dy)/(dx*dx+dy*dy)));
                float px=x-path[a]-t*dx,py=y-path[a+1]-t*dy;best=Math.Min(best,px*px+py*py);
            }
            return best;
        }
        private static float CursorLineDistance(float x,float y,float ax,float ay,float bx,float by)
        {
            float dx=bx-ax,dy=by-ay,t=Math.Max(0,Math.Min(1,((x-ax)*dx+(y-ay)*dy)/(dx*dx+dy*dy)));
            x-=ax+t*dx;y-=ay+t*dy;return x*x+y*y;
        }
        private static float CursorBoxDistance(float x,float y,float cx,float cy,float half)
        {
            float dx=Math.Abs(x-cx)-half,dy=Math.Abs(y-cy)-half;
            return Math.Max(dx,dy);
        }
        internal static int ToolCursorLayerAt(Kind kind,float x,float y)
        {
            if(kind==Kind.MultiCut)return KnifeLayerAt(x,y);
            int layer=KnifePathContains(ToolPointer,x,y)?2:CursorPathDistance(ToolPointer,x,y,true)<=.7f*.7f?1:0;
            // Fit every badge to the same 14-pixel longest dimension, preserving proportions.
            float minX=1,minY=1,width=10,height=10;
            switch(kind)
            {
                case Kind.EdgeLoop:minY=.5f;height=11;break;
                case Kind.TargetWeld:break;
                case Kind.Extrude:minY=-.05f;height=11.05f;break;
                case Kind.Bevel:minY=2;height=9;break;
                case Kind.EdgeFlow:minX=-.2f;minY=2.3f;width=12.4f;height=7.9f;break;
                case Kind.Soften:minX=.9f;minY=.9f;width=11.2f;height=10.2f;break;
            }
            float badgeScale=14/Math.Max(width,height);
            float u=(x-13)/badgeScale+minX-(Math.Max(width,height)-width)*.5f;
            float v=(y-13)/badgeScale+minY-(Math.Max(width,height)-height)*.5f;
            float border=.7f/badgeScale;
            int badge=0;float accent=float.MaxValue;
            int Filled(float[] path)=>KnifePathContains(path,u,v)?2:CursorPathDistance(path,u,v,true)<=border*border?1:0;
            int Stroke(float distance,float width)=>distance<=width*width?2:distance<=(width+border)*(width+border)?1:0;
            switch(kind)
            {
                case Kind.EdgeLoop:
                    badge=Filled(LoopCube);
                    if(badge>0&&(CursorPathDistance(LoopSpokes,u,v)<.4f*.4f||CursorLineDistance(u,v,6,6,6,11.5f)<.4f*.4f))badge=1;
                    accent=CursorPathDistance(LoopBand,u,v,true);break;
                case Kind.TargetWeld:
                    float left=CursorBoxDistance(u,v,3,3,2),right=CursorBoxDistance(u,v,9,9,2);
                    float weld=Math.Min(left,right);badge=weld<=0?2:weld<=border?1:0;
                    accent=Math.Min(CursorLineDistance(u,v,5,5,7,7),Math.Min(CursorLineDistance(u,v,5,7,7,7),CursorLineDistance(u,v,7,5,7,7)));break;
                case Kind.Extrude:
                    badge=Filled(ExtrudeCube);
                    if(badge>0&&(CursorPathDistance(ExtrudeFace,u,v)<=.4f*.4f||CursorLineDistance(u,v,6,8,6,11)<=.4f*.4f))badge=1;
                    accent=Math.Min(CursorPathDistance(ExtrudeArrow,u,v),CursorLineDistance(u,v,6,.5f,6,6));break;
                case Kind.Bevel:
                    badge=Filled(BevelFace);if(badge>0&&CursorPathDistance(BevelInset,u,v)<=.4f*.4f)badge=1;
                    accent=CursorPathDistance(BevelCut,u,v);break;
                case Kind.EdgeFlow:
                    float flow=CursorPathDistance(FlowCurve,u,v);badge=Stroke(flow,1.1f);accent=flow;
                    float ends=Math.Min((u-1)*(u-1)+(v-9)*(v-9),(u-11)*(u-11)+(v-9)*(v-9));
                    if(ends<=1.2f*1.2f){badge=2;accent=float.MaxValue;}break;
                case Kind.Soften:
                    badge=Stroke(CursorPathDistance(SoftCorner,u,v),1.1f);accent=CursorPathDistance(SoftAccent,u,v);break;
                case Kind.Combine:
                    float back=CursorBoxDistance(u,v,4,4,3),front=CursorBoxDistance(u,v,8,8,3);
                    badge=back<=0?2:back<=border?1:0;
                    if(front<=border)badge=front<=0?2:1;
                    if(badge==2&&((u>=1&&u<=5&&Math.Abs(v-7)<.4f)||(v>=1&&v<=5&&Math.Abs(u-7)<.4f)))badge=1;
                    if(u>=5&&u<=7&&v>=5&&v<=7)badge=3;break;
            }
            if(accent<=.55f*.55f)badge=3;
            else if(accent<=(.55f+border)*(.55f+border)&&badge==0)badge=1;
            return layer!=0?layer:badge;
        }
        private static Texture2D Create(Kind kind)
        {
            const int size=32,samples=8;
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int white=0,dark=0,accent=0;
                for(int sy=0;sy<samples;sy++)for(int sx=0;sx<samples;sx++)
                {
                    int layer=ToolCursorLayerAt(kind,x+(sx+.5f)/samples,y+(sy+.5f)/samples);
                    if(layer==2)white++;else if(layer==1)dark++;else if(layer==3)accent++;
                }
                int coverage=white+dark+accent;
                byte Channel(int value)=>(byte)((white*255+dark*64+accent*value+coverage/2)/Math.Max(1,coverage));
                pixels[(size-1-y)*size+x]=new Color32(Channel(150),Channel(220),Channel(255),(byte)((coverage*255+samples*samples/2)/(samples*samples)));
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Studio mesh cursor "+kind,hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(pixels);texture.Apply();return texture;
        }

    }

    internal sealed partial class StudioModelEditor
    {
        internal StudioMeshToolCursors.Kind MeshToolCursor(EventModifiers modifiers)
        {
            if(CameraOptionsVisible||LightingOptionsVisible)return StudioMeshToolCursors.Kind.None;
            if(GUIUtility.hotControl!=0&&GUIUtility.hotControl!=cutControl&&GUIUtility.hotControl!=targetWeldControl)return StudioMeshToolCursors.Kind.None;
            if(cutTool==CutTool.MultiCut)
                return !HasMultiCutGesture&&(modifiers&(EventModifiers.Control|EventModifiers.Command))!=0?StudioMeshToolCursors.Kind.EdgeLoop:StudioMeshToolCursors.Kind.MultiCut;
            if(cutTool==CutTool.EdgeLoop)return StudioMeshToolCursors.Kind.EdgeLoop;
            if(targetWeldActive)return StudioMeshToolCursors.Kind.TargetWeld;
            if(bevelToolPane)return StudioMeshToolCursors.Kind.Bevel;
            if(extrudeToolPane)return StudioMeshToolCursors.Kind.Extrude;
            if(edgeFlowToolPane)return StudioMeshToolCursors.Kind.EdgeFlow;
            if(softenToolPane)return StudioMeshToolCursors.Kind.Soften;
            if(combineToolPane)return StudioMeshToolCursors.Kind.Combine;
            return StudioMeshToolCursors.Kind.None;
        }
    }

    public sealed partial class MashBoxStudioViewport
    {
        private IMGUIContainer meshCursorCanvas;
        private StudioMeshToolCursors.Kind meshCursorKind;
        // UI Toolkit style removal alone can leave SceneView's native custom texture latched.
        // Use the same view-local reset as Unity's EditorCursorManager, only inside our canvas GUI.
        private static readonly Action clearNativeMeshCursor = typeof(EditorGUIUtility).GetMethod("ClearCurrentViewCursor",
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,
            null,Type.EmptyTypes,null)?.CreateDelegate(typeof(Action)) as Action;
#if MASHBOX_STUDIO_TESTS
        internal int MeshNativeCursorClearCount { get; private set; }
#endif
        private void ResetMeshToolCursor(bool clearNative = false)
        {
            bool owned=meshCursorKind!=StudioMeshToolCursors.Kind.None;
            if(meshCursorCanvas!=null)meshCursorCanvas.style.cursor=StyleKeyword.Null;
            meshCursorCanvas=null;meshCursorKind=StudioMeshToolCursors.Kind.None;
            if(clearNative&&owned&&clearNativeMeshCursor!=null)
            {
                clearNativeMeshCursor();
#if MASHBOX_STUDIO_TESTS
                MeshNativeCursorClearCount++;
#endif
            }
        }
        private void UpdateMeshToolCursor(IMGUIContainer canvas,bool blocked)
        {
            var evt=Event.current;var editor=Studio?.ToolbarModelEditor;
            var pointer=(CanvasMousePosition??Vector2.zero)+CanvasInputOrigin;
            bool allowed=!blocked&&editor!=null&&studioInputActive!=null&&studioInputActive()&&!navigating&&!evt.alt&&!SelectionPaintHoldActive&&
                evt.type!=EventType.MouseLeaveWindow&&ModelCanvasRect.Contains(pointer)&&!IsOverViewportControls(pointer);
            var kind=allowed?editor.MeshToolCursor(evt.modifiers):StudioMeshToolCursors.Kind.None;
            if(kind==StudioMeshToolCursors.Kind.None){ResetMeshToolCursor(true);return;}
            if(meshCursorCanvas!=canvas||meshCursorKind!=kind)
            {
                ResetMeshToolCursor(true);meshCursorCanvas=canvas;meshCursorKind=kind;
                canvas.style.cursor=new UnityEngine.UIElements.Cursor{texture=StudioMeshToolCursors.Get(kind),hotspot=StudioMeshToolCursors.Hotspot};
            }
            var rect=ModelCanvasRect;rect.position-=CanvasInputOrigin;
            EditorGUIUtility.AddCursorRect(rect,MouseCursor.CustomCursor);
        }
    }
}
