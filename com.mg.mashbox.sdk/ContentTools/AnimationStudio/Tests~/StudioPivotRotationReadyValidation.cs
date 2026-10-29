#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
using Object=UnityEngine.Object;
public static class StudioPivotRotationReadyValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static int checks;static string stage;static Queue<(string,Action)> steps=new Queue<(string,Action)>();static double next;static Vector3[] positions;static Quaternion[] rotations,frames;static Vector3[][] before,after;static object gesture;static Quaternion orientation,turn;static ToolChannelPane pane;
    static object Call(string name,params object[] args)
    {var m=typeof(StudioModelEditor).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length>=args.Length&&m.GetParameters().Skip(args.Length).All(p=>p.IsOptional));return m.Invoke(editor,m.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
    static void Act(Action a)=>Call("Run",a,true);static void Check(bool x,string why){if(!x)throw new Exception(stage+": "+why);checks++;}
    static Vector3[] World(int i)=>doc.nodes[i].mesh.vertices.Select(editor.Transforms[i].TransformPoint).ToArray();
    static void Load(int count=3)
    {
        doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"rotation-ready-"+Guid.NewGuid().ToString("N")+".workspace"));
        for(int i=0;i<count;i++){var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Target "+i,parent=i==1?0:-1,position=new Vector3(2*i,.4f*i,-i),rotation=Quaternion.Euler(10+i*13,20+i*27,5+i*9),scale=i==2?new Vector3(-1,1.7f,.8f):Vector3.one,mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},pivotRotation=Quaternion.Euler(3*i,5*i,0)});}
        editor.Load(doc);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);editor.SetPivotLocked(false);editor.SelectObject(0);for(int i=1;i<count;i++)editor.SelectObject(i,true);Call("EstablishSelectionBaseline");
    }
    static StudioMenu.Item Menu(string label){var menu=new StudioMenu("Mesh Tools",true);Call("PopulateModelMenu","Mesh Tools",menu,view,null);return menu.Items.Single(i=>i.Text==label);}
    static void Capture(){positions=editor.Transforms.Select(t=>t.position).ToArray();rotations=editor.Transforms.Select(t=>t.rotation).ToArray();frames=Enumerable.Range(0,doc.nodes.Count).Select(i=>editor.Transforms[i].rotation*doc.nodes[i].pivotRotation).ToArray();before=Enumerable.Range(0,doc.nodes.Count).Select(World).ToArray();}
    static void CapturePanel(string name)
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(view);var rect=new Rect(Vector2.zero,view.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;
        try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(rect,0,0);image.Apply();var pixels=image.GetPixels32();Check(pixels.Select(p=>p.r).Distinct().Count()>20,"Options capture empty");var flipped=new Color32[pixels.Length];for(int y=0;y<image.height;y++)Array.Copy(pixels,y*image.width,flipped,(image.height-1-y)*image.width,image.width);image.SetPixels32(flipped);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name)),image.EncodeToPNG());Object.DestroyImmediate(image);}finally{RenderTexture.active=old;Object.DestroyImmediate(rt);}
    }
    static void Objects(bool world)
    {
        Capture();orientation=world?Quaternion.identity:editor.PivotOrientation;turn=Quaternion.AngleAxis(27,Vector3.up);
        Act(()=>{Undo.RegisterCompleteObjectUndo(doc,"Rotate model objects");Call("RotateSelectedObjects",orientation*turn,orientation,positions[editor.Active]);editor.Changed(false);});
        for(int i=0;i<doc.nodes.Count;i++)
        {
            if(!world){Check(Vector3.Distance(editor.Transforms[i].position,positions[i])<.0001f,"Local rotation orbited object "+i);Check(Quaternion.Angle(editor.Transforms[i].rotation,frames[i]*turn*Quaternion.Inverse(frames[i])*rotations[i])<.02f,"Object used another object's axes "+i);}
            else if(i==0||i==2)Check(Vector3.Distance(editor.Transforms[i].position,positions[editor.Active]+turn*(positions[i]-positions[editor.Active]))<.0001f,"World group rotation changed");
        }
    }
    static void Components(bool component,bool sharedInstance=false)
    {
        Load(sharedInstance?3:2);
        if(sharedInstance){doc.nodes[1].mesh=doc.nodes[2].mesh=doc.nodes[0].mesh;editor.Load(doc);editor.SelectObject(0);editor.SelectObject(1,true);}
        editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
        for(int i=0;i<2;i++){Call("ActivateComponentTarget",i,StudioModelEditor.ComponentMode.Face);editor.Components.Add(i);}
        editor.ActivateTool(Tool.Rotate);if(component)editor.SetToolbarComponentAxes();else editor.SetToolbarLocalAxes(true);Call("EstablishSelectionBaseline");
        Capture();orientation=editor.PivotOrientation;turn=Quaternion.AngleAxis(31,Vector3.right);
        gesture=Call("CaptureMultiComponentGesture",new List<int>{0,1},new Vector3(100,20,30),orientation,0);
        var targetList=(System.Collections.IEnumerable)gesture.GetType().GetField("targets",F|BindingFlags.Public).GetValue(gesture);
        Act(()=>Call("ApplyMultiComponentGesture",gesture,new Vector3(100,20,30),orientation*turn,Vector3.one));
        foreach(var target in targetList)
        {
            var type=target.GetType();int node=(int)type.GetField("node",F|BindingFlags.Public).GetValue(target);var indices=(int[])type.GetField("indices",F|BindingFlags.Public).GetValue(target);var center=(Vector3)type.GetField("center",F|BindingFlags.Public).GetValue(target);var frame=(Quaternion)type.GetField("orientation",F|BindingFlags.Public).GetValue(target);var current=World(node);
            var expectedTurn=frame*turn*Quaternion.Inverse(frame);
            foreach(int index in Enumerable.Range(0,current.Length))Check(Vector3.Distance(current[index],indices.Contains(index)?center+expectedTurn*(before[node][index]-center):before[node][index])<.0002f,"Wrong per-mesh component center/axes");
        }
        if(sharedInstance){Check(doc.nodes[0].mesh!=doc.nodes[1].mesh&&doc.nodes[1].mesh!=doc.nodes[2].mesh,"Local component rotation retained shared compensation");Check(World(2).Zip(before[2],Vector3.Distance).Max()<.0002f,"Unselected shared instance moved");}
        after=Enumerable.Range(0,2).Select(World).ToArray();
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            Check(Application.dataPath.Contains("SelectionUndoProject"),"Disposable project required");Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});editor=host.ToolbarModelEditor;view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);
            void Step(string n,Action a)=>steps.Enqueue((n,a));
            Step("Object axes rotation",()=>{Load();editor.SetToolbarLocalAxes(true);Objects(false);});
            Step("Undo object rotation",()=>Undo.PerformUndo());Step("check object Undo",()=>{for(int i=0;i<3;i++)Check(Quaternion.Angle(editor.Transforms[i].rotation,rotations[i])<.02f&&Vector3.Distance(editor.Transforms[i].position,positions[i])<.0001f,"Object Undo failed");});
            Step("Redo object rotation",()=>Undo.PerformRedo());Step("check object Redo",()=>{for(int i=0;i<3;i++)Check(Quaternion.Angle(editor.Transforms[i].rotation,frames[i]*turn*Quaternion.Inverse(frames[i])*rotations[i])<.02f,"Object Redo failed");});
            Step("Component axes object rotation",()=>{Load();editor.SetToolbarComponentAxes();Objects(false);});
            Step("World rotation",()=>{Load();editor.SetToolbarLocalAxes(false);Objects(true);});
            foreach(bool component in new[]{false,true})
            {
                Step("Multi-mesh components "+component,()=>Components(component));
                Step("Undo components",()=>Undo.PerformUndo());Step("check component Undo",()=>{for(int i=0;i<2;i++)Check(World(i).Zip(before[i],Vector3.Distance).Max()<.0002f,"Component Undo failed");});
                Step("Redo components",()=>Undo.PerformRedo());Step("check component Redo",()=>{for(int i=0;i<2;i++)Check(World(i).Zip(after[i],Vector3.Distance).Max()<.0002f,"Component Redo failed");});
            }
            Step("pivot already at target",()=>{Load();Capture();var point=positions[editor.Active];Act(()=>Call("MoveEditingPivot",point));for(int i=0;i<3;i++){Check(Vector3.Distance(editor.Transforms[i].position,point)<.0001f,"An existing active target skipped other pivots");Check(World(i).Zip(before[i],Vector3.Distance).Max()<.0002f,"Repivot changed surface");}});
            Step("shared component instances",()=>Components(false,true));
            Step("Undo shared components",()=>Undo.PerformUndo());Step("check shared Undo",()=>{for(int i=0;i<3;i++)Check(World(i).Zip(before[i],Vector3.Distance).Max()<.0002f,"Shared component Undo failed");Check(doc.nodes[0].mesh==doc.nodes[1].mesh&&doc.nodes[1].mesh==doc.nodes[2].mesh,"Undo did not restore sharing");});
            Step("Redo shared components",()=>Undo.PerformRedo());Step("check shared Redo",()=>{for(int i=0;i<2;i++)Check(World(i).Zip(after[i],Vector3.Distance).Max()<.0002f,"Shared component Redo failed");});
            Step("arm Split",()=>{Load(1);Menu("Split Selection").Action();pane=(ToolChannelPane)typeof(StudioModelEditor).GetProperty("ActiveChannelTool",F).GetValue(editor);Check(pane&&pane.IsEmbedded&&pane.titleContent.text=="Split Selection","Split not retained in Tool Options");Check(editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Face,"Split not ready for faces");int count=doc.nodes.Count;Check(!pane.ExecuteCommand(true),"Invalid Execute reported success");Check(pane&&doc.nodes.Count==count,"Invalid Execute closed tool or created geometry");});
            Step("capture Split options",()=>CapturePanel("split-ready-options.png"));
            Step("select then execute",()=>{editor.Components.Add(0);Call("EstablishSelectionBaseline");Check(pane.ExecuteCommand(false),"Valid Split Apply failed");Check(pane&&doc.nodes.Count==2,"Apply did not retain tool and split");editor.ActivateTool(Tool.Move);Check(!pane,"Transform switch retained action pane");});
            Step("arm Bridge",()=>{Load(1);Menu("Bridge Edges").Action();pane=(ToolChannelPane)typeof(StudioModelEditor).GetProperty("ActiveChannelTool",F).GetValue(editor);Check(pane&&pane.titleContent.text=="Bridge Edges"&&editor.ToolbarComponentMode==StudioModelEditor.ComponentMode.Edge,"Bridge not active");Check(!pane.ExecuteCommand(true)&&pane,"Invalid Bridge did not remain active");pane.Close();Check(typeof(StudioModelEditor).GetProperty("ActiveChannelTool",F).GetValue(editor)==null,"Cancel retained options");});
            next=EditorApplication.timeSinceStartup+.3;EditorApplication.update+=Pump;
        }catch(Exception e){Finish(e.ToString());}
    }
    static void Pump(){if(EditorApplication.timeSinceStartup<next)return;try{if(steps.Count==0){Finish("PASS "+checks+" individual Object/Component rotation, World group preservation, native history, common-world pivot convergence and armed Split/Bridge Tool Options checks");return;}UnityEditor.Undo.FlushUndoRecordObjects();var step=steps.Dequeue();stage=step.Item1;step.Item2();UnityEditor.Undo.FlushUndoRecordObjects();next=EditorApplication.timeSinceStartup+.25;}catch(Exception e){Finish(stage+"\n"+e);}}
    static void Finish(string result){EditorApplication.update-=Pump;File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../pivot-rotation-ready-result.txt")),result);EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)Object.DestroyImmediate(host);EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);};}
}
#endif
