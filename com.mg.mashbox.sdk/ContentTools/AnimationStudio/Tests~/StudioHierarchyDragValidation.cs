using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

public class StudioHierarchyDragTestWindow : EditorWindow
{
    internal StudioModelEditor editor;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    void OnGUI()
    {
        if(editor==null)return;
        typeof(StudioModelEditor).GetMethod("HandleHierarchyDragCancellation",Flags).Invoke(editor,null);
        editor.HandleModelDrop(null,editor.Load);
        for(int i=0;i<editor.Document.nodes.Count;i++)
            typeof(StudioModelEditor).GetMethod("DrawHierarchyDragRow",Flags).Invoke(editor,new object[]{new Rect(10,30+i*24,300,22),new GUIContent(editor.Document.nodes[i].name),EditorStyles.miniButton,i});
        typeof(StudioModelEditor).GetMethod("HandleHierarchyDrop",Flags).Invoke(editor,new object[]{new Rect(10,180,300,180),-1});
    }
}
public static class StudioHierarchyDragValidation
{
    static readonly List<string> results=new List<string>();
    static StudioHierarchyDragTestWindow window;
    static StudioModelEditor editor;
    static int step,button;
    static double next;
    static string errors="";
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static void MatrixNear(Matrix4x4 a,Matrix4x4 b)
    {for(int i=0;i<16;i++)Check(Mathf.Abs(a[i]-b[i])<.001f,"World matrix changed at "+i+": "+a[i]+" vs "+b[i]);}
    public static void Run()
    {
        try
        {
            Debug.Log("Hierarchy drag validation: document checks");
            var doc=StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/HierarchyDrag.asset"));
            doc.nodes.Add(new StudioModelDocument.Node{name="Leaf",parent=1,position=Vector3.forward,mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube)});
            doc.nodes.Add(new StudioModelDocument.Node{name="Child",parent=2,position=Vector3.right});
            doc.nodes.Add(new StudioModelDocument.Node{name="Group",position=new Vector3(1,2,3),positionOffset=new Vector3(.2f,.3f,.4f),rotation=Quaternion.Euler(10,20,30)});
            doc.nodes.Add(new StudioModelDocument.Node{name="Target",position=new Vector3(-2,4,1),rotation=Quaternion.Euler(15,45,10),scale=Vector3.one*2});
            doc.nodes.Add(new StudioModelDocument.Node{name="Other"});doc.Save();
            var bytes=File.ReadAllBytes(AssetDatabase.GetAssetPath(doc));
            using(var model=new StudioModelEditor(()=>{}))
            {
                model.Load(doc);model.SelectObject(2);var world=model.Transforms.Select(t=>t.localToWorldMatrix).ToArray();
                Check(model.Transforms[0].parent==model.Transforms[1]&&model.Transforms[1].parent==model.Transforms[2],"Build supports parents stored after children");
                model.ReparentHierarchy(new[]{2,1},3);
                Check(doc.nodes[2].parent==3&&doc.nodes[1].parent==2,"Only selected roots reparent");
                for(int i=0;i<world.Length;i++)MatrixNear(model.Transforms[i].localToWorldMatrix,world[i]);
                Undo.PerformUndo();Check(doc.nodes[2].parent==-1&&model.Active==2,"Undo restores parent and selection");
                Undo.PerformRedo();Check(doc.nodes[2].parent==3,"Redo restores parent");
                model.ReparentHierarchy(new[]{2},-1);for(int i=0;i<world.Length;i++)MatrixNear(model.Transforms[i].localToWorldMatrix,world[i]);
                Check(!doc.CanReparent(new[]{2},0,model.Transforms,out _),"Cycle rejected");
                Check(!doc.CanReparent(new[]{2},2,model.Transforms,out _),"Self parenting rejected");
                model.Transforms[3].localScale=Vector3.zero;
                Check(!doc.CanReparent(new[]{2},3,model.Transforms,out _),"Singular parent rejected");
                model.Transforms[3].localScale=new Vector3(2,1,3);
                Check(!doc.CanReparent(new[]{2},3,model.Transforms,out _),"Unrepresentable shear rejected atomically");
                model.Transforms[3].localScale=Vector3.one*2;
                model.ReparentHierarchy(new[]{2},3);
                model.SelectObject(3);model.DuplicateSelectedObjects();
                Check(doc.nodes.Count==9,"Duplicate includes entire reordered subtree");
                model.DeleteSelectedObjects();Check(doc.nodes.Count==5,"Delete includes entire reordered subtree");
                model.SelectObject(3);
                string export=AssetDatabase.GenerateUniqueAssetPath("Assets/HierarchyDragExport.fbx");model.ExportFile(export,true);AssetDatabase.ImportAsset(export,ImportAssetOptions.ForceSynchronousImport);
                var imported=AssetDatabase.LoadAssetAtPath<GameObject>(export);
                Check(imported&&imported.transform.Find("Group/Child/Leaf"),"Export retains reordered descendants");
                Check(File.ReadAllBytes(AssetDatabase.GetAssetPath(doc)).SequenceEqual(bytes),"Reparent does not write workspace to disk");
                results.Add("PASS: Later-index parents, nested multi-selection, world matrices, Undo/Redo, cycles, invalid scale/shear, subtree duplicate/delete, FBX export and no autosave.");
            }
            var anchorDoc=ScriptableObject.CreateInstance<StudioModelDocument>();
            anchorDoc.nodes.Add(new StudioModelDocument.Node{name="Parent"});anchorDoc.nodes.Add(new StudioModelDocument.Node{name="Stem_Anchor",anchorType="Stem_Anchor"});anchorDoc.nodes.Add(new StudioModelDocument.Node{name="Stem_Anchor",anchorType="Stem_Anchor",parent=0});
            using(var model=new StudioModelEditor(()=>{})){model.Load(anchorDoc);Check(!anchorDoc.CanReparent(new[]{1},0,model.Transforms,out _),"Duplicate anchor names rejected");}
            UnityEngine.Object.DestroyImmediate(anchorDoc);
            var uiDoc=ScriptableObject.CreateInstance<StudioModelDocument>();uiDoc.AddEmptyTransform();uiDoc.AddEmptyTransform();uiDoc.AddEmptyTransform();
            Debug.Log("Hierarchy drag validation: native UI checks");
            editor=new StudioModelEditor(()=>{if(window)window.Repaint();});editor.Load(uiDoc);
            // Native OLE dragging blocks this synthetic sender until a physical mouse
            // release. Exercise IMGUI capture/drop events with only that handoff stubbed.
            typeof(StudioModelEditor).GetField("beginHierarchyDrag",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(editor,(Action<string>)(_=>{}));
            window=ScriptableObject.CreateInstance<StudioHierarchyDragTestWindow>();window.editor=editor;window.position=new Rect(200,150,340,400);window.ShowUtility();window.Focus();
            Application.logMessageReceived+=Log;EditorApplication.update+=Tick;next=EditorApplication.timeSinceStartup+1;
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Mouse(EventType type,Vector2 position)=>window.SendEvent(new Event{type=type,button=button,mousePosition=position,delta=new Vector2(12,0)});
    static void Log(string text,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors+=text+"\n"+stack;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.15;
        try
        {
            Debug.Log("Hierarchy drag validation: button " + button + " step " + step);
            var source=new Vector2(80,40);var target=new Vector2(80,64);var blank=new Vector2(80,240);
            switch(step++)
            {
                case 0:Mouse(EventType.MouseDown,source);break;
                case 1:Mouse(EventType.MouseDrag,source+new Vector2(15,0));Check(DragAndDrop.GetGenericData("MashBoxStudio.HierarchyDrag")!=null,"Native button "+button+" starts internal drag");break;
                case 2:Mouse(EventType.DragUpdated,target);Check(DragAndDrop.visualMode==DragAndDropVisualMode.Move,"Target accepts drag");break;
                case 3:Mouse(EventType.DragPerform,target);Check(editor.Document.nodes[0].parent==1,"Drop reparents for button "+button);break;
                case 4:Mouse(EventType.MouseDown,source);break;
                case 5:Mouse(EventType.MouseDrag,source+new Vector2(15,0));break;
                case 6:Mouse(EventType.DragUpdated,blank);Mouse(EventType.DragPerform,blank);Check(editor.Document.nodes[0].parent==-1,"Blank space unparents for button "+button);break;
                case 7:Mouse(EventType.MouseDown,source);break;
                case 8:Mouse(EventType.MouseDrag,source+new Vector2(15,0));break;
                case 9:window.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape});Check(DragAndDrop.GetGenericData("MashBoxStudio.HierarchyDrag")==null&&editor.Document.nodes[0].parent==-1,"Escape cancels without editing");break;
                case 10:
                    results.Add("PASS: IMGUI "+(button==0?"left":"middle")+" mouse drag starts, parent drop, root drop, and Escape cancel (OS handoff stubbed).");
                    if(button==0){button=2;step=0;break;}
                    Check(string.IsNullOrEmpty(errors),errors);Finish(null);break;
            }
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        if(error!=null)results.Add("FAIL: "+error+"\n"+errors);
        if(window)window.Close();editor?.Dispose();
        File.WriteAllLines("hierarchy-drag-result.txt",results);EditorApplication.delayCall+=()=>EditorApplication.Exit(error==null?0:1);
    }
}
