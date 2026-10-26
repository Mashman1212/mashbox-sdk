using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        internal Action NewWorkspace;
        internal Action<StudioRetopologyDocument> OpenWorkspace;
        internal void DrawMenu()
        {
            using(new GUILayout.HorizontalScope())
            {
                Menu("File",menu=>
                {
                    menu.AddItem(new GUIContent("New Workspace"),false,()=>NewWorkspace?.Invoke());
                    menu.AddItem(new GUIContent("Open Workspace..."),false,()=>Run(()=>
                    {
                        string path=EditorUtility.OpenFilePanel("Open Retopology Workspace",Application.dataPath,"asset");if(string.IsNullOrEmpty(path))return;
                        var document=AssetDatabase.LoadAssetAtPath<StudioRetopologyDocument>(FileUtil.GetProjectRelativePath(path));
                        if(!document)throw new InvalidOperationException("Choose a Retopology workspace asset from this project.");OpenWorkspace?.Invoke(document);
                    }));
                    menu.AddItem(new GUIContent("Import Reference..."),false,()=>Run(()=>ImportPath(EditorUtility.OpenFilePanel("Import Reference",Application.dataPath,"fbx,obj,blend"))));
                    menu.AddItem(new GUIContent("Save Workspace\tCtrl+S"),false,()=>Run(Document.SaveWorkspace));
                    menu.AddSeparator("");
                    Command(menu,"Export Working Mesh / FBX...",()=>Export(false));Command(menu,"Export Working Mesh / OBJ (quads)...",()=>Export(true));
                });
                Menu("Edit",menu=>
                {
                    menu.AddItem(new GUIContent("Undo\tCtrl+Z"),false,Undo.PerformUndo);menu.AddItem(new GUIContent("Redo\tCtrl+Y"),false,Undo.PerformRedo);
                    menu.AddSeparator("");Command(menu,"Delete Selected Components",DeleteComponents);
                    Command(menu,"Reverse Winding",()=>Change("Reverse winding",()=>{foreach(var f in Active.faces)Array.Reverse(f.vertices);}));
                    Command(menu,"Clear Working Mesh",ClearWorkingMesh);
                    if(Active!=null)menu.AddItem(new GUIContent("Remove Mesh"),false,()=>Run(RemoveMesh));
                    else menu.AddDisabledItem(new GUIContent("Remove Mesh"));
                    Command(menu,"Continue in Modeling",()=>Send(EditInModeling));
                    Command(menu,"Export FBX and Continue in Skinning...",()=>Export(false,true));
                });
                Menu("Topology",menu=>
                {
                    if(Active!=null)menu.AddItem(new GUIContent("Draw Tube"),activeTool==RetopoTool.Tube,StartTube);
                    else menu.AddDisabledItem(new GUIContent("Draw Tube"));
                    if(Active!=null)menu.AddItem(new GUIContent("Generate Pants Quad Cage"),false,()=>Run(GeneratePants));
                    else menu.AddDisabledItem(new GUIContent("Generate Pants Quad Cage"));
                    Command(menu,"Reduce Working Mesh (LOD)",()=>Generate(false));Command(menu,"Pair Triangles into Quads",()=>Change("Pair quads",()=>StudioRetopologyOperations.PairQuads(Active)));
                    if(Active!=null)menu.AddItem(new GUIContent("Relax Brush"),activeTool==RetopoTool.RelaxBrush,StartRelaxBrush);else menu.AddDisabledItem(new GUIContent("Relax Brush"));
                    Command(menu,"Project to Reference",Project);Command(menu,"Relax on Surface",Relax);
                    Command(menu,"Target Weld",StartTargetWeld);
                    Command(menu,"Bridge Selected Borders",BridgeLoops);
                    Command(menu,"Bridge Pants / Crotch",BridgePants);
                    Command(menu,"Subdivide and Project",()=>Change("Subdivide topology",()=>StudioRetopologyOperations.Subdivide(Active,Document.project?Surface:null,Document.offset)));
                    Command(menu,"Clean Mesh",()=>Change("Clean topology",()=>StudioRetopologyOperations.Clean(Active)));
                    menu.AddSeparator("");
                    Command(menu,"Mirror +X to -X",()=>Change("Mirror topology",()=>StudioRetopologyOperations.Mirror(Active,true)));
                    Command(menu,"Mirror -X to +X",()=>Change("Mirror topology",()=>StudioRetopologyOperations.Mirror(Active,false)));
                    Command(menu,"Validate Topology",()=>{status=StudioRetopologyOperations.Audit(Active);view?.ShowNotification(new GUIContent(status));});
                });
            }
        }
        private void Menu(string title,Action<StudioMenu> build)
        {
            var content=new GUIContent(title);var rect=GUILayoutUtility.GetRect(content,StudioTheme.Styles.menuDropdown,GUILayout.Width(title.Length*8+22),GUILayout.Height(21));
            if(!EditorGUI.DropdownButton(rect,content,FocusType.Passive,StudioTheme.Styles.menuDropdown))return;
            var menu=new StudioMenu();build(menu);menu.DropDown(rect);
        }
        private void Command(StudioMenu menu,string title,Action action)
        {if(Active!=null&&Active.faces.Count>0)menu.AddItem(new GUIContent(title),false,()=>Run(action));else menu.AddDisabledItem(new GUIContent(title));}
        private void Send(Action<Mesh,string> target)
        {
            if(target==null)return;
            var mesh=StudioRetopologyDocument.BuildMesh(Active);
            try { target(mesh,Active.name+" Low"); } finally {UnityEngine.Object.DestroyImmediate(mesh);}
        }
        private void HandleDrop(Event evt)
        {
            if(evt.type!=EventType.DragUpdated&&evt.type!=EventType.DragPerform)return;
            bool valid=DragAndDrop.objectReferences.OfType<GameObject>().Any()||DragAndDrop.paths.Any(p=>new[]{".fbx",".obj",".blend"}.Contains(Path.GetExtension(p).ToLowerInvariant()));
            if(!valid)return;DragAndDrop.visualMode=DragAndDropVisualMode.Copy;
            if(evt.type==EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var roots=DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
                Run(()=>{if(roots.Length>0)foreach(var root in roots)Import(root);else foreach(var path in DragAndDrop.paths)ImportPath(path);});
            }
            evt.Use();
        }
        private void ImportPath(string path)
        {
            if(string.IsNullOrEmpty(path))return;
            string relative=FileUtil.GetProjectRelativePath(path);
            if(string.IsNullOrEmpty(relative))
            {
                const string folder="Assets/MashBox Retopology Imports";
                Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                relative=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+Path.GetFileName(path));File.Copy(path,relative,false);
                AssetDatabase.ImportAsset(relative,ImportAssetOptions.ForceSynchronousImport);
            }
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(relative);
            if(!model)throw new InvalidOperationException("Unity could not import this model. Blender files require a compatible Blender installation; FBX or OBJ can be used directly.");
            Import(model);
        }
        private void Export(bool obj,bool skin=false)
        {
            if (!obj && !StudioFbxDependency.Require()) return;
            if(Active==null||Active.faces.Count==0)return;
            string path=EditorUtility.SaveFilePanel("Export Retopology Mesh",Application.dataPath,Active.name+"_Low",obj?"obj":"fbx");if(string.IsNullOrEmpty(path))return;
            if(obj)
            {
                var culture=CultureInfo.InvariantCulture;var text=new StringBuilder("# MashBox Studio Retopology\n");
                foreach(var v in Active.vertices)text.AppendFormat(culture,"v {0:R} {1:R} {2:R}\n",-v.x,v.y,v.z);
                foreach(var uv in Active.uv)text.AppendFormat(culture,"vt {0:R} {1:R}\n",uv.x,uv.y);
                foreach(var group in Active.faces.GroupBy(f=>f.material))
                {
                    text.AppendLine("g Material_"+group.Key);
                    foreach(var face in group){text.Append("f");foreach(int i in face.vertices.Reverse())text.Append(" "+(i+1)+"/"+(i+1));text.AppendLine();}
                }
                File.WriteAllText(path,text.ToString());
            }
            else
            {
                var mesh=StudioRetopologyDocument.BuildMesh(Active);var root=new GameObject(Active.name+" Low"){hideFlags=HideFlags.HideAndDontSave};
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,Scene);
                try
                {
                    root.AddComponent<MeshFilter>().sharedMesh=mesh;
                    root.AddComponent<MeshRenderer>().sharedMaterials=Active.materials??Array.Empty<Material>();
                    StudioModelEditor.ExportTo(path,root);
                }
                finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
            }
            AssetDatabase.Refresh();status="Exported "+Path.GetFileName(path)+". UVs and skin binding can be finished in Modeling / Skinning.";
            if(skin)
            {
                var relative=FileUtil.GetProjectRelativePath(path);
                if(string.IsNullOrEmpty(relative))
                {status="Exported FBX. Import it into the project before opening it in Skinning.";return;}
                AssetDatabase.ImportAsset(relative,ImportAssetOptions.ForceSynchronousImport);
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(relative);
                if(!asset)throw new InvalidOperationException("The exported FBX could not be loaded for Skinning.");
                SkinOutput?.Invoke(asset);
            }
        }
    }
}
