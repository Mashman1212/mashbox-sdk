using System;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Small code-native symbols fill gaps in Unity's stock artwork. Created once
    // per semantic action, antialiased at 4x, then owned by StudioToolbarIcons.
    internal static class StudioSymbolIcons
    {
        internal static bool TryGet(string key, out Texture2D texture)
        {
            string symbol = Symbol(key);
            texture = symbol == null ? null : StudioToolbarIcons.Get("Studio symbol " + symbol, () => Draw(symbol));
            return texture;
        }
        private static string Symbol(string key)
        {
            switch (key)
            {
                case "File/Save Workspace As…": return "save-as";
                case "File/Export All… (FBX)": return "export-all";
                case "File/Export Selection… (FBX)": return "export-selection";
                case "File/Import FBX…": return "import";
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
            internal readonly Color White=new Color(.88f,.9f,.92f,1), Gray=new Color(.4f,.44f,.48f,1), Accent=new Color(.25f,.8f,.91f,1), Gold=new Color(1,.68f,.25f,1);
            internal void Line(float x,float y,float u,float v,Color color,float width=1.3f)
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
                case "extrude":c.Path(w,4,5,15,5,20,9,9,9,4,5);c.Path(c.Gray,4,5,4,12,9,16,20,16,20,9);c.Arrow(12,11,12,22,g);break;
                case "bevel":c.Path(w,4,3,4,14,10,20,21,20,21,3,4,3);c.Line(4,14,10,20,g,2);c.Line(4,20,4,17,c.Gray);c.Line(4,20,7,20,c.Gray);break;
                case "merge":foreach(var p in new[]{new Vector2(4,4),new Vector2(20,4),new Vector2(4,20),new Vector2(20,20)}){c.Circle(p.x,p.y,1.7f,w,true);c.Arrow(p.x,p.y,12+(p.x-12)*.3f,12+(p.y-12)*.3f,a);}c.Circle(12,12,2.4f,g,true);break;
                case "weld":c.Circle(4,12,2,w);c.Arrow(7,12,14,12,g);c.Circle(18,12,4,a);c.Circle(18,12,1.5f,w,true);break;
                case "loop":c.Cube();c.Path(g,4,11,12,7,20,11,12,16,4,11);break;
                case "cut":c.Rect(4,4,16,16,w);c.Path(g,3,8,10,15,15,11,21,18);c.Circle(10,15,1.8f,g,true);c.Circle(15,11,1.8f,g,true);break;
                case "blend":c.Path(c.Gray,3,8,3,19,10,19,10,8,3,8);c.Path(a,14,8,12,17,18,21,22,12,14,8);c.Arrow(5,4,20,4,g);break;
                case "bake":c.Path(w,4,18,9,12,14,17,19,12);c.Circle(9,12,1.8f,g,true);c.Arrow(12,12,12,5,g);c.Rect(4,2,16,3,w);break;
                case "soften":case "soften-options":c.Path(w,3,4,3,13);c.Arc(12,13,9,90,180,w);c.Line(12,22,21,22,w);c.Path(a,6,6,6,13,8,17,12,19,18,19);if(symbol.EndsWith("options"))c.Gear(18,6);break;
                case "harden":c.Path(w,4,3,4,20,21,20);c.Arrow(4,11,11,11,a);c.Arrow(13,20,13,13,g);break;
                case "wire":c.Cube();break;
                case "shaded-wire":c.Cube(true);c.Path(a,4,6,12,12,20,6);break;
                case "shaded":c.Circle(12,12,9,w,true);c.Circle(15,10,7,c.Gray,true);c.Arc(12,12,9,0,360,w);c.Circle(8,16,2,w,true);break;
                case "xray":c.Path(c.Gray,4,5,4,18,17,18,17,5,4,5);c.Rect(8,3,13,13,a);for(int i=5;i<18;i+=4)c.Line(i,11,i+2,11,w);c.Circle(14,10,3,g);break;
                case "backface":c.Cube();c.Path(g,4,6,12,12,12,2);c.Arrow(19,7,23,4,a);break;
                case "uv-editor":c.Grid();c.Path(a,3,4,11,12,15,8,20,20);c.Circle(11,12,1.7f,g,true);break;
                case "select-all":c.Frame();c.Fill(7,8,10,8,a);break;
                case "deselect":c.Frame();c.Line(7,12,17,12,g,2);break;
                case "invert":c.Rect(4,5,16,14,w);c.Fill(5,6,7,12,a);c.Arrow(7,22,17,22,g);c.Arrow(17,2,7,2,g);break;
                case "frame":c.Frame();c.Plus(12,12,a);break;
                case "through":c.Path(w,2,12,7,18,16,18,22,12,16,6,7,6,2,12);c.Circle(12,12,3,a);c.Line(12,1,12,4,g);c.Line(12,20,12,23,g);break;
                case "empty":c.Line(4,12,20,12,w);c.Line(12,4,12,20,w);c.Circle(12,12,3,a);break;
                case "child":c.Path(w,4,21,4,10,13,10);c.Rect(1,18,6,5,w);c.Rect(13,4,9,10,a);c.Plus(17.5f,9,g);break;
                case "anchor":c.Circle(12,12,6,g);c.Line(12,2,12,22,g);c.Line(12,12,22,12,g);c.Line(2,12,12,12,new Color(.18f,.38f,.8f,1));break;
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
