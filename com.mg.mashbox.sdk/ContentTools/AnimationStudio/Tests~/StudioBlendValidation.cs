#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StudioBlendValidation
{
    static readonly List<string> Lines=new List<string>();
    static void Check(bool pass,string message) { if(!pass)throw new Exception(message);Lines.Add("PASS "+message); }
    static Mesh Triangle(float height,bool permuted=false)
    {
        var mesh=new Mesh { name="Test shape" };
        mesh.vertices=permuted?new[]{new Vector3(0,height,1),new Vector3(0,height,0),new Vector3(1,height,0)}:new[]{new Vector3(0,height,0),new Vector3(1,height,0),new Vector3(0,height,1)};
        mesh.triangles=permuted?new[]{1,0,2}:new[]{0,2,1};
        mesh.uv=permuted?new[]{new Vector2(0,height+1),new Vector2(0,height),new Vector2(1,height)}:new[]{new Vector2(0,height),new Vector2(1,height),new Vector2(0,height+1)};
        mesh.uv2=permuted?new[]{new Vector2(3,3),new Vector2(1,3),new Vector2(2,3)}:new[]{new Vector2(1,3),new Vector2(2,3),new Vector2(3,3)};
        mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
    }
    public static void Run()
    {
        Lines.Clear();
        StudioModelDocument doc=null,copy=null;Mesh output=null;Mesh[] input=null;
        string saved="Assets/StudioBlendValidation_Temporary.asset";
        try
        {
            input=new[]{Triangle(0),Triangle(2,true),Triangle(4)};
            var blend=StudioBlendShape.Capture(input,new[]{"Base","Middle","Last"},Enumerable.Repeat(Matrix4x4.identity,3).ToArray());
            output=Object.Instantiate(input[0]);
            foreach(float master in new[]{0f,.25f,.5f,.75f,1f})
            {
                blend.SetMaster(master);blend.Apply(output);
                Check(Mathf.Abs(output.vertices[0].y-master*4)<1e-5f,"Sequential position "+master);
                Check(Mathf.Abs(output.uv[0].y-master*4)<1e-5f,"Sequential UV "+master);
                Check(output.normals.All(n=>Mathf.Abs(n.magnitude-1)<1e-5f),"Normalized normals "+master);
            }
            blend.SetMaster(.75f);var weights=blend.EffectiveWeights();Check(weights.SequenceEqual(new[]{.5f,.5f}),"Only adjacent targets interpolate equally at segment midpoint");
            blend.SetWeight(0,.2f);Check(!blend.sequential&&Mathf.Abs(blend.weights[1]-.5f)<1e-6f,"Manual editing starts from current master weights");
            blend.Apply(output);Check(Mathf.Abs(output.vertices[0].y-2.4f)<1e-5f,"Independent weights add target deltas");
            blend.SetMaster(2);Check(blend.master==1&&blend.sequential,"Master clamps and restores sequence mode");
            var original=input[2].triangles;input[2].triangles=new[]{0,1,2};bool rejected=false;
            try { StudioBlendShape.Capture(input,new[]{"A","B","C"},Enumerable.Repeat(Matrix4x4.identity,3).ToArray()); }catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Rejects reversed winding despite equal vertex counts");input[2].triangles=original;
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();
            for(int i=0;i<3;i++)doc.nodes.Add(new StudioModelDocument.Node {name="Test"+i,mesh=input[i],position=new Vector3(i*10,0,0)});
            Undo.IncrementCurrentGroup();int created=doc.CreateBlendShape(new[]{0,1,2});Undo.FlushUndoRecordObjects();
            Check(doc.nodes.Count==4&&doc.nodes.Take(3).All(n=>!n.active),"Creates independent result and hides source shapes");
            var createdMesh=doc.nodes[created].mesh;
            Undo.PerformUndo();Check(doc.nodes.Count==3&&doc.nodes.All(n=>n.active),"Undo creation restores source visibility");
            Undo.PerformRedo();Check(doc.nodes.Count==4&&doc.nodes[3].mesh==createdMesh,"Redo restores complete blend result");
            Undo.IncrementCurrentGroup();doc.SetBlendMaster(created,.5f);Undo.FlushUndoRecordObjects();
            Check(Mathf.Abs(doc.nodes[created].mesh.vertices[0].y-2)<1e-5f&&Mathf.Abs(doc.nodes[created].mesh.vertices[0].x)<1e-5f,"Target layout offsets do not move the blended object");
            Undo.PerformUndo();Check(doc.nodes[created].blendShape.master==0&&Mathf.Abs(doc.nodes[created].mesh.vertices[0].y)<1e-5f,"Undo restores both weights and geometry");
            Undo.PerformRedo();Check(doc.nodes[created].blendShape.master==.5f&&Mathf.Abs(doc.nodes[created].mesh.vertices[0].y-2)<1e-5f,"Redo restores both weights and geometry");
            Check(input[0].vertices[0]==Vector3.zero&&input[1].vertices[0]==new Vector3(0,2,1),"Source meshes remain unchanged");
            copy=doc.SaveAs(saved);AssetDatabase.ImportAsset(saved,ImportAssetOptions.ForceSynchronousImport);
            copy=AssetDatabase.LoadAssetAtPath<StudioModelDocument>(saved);
            Check(copy.nodes.Take(3).All(n=>n.blendShape==null),"Ordinary meshes retain null deformation data after reload");
            Check(copy.nodes[3].blendShape.shapes.Length==3&&copy.nodes[3].blendShape.master==.5f,"Saved workspace retains ordered snapshots and master");
            copy.SetBlendMaster(3,1);Check(Mathf.Abs(copy.nodes[3].mesh.vertices[0].y-4)<1e-5f&&doc.nodes[3].blendShape.master==.5f,"Save As is independent and still editable");
            var duplicate=typeof(StudioModelDocument).GetMethod("DuplicateObjects",BindingFlags.NonPublic|BindingFlags.Instance);
            var duplicates=(int[])duplicate.Invoke(doc,new object[]{new[]{3}});doc.SetBlendMaster(duplicates[0],1);
            Check(doc.nodes[3].blendShape.master==.5f,"Duplicate has independent deformation snapshots and weights");
            copy.BakeBlendShape(3);Check(copy.nodes[3].blendShape==null&&Mathf.Abs(copy.nodes[3].mesh.vertices[0].y-4)<1e-5f,"Bake keeps current geometry and removes controls");
            Lines.Add("SUCCESS");
        }
        catch(Exception e){Lines.Add("FAIL "+e);Debug.LogException(e);}
        finally
        {
            if(doc){Undo.ClearUndo(doc);foreach(var n in doc.nodes)if(n.mesh){Undo.ClearUndo(n.mesh);Object.DestroyImmediate(n.mesh);}Object.DestroyImmediate(doc);}
            if(copy)Undo.ClearUndo(copy);
            if(AssetDatabase.LoadMainAssetAtPath(saved))AssetDatabase.DeleteAsset(saved);
            if(output)Object.DestroyImmediate(output);
            if(input!=null)foreach(var mesh in input)if(mesh)Object.DestroyImmediate(mesh);
            File.WriteAllLines(Path.Combine(Path.GetTempPath(),"studio-blend-test.txt"),Lines);
        }
    }
}
#endif
