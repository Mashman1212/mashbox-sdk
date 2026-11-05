using System;
using UnityEngine;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    // Small code-native symbols fill gaps in Unity's stock artwork. Created once
    // per semantic action, antialiased at 4x, then owned by StudioToolbarIcons.
    internal static class StudioSymbolIcons
    {
        internal static bool TryGet(string key, out Texture2D texture)
        {
            string symbol = Symbol(key);
            texture = symbol == null ? null : StudioToolbarIcons.Get("Studio symbol " + symbol + (EditorGUIUtility.isProSkin ? " dark" : " light"), () => Draw(symbol));
            return texture;
        }
        private static string Symbol(string key)
        {
            switch (key)
            {
                case "Animation/Rig Bones": return "rig-bones";
                case "File/Save Workspace As…": return "save-as";
                case "File/Export All… (FBX)": return "export-all";
                case "File/Export Selection… (FBX)": return "export-selection";
                case "File/Import FBX…": return "import";
                case "Select/Convert Selection/Vertices": return "convert-vertices";
                case "Select/Convert Selection/To Perimeter Vertices": return "convert-perimeter-vertices";
                case "Select/Convert Selection/Edges": return "convert-edges";
                case "Select/Convert Selection/To Perimeter Edges": return "convert-perimeter-edges";
                case "Select/Convert Selection/Faces": return "convert-faces";
                case "Select/Convert Selection/To Perimeter Faces": return "convert-perimeter-faces";
                case "Menu/Select/Convert Selection": return "convert-selection";
                case "Select/Convert Selection/Objects": return "convert-objects";
                case "Select/Contiguous Path": return "contiguous-path";
                case "Select/Hide Selection": return "hide";
                case "Select/Unhide Last Hidden": return "unhide";
                case "Select/Grow Selection": return "grow";
                case "Select/Shrink Selection": return "shrink";
                case "Select/Isolate Selected": return "isolate";
                case "Select/All": return "select-all";
                case "Select/Deselect": return "deselect";
                case "Select/Invert Selection": return "invert";
                case "Select/Frame Selection": return "frame";
                case "Select/Through Selection": return "through";
                case "Create/Empty Transform": return "empty";
                case "Create/Empty Child": return "child";
                case "Create/Anchor Point": return "anchor";
                case "Modify/Edit Pivot": return "pivot-edit";
                case "Modify/Center Pivot": return "pivot-center";
                case "Mesh Tools/Combine Meshes…": return "combine";
                case "Mesh Tools/Duplicate Face": return "duplicate-face";
                case "Mesh Tools/Duplicate Face as New Object": return "duplicate-face-object";
                case "Mesh Tools/Detach Face": return "detach-face";
                case "Mesh Tools/Split Selection": return "split";
                case "Mesh Tools/Bridge Edges": return "bridge";
                case "Mesh Tools/Circularize…": return "circularize";
                case "Mesh Tools/Weld…": return "weld-range";
                case "Mesh Tools/Bisect": return "bisect";
                case "Mesh Tools/Fracture": return "fracture";
                case "Mesh Tools/Extrude": return "extrude";
                case "Mesh Tools/Bevel…": return "bevel";
                case "Mesh Tools/Merge Vertices": return "merge";
                case "Mesh Tools/Target Weld": return "weld";
                case "Mesh Tools/Insert Edge Loop": return "loop";
                case "Mesh Tools/Multi-Cut": return "cut";
                case "Deform/Blend Shape": return "blend";
                case "Deform/Bake Current Pose": return "bake";
                case "Mesh Display/Soften": return "soften";
                case "Mesh Display/Soften…": return "soften-options";
                case "Mesh Display/Harden": return "harden";
                case "Mesh Display/X-ray Mode": return "xray";
                case "UV/UV Editor": return "uv-editor";
                case "Lighting/Bake Vertex AO (Red)…": return "ao";
                case "Viewport/Camera": return "camera-settings";
                case "Viewport/Lighting": return "light-settings";
                case "Viewport/BackfaceCulling": return "backface";
                case "Display/Mesh Statistics": return "statistics";
                case "Display/FPS Display": return "fps";
                case "Display/Tool Options": return "tool-options";
            }
            if (key.StartsWith("UV/UV Sets/UV ", StringComparison.Ordinal)) return "uv-" + key.Substring(key.Length - 1);
            if (key.StartsWith("Menu/", StringComparison.Ordinal))
            {
                if (key.Contains("Polygon Primitives")) return "primitive-group";
                if (key.Contains("Vehicle Part")) return "vehicle-group";
                if (key.Contains("Deform")) return "deform-group";
                if (key.Contains("UV Sets")) return "uv-group";
                if (key.Contains("Recents")) return "recent-group";
            }
            return null;
        }
        private sealed class Canvas
        {
            const int Scale=4, N=96;
            readonly Color[] pixels=new Color[N*N];
            // Neutral geometry, one action accent. Native primitive/shading/mode
            // artwork is resolved elsewhere and deliberately remains unchanged.
            internal readonly Color White=EditorGUIUtility.isProSkin?new Color(.82f,.82f,.82f,1):new Color(.22f,.22f,.22f,1);
            internal readonly Color Gray=EditorGUIUtility.isProSkin?new Color(.55f,.55f,.55f,1):new Color(.48f,.48f,.48f,1);
            internal readonly Color Accent=EditorGUIUtility.isProSkin?new Color(1,.75f,.34f,1):new Color(.64f,.33f,.025f,1);
            internal Color Gold=>Accent;
            internal void Line(float x,float y,float u,float v,Color color,float width=1.6f)
            {
                var a=new Vector2(x,y);var d=new Vector2(u-x,v-y);float len=d.sqrMagnitude;
                for(int py=Mathf.Max(0,(int)((Mathf.Min(y,v)-width)*Scale));py<Mathf.Min(N,(Mathf.Max(y,v)+width)*Scale+1);py++)
                for(int px=Mathf.Max(0,(int)((Mathf.Min(x,u)-width)*Scale));px<Mathf.Min(N,(Mathf.Max(x,u)+width)*Scale+1);px++)
                {var p=new Vector2((px+.5f)/Scale,(py+.5f)/Scale);float t=len>0?Mathf.Clamp01(Vector2.Dot(p-a,d)/len):0;if((p-a-d*t).sqrMagnitude<=width*width*.25f)pixels[py*N+px]=color;}
            }
            internal void Path(Color color,params float[] p){for(int i=2;i<p.Length;i+=2)Line(p[i-2],p[i-1],p[i],p[i+1],color);}
            internal void Rect(float x,float y,float w,float h,Color c){Path(c,x,y,x+w,y,x+w,y+h,x,y+h,x,y);}
            internal void Fill(float x,float y,float w,float h,Color c){for(int py=Mathf.Max(0,(int)(y*Scale));py<Mathf.Min(N,(y+h)*Scale);py++)for(int px=Mathf.Max(0,(int)(x*Scale));px<Mathf.Min(N,(x+w)*Scale);px++)pixels[py*N+px]=c;}
            internal void Circle(float x,float y,float r,Color c,bool filled=false)
            {if(filled){for(int py=Mathf.Max(0,(int)((y-r)*Scale));py<Mathf.Min(N,(y+r)*Scale);py++)for(int px=Mathf.Max(0,(int)((x-r)*Scale));px<Mathf.Min(N,(x+r)*Scale);px++)if(new Vector2((px+.5f)/Scale-x,(py+.5f)/Scale-y).sqrMagnitude<r*r)pixels[py*N+px]=c;}else Arc(x,y,r,0,360,c);}
            internal void Arc(float x,float y,float r,float start,float end,Color c){for(int i=0;i<40;i++){float a=Mathf.Lerp(start,end,i/40f)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(i+1)/40f)*Mathf.Deg2Rad;Line(x+r*Mathf.Cos(a),y+r*Mathf.Sin(a),x+r*Mathf.Cos(b),y+r*Mathf.Sin(b),c);}}
            internal void Arrow(float x,float y,float u,float v,Color c){Line(x,y,u,v,c);var d=new Vector2(x-u,y-v).normalized;var n=new Vector2(-d.y,d.x);Line(u,v,u+d.x*3+n.x*2,v+d.y*3+n.y*2,c);Line(u,v,u+d.x*3-n.x*2,v+d.y*3-n.y*2,c);}
            internal void Plus(float x,float y,Color c){Line(x-2.5f,y,x+2.5f,y,c);Line(x,y-2.5f,x,y+2.5f,c);}
            internal void Cube(bool shaded=false){if(shaded)Fill(5,5,14,11,Gray);Path(White,4,6,4,16,12,21,20,16,20,6,12,2,4,6);Path(White,4,16,12,12,20,16);Line(12,2,12,12,White);}
            internal void Grid(){Rect(3,4,17,16,White);Line(8,4,8,20,Gray);Line(14,4,14,20,Gray);Line(3,9,20,9,Gray);Line(3,15,20,15,Gray);}
            internal void Frame(){Path(White,3,15,3,20,8,20);Path(White,16,20,21,20,21,15);Path(White,3,9,3,4,8,4);Path(White,16,4,21,4,21,9);}
            internal void Gear(float x,float y){Circle(x,y,3,White);Circle(x,y,1,Gray,true);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Line(x+3*Mathf.Cos(a),y+3*Mathf.Sin(a),x+4*Mathf.Cos(a),y+4*Mathf.Sin(a),White);}}
            internal void Digit(int digit,float x,float y)
            {string[] rows={"111101101101111","010110010010111","111001111100111","111001111001111","101101111001001","111100111001111","111100111101111","111001001001001"};string data=rows[Mathf.Clamp(digit,0,7)];Fill(x-1,y-1,7,10,new Color(.18f,.2f,.22f,1));for(int row=0;row<5;row++)for(int col=0;col<3;col++)if(data[row*3+col]=='1')Fill(x+col*1.7f,y+(4-row)*1.7f,1.3f,1.3f,Gold);}
            internal Texture2D Texture()
            {var small=new Color[576];for(int y=0;y<24;y++)for(int x=0;x<24;x++){float a=0;var rgb=Vector3.zero;for(int dy=0;dy<Scale;dy++)for(int dx=0;dx<Scale;dx++){var c=pixels[(y*Scale+dy)*N+x*Scale+dx];rgb+=new Vector3(c.r,c.g,c.b)*c.a;a+=c.a;}small[y*24+x]=a>0?new Color(rgb.x/a,rgb.y/a,rgb.z/a,a/16):Color.clear;}var t=new Texture2D(24,24,TextureFormat.RGBA32,false);t.SetPixels(small);t.Apply(false,true);return t;}
        }
        private static Texture2D Draw(string symbol)
        {
            var c=new Canvas();var w=c.White;var a=c.Accent;var g=c.Gold;
            switch(symbol)
            {
                case "rig-bones":
                    c.Circle(5,19,2,w);c.Circle(19,5,2,w);
                    c.Path(w,6,17,9,10,17,6,14,14,6,17);c.Line(6,17,17,6,a);break;
                case "convert-vertices":case "convert-perimeter-vertices":
                    c.Rect(5,5,14,14,c.Gray);
                    foreach(var p in new[]{new Vector2(5,5),new Vector2(19,5),new Vector2(5,19),new Vector2(19,19)})c.Circle(p.x,p.y,2,a,true);
                    c.Circle(12,12,1.8f,symbol=="convert-vertices"?a:c.Gray,true);break;
                case "convert-edges":c.Rect(4,4,16,16,c.Gray);c.Line(4,12,20,12,a,2);break;
                case "convert-perimeter-edges":c.Rect(4,4,16,16,a);c.Line(4,12,20,12,c.Gray);break;
                case "convert-faces":c.Fill(7,7,10,10,a);c.Rect(4,4,16,16,w);break;
                case "convert-perimeter-faces":c.Fill(8,8,8,8,c.Gray);c.Rect(4,4,16,16,a);break;
                case "convert-objects":c.Path(w,4,6,4,17,12,21,20,17,20,6,12,2,4,6);c.Path(w,4,17,12,13,20,17);c.Line(12,2,12,13,a);break;
                case "convert-selection":c.Rect(4,4,9,9,w);c.Arrow(5,19,19,19,a);c.Rect(16,3,5,9,w);break;
                case "combine":
                    c.Rect(3,15,6,6,w);c.Rect(15,15,6,6,w);
                    c.Path(a,6,12,12,8,18,12);c.Line(12,8,12,5,a);c.Rect(7,2,10,4,w);break;
                case "duplicate-face":c.Rect(8,8,12,12,c.Gray);c.Rect(3,3,12,12,w);c.Plus(19,20,a);break;
                case "duplicate-face-object":
                    c.Rect(3,3,7,7,c.Gray);c.Path(w,12,8,12,17,17,20,22,17,22,8,17,5,12,8);
                    c.Path(w,12,17,17,14,22,17);c.Line(17,5,17,14,w);c.Arrow(4,18,9,18,a);break;
                case "detach-face":c.Path(w,4,4,19,4,19,10,10,10,10,19,4,19,4,4);c.Rect(14,14,6,6,a);break;
                case "split":c.Rect(3,5,6,14,w);c.Rect(15,5,6,14,w);c.Arrow(10,12,5,12,a);c.Arrow(14,12,19,12,a);break;
                case "bridge":c.Fill(3,4,3,16,w);c.Fill(18,4,3,16,w);c.Line(6,7,18,7,a,2);c.Line(6,17,18,17,a,2);break;
                case "circularize":c.Path(c.Gray,4,5,5,20,19,19,21,7,4,5);c.Circle(12,12,7,a);break;
                case "weld-range":for(int i=0;i<6;i++)c.Arc(12,12,9,i*60,i*60+31,a);c.Circle(8,12,2.4f,w,true);c.Circle(16,12,2.4f,w,true);c.Line(10,12,14,12,a,2);break;
                case "bisect":c.Path(w,3,7,3,21,17,21);c.Path(w,7,3,21,3,21,17);c.Line(3,3,21,21,a,2);break;
                case "fracture":c.Rect(3,3,18,18,w);c.Path(a,9,3,13,11,8,21);c.Path(a,13,11,21,15);break;
                case "extrude":c.Rect(4,4,16,8,w);c.Arrow(12,10,12,22,a);break;
                case "bevel":c.Path(w,4,4,4,14,10,20,20,20,20,4,4,4);c.Line(4,14,10,20,a,2);break;
                case "merge":c.Circle(3,12,2,w,true);c.Circle(21,12,2,w,true);c.Arrow(6,12,10,12,a);c.Arrow(18,12,14,12,a);c.Circle(12,12,2,a,true);break;
                case "weld":c.Circle(4,12,2,w);c.Circle(18,12,4,w);c.Arrow(7,12,16,12,a);break;
                case "loop":c.Rect(4,4,16,16,w);c.Line(12,4,12,20,a,2);break;
                case "cut":c.Rect(4,4,16,16,w);c.Path(a,3,8,10,15,16,11,21,18);break;
                case "blend":c.Rect(3,6,7,12,c.Gray);c.Path(w,15,5,13,18,20,20,22,8,15,5);c.Arrow(5,3,19,3,a);break;
                case "bake":c.Path(w,4,18,9,13,15,18,20,13);c.Arrow(12,13,12,6,a);c.Rect(4,3,16,3,w);break;
                case "soften":case "soften-options":c.Path(w,4,4,4,12);c.Arc(12,12,8,90,180,a);c.Line(12,20,20,20,w);if(symbol.EndsWith("options"))c.Gear(18,6);break;
                case "harden":c.Path(w,4,3,4,20,21,20);c.Arrow(4,11,11,11,a);c.Arrow(13,20,13,13,g);break;
                case "wire":c.Cube();break;
                case "shaded-wire":c.Cube(true);c.Path(a,4,6,12,12,20,6);break;
                case "shaded":c.Circle(12,12,9,w,true);c.Circle(15,10,7,c.Gray,true);c.Arc(12,12,9,0,360,w);c.Circle(8,16,2,w,true);break;
                case "xray":c.Path(c.Gray,4,5,4,18,17,18,17,5,4,5);c.Rect(8,3,13,13,a);for(int i=5;i<18;i+=4)c.Line(i,11,i+2,11,w);c.Circle(14,10,3,g);break;
                case "backface":c.Cube();c.Path(g,4,6,12,12,12,2);c.Arrow(19,7,23,4,a);break;
                case "uv-editor":c.Grid();c.Path(a,3,4,11,12,15,8,20,20);c.Circle(11,12,1.7f,g,true);break;
                case "select-all":c.Frame();c.Fill(7,7,10,10,a);break;
                case "deselect":c.Frame();c.Line(8,8,16,16,a,2);c.Line(8,16,16,8,a,2);break;
                case "invert":c.Rect(4,5,16,14,w);c.Fill(5,6,7,12,c.Gray);c.Fill(13,6,6,12,a);break;
                case "frame":c.Frame();c.Plus(12,12,a);break;
                case "through":c.Rect(3,7,12,13,c.Gray);c.Rect(9,3,12,13,w);for(int i=4;i<21;i+=5)c.Line(12,i,12,i+2,a,2);break;
                case "contiguous-path":c.Path(a,4,5,12,5,12,19,20,19);c.Circle(4,5,2,w,true);c.Circle(20,19,2,w,true);break;
                case "hide":c.Path(w,2,12,7,18,17,18,22,12,17,6,7,6,2,12);c.Line(4,3,20,21,a,2);break;
                case "unhide":c.Path(w,2,12,7,18,17,18,22,12,17,6,7,6,2,12);c.Circle(12,12,3,a,true);break;
                case "grow":c.Rect(7,7,10,10,w);c.Path(a,3,14,3,21,10,21);c.Path(a,14,3,21,3,21,10);break;
                case "shrink":c.Rect(3,3,18,18,c.Gray);c.Path(a,6,12,6,6,12,6);c.Path(a,12,18,18,18,18,12);break;
                case "isolate":c.Frame();c.Rect(9,8,6,8,a);c.Line(5,12,7,12,c.Gray);c.Line(17,12,19,12,c.Gray);break;
                case "empty":c.Line(4,12,20,12,w);c.Line(12,4,12,20,w);c.Circle(12,12,3,a);break;
                case "child":c.Path(w,4,21,4,10,13,10);c.Rect(1,18,6,5,w);c.Rect(13,4,9,10,a);c.Plus(17.5f,9,g);break;
                case "anchor":c.Circle(12,12,6,g);c.Line(12,2,12,22,g);c.Line(12,12,22,12,g);c.Line(2,12,12,12,a);break;
                case "pivot-center":c.Frame();c.Arrow(3,12,9,12,a);c.Arrow(21,12,15,12,a);c.Circle(12,12,2,g,true);break;
                case "pivot-edit":c.Line(3,12,17,12,w);c.Line(10,5,10,20,w);c.Circle(10,12,2,g);c.Path(a,14,3,20,9,22,7,16,1,14,3);break;
                case "save-as":c.Rect(4,3,15,18,w);c.Rect(7,13,8,7,w);c.Rect(7,4,9,6,c.Gray);c.Plus(20,4,g);break;
                case "import":c.Path(w,3,16,3,3,21,3,21,16);c.Arrow(12,22,12,8,a);break;
                case "export-all":c.Rect(3,3,12,11,c.Gray);c.Rect(6,6,12,11,w);c.Arrow(12,12,12,23,a);break;
                case "export-selection":c.Frame();c.Arrow(12,9,12,22,g);break;
                case "camera-settings":c.Rect(3,8,13,11,w);c.Path(w,16,16,22,20,22,9,16,12);c.Gear(9,5);break;
                case "light-settings":c.Circle(12,16,6,w);c.Rect(9,7,6,4,g);c.Gear(17,5);break;
                case "ao":c.Rect(4,4,16,5,w);c.Arc(12,10,8,0,180,w);for(int i=5;i<=19;i+=3)c.Circle(i,7,1,g,true);break;
                case "statistics":c.Line(3,3,21,3,w);c.Fill(5,4,3,7,a);c.Fill(11,4,3,12,a);c.Fill(17,4,3,18,a);break;
                case "fps":c.Circle(12,11,9,w);c.Line(9,22,15,22,w);c.Arrow(12,11,17,16,g);c.Circle(12,11,1.5f,a,true);break;
                case "tool-options":c.Rect(3,3,18,18,w);c.Line(6,16,18,16,a);c.Line(6,9,18,9,a);c.Rect(9,14,3,4,g);c.Rect(14,7,3,4,g);break;
                case "primitive-group":c.Cube();c.Plus(20,4,g);break;
                case "vehicle-group":c.Circle(6,6,3,w);c.Circle(18,6,3,w);c.Path(a,6,6,10,15,16,15,18,6,6,6);break;
                case "deform-group":c.Path(w,3,5,4,20,12,16,20,20,21,5);c.Path(a,4,10,12,6,20,10);break;
                case "uv-group":c.Grid();c.Plus(19,4,g);break;
                case "recent-group":c.Rect(3,6,14,13,w);c.Circle(16,7,6,a);c.Path(g,16,11,16,7,19,7);break;
                default:if(symbol.StartsWith("uv-")){c.Grid();c.Digit(symbol[symbol.Length-1]-'0',16,3);}break;
            }
            return c.Texture();
        }
    }
}
