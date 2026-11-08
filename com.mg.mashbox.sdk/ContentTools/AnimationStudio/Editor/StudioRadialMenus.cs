using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Individual portable JSON presets, using the shared toolbar command-list schema.
    internal static class StudioRadialMenus
    {
        internal const string MaterialSlotsContextId = "context-material-assign-selection";
        internal static string MaterialAssignBranch => Branch(MaterialSlotsContextId);
        internal const string SpacerKey = "Radial/EmptySpacer";
        internal const string PreferenceKey = "Studio.RadialMenus";
        [Serializable] private sealed class Collection { public List<StudioCustomToolbarDefinition> items; }
        [Serializable] private sealed class PresetFile
        {
            public string format="MashBoxStudio.RadialMenu"; public int version=1;
            public string id,name; public List<string> commands;
        }
        [Serializable] private sealed class Manifest
        {
            public string format="MashBoxStudio.RadialMenus";public int version=1;public List<string> order=new List<string>();
        }
        internal static string DirectoryPath=>StudioPreferenceStore.RadialMenusDirectory;
        internal static string PresetsPath=>Path.Combine(DirectoryPath,"Presets");
        internal static string DefaultsPath=>Path.Combine(DirectoryPath,"Defaults");
        private static string ManifestPath=>Path.Combine(DirectoryPath,"manifest.json");
        private static bool initialized;
        private static int dirty=1,notificationQueued;
        private static SynchronizationContext context;
        private static FileSystemWatcher presetWatcher,manifestWatcher;
        private static readonly HashSet<string> loadedIds=new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> reportedErrors=new HashSet<string>(StringComparer.Ordinal);
        internal static event Action Changed;
        static StudioRadialMenus(){context=SynchronizationContext.Current;AssemblyReloadEvents.beforeAssemblyReload+=DisposeWatchers;EditorApplication.quitting+=DisposeWatchers;}
        private static bool ValidId(string id)=>!string.IsNullOrEmpty(id)&&id.Length<=128&&id.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_');
        private static string PresetPath(string folder,string id)
        {if(!ValidId(id))throw new InvalidDataException("Invalid radial preset ID: "+id);return Path.Combine(folder,id+".json");}
        private static StudioCustomToolbarDefinition Copy(StudioCustomToolbarDefinition value)=>new StudioCustomToolbarDefinition{id=value.id,name=value.name,commands=value.commands.ToList()};
        private static string Serialize(StudioCustomToolbarDefinition value)=>JsonUtility.ToJson(new PresetFile{id=value.id,name=value.name,commands=value.commands},true);
        private static void WriteAtomic(string path,string value)
        {
            if(File.Exists(path)&&File.ReadAllText(path)==value)return;
            StudioPreferenceStore.WriteAtomic(path,value);
        }
        private static void Validate(List<StudioCustomToolbarDefinition> values)
        {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in values)
                if(item==null||!ValidId(item.id)||!ids.Add(item.id)||string.IsNullOrWhiteSpace(item.name)||item.commands==null||item.commands.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Radial presets require unique portable IDs, a name and nonempty command keys.");
        }
        private static void WriteFiles(List<StudioCustomToolbarDefinition> values,bool removeMissing)
        {
            Validate(values);
            foreach(var item in values)WriteAtomic(PresetPath(PresetsPath,item.id),Serialize(item));
            WriteAtomic(ManifestPath,JsonUtility.ToJson(new Manifest{order=values.Select(x=>x.id).ToList()},true));
            // Delete only recognized presets intentionally removed in this editor.
            if(removeMissing)foreach(string id in loadedIds.Where(id=>!values.Any(x=>x.id==id)).ToArray())
            {string path=PresetPath(PresetsPath,id);if(File.Exists(path))File.Delete(path);}
        }
        private static void InitializeStorage()
        {
            if(initialized)return;
            context=context??SynchronizationContext.Current;
            System.IO.Directory.CreateDirectory(DefaultsPath);System.IO.Directory.CreateDirectory(PresetsPath);
            foreach(var item in Defaults())WriteAtomic(PresetPath(DefaultsPath,item.id),Serialize(item));
            if(!File.Exists(ManifestPath)&&!System.IO.Directory.EnumerateFiles(PresetsPath,"*.json").Any())
            {
                string legacy=StudioPreferenceStore.GetString(PreferenceKey);
                var values=string.IsNullOrEmpty(legacy)?Defaults():JsonUtility.FromJson<Collection>(legacy)?.items;
                if(values==null)throw new InvalidDataException("Cannot migrate the existing radial preset collection.");
                WriteFiles(values,false);
            }
            initialized=true;
            presetWatcher=new FileSystemWatcher(PresetsPath,"*.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
            manifestWatcher=new FileSystemWatcher(DirectoryPath,"manifest.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
            foreach(var watcher in new[]{presetWatcher,manifestWatcher})
            {watcher.Changed+=FilesChanged;watcher.Created+=FilesChanged;watcher.Deleted+=FilesChanged;watcher.Renamed+=FilesChanged;watcher.EnableRaisingEvents=true;}
        }
        private static void DisposeWatchers(){presetWatcher?.Dispose();manifestWatcher?.Dispose();presetWatcher=manifestWatcher=null;}
        private static void FilesChanged(object sender,FileSystemEventArgs args)
        {
            Interlocked.Exchange(ref dirty,1);
            if(context==null||Interlocked.Exchange(ref notificationQueued,1)!=0)return;
            context.Post(_=>{
                Interlocked.Exchange(ref notificationQueued,0);
                if(presetWatcher==null)return;
                Read();NotifyChanged();
            },null);
        }
        private static void NotifyChanged()
        {
            foreach(var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>())
            {view.Studio?.ToolbarModelEditor?.CancelMarkingMenu();view.MarkDirty(MashBoxStudioViewport.DirtyState.UI);}
            Changed?.Invoke();
        }
        private static List<StudioCustomToolbarDefinition> LoadFiles()
        {
            var order=new List<string>();
            if(File.Exists(ManifestPath))
            {
                var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
                if(manifest==null||manifest.format!="MashBoxStudio.RadialMenus"||manifest.version!=1)throw new InvalidDataException("Unsupported radial menu manifest.");
                order=manifest.order??order;
            }
            var result=new List<StudioCustomToolbarDefinition>();
            foreach(string path in System.IO.Directory.EnumerateFiles(PresetsPath,"*.json").OrderBy(x=>x,StringComparer.Ordinal))
            {
                string id=Path.GetFileNameWithoutExtension(path);
                try
                {
                    var file=JsonUtility.FromJson<PresetFile>(File.ReadAllText(path));
                    if(file==null||file.format!="MashBoxStudio.RadialMenu"||file.version!=1||file.id!=id)throw new InvalidDataException("Unsupported radial preset or filename/ID mismatch.");
                    var value=new StudioCustomToolbarDefinition{id=file.id,name=file.name,commands=file.commands};Validate(new List<StudioCustomToolbarDefinition>{value});result.Add(value);
                }
                catch(Exception error) when(error is ArgumentException||error is IOException||error is InvalidDataException)
                {
                    string message=path+": "+error.Message;if(reportedErrors.Add(message))Debug.LogWarning("Cannot load radial preset "+message);
                    var previous=cached?.Find(x=>x.id==id);if(previous!=null)result.Add(Copy(previous));
                }
            }
            result=result.OrderBy(x=>{int index=order.IndexOf(x.id);return index<0?int.MaxValue:index;}).ThenBy(x=>order.Contains(x.id)?"":x.name,StringComparer.Ordinal).ToList();
            loadedIds.Clear();foreach(var item in result)loadedIds.Add(item.id);
            return result;
        }
        private static List<StudioCustomToolbarDefinition> cached;
        internal static StudioShortcut Shortcut(string id) => id == "components" ? StudioShortcut.MarkingMenu : id == "axes" ? StudioShortcut.AxisMenu : StudioModelCommands.StableId("Radial Menus/" + id);
        internal static bool Matches(Event evt) => Read().Any(x=>StudioShortcuts.Matches(Shortcut(x.id),evt));
        internal static string Branch(string id) => "RadialPreset/" + id;
        internal static List<StudioCustomToolbarDefinition> Defaults()
        {
            StudioCustomToolbarDefinition D(string id,string name,params string[] items) => new StudioCustomToolbarDefinition { id=id,name=name,commands=items.ToList() };
            return new List<StudioCustomToolbarDefinition> {
                D("components","Standard","Select/Edge","Select/Object Mode","RadialPreset/uv","Select/Face","Select/Vertex","RadialPreset/pivot","RadialPreset/context-material-assign-selection"),
                D("pivot","Pivot","Modify/Edit Pivot","Modify/Center Pivot","Modify/Freeze Transforms"),
                D("uv","UVs","UV/Select UVs","UV/Select UV Shells"),
                D("symmetry","Symmetry","Modify/Symmetry/Enabled","Modify/Symmetry/Space","Modify/Symmetry/X","Modify/Symmetry/Y","Modify/Symmetry/Z"),
                D("axes","Orientation","RadialPreset/symmetry","Legacy/ComponentAxes","Legacy/WorldAxes","Legacy/ObjectAxes","Legacy/CenterAxes"),
                D("8e912b9b50274f31b2a787d56ed430f6","Convert Selection","RadialPreset/convert-vertices","RadialPreset/convert-edges","RadialPreset/convert-faces","Select/Convert Selection/Objects"),
                D("convert-vertices","To Vertices","Select/Convert Selection/Vertices","Select/Convert Selection/To Perimeter Vertices"),
                D("convert-edges","To Edges","Select/Convert Selection/Edges","Select/Convert Selection/To Perimeter Edges"),
                D("convert-faces","To Faces","Select/Convert Selection/Faces","Select/Convert Selection/To Perimeter Faces")
            };
        }
        internal static List<StudioCustomToolbarDefinition> Read()
        {
            bool reload=Interlocked.Exchange(ref dirty,0)!=0;
            if(cached==null||reload)
            {
                try
                {
                    InitializeStorage();cached=LoadFiles();
                    if (!cached.Any(p => p.id == "symmetry")) cached.Add(Defaults().Single(p => p.id == "symmetry"));
                    StudioModelCommands.SyncRadialMenus(cached);
                    // Migration is complete only once the individual files load.
                    // Keep an empty legacy tombstone, never a second live store.
                    if(!string.IsNullOrEmpty(StudioPreferenceStore.GetString(PreferenceKey)))
                    {StudioPreferenceStore.DeleteKey(PreferenceKey);StudioPreferenceStore.Flush();}
                }
                catch(Exception error) when(error is ArgumentException||error is IOException||error is UnauthorizedAccessException)
                {
                    if(reportedErrors.Add(error.Message))Debug.LogWarning("Cannot load radial menu files: "+error.Message);
                    if(cached==null)cached=Defaults();
                    StudioModelCommands.SyncRadialMenus(cached);
                }
            }
            return cached.Select(Copy).ToList();
        }
        internal static void EnsureRegistered(){Read();}
        internal static StudioCustomToolbarDefinition Create(string name)=>new StudioCustomToolbarDefinition{id=Guid.NewGuid().ToString("N"),name=name};
        internal static void Save(List<StudioCustomToolbarDefinition> values)
        {
            InitializeStorage();WriteFiles(values,true);
            cached=values.Select(Copy).ToList();loadedIds.Clear();foreach(var item in cached)loadedIds.Add(item.id);
            Interlocked.Exchange(ref dirty,0);StudioModelCommands.SyncRadialMenus(cached);NotifyChanged();
        }
        // Reference pie layout: equal-angle circular spokes with outward horizontal labels.
        internal static float ButtonGap(Rect a,Rect b)
        {
            float x=Mathf.Max(0,Mathf.Abs(a.center.x-b.center.x)-(a.width+b.width)*.5f);
            float y=Mathf.Max(0,Mathf.Abs(a.center.y-b.center.y)-(a.height+b.height)*.5f);
            return Mathf.Sqrt(x*x+y*y);
        }
        internal static float InnerClearance(Rect[] rects)
        {
            float distance=float.PositiveInfinity;
            foreach(var rect in rects)
            {
                float x=Mathf.Max(rect.xMin,Mathf.Max(-rect.xMax,0));
                float y=Mathf.Max(rect.yMin,Mathf.Max(-rect.yMax,0));
                distance=Mathf.Min(distance,Mathf.Sqrt(x*x+y*y));
            }
            return rects.Length==0?0:distance;
        }
        internal static float RequiredInnerClearance(Vector2[] sizes)
        {
            float width=0,height=0;foreach(var size in sizes){width=Mathf.Max(width,size.x);height=Mathf.Max(height,size.y);}
            return Mathf.Max(height*1.6f,width*.3f)*(1+.15f/Mathf.Sqrt(Mathf.Max(1,sizes.Length)));
        }
        internal static Vector2 AnchorDirection(int index,int count)
        {
            float angle=index*Mathf.PI*2/count;
            var direction=new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle));
            if(Mathf.Abs(direction.x)<1e-5f)direction.x=0;
            if(Mathf.Abs(direction.y)<1e-5f)direction.y=0;
            return direction;
        }
        internal static Vector2 AnchorOffset(Vector2 direction,Vector2 size)
        {
            // Match reference pie menus: horizontal labels extend away from
            // their circular spoke, with north/south labels centered on it.
            return new Vector2(direction.x==0?0:Mathf.Sign(direction.x)*size.x*.35f,0);
        }
        private static Rect[] Place(int count,Vector2[] sizes,float radius)
        {
            var rects=new Rect[count];
            for(int i=0;i<count;i++)
            {
                var direction=AnchorDirection(i,count);
                var center=direction*radius+AnchorOffset(direction,sizes[i]);
                rects[i]=new Rect(center-sizes[i]*.5f,sizes[i]);
            }
            // Compact the upper/lower groups into evenly padded rows while
            // retaining each button's horizontal radial placement.
            if(count>=6)
            {
                var rows=new List<List<int>>();
                foreach(int index in Enumerable.Range(0,count).OrderBy(i=>rects[i].center.y))
                {
                    if(rows.Count==0||Mathf.Abs(rects[rows[rows.Count-1][0]].center.y-rects[index].center.y)>.1f)
                        rows.Add(new List<int>());
                    rows[rows.Count-1].Add(index);
                }
                float y=0;
                foreach(var row in rows)
                {
                    float height=row.Max(i=>sizes[i].y);
                    foreach(int i in row)rects[i].y=y+(height-sizes[i].y)*.5f;
                    y+=height+6;
                }
            }
            var offset=ContentBounds(rects).center;
            for(int i=0;i<count;i++)rects[i].position-=offset;
            return rects;
        }

        private static bool Fits(Rect[] rects,float clearance)
        {
            float rowHeight=rects.Max(r=>r.height);
            if(rects.Length>1&&InnerClearance(rects)<(rects.Length>=6?Mathf.Min(clearance,rowHeight*1.25f):clearance))return false;
            // Keep six pixels between visible buttons; projected label ends may overlap
            // freely when the rows are vertically separate.
            for(int i=0;i<rects.Length;i++)for(int j=0;j<i;j++)
            {
                float x=Mathf.Min(rects[i].xMax,rects[j].xMax)-Mathf.Max(rects[i].xMin,rects[j].xMin);
                float y=Mathf.Min(rects[i].yMax,rects[j].yMax)-Mathf.Max(rects[i].yMin,rects[j].yMin);
                if(x> -5.99f&&y> -5.99f)return false;
            }
            return true;
        }
        private static readonly Dictionary<(int count,float width,float height),Rect[]> layoutCache=new Dictionary<(int,float,float),Rect[]>();
        internal static Rect[] Layout(int count,Vector2[] sizes)
        {
            if(count==0)return Array.Empty<Rect>();
            bool uniform=true;for(int i=1;i<count;i++)if(sizes[i]!=sizes[0])uniform=false;
            if(!uniform)return ComputeLayout(count,sizes);
            var key=(count,sizes[0].x,sizes[0].y);
            if(!layoutCache.TryGetValue(key,out var result))
            {
                result=ComputeLayout(count,sizes);
                if(layoutCache.Count>=64)layoutCache.Clear();
                layoutCache.Add(key,result);
            }
            // Callers cannot mutate the cached positions. Contents/selection do
            // not affect geometry; dimensions/count naturally select another key.
            return (Rect[])result.Clone();
        }
        private static Rect[] ComputeLayout(int count,Vector2[] sizes)
        {
            if(count==0)return Array.Empty<Rect>();
            float width=0,height=0;foreach(var size in sizes){width=Mathf.Max(width,size.x);height=Mathf.Max(height,size.y);}
            float clearance=RequiredInnerClearance(sizes);
            float radius=Mathf.Max(Mathf.Max(height*2,width*.4f),clearance);
            if(count>1)radius=Mathf.Max(radius,(height+5)/(2*Mathf.Sin(Mathf.PI/count)));
            float lastFailed=radius;
            bool failed=false;
            for(int attempt=0;attempt<128;attempt++)
            {
                var rects=Place(count,sizes,radius);
                if(Fits(rects,clearance))
                {
                    if(failed)
                    {
                        float lower=lastFailed,upper=radius;
                        for(int step=0;step<16&&upper-lower>.1f;step++)
                        {
                            float middle=(lower+upper)*.5f;var candidate=Place(count,sizes,middle);
                            if(Fits(candidate,clearance)){upper=middle;rects=candidate;}else lower=middle;
                        }
                    }
                    return rects;
                }
                failed=true;lastFailed=radius;radius*=1.08f;
            }
            throw new InvalidOperationException("Cannot lay out radial buttons.");
        }
        internal static Rect ContentBounds(Rect[] rects)
        {
            if(rects.Length==0)return new Rect();
            var bounds=rects[0];
            for(int i=1;i<rects.Length;i++){bounds.xMin=Mathf.Min(bounds.xMin,rects[i].xMin);bounds.xMax=Mathf.Max(bounds.xMax,rects[i].xMax);bounds.yMin=Mathf.Min(bounds.yMin,rects[i].yMin);bounds.yMax=Mathf.Max(bounds.yMax,rects[i].yMax);}
            return bounds;
        }
        internal static Rect Bounds(Rect[] rects)
        {
            var bounds=new Rect(-48,-12,96,50);
            foreach(var rect in rects){bounds.xMin=Mathf.Min(bounds.xMin,rect.xMin);bounds.xMax=Mathf.Max(bounds.xMax,rect.xMax);bounds.yMin=Mathf.Min(bounds.yMin,rect.yMin);bounds.yMax=Mathf.Max(bounds.yMax,rect.yMax);}
            return bounds;
        }
        internal static int Hit(Rect[] rects,Vector2 pointer)
        {for(int i=0;i<rects.Length;i++)if(rects[i].Contains(pointer))return i;return -1;}

    }
    internal sealed class StudioRadialControlWindow : EditorWindow
    {
        internal static void Open(string key,MashBoxStudioViewport owner)
        {
            var window=CreateInstance<StudioRadialControlWindow>();window.titleContent=new GUIContent("Radial Control");window.minSize=new Vector2(180,44);
            var control=StudioCustomToolbars.CreateControl(key,owner);if(control!=null)window.rootVisualElement.Add(control);
            StudioWindowOwnership.ShowUtility(window,owner);
        }
    }
}
