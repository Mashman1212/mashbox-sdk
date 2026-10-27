using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using UnityEditor;using UnityEngine;using MashBoxSDK.AnimationStudio;using Object=UnityEngine.Object;
public static class StudioMaterialLibraryValidation
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;const string Root="Assets/MaterialLibraryFixture";
 static int checks,stage;static bool reading,pinned,presentation;static int oldSources;static double next,deadline;static StudioMaterialLibrary library;static MashBoxStudioViewport owner;static Material blue,red;static StudioModelEditor editor;static StudioModelDocument document;static Vector2 drag;static bool inject;static string errors="";
 static void Check(bool b,string text){if(!b)throw new Exception(text);checks++;}
 static object Read(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
 static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
 static string Out(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",file));
 public static void Run(){EditorApplication.delayCall+=Start;}
 public static void Reload(){EditorApplication.delayCall+=()=>{try{if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");reading=true;Application.logMessageReceived+=Log;owner=ScriptableObject.CreateInstance<MashBoxStudioViewport>();owner.position=new Rect(60,60,1200,760);StudioWindowOwnership.Show(owner);library=StudioMaterialLibrary.Open(owner);stage=90;deadline=EditorApplication.timeSinceStartup+60;next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;}catch(Exception e){Finish(e);}};}

 public static void Pinned(){EditorApplication.delayCall+=()=>{try{if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");pinned=true;Application.logMessageReceived+=Log;oldSources=StudioMaterialLibraryIndex.Sources;StudioMaterialLibraryIndex.SetSources(1);owner=ScriptableObject.CreateInstance<MashBoxStudioViewport>();owner.position=new Rect(60,60,1200,760);StudioWindowOwnership.Show(owner);library=StudioMaterialLibrary.Open(owner);library.SelectFolder(StudioMaterialLibraryIndex.StandardCategory);stage=100;deadline=EditorApplication.timeSinceStartup+60;next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;}catch(Exception e){Finish(e);}};}
 static readonly List<Rect> observedRows=new List<Rect>();
 public static void Presentation(){EditorApplication.delayCall+=()=>{try{if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");presentation=true;Application.logMessageReceived+=Log;StudioPreferenceStore.SetString("Studio.MaterialLibrary.Placement","");owner=ScriptableObject.CreateInstance<MashBoxStudioViewport>();owner.position=new Rect(60,60,1200,760);StudioWindowOwnership.Show(owner);library=StudioMaterialLibrary.Open(owner);library.SelectFolder(StudioMaterialLibraryIndex.StandardCategory);Set(library,"thumbnail",130f);stage=110;deadline=EditorApplication.timeSinceStartup+60;next=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Tick;}catch(Exception e){Finish(e);}};}
 // Set the saved slider endpoints directly; rendering, list input and
 // reopening exercise production behavior without synthetic native thumb hit testing.
 static void Zoom(bool minimum)
 {
  float value=minimum?40:130;Set(library,"thumbnail",value);
  StudioPreferenceStore.SetFloat("Studio.MaterialLibrary.Thumbnail",value);
  if(minimum){typeof(StudioMaterialLibrary).GetMethod("OnBecameInvisible",F).Invoke(library,null);}
  library.Repaint();
 }
 static void Start(){try{
 if(!Application.dataPath.Replace("\\","/").Contains("/obj/StudioControlsValidation/"))throw new Exception("Disposable project only");
 Application.logMessageReceived+=Log;if(!AssetDatabase.IsValidFolder(Root))AssetDatabase.CreateFolder("Assets","MaterialLibraryFixture");if(!AssetDatabase.IsValidFolder(Root+"/Props"))AssetDatabase.CreateFolder(Root,"Props");if(!AssetDatabase.IsValidFolder(Root+"/Empty"))AssetDatabase.CreateFolder(Root,"Empty");
 blue=new Material(Shader.Find("Unlit/Color")){name="Ocean Blue",color=Color.blue};red=new Material(Shader.Find("Unlit/Color")){name="Warm Red",color=Color.red};AssetDatabase.CreateAsset(blue,Root+"/Props/Ocean Blue.mat");AssetDatabase.CreateAsset(red,Root+"/Warm Red.mat");
 var t=new Texture2D(4,4);AssetDatabase.CreateAsset(t,Root+"/Empty/Not Material.asset");
 var embedded=ScriptableObject.CreateInstance<StudioModelDocument>();AssetDatabase.CreateAsset(embedded,Root+"/Embedded.asset");AssetDatabase.AddObjectToAsset(new Material(blue){name="Embedded Blue"},embedded);AssetDatabase.AddObjectToAsset(new Material(red){name="Embedded Red"},embedded);AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(Root+"/Embedded.asset",ImportAssetOptions.ForceSynchronousImport);
 StudioMaterialLibraryIndex.SetSources(3);StudioMaterialLibraryIndex.Rescan();deadline=EditorApplication.timeSinceStartup+75;next=EditorApplication.timeSinceStartup+.2;EditorApplication.update+=Tick;
 }catch(Exception e){Finish(e);}}
 static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)errors+=text+"\n"+stack+"\n";}
 static void Tick(){if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.3;try{
 if(EditorApplication.timeSinceStartup>=deadline)throw new Exception("Timed out at stage "+stage);
 if(StudioMaterialLibraryIndex.Scanning)return;
 if(library&&(bool)Read(library,"restoringPlacement"))return;
 switch(stage){
 case 110:
  Check(!library.CapturePlacement().floating&&library.CapturePlacement().side=="Right","New default must dock right");Zoom(true);Check(library.ListMode,"Minimum zoom value did not enter list mode; value="+Read(library,"thumbnail")+" hot="+GUIUtility.hotControl);
  var source=StudioMaterialLibraryIndex.Query(StudioMaterialLibraryIndex.StandardCategory,"");Check(source.Count==2,"Standard resources missing for list test");
  Set(library,"filtered",new List<StudioMaterialLibraryIndex.Entry>{new StudioMaterialLibraryIndex.Entry{guid=source[0].guid,path=source[0].path,name="Default material with a deliberately long single-line name for layout testing",localId=source[0].localId,material=StudioMaterialLibraryIndex.Resolve(source[0])},source[1]});
  ((Dictionary<string,Material>)Read(library,"loaded")).Clear();Set(library,"listContentWidth",0f);observedRows.Clear();
  StudioMaterialLibrary.TileInputProbe=(entry,rect)=>{observedRows.Add(rect);if(inject&&entry.name.StartsWith("Default"))Event.current.mousePosition=rect.center+drag;};stage=111;next=EditorApplication.timeSinceStartup+.5;break;
 case 111:
  Check(observedRows.Count>0&&observedRows.All(r=>Mathf.Abs(r.height-20)<.01f),"List did not render compact rows");Check((float)Read(library,"listContentWidth")>library.position.width-149,"Long names lack horizontal scroll extent");
  Check(((Dictionary<string,Material>)Read(library,"loaded")).Count==0,"List rendering loaded large preview materials");Check(((HashSet<int>)Read(library,"pendingPreviews")).Count==0,"List rendering requested large previews");Capture(library,"material-library-list.png");
  Selection.activeObject=StudioMeshMaterialPreview.CheckerMaterial;Set(library,"lockInspector",false);Click();Check(Selection.activeObject==StudioMeshMaterialPreview.DefaultMaterial,"List click failed Inspector selection");
  StudioMaterialLibrary.SuppressNativeDragForTests=true;drag=new Vector2(24,0);Send(EventType.MouseDrag);StudioMaterialLibrary.SuppressNativeDragForTests=false;Check(DragAndDrop.objectReferences.Length==1&&DragAndDrop.objectReferences[0]==StudioMeshMaterialPreview.DefaultMaterial,"List drag lost material payload");DragAndDrop.PrepareStartDrag();Send(EventType.MouseUp);drag=Vector2.zero;
  Check(library.TryDockBeside(owner,"Left",340),"Unable to set saved left placement");stage=114;next=EditorApplication.timeSinceStartup+.5;break;
 case 114:
  StudioMaterialLibrary.Toggle(owner);StudioMaterialLibrary.Toggle(owner);library=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Single();stage=112;next=EditorApplication.timeSinceStartup+.5;break;
 case 112:
  Check(library.CapturePlacement().side=="Left","Saved left placement was overridden by Right default");Check(library.ListMode,"Minimum zoom list mode was not restored");observedRows.Clear();Zoom(false);Check(!library.ListMode,"Zooming up did not restore grid view");stage=113;next=EditorApplication.timeSinceStartup+.5;break;
 case 113:
  Check(observedRows.Any(r=>r.height>20),"Grid did not restore thumbnail layout");Check(errors.Length==0,"List presentation errors: "+errors);Finish(null);return;
 case 100:
  var standard=StudioMaterialLibraryIndex.Query(StudioMaterialLibraryIndex.StandardCategory,"");
  Check(standard.Count==2,"Standard must contain two existing materials");Check(StudioMaterialLibraryIndex.Resolve(standard[0])==StudioMeshMaterialPreview.DefaultMaterial,"Standard default reference changed");Check(StudioMaterialLibraryIndex.Resolve(standard[1])==StudioMeshMaterialPreview.CheckerMaterial,"Standard checker reference changed");
  Check(StudioMaterialLibraryIndex.CategoryLabel(StudioMaterialLibraryIndex.StandardCategory)=="Standard"&&StudioMaterialLibraryIndex.CategoryLabel(StudioMaterialLibraryIndex.PrototypingCategory)=="Prototyping","Category display names incorrect");
  Check(StudioMaterialLibraryIndex.Query(StudioMaterialLibraryIndex.StandardCategory,"checker").Count==1,"Standard search failed");Check((string)Read(library,"folder")==StudioMaterialLibraryIndex.StandardCategory,"Scan changed pinned selection");Check(((List<StudioMaterialLibraryIndex.Entry>)Read(library,"filtered")).Count==2,"Standard tiles missing");
  Capture(library,"material-library-standard.png");library.SelectFolder(StudioMaterialLibraryIndex.PrototypingCategory);stage=101;next=EditorApplication.timeSinceStartup+.5;break;
 case 101:
  Check(StudioMaterialLibraryIndex.Query(StudioMaterialLibraryIndex.PrototypingCategory,"").Count==0,"Prototyping must start empty");Check(((List<StudioMaterialLibraryIndex.Entry>)Read(library,"filtered")).Count==0,"Empty Prototyping tiles incorrect");
  Capture(library,"material-library-prototyping.png");StudioMaterialLibraryIndex.Rescan();stage=102;break;
 case 102:
  Check((string)Read(library,"folder")==StudioMaterialLibraryIndex.PrototypingCategory,"Rescan removed empty pinned category");Check(StudioMaterialLibraryIndex.Query(StudioMaterialLibraryIndex.PrototypingCategory,"").Count==0,"Rescan populated empty Prototyping");Check(errors.Length==0,"Pinned UI errors: "+errors);Finish(null);return;
 case 90:
  var restored=library.CapturePlacement();Check(!restored.floating&&!restored.tab&&restored.side=="Right","Fresh-session docking did not restore");
  Check(StudioMaterialLibraryIndex.Sources==3,"Fresh-session source options lost");Check((string)Read(library,"folder")==Root,"Fresh-session browser folder lost");Check(StudioMaterialLibraryIndex.Query(Root,"").Count==4,"Project-open scan did not find expected materials");Check(errors.Length==0,"Fresh-session errors: "+errors);Finish(null);return;
 case 0:
  Check(StudioMaterialLibraryIndex.Query(Root,"").Count==4,"Material-only scan/embedded materials missing");Check(!StudioMaterialLibraryIndex.Folders.Contains(Root+"/Empty"),"Empty/nonmaterial folder included");Check(StudioMaterialLibraryIndex.Folders.Contains(Root+"/Props")&&StudioMaterialLibraryIndex.Folders.Contains("Assets"),"Folder ancestors missing");
  Check(StudioMaterialLibraryIndex.Query(Root,"ocean").Count==1,"Name query failed");Check(StudioMaterialLibraryIndex.Query(Root,"Props").Count==1,"Path query failed");Check(StudioMaterialLibraryIndex.Query(Root,"t:Texture").Count==0,"Search changed type filter");
  foreach(var entry in StudioMaterialLibraryIndex.Query(Root,"Embedded"))Check(StudioMaterialLibraryIndex.Resolve(entry)&&entry.localId!=0,"Embedded identity did not resolve");
  Check(StudioMaterialLibraryIndex.Entries.Any(e=>e.path.StartsWith("Packages/")),"Packages were excluded from both-source scan");
  StudioMaterialLibraryIndex.SetSources(1);stage++;break;
 case 1:
  Check(StudioMaterialLibraryIndex.Entries.All(e=>e.path.StartsWith("Assets/")),"Assets-only leaked packages");Check(StudioMaterialLibraryIndex.Query(Root,"").Count==4,"Assets scope lost fixture");StudioMaterialLibraryIndex.SetSources(2);stage++;break;
 case 2:
  Check(StudioMaterialLibraryIndex.Entries.Count>0&&StudioMaterialLibraryIndex.Entries.All(e=>e.path.StartsWith("Packages/")),"Packages-only scope incorrect");Check(StudioMaterialLibraryIndex.Query(Root,"").Count==0,"Packages scope leaked Assets");StudioMaterialLibraryIndex.SetSources(3);stage++;break;
 case 3:
  document=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);document.nodes.Add(new StudioModelDocument.Node{name="Preview",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles});editor=new StudioModelEditor(()=>{});editor.Load(document);owner=ScriptableObject.CreateInstance<MashBoxStudioViewport>();owner.position=new Rect(60,60,1200,760);owner.Bind(editor.Scene);StudioWindowOwnership.Show(owner);editor.BindModelCommands(owner,_=>{});
  StudioPreferenceStore.SetString("Studio.MaterialLibrary.Placement","");library=StudioMaterialLibrary.Open(owner);stage++;next=EditorApplication.timeSinceStartup+1;break;
 case 4:
  var placement=library.CapturePlacement();Check(!placement.floating&&!placement.tab&&placement.side=="Right","First-open docking was not right: "+JsonUtility.ToJson(placement));Check(library.position.x>owner.position.x,"Library is not on Modeler's right");
  Check(library.TryDockBeside(owner,"Left",340),"Could not dock to left for restore test");stage++;next=EditorApplication.timeSinceStartup+.5;break;
 case 5:
  Check(library.CapturePlacement().side=="Left","Left docking not captured");StudioMaterialLibrary.Toggle(owner);Check(!StudioMaterialLibrary.Visible,"Menu toggle failed to hide");StudioMaterialLibrary.Toggle(owner);library=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Single();stage++;next=EditorApplication.timeSinceStartup+1;break;
 case 6:
  Check(library.CapturePlacement().side=="Left"&&!library.CapturePlacement().floating,"Hide/show lost dock position");
  Check((bool)typeof(StudioMaterialLibrary).GetMethod("TryDockTab",F).Invoke(library,new object[]{owner,1}),"Tab docking failed");stage=60;next=EditorApplication.timeSinceStartup+.5;break;
 case 60:
  Check(library.CapturePlacement().tab,"Tab placement not captured");StudioMaterialLibrary.Toggle(owner);StudioMaterialLibrary.Toggle(owner);library=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Single();stage=61;next=EditorApplication.timeSinceStartup+.5;break;
 case 61:
  Check(library.CapturePlacement().tab,"Tab placement not restored");StudioMaterialLibrary.Toggle(owner);
  StudioPreferenceStore.SetString("Studio.MaterialLibrary.Placement",JsonUtility.ToJson(new StudioMaterialLibrary.Placement{floating=true,bounds=new Rect(400,100,480,600)}));
  StudioMaterialLibrary.Toggle(owner);library=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Single();stage=63;next=EditorApplication.timeSinceStartup+.5;break;
 case 63:
  Check(library.CapturePlacement().floating&&Mathf.Abs(library.position.width-480)<3,"Floating bounds not restored: "+JsonUtility.ToJson(library.CapturePlacement())+" stored="+StudioPreferenceStore.GetString("Studio.MaterialLibrary.Placement"));StudioMaterialLibrary.Toggle(owner);StudioMaterialLibrary.Toggle(owner);library=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Single();stage=64;next=EditorApplication.timeSinceStartup+.5;break;
 case 64:
  Check(library.CapturePlacement().floating&&Mathf.Abs(library.position.width-480)<3,"Floating hide/show lost bounds");Check(library.TryDockBeside(owner,"Right",340),"Return to right dock failed");stage=62;next=EditorApplication.timeSinceStartup+.5;break;
 case 62:
  var menu=new StudioMenu("Window");typeof(StudioModelEditor).GetMethod("PopulateModelMenu",F).Invoke(editor,new object[]{"Window",menu,owner,(Action<StudioModelDocument>)(_=>{})});var item=menu.Items.Single(i=>i.Text=="Material Library");Check(item.Checked&&item.Enabled&&!item.ShortcutEligible&&item.Image,"Window menu state/icon/shortcut exclusion");
  library.SelectFolder(Root+"/Props");Selection.activeObject=red;
  StudioMaterialLibrary.TileInputProbe=(entry,rect)=>{if(inject&&entry.name=="Ocean Blue")Event.current.mousePosition=rect.center+drag;};
  Click();Check(Selection.activeObject==blue,"Click did not inspect material");Check((string)Read(library,"folder")==Root+"/Props","Click changed library folder");
  Selection.activeObject=red;Check((string)Read(library,"folder")==Root+"/Props","External selection linked library folders");
  Set(library,"lockInspector",true);Click();Check(Selection.activeObject==red,"Lock did not preserve Inspector selection");Set(library,"lockInspector",false);Click();
  StudioMaterialLibrary.SuppressNativeDragForTests=true;drag=new Vector2(24,0);Send(EventType.MouseDrag);StudioMaterialLibrary.SuppressNativeDragForTests=false;Check(DragAndDrop.objectReferences.Length==1&&DragAndDrop.objectReferences[0]==blue,"Tile drag did not carry actual material; pressed="+Read(library,"pressed")+" hot="+GUIUtility.hotControl+" refs="+string.Join(",",DragAndDrop.objectReferences.Select(o=>o?o.name:"null")));DragAndDrop.PrepareStartDrag();Send(EventType.MouseUp);drag=Vector2.zero;
  library.SelectFolder(Root);stage=7;next=EditorApplication.timeSinceStartup+2;break;
 case 7:
  Capture(library,"material-library-native.png");Check(StudioMaterialLibrary.Visible,"Library disappeared during preview");
  StudioMaterialLibrary.HeaderInputProbe=()=>{if(Event.current.isMouse)Event.current.mousePosition=new Vector2(library.position.width-12,10);};library.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=new Vector2(200,100)});library.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=new Vector2(200,100)});StudioMaterialLibrary.HeaderInputProbe=null;stage++;next=EditorApplication.timeSinceStartup+1;break;
 case 8:
  var popupWindow=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetType().FullName=="UnityEditor.PopupWindow");Check(popupWindow,"Options gear did not open popup");if(popupWindow){Capture(popupWindow,"material-library-options.png");popupWindow.Close();}
  Check(errors.Length==0,"Native UI errors: "+errors);Finish(null);return;
 }
 }catch(Exception e){Finish(e);}}
 static void SetOption(PopupWindowContent popup){Check(popup.GetWindowSize().x>=200,"Options popup has invalid bounds");}
 static void Click(){library.Focus();drag=Vector2.zero;Send(EventType.MouseDown);}
 static void Send(EventType type){inject=true;try{library.SendEvent(new Event{type=type,button=0,mousePosition=new Vector2(200,100),delta=drag});}finally{inject=false;}}
 static void Capture(EditorWindow window,string name)
 {
  var parent=typeof(EditorWindow).GetField("m_Parent",F).GetValue(window);var rect=new Rect(Vector2.zero,window.position.size);var rt=new RenderTexture((int)rect.width,(int)rect.height,0);rt.Create();var old=RenderTexture.active;
  try{parent.GetType().GetMethod("GrabPixels",F).Invoke(parent,new object[]{rt,rect});RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();var pixels=image.GetPixels32();Check(pixels.Select(c=>c.r).Distinct().Count()>20,"Native library did not render");var flipped=new Color32[pixels.Length];for(int y=0;y<rt.height;y++)Array.Copy(pixels,(rt.height-1-y)*rt.width,flipped,y*rt.width,rt.width);image.SetPixels32(flipped);image.Apply();File.WriteAllBytes(Out(name),image.EncodeToPNG());Object.DestroyImmediate(image);}finally{RenderTexture.active=old;Object.DestroyImmediate(rt);}
 }
 static void Finish(Exception e){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;StudioMaterialLibrary.TileInputProbe=null;StudioMaterialLibrary.HeaderInputProbe=null;StudioMaterialLibrary.SuppressNativeDragForTests=false;File.WriteAllText(Out(presentation?"material-library-presentation-result.txt":pinned?"material-library-pinned-result.txt":reading?"material-library-reload-result.txt":"material-library-result.txt"),e==null?"PASS "+checks+" material scan/scope/query/docking/input/native-render checks":e.ToString()+"\n"+errors);EditorApplication.delayCall+=()=>{if(pinned)StudioMaterialLibraryIndex.SetSources(oldSources);if(library)library.Close();if(owner)owner.Close();editor?.Dispose();if(document){foreach(var n in document.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(document);}EditorApplication.Exit(e==null?0:1);};}
}
