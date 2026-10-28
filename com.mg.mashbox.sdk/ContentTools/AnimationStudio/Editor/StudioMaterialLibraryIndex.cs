using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioMaterialLibraryIndex
    {
        [Serializable] internal sealed class Entry
        {
            public string guid, path, name;
            public long localId;
            [NonSerialized] internal Material material;
            [NonSerialized] private string cachedKey;
            internal string Key => cachedKey??(cachedKey=guid+":"+localId);
            internal string Folder => path.Substring(0,path.LastIndexOf('/'));
        }
        [Serializable] private sealed class Snapshot { public string project; public int sources; public List<Entry> entries; }
        internal const string StandardCategory = "@Standard", PrototypingCategory = "@Prototyping";
        internal const string PrototypingFolderGuid = "67bf662064144647917bad78a5a111de";
        internal static bool IsPinned(string category) => category==StandardCategory||category==PrototypingCategory;
        internal static string CategoryLabel(string category) => category==StandardCategory?"Standard":category==PrototypingCategory?"Prototyping":string.IsNullOrEmpty(category)?"All Materials":category;
        internal const string SourcesKey = "Studio.MaterialLibrary.Sources";
        private const string SessionKey = "MashBox.Studio.MaterialLibrary.Index";
        internal static event Action Changed;
        internal static IReadOnlyList<Entry> Entries => entries;
        internal static IReadOnlyList<string> Folders => folders;
        internal static int Sources => Mathf.Clamp(Mathf.RoundToInt(StudioPreferenceStore.GetFloat(SourcesKey,3)),1,3);
        internal static bool Scanning => pending != null;
        internal static bool Stale { get; private set; }
        internal static string Error { get; private set; }
        internal static int Revision { get; private set; }
        internal static float Progress => pending == null ? 1 : cursor/(float)Math.Max(1,pending.Length);
        private static List<Entry> entries = new List<Entry>();
        private static List<string> folders = new List<string>();
        private static List<Entry> building;
        private static string[] pending;
        private static int cursor, scanSources;
        private static bool initialized;

        static StudioMaterialLibraryIndex()
        {
            EditorApplication.delayCall += Initialize;
            EditorApplication.projectChanged += MarkStale;
            AssemblyReloadEvents.beforeAssemblyReload += StopScan;
        }
        internal static bool Includes(string path,int sources) =>
            (sources&1)!=0 && path.StartsWith("Assets/",StringComparison.Ordinal) ||
            (sources&2)!=0 && path.StartsWith("Packages/",StringComparison.Ordinal);
        private static void Initialize()
        {
            if(initialized)return;initialized=true;
            try
            {
                var saved=JsonUtility.FromJson<Snapshot>(SessionState.GetString(SessionKey,""));
                if(saved!=null&&saved.project==Application.dataPath&&saved.sources==Sources&&saved.entries!=null)
                { entries=saved.entries;BuildFolders();Revision++;Changed?.Invoke();return; }
            }
            catch { /* A generated session cache can be replaced by a fresh scan. */ }
            Rescan();
        }
        internal static void EnsureInitialized(){if(!initialized)Initialize();}
        internal static void SetSources(int value)
        {
            value=Mathf.Clamp(value,1,3);if(value==Sources)return;
            StudioPreferenceStore.SetFloat(SourcesKey,value);Rescan();
        }
        internal static void Rescan()
        {
            initialized=true;StopScan();scanSources=Sources;Error=null;
            // One native indexed query. Subsequent GUID/path work is sliced; no
            // material previews or recursive filesystem traversal during scanning.
            try { pending=AssetDatabase.FindAssets("t:Material",scanSources==1?new[]{"Assets"}:null); }
            catch(Exception ex){Error=ex.Message;Stale=true;Changed?.Invoke();return;}
            building=new List<Entry>(pending.Length);cursor=0;Stale=false;
            EditorApplication.update+=ScanBatch;Changed?.Invoke();
        }
        private static void StopScan(){EditorApplication.update-=ScanBatch;pending=null;building=null;}
        private static void MarkStale(){if(!Scanning&&!Stale){Stale=true;Changed?.Invoke();}}
        private static void ScanBatch()
        {
            try { ScanNextBatch(); }
            catch(Exception ex){StopScan();Error=ex.Message;Stale=true;Changed?.Invoke();}
        }
        private static void ScanNextBatch()
        {
            if(pending==null)return;
            double until=EditorApplication.timeSinceStartup+.004;int processed=0;
            while(cursor<pending.Length&&(processed++==0||EditorApplication.timeSinceStartup<until))
            {
                string guid=pending[cursor++],path=AssetDatabase.GUIDToAssetPath(guid);
                if(string.IsNullOrEmpty(path)||!Includes(path,scanSources))continue;
                if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))
                    building.Add(new Entry{guid=guid,path=path,name=Path.GetFileNameWithoutExtension(path)});
                else
                {
                    // Embedded materials need their own local asset ID; do not
                    // collapse several model sub-assets into one browser tile.
                    foreach(var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                        if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string _,out long id))
                            building.Add(new Entry{guid=guid,path=path,name=material.name,localId=id});
                }
            }
            if(cursor<pending.Length){Changed?.Invoke();return;}
            entries=building.OrderBy(e=>e.name,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.path,StringComparer.Ordinal).ThenBy(e=>e.localId).ToList();
            StopScan();BuildFolders();Revision++;Stale=false;
            SessionState.SetString(SessionKey,JsonUtility.ToJson(new Snapshot{project=Application.dataPath,sources=scanSources,entries=entries}));
            Changed?.Invoke();
        }
        private static void BuildFolders()
        {
            var paths=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries)
            {
                string path=entry.Folder;
                while(!string.IsNullOrEmpty(path)){paths.Add(path);int slash=path.LastIndexOf('/');path=slash<0?null:path.Substring(0,slash);}
            }
            folders=paths.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static Entry StandardEntry(Material material,string label)
        {
            if(!material)return null;
            string path=AssetDatabase.GetAssetPath(material);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string guid,out long localId);
            return new Entry{guid=string.IsNullOrEmpty(guid)?"standard:"+label:guid,path=path,name=label,
                localId=AssetDatabase.IsMainAsset(material)?0:localId,material=material};
        }
        private static IEnumerable<Entry> PinnedEntries(string category)
        {
            if(category==StandardCategory)
            {
                var standard=StandardEntry(StudioMeshMaterialPreview.DefaultMaterial,"Default");if(standard!=null)yield return standard;
                var checker=StandardEntry(StudioMeshMaterialPreview.CheckerMaterial,"Checker");if(checker!=null)yield return checker;
                yield break;
            }
            string path=AssetDatabase.GUIDToAssetPath(PrototypingFolderGuid);
            if(string.IsNullOrEmpty(path)||!AssetDatabase.IsValidFolder(path))yield break;
            // This intentionally includes an empty pinned collection. Future
            // authored prototype materials are discovered only within this folder.
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{path}))
            {
                string asset=AssetDatabase.GUIDToAssetPath(guid);
                if(asset.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))
                    yield return new Entry{guid=guid,path=asset,name=Path.GetFileNameWithoutExtension(asset)};
            }
        }
        internal static List<Entry> Query(string folder,string search)
        {
            string[] words=(search??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            int sources=Sources;bool pinned=IsPinned(folder);
            IEnumerable<Entry> candidates=pinned?PinnedEntries(folder):entries.Where(e=>Includes(e.path,sources)&&
                (string.IsNullOrEmpty(folder)||e.path.StartsWith(folder+"/",StringComparison.Ordinal)));
            return candidates.Where(e=>words.All(w=>e.name.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0||e.path.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
        }
        internal static Material Resolve(Entry entry)
        {
            if(entry==null)return null;
            if(entry.material)return entry.material;
            if(entry.localId==0)return AssetDatabase.LoadAssetAtPath<Material>(entry.path);
            foreach(var material in AssetDatabase.LoadAllAssetsAtPath(entry.path).OfType<Material>())
                if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string _,out long id)&&id==entry.localId)return material;
            return null;
        }
    }
}
