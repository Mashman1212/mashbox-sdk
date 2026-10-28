using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMaterialLibrary : EditorWindow, IHasCustomMenu
    {
        [SerializeField] private MashBoxStudioViewport owner;
        [SerializeField] private string folder="", search="", selected="";
        [SerializeField] private List<string> expanded=new List<string>{"Assets","Packages"};
        private const string Prefix="Studio.MaterialLibrary.";
        private Vector2 folderScroll, materialScroll;
        private float folderWidth=145, thumbnail=76;
        private float listContentWidth;
        internal bool ListMode => thumbnail<=40.01f;
        private bool lockInspector, resizing;
        private string pressed;
        private Vector2 pressedAt;
        private int tileControl;
        private List<StudioMaterialLibraryIndex.Entry> filtered=new List<StudioMaterialLibraryIndex.Entry>();
        private List<string> folderRows=new List<string>();
        private readonly HashSet<string> foldersWithChildren=new HashSet<string>();
        private readonly Dictionary<string,Material> loaded=new Dictionary<string,Material>();
        private readonly HashSet<int> pendingPreviews=new HashSet<int>();
        private readonly HashSet<int> timedOutPreviews=new HashSet<int>();
        private double previewDeadline,nextPreview;
        private int indexRevision=-1;
        private GUIStyle tileLabel, folderLabel;
        internal static bool Visible => Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().Any(w=>w&&Host(w)!=null);
        internal static void Toggle(MashBoxStudioViewport view)
        {
            var existing=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().FirstOrDefault(w=>w&&Host(w)!=null);
            if(existing){existing.SavePlacement();existing.closing=true;existing.Close();return;}
            Open(view);
        }
        internal static StudioMaterialLibrary Open(MashBoxStudioViewport view)
        {
            var existing=Resources.FindObjectsOfTypeAll<StudioMaterialLibrary>().FirstOrDefault(w=>w&&Host(w)!=null);
            if(existing){existing.Focus();return existing;}
            var window=CreateInstance<StudioMaterialLibrary>();window.restoringPlacement=true;window.owner=view?view:StudioWindowOwnership.FindStudioOwner() as MashBoxStudioViewport;
            window.position=new Rect(100,100,480,640);StudioWindowOwnership.Show(window,window.owner);
            EditorApplication.delayCall+=window.RestorePlacement;return window;
        }
        private void OnEnable()
        {
            titleContent=new GUIContent("Material Library",StudioToolbarIcons.Get("Material Icon"));minSize=new Vector2(300,260);wantsMouseMove=true;
            folder=StudioPreferenceStore.GetString(Prefix+"Folder",folder);thumbnail=Mathf.Clamp(StudioPreferenceStore.GetFloat(Prefix+"Thumbnail",76),40,130);
            folderWidth=Mathf.Clamp(StudioPreferenceStore.GetFloat(Prefix+"FolderWidth",145),95,300);lockInspector=StudioPreferenceStore.GetFloat(Prefix+"LockInspector",0)>.5f;
            StudioMaterialLibraryIndex.Changed+=IndexChanged;StudioMaterialLibraryIndex.EnsureInitialized();RefreshResults();
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_=>SavePlacement());
        }
        private void OnLostFocus(){pressed=null;SavePlacement();}
        private void OnBecameInvisible(){EditorApplication.update-=PollPreviews;pendingPreviews.Clear();}
        private void OnBecameVisible()=>Repaint();
        private void OnDisable(){StudioMaterialLibraryIndex.Changed-=IndexChanged;EditorApplication.update-=PollPreviews;pendingPreviews.Clear();}
        private void OnDestroy(){SavePlacement();StudioWindowOwnership.CloseOwnedWindows(this);}
        private void IndexChanged()
        {
            if(indexRevision!=StudioMaterialLibraryIndex.Revision)RefreshResults();
            Repaint();
        }
        private void RefreshResults()
        {
            indexRevision=StudioMaterialLibraryIndex.Revision;
            if(!StudioMaterialLibraryIndex.Scanning&&indexRevision>0&&folder.Length>0&&!StudioMaterialLibraryIndex.IsPinned(folder)&&!StudioMaterialLibraryIndex.Folders.Contains(folder))folder="";
            filtered=StudioMaterialLibraryIndex.Query(folder,search);listContentWidth=0;loaded.Clear();timedOutPreviews.Clear();BuildFolderRows();
        }
        private void BuildFolderRows()
        {
            foldersWithChildren.Clear();
            foreach(string item in StudioMaterialLibraryIndex.Folders){int parent=item.LastIndexOf('/');if(parent>=0)foldersWithChildren.Add(item.Substring(0,parent));}
            folderRows=StudioMaterialLibraryIndex.Folders.Where(path=>
            {
                int slash=path.IndexOf('/');while(slash>=0){if(!expanded.Contains(path.Substring(0,slash)))return false;slash=path.IndexOf('/',slash+1);}return true;
            }).ToList();
        }
        internal void SelectFolder(string path)
        {
            folder=path??"";StudioPreferenceStore.SetString(Prefix+"Folder",folder);materialScroll=Vector2.zero;RefreshResults();Repaint();
        }
        private void OnGUI()
        {
            using(StudioTheme.Scope())
            {
                EditorGUI.DrawRect(new Rect(Vector2.zero,position.size),StudioTheme.Current.Panel);
                DrawHeader();
                var body=new Rect(0,21,position.width,Mathf.Max(0,position.height-44));
                folderWidth=Mathf.Clamp(folderWidth,95,Mathf.Max(95,position.width-160));
                DrawFolders(new Rect(body.x,body.y,folderWidth,body.height));
                DrawSplitter(new Rect(folderWidth,body.y,4,body.height));
                DrawMaterials(new Rect(folderWidth+4,body.y,Mathf.Max(1,body.width-folderWidth-4),body.height));
                DrawFooter(new Rect(0,position.height-23,position.width,23));
            }
        }
        private void DrawHeader()
        {
#if MASHBOX_STUDIO_TESTS
            HeaderInputProbe?.Invoke();
#endif
            GUILayout.BeginHorizontal(EditorStyles.toolbar,GUILayout.Height(21));
            string query=GUILayout.TextField(search,EditorStyles.toolbarSearchField);
            if(query!=search){search=query;materialScroll=Vector2.zero;RefreshResults();}
            if(search.Length>0&&GUILayout.Button(new GUIContent("×","Clear search"),EditorStyles.toolbarButton,GUILayout.Width(20))){search="";RefreshResults();GUI.FocusControl(null);}
            if(GUILayout.Button(new GUIContent(StudioToolbarIcons.Get("Refresh"),"Rescan material folders"),EditorStyles.toolbarButton,GUILayout.Width(24)))StudioMaterialLibraryIndex.Rescan();
            if(GUILayout.Button(new GUIContent(StudioToolbarIcons.Get("SettingsIcon"),"Material Library options"),EditorStyles.toolbarButton,GUILayout.Width(24)))
                UnityEditor.PopupWindow.Show(GUILayoutUtility.GetLastRect(),new Options(this));
            GUILayout.EndHorizontal();
        }
        private void DrawFolders(Rect area)
        {
            GUI.Box(area,GUIContent.none,EditorStyles.helpBox);float rowHeight=20;
            DrawFolderRow(new Rect(area.x,area.y,area.width,rowHeight),StudioMaterialLibraryIndex.StandardCategory,0,false);
            DrawFolderRow(new Rect(area.x,area.y+rowHeight,area.width,rowHeight),StudioMaterialLibraryIndex.PrototypingCategory,0,false);
            EditorGUI.DrawRect(new Rect(area.x+5,area.y+41,Mathf.Max(1,area.width-10),1),new Color(.5f,.5f,.5f,.25f));
            area.y+=45;area.height=Mathf.Max(1,area.height-45);
            folderScroll=GUI.BeginScrollView(area,folderScroll,new Rect(0,0,Mathf.Max(1,area.width-16),(folderRows.Count+1)*rowHeight));
            DrawFolderRow(new Rect(0,0,area.width-16,rowHeight),"",0,false);
            int start=Mathf.Max(0,Mathf.FloorToInt(folderScroll.y/rowHeight)-1),end=Mathf.Min(folderRows.Count,Mathf.CeilToInt((folderScroll.y+area.height)/rowHeight));
            for(int i=start;i<end;i++)
            {
                string path=folderRows[i];int depth=path.Count(c=>c=='/');bool child=foldersWithChildren.Contains(path);
                DrawFolderRow(new Rect(0,(i+1)*rowHeight,area.width-16,rowHeight),path,depth,child);
            }
            GUI.EndScrollView();
        }
        private void DrawFolderRow(Rect row,string path,int depth,bool children)
        {
            if(folder==path)EditorGUI.DrawRect(row,StudioTheme.Current.Accent*.55f);
            float indent=5+depth*12;
            if(children)
            {
                bool open=expanded.Contains(path),next=EditorGUI.Foldout(new Rect(indent,row.y+1,14,18),open,GUIContent.none);
                if(next!=open){if(next)expanded.Add(path);else expanded.Remove(path);BuildFolderRows();Event.current.Use();Repaint();}
            }
            var content=new Rect(indent+15,row.y,row.width-indent-15,row.height);
            GUI.Label(new Rect(content.x,content.y+2,16,16),StudioToolbarIcons.Get(path.Length==0?"Material Icon":"Folder Icon"));
            if(folderLabel==null)folderLabel=new GUIStyle(EditorStyles.label){clipping=TextClipping.Clip};
            string label=StudioMaterialLibraryIndex.IsPinned(path)?StudioMaterialLibraryIndex.CategoryLabel(path):path.Length==0?"All Materials":path.Substring(path.LastIndexOf('/')+1);
            GUI.Label(new Rect(content.x+20,row.y,Mathf.Max(0,content.width-20),row.height),new GUIContent(label,StudioMaterialLibraryIndex.IsPinned(path)?label+" materials":path),folderLabel);
            if(Event.current.type==EventType.MouseDown&&Event.current.button==0&&row.Contains(Event.current.mousePosition)){SelectFolder(path);Event.current.Use();}
        }
        private void DrawSplitter(Rect rect)
        {
            EditorGUIUtility.AddCursorRect(rect,MouseCursor.ResizeHorizontal);var evt=Event.current;
            int id=GUIUtility.GetControlID("MaterialLibrary.Splitter".GetHashCode(),FocusType.Passive);
            if(evt.type==EventType.MouseDown&&rect.Contains(evt.mousePosition)){resizing=true;GUIUtility.hotControl=id;evt.Use();}
            if(resizing&&GUIUtility.hotControl==id&&evt.type==EventType.MouseDrag){folderWidth=evt.mousePosition.x;Repaint();evt.Use();}
            if(resizing&&evt.rawType==EventType.MouseUp){resizing=false;GUIUtility.hotControl=0;StudioPreferenceStore.SetFloat(Prefix+"FolderWidth",folderWidth);evt.Use();}
        }
        private void DrawMaterials(Rect area)
        {
            tileControl=GUIUtility.GetControlID("MaterialLibrary.Tiles".GetHashCode(),FocusType.Passive);
            GUI.Label(new Rect(area.x+7,area.y+2,area.width-14,20),new GUIContent(StudioMaterialLibraryIndex.CategoryLabel(folder),StudioMaterialLibraryIndex.IsPinned(folder)?"":folder),EditorStyles.miniLabel);
            area.y+=23;area.height-=23;bool list=ListMode;
            float cell=thumbnail+16,rowHeight=list?20:thumbnail+39;
            if(list&&listContentWidth==0)
            {
                listContentWidth=32;
                foreach(var entry in filtered)listContentWidth=Mathf.Max(listContentWidth,EditorStyles.label.CalcSize(new GUIContent(entry.name)).x+32);
            }
            int columns=list?1:Mathf.Max(1,Mathf.FloorToInt((area.width-16)/cell));
            float contentWidth=Mathf.Max(1,area.width-16);if(list)contentWidth=Mathf.Max(contentWidth,listContentWidth);
            float width=contentWidth/columns;
            int rows=(filtered.Count+columns-1)/columns;
            materialScroll=GUI.BeginScrollView(area,materialScroll,new Rect(0,0,contentWidth,rows*rowHeight));
            if(filtered.Count==0)GUI.Label(new Rect(8,10,Mathf.Max(1,area.width-24),40),StudioMaterialLibraryIndex.Scanning&&!StudioMaterialLibraryIndex.IsPinned(folder)?"Scanning materials…":folder==StudioMaterialLibraryIndex.PrototypingCategory&&search.Length==0?"No prototyping materials yet.":"No materials match this view.",EditorStyles.wordWrappedMiniLabel);
            int first=Mathf.Max(0,Mathf.FloorToInt(materialScroll.y/rowHeight)*columns),last=Mathf.Min(filtered.Count,Mathf.CeilToInt((materialScroll.y+area.height)/rowHeight)*columns);
            for(int i=first;i<last;i++)DrawTile(new Rect((i%columns)*width,(i/columns)*rowHeight,width,rowHeight),filtered[i],list);
            GUI.EndScrollView();
            if(Event.current.rawType==EventType.MouseUp&&GUIUtility.hotControl==tileControl){GUIUtility.hotControl=0;pressed=null;}
        }
        private Material MaterialFor(StudioMaterialLibraryIndex.Entry entry)
        {
            if(loaded.TryGetValue(entry.Key,out var material)&&material)return material;
            material=StudioMaterialLibraryIndex.Resolve(entry);if(loaded.Count>256)loaded.Clear();loaded[entry.Key]=material;return material;
        }
