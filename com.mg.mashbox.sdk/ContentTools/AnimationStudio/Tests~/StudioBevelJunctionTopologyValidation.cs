using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object=UnityEngine.Object;

public static class StudioBevelJunctionTopologyValidation
{
    // Independent sequential-bevel reference: L pair, then both the
    // remaining cube edge AND the new diagonal join. Not derived from Studio.
    static readonly int[][] Reference={new[]{0,1,13,12,2},new[]{3,7,6,1,0},new[]{3,0,2,5,4},new[]{3,4,9,8,7},new[]{8,9,11,10},new[]{10,11,12,13},new[]{4,5,11,9},new[]{8,10,6,7},new[]{2,12,11,5},new[]{1,6,10,13}};
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static List<(int,int)> Edges(StudioMeshCuts t)
    {
        var seen=new HashSet<(Vector3,Vector3)>();var result=new List<(int,int)>();
        foreach(var f in t.Faces)for(int i=0;i<f.Vertices.Count;i++){int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(t.Positions[a],t.Positions[b])))result.Add((a,b));}return result;
    }
    static string Cycle(int[] face)
    {
        string best=null;for(int start=0;start<face.Length;start++)foreach(int step in new[]{-1,1}){string key=string.Join(",",Enumerable.Range(0,face.Length).Select(i=>face[(start+step*i+face.Length)%face.Length]));if(best==null||string.CompareOrdinal(key,best)<0)best=key;}return best;
    }
    static void MatchReference(StudioModelDocument.Node node)
    {
        var t=new StudioMeshCuts(node);var ids=new Dictionary<Vector3,int>();
        int Point(int i){var p=t.Positions[i];if(!ids.TryGetValue(p,out int id)){id=ids.Count;ids.Add(p,id);}return id;}
        var faces=t.Faces.Select(f=>f.Vertices.Select(Point).ToArray()).ToArray();
        Check(ids.Count==14&&faces.Length==10,"Junction reference graph requires 14 vertices/10 faces; got "+ids.Count+"/"+faces.Length);
        bool[,] Adjacency(int[][] fs){var a=new bool[14,14];foreach(var f in fs)for(int i=0;i<f.Length;i++)a[f[i],f[(i+1)%f.Length]]=a[f[(i+1)%f.Length],f[i]]=true;return a;}
        string[] Colors(int[][] fs)=>Enumerable.Range(0,14).Select(v=>string.Join(",",fs.Where(f=>f.Contains(v)).Select(f=>f.Length).OrderBy(n=>n))).ToArray();
        var actual=Adjacency(faces);var reference=Adjacency(Reference);var ca=Colors(faces);var cr=Colors(Reference);var map=Enumerable.Repeat(-1,14).ToArray();var used=new bool[14];var referenceFaces=new HashSet<string>(Reference.Select(Cycle));
        bool Search(int depth)
        {
            if(depth==14)return faces.All(f=>referenceFaces.Contains(Cycle(f.Select(v=>map[v]).ToArray())));
            int vertex=-1;List<int> candidates=null;
            for(int v=0;v<14;v++)if(map[v]<0)
            {
                var possible=new List<int>();for(int r=0;r<14;r++)if(!used[r]&&ca[v]==cr[r]){bool valid=true;for(int other=0;other<14;other++)if(map[other]>=0&&actual[v,other]!=reference[r,map[other]]){valid=false;break;}if(valid)possible.Add(r);}
                if(candidates==null||possible.Count<candidates.Count){vertex=v;candidates=possible;}
            }
            foreach(int r in candidates){map[vertex]=r;used[r]=true;if(Search(depth+1))return true;map[vertex]=-1;used[r]=false;}return false;
        }
        Check(Search(0),"Editable face/edge incidence differs from the independently exported reference junction");
        var counter=typeof(StudioBevelCornerPlanValidation).GetField("cases",BindingFlags.Static|BindingFlags.NonPublic);var before=counter.GetValue(null);
        try{typeof(StudioBevelCornerPlanValidation).GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(StudioModelDocument.Node)},null).Invoke(null,new object[]{node});}finally{counter.SetValue(null,before);}
    }
    public static string Validate()
    {
        int cases=0;string phase="";string result;var orderedOutputs=new Dictionary<string,string>();
        try{
            foreach(float shear in new[]{-.65f,-.25f,0,.3f,.8f})foreach(bool rotate in new[]{false,true})foreach(float first in new[]{.08f,.449f,.994f})foreach(float second in new[]{.08f,.5f,.994f})foreach(bool reverseFirst in new[]{false,true})
            {
                var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);
                try{
                    phase=$"shear={shear} rotate={rotate} first={first} second={second} reverseFirst={reverseFirst}";
                    var shearMatrix=Matrix4x4.identity;shearMatrix.m01=shear;shearMatrix.m12=shear*.37f;
                    var transform=Matrix4x4.TRS(Vector3.zero,rotate?Quaternion.Euler(13,27,-19):Quaternion.identity,new Vector3(1.3f,.8f,1.1f))*shearMatrix;var inverse=transform.inverse;
                    mesh.vertices=mesh.vertices.Select(transform.MultiplyPoint3x4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles};var t=new StudioMeshCuts(node);
                    Vector3 Local(int i)=>inverse.MultiplyPoint3x4(t.Positions[i]);
                    var firstEdges=Edges(t).Where(e=>{var a=Local(e.Item1);var b=Local(e.Item2);return Mathf.Abs(a.z+.5f)<1e-5&&Mathf.Abs(b.z+.5f)<1e-5&&((Mathf.Abs(a.x-.5f)<1e-5&&Mathf.Abs(b.x-.5f)<1e-5)||(Mathf.Abs(a.y-.5f)<1e-5&&Mathf.Abs(b.y-.5f)<1e-5));}).ToArray();
                    Check(firstEdges.Length==2,"Initial L selection mismatch");if(reverseFirst)Array.Reverse(firstEdges);t.BevelEdges(node,firstEdges,new StudioMeshCuts.BevelOptions{Width=first});t=new StudioMeshCuts(node);var edges=Edges(t);
                    var remaining=edges.Single(e=>{var a=Local(e.Item1);var b=Local(e.Item2);return Mathf.Abs(a.x-.5f)<1e-5&&Mathf.Abs(a.y-.5f)<1e-5&&Mathf.Abs(b.x-.5f)<1e-5&&Mathf.Abs(b.y-.5f)<1e-5;});
                    var p=Local(remaining.Item1).z<Local(remaining.Item2).z?t.Positions[remaining.Item1]:t.Positions[remaining.Item2];
                    var diagonal=edges.Single(e=>{var a=t.Positions[e.Item1];var b=t.Positions[e.Item2];var other=Local(a==p?e.Item2:e.Item1);return (a==p||b==p)&&Mathf.Abs(other.z+.5f)<1e-5&&other.x>-.49f&&other.y>-.49f;});
                    string signature=null;
                    foreach(bool reverseSecond in new[]{false,true})
                    {
                        var copy=Object.Instantiate(mesh);try{var n=new StudioModelDocument.Node{mesh=copy,cutPolygons=node.cutPolygons,cutTriangles=node.cutTriangles,normalGroups=node.normalGroups};var cuts=new StudioMeshCuts(n);cuts.BevelEdges(n,reverseSecond?new[]{diagonal,remaining}:new[]{remaining,diagonal},new StudioMeshCuts.BevelOptions{Width=second});
                            Check(typeof(StudioMeshCuts).GetField("bevelFlatClip",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cuts)!=null,"Clipping miter backend was bypassed");MatchReference(n);
                            var output=new StudioMeshCuts(n);string current=string.Join("|",output.Faces.Select(f=>string.Join(";",f.Vertices.Select(i=>output.Positions[i].ToString("F6")).OrderBy(v=>v))).OrderBy(v=>v));if(signature!=null)Check(signature==current,"Second selection enumeration changed output");signature=current;
                            string key=$"{shear}/{rotate}/{first}/{second}";if(orderedOutputs.TryGetValue(key,out string previous))Check(previous==current,"First selection enumeration changed output");else orderedOutputs.Add(key,current);cases++;
                        }finally{Object.DestroyImmediate(copy);}
                    }
                }finally{Object.DestroyImmediate(mesh);}
            }
            result="PASS "+cases+" corrected two-edge-chain reference junction graph, oblique/rotation, width and selection-order checks";
        }catch(Exception e){result="FAIL "+phase+" cases="+cases+": "+e;}
        typeof(StudioBevelCornerPlanValidation).GetField("cases",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../bevel-junction-topology-result.txt")),result);return result;
    }
    public static void Run(){EditorApplication.delayCall+=()=>{string result=Validate();EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
