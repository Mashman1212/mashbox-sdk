#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

// Copy into a project Editor folder to run against disposable fixtures; no scene assets are edited.
[InitializeOnLoad]
public static class StudioExtrusionValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type Cuts = typeof(StudioModelDocument).Assembly.GetType("MashBoxSDK.AnimationStudio.StudioMeshCuts");
    static readonly List<string> Report = new List<string>();
    static readonly List<Mesh> Meshes = new List<Mesh>();
    static StudioExtrusionValidation() { EditorApplication.delayCall += () => { if (!SessionState.GetBool("StudioExtrusionValidation.v2",false)) { var w=ScriptableObject.CreateInstance<StudioExtrusionValidationWindow>(); w.ShowUtility(); } }; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Report.Add("PASS " + message); }
    static object Topology(StudioModelDocument.Node node) => Activator.CreateInstance(Cuts, Flags, null, new object[] { node }, null);
    static object Extrude(StudioModelDocument.Node node, int[] faces, (int,int)[] edges = null, float distance = .5f, bool together = true, bool seams = true)
        => Cuts.GetMethod("Extrude", Flags).Invoke(Topology(node), new object[] { node, faces, edges, distance, together, seams });
    static int Faces(StudioModelDocument.Node n) { int count=0; for(int i=0;i<n.cutPolygons.Length;) { int size=n.cutPolygons[i+1]; i+=size+2; count++; } return count; }
    static StudioModelDocument.Node Fixture(bool seams = false)
    {
        var m = new Mesh { name="Extrusion test" }; Meshes.Add(m);
        var p = new List<Vector3> { new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),new Vector3(2,0,0),new Vector3(2,1,0) };
        if(seams) {p.Add(p[1]);p.Add(p[2]);}
        m.SetVertices(p); m.subMeshCount=2;
        int a=seams?6:1,b=seams?7:2;
        m.SetTriangles(new[]{0,1,2,0,2,3},0);m.SetTriangles(new[]{a,4,5,a,5,b},1);
        m.SetUVs(0,p.Select(v=>new Vector2(v.x,v.y)).ToList());
        m.SetUVs(2,p.Select(v=>new Vector3(v.x,v.y,7)).ToList());
        m.SetUVs(3,p.Select(v=>new Vector4(v.x,v.y,7,9)).ToList());
        m.colors=p.Select(v=>new Color(v.x/2,v.y,.2f,1)).ToArray();m.RecalculateNormals();m.RecalculateBounds();
        return new StudioModelDocument.Node {name="Fixture", mesh=m,cutTriangles=m.triangles,cutPolygons=new[]{0,4,0,1,2,3,1,4,a,4,5,b}};
    }
    static void Valid(StudioModelDocument.Node node)
    {
        var mesh=node.mesh;var v=mesh.vertices;var triangles=mesh.triangles;
        Check(triangles.All(i=>i>=0&&i<v.Length),"All triangle indices are valid");
        Check(triangles.Distinct().Count()==v.Length,"No orphan vertices after extrusion");
        for(int i=0;i<triangles.Length;i+=3)
            if(Vector3.Cross(v[triangles[i+1]]-v[triangles[i]],v[triangles[i+2]]-v[triangles[i]]).sqrMagnitude<1e-12f)throw new Exception("Degenerate extrusion triangle");
        Check(mesh.normals.Length==v.Length&&mesh.tangents.Length==v.Length,"Normals and tangents match expanded topology");
        Check(mesh.uv.Length==v.Length&&mesh.colors.Length==v.Length,"UV0 and colors match expanded topology");
        var uv=new List<Vector4>();mesh.GetUVs(3,uv);
        Check(uv.Count==v.Length&&uv.All(x=>x.z==7&&x.w==9),"Four-component UV data is preserved");
        Check(mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord2)==3,"Three-component UV dimension is preserved");
    }
    public static void Run()
    {
        if (SessionState.GetBool("StudioExtrusionValidation.v2",false)) return;
        SessionState.SetBool("StudioExtrusionValidation.v2",true);
        StudioModelDocument document=null;IDisposable editor=null;
        try
        {
            var n=Fixture();Extrude(n,new[]{0,1});
            Check(Faces(n)==8,"Connected region creates two caps and six boundary walls, no internal wall");Valid(n);
            Check(n.mesh.vertices.Count(v=>Mathf.Abs(v.z-.5f)<1e-6f)>0,"Caps move by requested normal distance");
            n=Fixture();Extrude(n,new[]{0,1},together:false);Check(Faces(n)==10,"Individual faces create independent side walls");Valid(n);
            n=Fixture(true);Extrude(n,new[]{0,1});Check(Faces(n)==8,"UV seam is treated as a connected region");Valid(n);
            n=Fixture(true);Extrude(n,new[]{0,1},seams:false);Check(Faces(n)==10,"Disabling seam welding preserves separate islands");
            n=Fixture();Extrude(n,new[]{0},distance:-.5f);Check(n.mesh.vertices.Any(v=>v.z<-.49f),"Negative extrusion moves inward");Valid(n);
            n=Fixture();var result=Extrude(n,null,new[]{(0,1)});Check(Faces(n)==3,"One boundary edge adds one quad");Valid(n);
            var rim=((ValueTuple<int,int>[])result.GetType().GetField("Edges",Flags).GetValue(result));
            Check(rim.Length==1,"New outer edge is returned for selection");
            Extrude(n,null,rim);Check(Faces(n)==4,"Repeated edge extrusion extends the new boundary");Valid(n);
            n=Fixture();var before=n.mesh.vertices;var beforeTris=n.mesh.triangles;
            bool rejected=false;try{Extrude(n,null,new[]{(1,2)});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}
            Check(rejected&&n.mesh.vertices.SequenceEqual(before)&&n.mesh.triangles.SequenceEqual(beforeTris),"Interior edge rejection leaves mesh unchanged");
            rejected=false;try{Extrude(n,new[]{0},distance:0);}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}
            Check(rejected,"Zero distance rejected before degenerate walls can be created");
            n=Fixture();document=ScriptableObject.CreateInstance<StudioModelDocument>();document.nodes.Add(n);
            var type=typeof(StudioModelDocument).Assembly.GetType("MashBoxSDK.AnimationStudio.StudioModelEditor");
            editor=(IDisposable)Activator.CreateInstance(type,Flags,null,new object[]{(Action)(()=>{})},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,Flags).Invoke(editor,args);
            Call("Load",document);Call("SelectObject",0,false,false);
            var mode=type.GetField("mode",Flags);mode.SetValue(editor,Enum.Parse(mode.FieldType,"Face"));
            var selection=(HashSet<int>)type.GetField("Components",Flags).GetValue(editor);selection.Add(0);
            before=n.mesh.vertices;beforeTris=n.mesh.triangles;
            EditorGUIUtility.editingTextField=false; var oldEvent=Event.current;Event.current=new Event{type=EventType.KeyDown,keyCode=KeyCode.E,modifiers=EventModifiers.Control};
            try { Check((bool)Call("HandleExtrudeShortcut")&&Event.current.type==EventType.Used,"Ctrl+E invokes extrusion and consumes shortcut"); }
            finally {Event.current=oldEvent;}
            Check(Faces(document.nodes[0])==6&&selection.SetEquals(new[]{0}),"Editor retains selected cap after extrusion");
            Undo.FlushUndoRecordObjects();var after=n.mesh.vertices;var afterTris=n.mesh.triangles;
            Undo.PerformUndo();Check(n.mesh.vertices.SequenceEqual(before)&&n.mesh.triangles.SequenceEqual(beforeTris)&&Faces(document.nodes[0])==2,"Undo restores geometry and polygon topology together");
            Undo.PerformRedo();Check(n.mesh.vertices.SequenceEqual(after)&&n.mesh.triangles.SequenceEqual(afterTris)&&Faces(document.nodes[0])==6,"Redo restores full extrusion");
            var menuType=typeof(StudioModelDocument).Assembly.GetType("MashBoxSDK.AnimationStudio.StudioMenu");
            var menu=Activator.CreateInstance(menuType,true);menuType.GetMethod("AddItem",Flags).Invoke(menu,new object[]{new GUIContent("Extrude\tCtrl+E"),false,(Action)(()=>{})});
            var items=(System.Collections.IList)menuType.GetField("Items",Flags).GetValue(menu);var item=items[0];
            Check((string)item.GetType().GetField("Text",Flags).GetValue(item)=="Extrude"&&(string)item.GetType().GetField("Shortcut",Flags).GetValue(item)=="Ctrl+E","Menu stores shortcut separately for right alignment");
            Report.Add("ALL CHECKS PASSED");
        }
        catch(Exception e){Report.Add("FAIL "+e);}
        finally
        {
            editor?.Dispose(); if(document){Undo.ClearUndo(document);Object.DestroyImmediate(document);}
            foreach(var m in Meshes)if(m){Undo.ClearUndo(m);Object.DestroyImmediate(m);}
            File.WriteAllLines(Path.Combine(Path.GetTempPath(),"studio-extrusion-test.txt"),Report);
        }
    }
}
public class StudioExtrusionValidationWindow : EditorWindow
{
    bool ran;
    void OnGUI() { if(ran||Event.current.type!=EventType.Repaint)return;ran=true;StudioExtrusionValidation.Run();EditorApplication.delayCall+=Close; }
}
#endif