#if MASHBOX_STUDIO_TESTS
        internal static Action<StudioMaterialLibraryIndex.Entry,Rect> TileInputProbe;
        internal static bool SuppressNativeDragForTests;
        internal static Action HeaderInputProbe;
#endif
        private void DrawTile(Rect cell,StudioMaterialLibraryIndex.Entry entry,bool list=false)
        {
#if MASHBOX_STUDIO_TESTS
            TileInputProbe?.Invoke(entry,cell);
#endif
            var tile=list?new Rect(cell.x,cell.y,cell.width,cell.height):new Rect(cell.x+3,cell.y+3,cell.width-6,cell.height-6);bool isSelected=selected==entry.Key;
            if(isSelected)EditorGUI.DrawRect(tile,StudioTheme.Current.Accent*.6f);
            var image=list?new Rect(cell.x+4,cell.y+2,16,16):new Rect(cell.center.x-thumbnail*.5f,cell.y+5,thumbnail,thumbnail);
            if(list)GUI.DrawTexture(image,StudioToolbarIcons.Get("Material Icon"),ScaleMode.ScaleToFit,true);
            else if(Event.current.type==EventType.Repaint)
            {
                var material=MaterialFor(entry);Texture preview=null;
                if(material){preview=AssetPreview.GetAssetPreview(material);if(!preview&&AssetPreview.IsLoadingAssetPreview(material.GetInstanceID()))QueuePreview(material.GetInstanceID());}
                EditorGUI.DrawRect(image,new Color(.25f,.25f,.25f,1));
                GUI.DrawTexture(image,preview?preview:StudioToolbarIcons.Get("Material Icon"),ScaleMode.ScaleToFit,true);
            }
            if(tileLabel==null)tileLabel=new GUIStyle(EditorStyles.label){alignment=TextAnchor.UpperCenter,clipping=TextClipping.Clip};
            GUI.Label(list?new Rect(cell.x+24,cell.y,Mathf.Max(1,cell.width-24),cell.height):new Rect(cell.x+3,image.yMax+3,cell.width-6,32),new GUIContent(entry.name,entry.name+"\n"+entry.path),list?EditorStyles.label:tileLabel);
            var evt=Event.current;
            if(evt.type==EventType.MouseDown&&evt.button==0&&tile.Contains(evt.mousePosition))
            {
                selected=pressed=entry.Key;pressedAt=evt.mousePosition;GUIUtility.hotControl=tileControl;var material=MaterialFor(entry);
                if(material&&!lockInspector)Selection.activeObject=material;
                if(evt.clickCount>1&&material)AssetDatabase.OpenAsset(material);
                evt.Use();Repaint();
            }
            if(evt.type==EventType.MouseDrag&&GUIUtility.hotControl==tileControl&&pressed==entry.Key&&(evt.mousePosition-pressedAt).sqrMagnitude>25)
            {
                var material=MaterialFor(entry);GUIUtility.hotControl=0;if(material){DragAndDrop.PrepareStartDrag();DragAndDrop.objectReferences=new UnityEngine.Object[]{material};DragAndDrop.paths=new[]{entry.path};
#if MASHBOX_STUDIO_TESTS
                if(!SuppressNativeDragForTests)
#endif
                    DragAndDrop.StartDrag(entry.name);}pressed=null;evt.Use();
            }
            if(evt.rawType==EventType.MouseUp)pressed=null;
            if(evt.type==EventType.ContextClick&&tile.Contains(evt.mousePosition))
            {
                var menu=new GenericMenu();menu.AddItem(new GUIContent("Inspect Material"),false,()=>{var m=MaterialFor(entry);if(m)Selection.activeObject=m;});
                menu.AddItem(new GUIContent("Ping in Project"),false,()=>{var m=MaterialFor(entry);if(m)EditorGUIUtility.PingObject(m);});menu.ShowAsContext();evt.Use();
            }
        }
        private void DrawFooter(Rect area)
        {
            GUI.Box(area,GUIContent.none,EditorStyles.toolbar);
            string status=StudioMaterialLibraryIndex.Error!=null?"Scan failed · Rescan available":StudioMaterialLibraryIndex.Scanning?"Scanning… "+Mathf.RoundToInt(StudioMaterialLibraryIndex.Progress*100)+"%":filtered.Count+" materials"+(StudioMaterialLibraryIndex.Stale?" · Rescan available":"");
            GUI.Label(new Rect(7,area.y+3,Mathf.Max(1,area.width-130),18),new GUIContent(status,StudioMaterialLibraryIndex.Error),EditorStyles.miniLabel);
            float size=GUI.HorizontalSlider(new Rect(area.xMax-115,area.y+6,105,16),thumbnail,40,130);
            if(size!=thumbnail)
            {
                bool wasList=ListMode;thumbnail=size;
                if(wasList!=ListMode)materialScroll=Vector2.zero;
                if(ListMode){EditorApplication.update-=PollPreviews;pendingPreviews.Clear();}
                StudioPreferenceStore.SetFloat(Prefix+"Thumbnail",thumbnail);Repaint();
            }
        }
        private void QueuePreview(int id)
        {
            if(timedOutPreviews.Contains(id)||!pendingPreviews.Add(id))return;previewDeadline=EditorApplication.timeSinceStartup+8;
            EditorApplication.update-=PollPreviews;EditorApplication.update+=PollPreviews;
        }
        private void PollPreviews()
        {
            double now=EditorApplication.timeSinceStartup;if(now<nextPreview)return;nextPreview=now+.1;
            pendingPreviews.RemoveWhere(id=>!AssetPreview.IsLoadingAssetPreview(id));Repaint();
            if(pendingPreviews.Count==0||now>previewDeadline){if(now>previewDeadline)timedOutPreviews.UnionWith(pendingPreviews);pendingPreviews.Clear();EditorApplication.update-=PollPreviews;}
        }
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Rescan Materials"),false,StudioMaterialLibraryIndex.Rescan);
            menu.AddItem(new GUIContent("All Materials"),folder.Length==0,()=>SelectFolder(""));
            menu.AddSeparator("");menu.AddItem(new GUIContent("Dock Right of Modeler"),false,()=>{if(owner){restoringPlacement=true;TryDockBeside(owner,"Right",360);restoringPlacement=false;SavePlacement();}});
        }
        private sealed class Options : PopupWindowContent
        {
            private readonly StudioMaterialLibrary library;
            internal Options(StudioMaterialLibrary library){this.library=library;}
            public override Vector2 GetWindowSize()=>new Vector2(220,146);
            public override void OnOpen()=>StudioWindowOwnership.Register(editorWindow,library);
            public override void OnGUI(Rect rect)
            {
                GUILayout.Label("Material sources",EditorStyles.boldLabel);int sources=StudioMaterialLibraryIndex.Sources;
                bool assets=(sources&1)!=0,packages=(sources&2)!=0;
                using(new EditorGUI.DisabledScope(!packages))assets=EditorGUILayout.ToggleLeft("Assets",assets);
                using(new EditorGUI.DisabledScope(!assets))packages=EditorGUILayout.ToggleLeft("Packages",packages);
                int next=(assets?1:0)|(packages?2:0);if(next!=sources)StudioMaterialLibraryIndex.SetSources(next);
                bool locked=EditorGUILayout.ToggleLeft(new GUIContent("Keep Inspector selection","Browse without changing the Inspector selection."),library.lockInspector);
                if(locked!=library.lockInspector){library.lockInspector=locked;StudioPreferenceStore.SetFloat(Prefix+"LockInspector",locked?1:0);}
                GUILayout.Space(4);GUILayout.Label("Choose either source or both. Type is always Material.",EditorStyles.wordWrappedMiniLabel);
                if(GUILayout.Button("Rescan Materials"))StudioMaterialLibraryIndex.Rescan();
            }
        }
    }
}
