using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioPreserveMoveUVValidation
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static int checks;static StudioModelEditor editor;static StudioModelDocument doc;
        static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../PreserveUVs/"+n));
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=Next;}
        static void Next(){EditorApplication.update-=Next;Run();}
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
        static object Call(string name,params object[] args)=>typeof(StudioModelEditor).GetMethod(name,F).Invoke(editor,args);
        static void Set(string name,object value)=>typeof(StudioModelEditor).GetField(name,F).SetValue(editor,value);
        static StudioModelDocument.Node Plane()
        {
            var mesh=new Mesh{name="UV-preserving plane"};mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};mesh.triangles=new[]{0,2,1,0,3,2};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            mesh.SetUVs(2,mesh.vertices.Select(v=>new Vector3(v.x*2,v.y,.5f*v.x)).ToList());mesh.SetUVs(7,mesh.vertices.Select(v=>new Vector4(v.x,v.y,v.x*2,-v.y)).ToList());
            StudioSubdivisionData.SetUV1(mesh,3,mesh.vertices.Select(v=>new Vector4(v.x+v.y,0,0,0)));mesh.RecalculateNormals();mesh.RecalculateBounds();
            return new StudioModelDocument.Node{name="UV plane",mesh=mesh,cutPolygons=new[]{0,4,0,3,2,1},cutTriangles=mesh.triangles,geometryVertices=new[]{0,1,2,3}};
        }
        static void Setup(int count=1)
        {
            editor?.Dispose();doc=ScriptableObject.CreateInstance<StudioModelDocument>();for(int i=0;i<count;i++)doc.nodes.Add(Plane());
            editor=new StudioModelEditor(()=>{});editor.Load(doc);editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);editor.Components.UnionWith(new[]{1,2});Set("tool",Tool.Move);Set("preserveMoveUVs",true);
        }
        static object Capture()
        {var drag=Call("CaptureMoveDrag",0,Vector3.zero,Quaternion.identity,editor.SelectedVertices(),doc.nodes[0].mesh.vertices);Set("moveDrag",drag);return drag;}
        static bool Near(Vector2 a,Vector2 b)=>(a-b).sqrMagnitude<1e-10f;
        static void AffineCheck(Mesh mesh)
        {
            var p=mesh.vertices;var uv=mesh.uv;Check(Enumerable.Range(0,p.Length).All(i=>Near(uv[i],new Vector2(p[i].x,p[i].y))),"UV0 does not preserve source planar mapping");
            var uv2=new List<Vector3>();mesh.GetUVs(2,uv2);Check(Enumerable.Range(0,p.Length).All(i=>(uv2[i]-new Vector3(p[i].x*2,p[i].y,p[i].x*.5f)).sqrMagnitude<1e-10f),"UV2 dimension/projection lost");
            var uv7=new List<Vector4>();mesh.GetUVs(7,uv7);Check(Enumerable.Range(0,p.Length).All(i=>(uv7[i]-new Vector4(p[i].x,p[i].y,p[i].x*2,-p[i].y)).sqrMagnitude<1e-10f),"UV7 dimensions/projection lost");
            Check(mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord3)==1&&mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord2)==3&&mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord7)==4,"UV dimensions changed");
        }
        static Color32[] Render(Mesh mesh,Material material,string name)
        {
            var old=RenderTexture.active;var rt=RenderTexture.GetTemporary(128,128,0);var image=new Texture2D(128,128,TextureFormat.RGBA32,false);
            try{RenderTexture.active=rt;GL.Clear(true,true,Color.magenta);GL.PushMatrix();try{GL.LoadProjectionMatrix(Matrix4x4.Ortho(0,1,0,1,-1,1));GL.modelview=Matrix4x4.identity;if(!material.SetPass(0))throw new Exception("Checker pass unavailable");Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}finally{GL.PopMatrix();}
                image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();File.WriteAllBytes(Out(name),image.EncodeToPNG());return image.GetPixels32();}
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        static Color32[] RenderTilted(Mesh mesh,Material material,string name)
        {
            var old=RenderTexture.active;var rt=RenderTexture.GetTemporary(192,128,0);var image=new Texture2D(192,128,TextureFormat.RGBA32,false);
            try{RenderTexture.active=rt;GL.Clear(true,true,Color.magenta);GL.PushMatrix();try{GL.LoadProjectionMatrix(Matrix4x4.Ortho(0,1.5f,0,1,-3,3));GL.modelview=Matrix4x4.identity;material.SetPass(0);var p=mesh.vertices;var normal=Vector3.Cross(p[1]-p[0],p[3]-p[0]);Graphics.DrawMeshNow(mesh,Matrix4x4.Rotate(Quaternion.FromToRotation(normal,Vector3.forward)));}finally{GL.PopMatrix();}
                image.ReadPixels(new Rect(0,0,192,128),0,0);image.Apply();File.WriteAllBytes(Out(name),image.EncodeToPNG());return image.GetPixels32();}
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        static void SurfaceChecks(Material material,Color32[] baseline)
        {
            foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(27,53,19),Quaternion.Euler(90,0,0)})
            {
                var node=Plane();var mesh=node.mesh;var source=mesh.vertices.Select(v=>rotation*v).ToArray();mesh.vertices=source;
                var plan=StudioMoveUVPreservation.Capture(mesh,source,new[]{1,2});var moved=source.ToArray();
                moved[1]=rotation*new Vector3(Mathf.Sqrt(1.25f),0,1);moved[2]=rotation*new Vector3(Mathf.Sqrt(1.25f),1,1);
                plan.Evaluate(moved);plan.Publish(mesh);Check(Near(mesh.uv[1],new Vector2(1.5f,0))&&Near(mesh.uv[2],new Vector2(1.5f,1)),"Tilted face lost texel density or gained shear");Check(Near(mesh.uv[0],Vector2.zero)&&Near(mesh.uv[3],Vector2.up),"Stationary UV anchors moved");
                // A rigid hinge changes orientation but not surface edge lengths.
                moved[1]=rotation*new Vector3(.5f,0,Mathf.Sqrt(.75f));moved[2]=rotation*new Vector3(.5f,1,Mathf.Sqrt(.75f));plan.Evaluate(moved);plan.Publish(mesh);
                Check(Near(mesh.uv[1],Vector2.right)&&Near(mesh.uv[2],Vector2.one),"Rigid face tilt compressed UVs");plan.Evaluate(source);plan.Publish(mesh);Check(Near(mesh.uv[1],Vector2.right),"Tilt reversal accumulated drift");UnityEngine.Object.DestroyImmediate(mesh);
            }
            var renderNode=Plane();var renderMesh=renderNode.mesh;var original=renderMesh.vertices;var uv=renderMesh.uv;
            var evaluated=original.ToArray();evaluated[1]=new Vector3(Mathf.Sqrt(1.25f),0,1);evaluated[2]=new Vector3(Mathf.Sqrt(1.25f),1,1);
            var preserve=StudioMoveUVPreservation.Capture(renderMesh,original,new[]{1,2});preserve.Evaluate(evaluated);preserve.Publish(renderMesh);renderMesh.vertices=evaluated;renderMesh.RecalculateBounds();
            var corrected=RenderTilted(renderMesh,material,"checker-tilted-preserved.png");renderMesh.uv=uv;var projected=RenderTilted(renderMesh,material,"checker-tilted-original-uv.png");int match=0,different=0,drawn=0;
            for(int y=0;y<8;y++)for(int x=0;x<12;x++){int i=(y*16+6)*192+x*16+6;var expected=baseline[(y*16+6)*128+(x%8)*16+6];if(corrected[i].Equals(expected))match++;if(!projected[i].Equals(expected))different++;if(projected[i].r==projected[i].g&&corrected[i].r==corrected[i].g)drawn++;}
            Check(drawn==96,"Tilted texture control did not render");Check(match==96,"Tilted checker squares were not consistently sized");Check(different>20,"Original-UV tilted control did not expose stretch");UnityEngine.Object.DestroyImmediate(renderMesh);
        }
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;File.Delete(Out("request.txt"));
            Material material=null;Texture2D checker=null;
            try{
                string asset="Assets/PreserveUVsChecker_"+Guid.NewGuid().ToString("N")+".shader";
                string shader="Shader \"Hidden/StudioPreserveUVCheck\" { Properties { _MainTex(\"Texture\",2D)=\"white\"{} } SubShader { Pass { Cull Off ZWrite Off ZTest Always CGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\nsampler2D _MainTex; struct app {float4 vertex:POSITION;float2 uv:TEXCOORD0;};struct vary {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};vary vert(app v){vary o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}fixed4 frag(vary i):SV_Target{return tex2D(_MainTex,i.uv);}\nENDCG } } }";
                File.WriteAllText(Path.Combine(Application.dataPath,asset.Substring(7)),shader);AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport);material=new Material(AssetDatabase.LoadAssetAtPath<Shader>(asset));
                checker=new Texture2D(8,8,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};checker.SetPixels(Enumerable.Range(0,64).Select(i=>(i%8+i/8)%2==0?Color.white:Color.black).ToArray());checker.Apply();material.mainTexture=checker;
                Setup();var mesh=doc.nodes[0].mesh;var originalUV=mesh.uv.ToArray();var originalVertices=mesh.vertices.ToArray();var triangles=mesh.triangles.ToArray();
                var baseline=Render(mesh,material,"checker-baseline.png");Capture();Call("ApplyMoveDrag",Vector3.right*.5f);AffineCheck(mesh);Check(mesh.vertices[0]==originalVertices[0]&&mesh.vertices[3]==originalVertices[3],"Unselected geometry moved");
                Check(mesh.triangles.SequenceEqual(triangles)&&new StudioMeshCuts(doc.nodes[0]).Faces.Count==1,"Authored topology changed");
                var preserved=Render(mesh,material,"checker-preserved.png");
                int same=0,drawn=0;for(int y=0;y<8;y++)for(int x=0;x<8;x++){int i=(int)((y+.37f)*16)*128+(int)((x+.37f)*16);if(baseline[i].Equals(preserved[i]))same++;if(baseline[i].r==baseline[i].g)drawn++;}
                Check(drawn==64,"Checker mesh did not render");Check(same==64,"Preserve UVs changed fixed-position texture samples");
                var preservedUV=mesh.uv;mesh.uv=originalUV;var stretched=Render(mesh,material,"checker-disabled.png");mesh.uv=preservedUV;
                int changed=0,drawnOff=0;for(int y=0;y<8;y++)for(int x=0;x<8;x++){int i=(int)((y+.37f)*16)*128+(int)((x+.37f)*16);if(!baseline[i].Equals(stretched[i]))changed++;if(stretched[i].r==stretched[i].g)drawnOff++;}
                Check(drawnOff==64,"Unpreserved checker control did not render");Check(changed>10,"Unpreserved control did not expose expected texture stretching");
                SurfaceChecks(material,baseline);
                Call("ApplyMoveDrag",Vector3.right);Call("ApplyMoveDrag",Vector3.right*.5f);AffineCheck(mesh);Call("ApplyMoveDrag",Vector3.zero);Check(mesh.uv.SequenceEqual(originalUV)&&mesh.vertices.SequenceEqual(originalVertices),"Returning drag to zero did not restore source");
                Call("ApplyMoveDrag",Vector3.right*.5f);Call("FlushModifierEdits");Check(doc.nodes[0].modifiers.operations.Last().kind=="Direct","UV edit collapsed into position-only history");
                var evaluated=StudioModifierEvaluator.Evaluate(doc.nodes[0].modifiers.Copy());try{AffineCheck(evaluated.mesh);}finally{UnityEngine.Object.DestroyImmediate(evaluated.mesh);}
                UnityEditor.Undo.FlushUndoRecordObjects();StudioUndo.PerformUndo();Check(mesh.uv.SequenceEqual(originalUV)&&mesh.vertices.SequenceEqual(originalVertices),"Undo did not restore geometry and UVs");StudioUndo.PerformRedo();AffineCheck(mesh);
                Setup();mesh=doc.nodes[0].mesh;Set("preserveMoveUVs",false);Capture();Call("ApplyMoveDrag",Vector3.right*.5f);Check(mesh.uv.SequenceEqual(originalUV),"Disabled Preserve UVs changed UVs");Call("FlushModifierEdits");
                Setup(2);editor.SelectObject(1,true);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);foreach(int n in new[]{0,1}){var target=Call("ComponentTargetAt",n,true);target.GetType().GetField("mode",F).SetValue(target,StudioModelEditor.ComponentMode.Vertex);var selected=(HashSet<int>)target.GetType().GetField("selection",F).GetValue(target);selected.Clear();selected.UnionWith(new[]{1,2});}Set("preserveMoveUVs",true);Set("tool",Tool.Move);Call("BindComponentTarget",editor.Active);
                var gesture=Call("CaptureMultiComponentGesture",new List<int>{0,1},Vector3.zero,Quaternion.identity,0);Call("ApplyMultiComponentGesture",gesture,Vector3.right*.5f,Quaternion.identity,Vector3.one);foreach(var node in doc.nodes){Check(Mathf.Abs(node.mesh.vertices[1].x-1.5f)<1e-6f,"A multi-mesh target did not move");AffineCheck(node.mesh);}Call("FlushModifierEdits");
                var seam=new Mesh();seam.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.zero,Vector3.up,Vector3.left};seam.triangles=new[]{0,1,2,3,4,5};seam.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,new Vector2(10,20),new Vector2(10,23),new Vector2(8,20)};
                var points=seam.vertices;var plan=StudioMoveUVPreservation.Capture(seam,points,new[]{0,3});var moved=points.ToArray();moved[0]+=Vector3.right*.1f;moved[3]+=Vector3.right*.1f;plan.Evaluate(moved);plan.Publish(seam);Check(Near(seam.uv[0],new Vector2(.1f,0))&&Near(seam.uv[3],new Vector2(10.2f,20)),"UV seam charts were mixed");UnityEngine.Object.DestroyImmediate(seam);
                File.WriteAllText(Out("result.txt"),"PASS "+checks+" native checks: planar and tilted surface charts in three orientations, fixed anchors/density/shear, rigid hinge/reversal, single/multi Move, UV dimensions/seams, Direct replay, Undo/Redo and native planar/tilted checker pixels with drawn controls. No exiting fixture or working-project/profile changes.");
            }catch(Exception e){File.WriteAllText(Out("result.txt"),"FAIL "+checks+": "+e);Debug.LogException(e);}
            finally{editor?.Dispose();editor=null;if(material)UnityEngine.Object.DestroyImmediate(material);if(checker)UnityEngine.Object.DestroyImmediate(checker);}
        }
    }
}
